using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal sealed partial class ViewerForm
    {
        private int selectedAnnotationIndex = -1;
        private byte[] currentAnnotationBytes;
        private bool syncingAnnotationSelection;
        private readonly Stack<EditTransaction> editHistory = new Stack<EditTransaction>();
        private readonly Button undoButton = new ModernButton();

        private void SelectAnnotation(int index)
        {
            selectedAnnotationIndex = index >= 0 && index < currentShapes.Count ? index : -1;
            syncingAnnotationSelection = true;
            try
            {
                foreach (ListViewItem item in annotationList.Items) item.Selected = item.Index == selectedAnnotationIndex;
                if (selectedAnnotationIndex >= 0)
                {
                    annotationList.Items[selectedAnnotationIndex].Focused = true;
                    annotationList.EnsureVisible(selectedAnnotationIndex);
                }
            }
            finally { syncingAnnotationSelection = false; }
            pictureBox.Invalidate();
        }

        private void SelectCanvasAnnotation(Point point)
        {
            imagePanel.Focus();
            PointF source = new PointF(point.X / zoom, point.Y / zoom);
            var candidates = new Dictionary<int, AnnotationShape>();
            if (CurrentViewMode() == ViewMode.Errors)
            {
                var match = DetectionMatcher.MatchDetections(currentShapes, currentPredShapes, (float)iouNumeric.Value);
                foreach (int i in match.FalseNegativeIndexes) candidates[i] = currentShapes[i];
                foreach (var pair in match.PredToGtIndexes) candidates[pair.Value] = currentPredShapes[pair.Key];
            }
            else for (int i = 0; i < currentShapes.Count; i++) candidates[i] = currentShapes[i];
            var hits = candidates.Where(p => HitShape(p.Value, source, 5F / zoom))
                .OrderBy(p => p.Value.Bounds.Value.Width * p.Value.Bounds.Value.Height).ThenBy(p => p.Key).Select(p => p.Key).ToList();
            // Repeated clicks cycle overlapping boxes without losing access to a larger box.
            int previous = hits.IndexOf(selectedAnnotationIndex);
            SelectAnnotation(hits.Count == 0 ? -1 : hits[(previous + 1) % hits.Count]);
        }

        private static bool HitShape(AnnotationShape shape, PointF point, float tolerance)
        {
            if (!shape.Bounds.HasValue) return false;
            if (shape.Points.Count > 2 && string.Equals(shape.ShapeType, "polygon", StringComparison.OrdinalIgnoreCase))
            {
                using (var path = new GraphicsPath())
                using (var pen = new Pen(Color.White, tolerance * 2))
                { path.AddPolygon(shape.Points.ToArray()); return path.IsVisible(point) || path.IsOutlineVisible(point, pen); }
            }
            RectangleF bounds = shape.Bounds.Value;
            bounds.Inflate(tolerance, tolerance);
            return bounds.Contains(point);
        }

        private void PaintAnnotationSelection(Graphics graphics)
        {
            if (selectedAnnotationIndex < 0 || selectedAnnotationIndex >= currentShapes.Count) return;
            AnnotationShape shape = currentShapes[selectedAnnotationIndex];
            if (!shape.Bounds.HasValue) return;
            RectangleF bounds = shape.Bounds.Value;
            bounds = new RectangleF(bounds.X * zoom, bounds.Y * zoom, bounds.Width * zoom, bounds.Height * zoom);
            using (var outline = new Pen(Color.FromArgb(20, 30, 43), 5))
            using (var selected = new Pen(Color.White, 2) { DashStyle = DashStyle.Dash })
            {
                if (shape.Points.Count > 2 && string.Equals(shape.ShapeType, "polygon", StringComparison.OrdinalIgnoreCase))
                {
                    var points = shape.Points.Select(p => new PointF(p.X * zoom, p.Y * zoom)).ToArray();
                    graphics.DrawPolygon(outline, points); graphics.DrawPolygon(selected, points);
                }
                else
                { graphics.DrawRectangle(outline, bounds.X, bounds.Y, bounds.Width, bounds.Height); graphics.DrawRectangle(selected, bounds.X, bounds.Y, bounds.Width, bounds.Height); }
            }
            foreach (PointF corner in new[] { new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top),
                new PointF(bounds.Left, bounds.Bottom), new PointF(bounds.Right, bounds.Bottom) })
                graphics.FillRectangle(Brushes.White, corner.X - 3, corner.Y - 3, 6, 6);
        }

        private async Task RefreshAfterEditAsync(string selectedPath, int oldIndex)
        {
            // Once committed, refreshing cannot cancel or erase the undo transaction.
            var refreshed = await Task.Run(() => DatasetScanner.BuildRecordsRecursively(loadedFolder));
            records.Clear(); records.AddRange(refreshed);
            ApplyFileFilter();
            int index = visibleRecords.FindIndex(r => r.ImagePath == selectedPath);
            if (index < 0) index = Math.Min(Math.Max(0, oldIndex), visibleRecords.Count - 1);
            fileList.SelectedIndex = index;
            if (index >= 0) LoadSelectedRecord();
        }

        private async Task ApplyEditAsync(Func<CancellationToken, List<FileChange>> plan, string message)
        {
            string selectedPath = GetSelectedRecord() == null ? null : GetSelectedRecord().ImagePath;
            int index = fileList.SelectedIndex;
            await RunWorkAsync("正在更新标注…", async delegate(CancellationToken cancellation)
            {
                var transaction = await Task.Run(() => EditTransaction.Apply(loadedFolder, plan(cancellation), cancellation), cancellation);
                if (transaction == null) { SetStatus("没有匹配的标注。"); return; }
                editHistory.Push(transaction);
                undoButton.Enabled = true;
                await RefreshAfterEditAsync(selectedPath, index);
                SetStatus(message + " · Ctrl+Z 撤销" + (transaction.RecoveryWarning == null ? "" : " · " + transaction.RecoveryWarning));
            });
        }

        private async Task DeleteImageAsync()
        {
            var record = GetSelectedRecord();
            if (record == null || isBusy) return;
            var snapshot = new List<ImageRecord>(records);
            await ApplyEditAsync(token => AnnotationEditor.RemoveImage(record, snapshot), "已删除图片及标注");
            fileList.Focus();
        }

        private async Task DeleteAnnotationAsync()
        {
            var record = GetSelectedRecord();
            int index = selectedAnnotationIndex;
            byte[] expected = currentAnnotationBytes;
            bool listFocused = annotationList.ContainsFocus;
            if (record == null || index < 0 || expected == null || isBusy) return;
            await ApplyEditAsync(token => new List<FileChange> { AnnotationEditor.RemoveShapes(record, new[] { index }, expected) }, "已删除标注");
            // A second Delete requires a fresh selection, avoiding key-repeat deletion of adjacent boxes.
            if (listFocused) annotationList.Focus(); else imagePanel.Focus();
        }

        private async Task UndoEditAsync()
        {
            if (isBusy || editHistory.Count == 0) return;
            string path = GetSelectedRecord() == null ? null : GetSelectedRecord().ImagePath;
            int index = fileList.SelectedIndex;
            await RunWorkAsync("正在撤销删除…", async delegate(CancellationToken cancellation)
            {
                var transaction = editHistory.Peek();
                await Task.Run(() => transaction.Undo());
                editHistory.Pop();
                undoButton.Enabled = editHistory.Count > 0;
                await RefreshAfterEditAsync(path, index);
                SetStatus("已撤销删除" + (transaction.RecoveryWarning == null ? "" : " · " + transaction.RecoveryWarning));
            });
        }

        private async Task<IList<StatsRow>> DeleteCategoriesAsync(IList<StatsRow> rows)
        {
            var snapshot = new List<ImageRecord>(records);
            await ApplyEditAsync(token => AnnotationEditor.RemoveCategories(loadedFolder, snapshot, rows, token), "已删除所选类别标注");
            return await Task.Run(() => StatisticsService.BuildStatsRows(loadedFolder, records.ToList()));
        }
    }
}
