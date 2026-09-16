using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal sealed partial class ViewerForm
    {
        private bool isBusy;
        private bool closeAfterWork;
        private CancellationTokenSource workCancellation;
        private string resolvedPredictionFolder;
        private string loadedFolder;

        private async Task RunWorkAsync(string message, Func<CancellationToken, Task> action)
        {
            if (isBusy) return;
            isBusy = true;
            workCancellation = new CancellationTokenSource();
            browseButton.Enabled = reloadButton.Enabled = statsButton.Enabled = exportCompareButton.Enabled = false;
            workspace.Enabled = false;
            folderText.ReadOnly = true;
            progressBar.Visible = cancelWorkButton.Visible = true;
            SetStatus(message);
            try { await action(workCancellation.Token); }
            catch (OperationCanceledException) { SetStatus("操作已取消"); }
            catch (Exception ex)
            {
                SetStatus("操作失败：" + ex.Message);
                MessageBox.Show(this, ex.Message, "MedVision", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                workCancellation.Dispose();
                workCancellation = null;
                isBusy = false;
                workspace.Enabled = true;
                folderText.ReadOnly = false;
                browseButton.Enabled = reloadButton.Enabled = statsButton.Enabled = true;
                progressBar.Visible = cancelWorkButton.Visible = false;
                UpdatePredictionControlsState();
                if (closeAfterWork) Close();
            }
        }

        internal async Task OpenFolderAsync(string folder)
        {
            if (isBusy) return;
            string fullPath;
            try
            {
                string entered = Environment.ExpandEnvironmentVariables((folder ?? string.Empty).Trim().Trim('"'));
                if (entered.Length == 0 || !Directory.Exists(entered)) { SetStatus("目录不存在：" + entered); return; }
                fullPath = Path.GetFullPath(entered);
            }
            catch (Exception ex) { SetStatus("目录无效：" + ex.Message); return; }
            await RunWorkAsync("正在扫描数据目录…", async delegate(CancellationToken cancellation)
            {
                List<ImageRecord> loaded = await Task.Run(() => DatasetScanner.BuildRecordsRecursively(fullPath, cancellation), cancellation);
                cancellation.ThrowIfCancellationRequested();
                ClearCurrentImage();
                records.Clear();
                records.AddRange(loaded);
                if (!string.Equals(loadedFolder, fullPath, StringComparison.OrdinalIgnoreCase))
                { editHistory.Clear(); undoButton.Enabled = false; }
                loadedFolder = fullPath;
                folderText.Text = fullPath;
                resolvedPredictionFolder = null;
                predFolderText.Clear();
                changingSelection = true;
                try { fileList.SelectedIndex = -1; }
                finally { changingSelection = false; }
                if (searchText.TextLength > 0) searchText.Clear();
                else ApplyFileFilter();
                if (loaded.Count == 0) SetStatus("未找到图片与同名 JSON / TXT 标注对，请检查所选目录。");
            });
        }

        private async Task ShowAnnotationStatsAsync()
        {
            if (records.Count == 0) { SetStatus("请先打开数据目录。"); return; }
            List<ImageRecord> snapshot = new List<ImageRecord>(records);
            string root = loadedFolder;
            List<StatsRow> rows = null;
            await RunWorkAsync("正在统计标注…", async delegate(CancellationToken cancellation)
            {
                var result = await Task.Run(() => StatisticsService.BuildStatsRows(root, snapshot, cancellation), cancellation);
                cancellation.ThrowIfCancellationRequested();
                rows = result;
                SetStatus("标注统计完成 · " + snapshot.Count + " 张图");
            });
            if (rows != null && !IsDisposed && !closeAfterWork)
                using (StatisticsForm dialog = StatisticsForm.Annotations(rows, root, DeleteCategoriesAsync)) dialog.ShowDialog(this);
        }

        private async Task ShowComparisonStatsAsync()
        {
            if (records.Count == 0 || !HasPredictionFolder()) return;
            List<ImageRecord> snapshot = new List<ImageRecord>(records);
            string root = loadedFolder;
            string predictions = resolvedPredictionFolder;
            float threshold = (float)iouNumeric.Value;
            List<ComparisonStatsRow> rows = null;
            await RunWorkAsync("正在计算 TP / FP / FN…", async delegate(CancellationToken cancellation)
            {
                var result = await Task.Run(() => {
                    PredictionResolver.ValidateMapping(snapshot, predictions);
                    return StatisticsService.BuildComparisonStatsRows(snapshot, predictions, threshold, root, cancellation);
                }, cancellation);
                cancellation.ThrowIfCancellationRequested();
                rows = result;
                SetStatus("误差统计完成 · IoU " + threshold.ToString("0.00"));
            });
            if (rows != null && !IsDisposed && !closeAfterWork)
                using (StatisticsForm dialog = StatisticsForm.Comparison(rows, root, predictions, threshold)) dialog.ShowDialog(this);
        }

        private async Task ExportComparisonImagesAsync()
        {
            if (records.Count == 0 || !HasPredictionFolder()) return;
            string destination;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择导出位置（自动创建独立子目录）" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                destination = Path.Combine(dialog.SelectedPath, "MedVision_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            }
            List<ImageRecord> snapshot = new List<ImageRecord>(records);
            string predictions = resolvedPredictionFolder;
            float threshold = (float)iouNumeric.Value;
            await RunWorkAsync("正在准备导出…", async delegate(CancellationToken cancellation)
            {
                var progress = new Progress<string>(message => { if (isBusy && !IsDisposed) SetStatus(message); });
                int count = await Task.Run(() => ComparisonExporter.Export(snapshot, predictions, threshold, destination, cancellation, progress), cancellation);
                cancellation.ThrowIfCancellationRequested();
                SetStatus("导出完成 · " + count + " 张图 · " + destination);
                MessageBox.Show(this, "已导出 " + count + " 张图的叠加图和错误分析图。\n\n" + destination,
                    "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }
    }
}
