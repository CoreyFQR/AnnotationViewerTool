using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static partial class Tests
    {
        private static void TestPredictionFilenameCase()
        {
            string folder = Path.Combine(qa, "prediction-case-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "SaMpLe_001.TxT");
            File.WriteAllText(path, "0 0.35 0.42 0.2 0.28 0.94\n");
            var records = DatasetScanner.BuildRecordsRecursively(dataset);
            Check(string.Equals(PredictionResolver.ResolvePath(records[0], folder), path, StringComparison.OrdinalIgnoreCase),
                "prediction filename and TXT extension ignore case");
            Check(PredictionResolver.LoadPredictionsFromFolder(records[0], folder, 960, 720).Count == 1,
                "mixed-case prediction file is read normally");
            string nested = Path.Combine(folder, "TRAIN", "LaBeLs");
            Directory.CreateDirectory(nested);
            string nestedPath = Path.Combine(nested, "SAMPLE_001.TXT");
            File.WriteAllText(nestedPath, "1 0.5 0.5 0.1 0.1\n");
            Check(string.Equals(PredictionResolver.ResolvePath(records[0], folder), nestedPath, StringComparison.OrdinalIgnoreCase),
                "nested prediction mapping retains precedence with different casing");
            Check(PredictionResolver.LoadPredictionsFromFolder(records[0], folder, 960, 720)[0].ClassId == 1,
                "nested mixed-case prediction uses correct file contents");
            var duplicate = new ImageRecord(records[0].ImagePath, records[0].AnnotationPath, records[0].AnnotationKind,
                records[0].ShapeCount, "TRAIN/IMAGES/SAMPLE_001.PNG", Classes);
            Throws<InvalidDataException>(() => PredictionResolver.ValidateMapping(new[] { records[0], duplicate }, folder),
                "case-insensitive prediction collisions remain rejected");
            Check(PredictionResolver.ResolvePath(records[1], folder) == null,
                "missing predictions remain empty with case-insensitive lookup");
            var escape = new ImageRecord(records[0].ImagePath, records[0].AnnotationPath, records[0].AnnotationKind,
                2, "../SAMPLE_001.PNG", Classes);
            Throws<InvalidDataException>(() => PredictionResolver.ResolvePath(escape, folder),
                "case-insensitive lookup retains prediction path containment");
        }

        private static bool RegionEquals(Bitmap a, Bitmap b, Rectangle region)
        {
            for (int y = region.Top; y < region.Bottom; y++)
                for (int x = region.Left; x < region.Right; x++)
                    if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
            return true;
        }

        private static void TestVisibilityRendering()
        {
            var a = new AnnotationShape("cell", "rectangle", new[] { new PointF(30, 100), new PointF(100, 180) });
            var b = new AnnotationShape("cell", "polygon", new[] { new PointF(230, 100), new PointF(300, 100), new PointF(300, 180) });
            var fp = new AnnotationShape("other", "rectangle", new[] { new PointF(400, 100), new PointF(470, 180) });
            var gt = new[] { a, b };
            var pred = new[] { a, fp };
            var hidden = new HashSet<int> { 0 };
            var match = DetectionMatcher.MatchDetections(gt, pred, 0.5F);
            foreach (ViewMode mode in new[] { ViewMode.Annotations, ViewMode.GroundTruth, ViewMode.Errors })
            using (var visible = new Bitmap(500, 220))
            using (var filtered = new Bitmap(500, 220))
            using (var empty = new Bitmap(500, 220))
            using (var all = Graphics.FromImage(visible))
            using (var some = Graphics.FromImage(filtered))
            {
                AnnotationRenderer.Draw(all, gt, pred, 1, mode, true, 0.5F, match);
                AnnotationRenderer.Draw(some, gt, pred, 1, mode, true, 0.5F, match, hidden);
                Check(!RegionEquals(visible, filtered, new Rectangle(20, 60, 170, 140)) &&
                    RegionEquals(filtered, empty, new Rectangle(20, 60, 170, 140)),
                    "unchecked annotation hides its box and text in " + mode);
                Check(RegionEquals(visible, filtered, new Rectangle(210, 60, 180, 140)),
                    "visible same-class polygon keeps its color, number and label in " + mode);
                if (mode == ViewMode.Errors)
                    Check(RegionEquals(visible, filtered, new Rectangle(390, 60, 110, 140)) &&
                        match.TruePositiveCount == 1 && match.FalsePositiveCount == 1 && match.FalseNegativeCount == 1,
                        "visibility preserves FP rendering and full-data TP FP FN counts");
            }
            using (var image = new Bitmap(500, 220))
            using (var g = Graphics.FromImage(image))
            {
                AnnotationRenderer.Draw(g, gt, pred, 1, ViewMode.Overlay, false, 0.5F, match, new HashSet<int> { 0, 1 });
                Check(image.GetPixel(260, 100).A == 0 && image.GetPixel(435, 100).A > 0,
                    "overlay hides unchecked GT and retains independent predictions");
                g.Clear(Color.Transparent);
                AnnotationRenderer.Draw(g, gt, pred, 1, ViewMode.Errors, false, 0.5F, match, new HashSet<int> { 0, 1 });
                Check(image.GetPixel(260, 100).A == 0, "unchecked FN is hidden in error analysis");
            }
        }

        private static void TestAnnotationVisibilityUi(ViewerForm form)
        {
            var list = Field<ListView>(form, "annotationList");
            var shapes = Field<List<AnnotationShape>>(form, "currentShapes");
            var files = Field<ListBox>(form, "fileList");
            var records = Field<List<ImageRecord>>(form, "records");
            byte[] original = File.ReadAllBytes(records[0].AnnotationPath);
            Check(list.CheckBoxes && list.Items.Count == 2 && list.Items.Cast<ListViewItem>().All(i => i.Checked),
                "all current annotations are checked by default");
            list.Focus();
            Rectangle row = list.Items[0].Bounds;
            int y = row.Top + row.Height / 2;
            int x = Enumerable.Range(0, list.Columns[0].Width).First(px =>
                (list.HitTest(px, y).Location & ListViewHitTestLocations.StateImage) != 0);
            IntPtr point = new IntPtr((y << 16) | x);
            SendMessage(list.Handle, 0x201, new IntPtr(1), point);
            SendMessage(list.Handle, 0x202, IntPtr.Zero, point);
            Check(!list.Items[0].Checked && list.Items[1].Checked,
                "native checkbox mouse click hides only the chosen annotation");
            Invoke(form, "SelectAnnotation", 0);
            var picture = Field<PictureBox>(form, "pictureBox");
            using (var selected = new Bitmap(picture.Width, picture.Height))
            using (var cleared = new Bitmap(picture.Width, picture.Height))
            using (var g1 = Graphics.FromImage(selected))
            using (var g2 = Graphics.FromImage(cleared))
            {
                Invoke(form, "PaintImage", picture, new PaintEventArgs(g1, picture.ClientRectangle));
                Invoke(form, "SelectAnnotation", -1);
                Invoke(form, "PaintImage", picture, new PaintEventArgs(g2, picture.ClientRectangle));
                Check(RegionEquals(selected, cleared, picture.ClientRectangle), "hidden annotation has no selection outline");
            }
            RectangleF bounds = shapes[0].Bounds.Value;
            float zoom = Field<float>(form, "zoom");
            Invoke(form, "SelectCanvasAnnotation", new Point((int)((bounds.X + bounds.Width / 2) * zoom),
                (int)((bounds.Y + bounds.Height / 2) * zoom)));
            Check(Field<int>(form, "selectedAnnotationIndex") == -1, "hidden annotation cannot be selected on canvas");
            Field<TextBox>(form, "predFolderText").Text = predictions;
            var method = typeof(ViewerForm).GetMethod("GetCurrentMatch", BindingFlags.Instance | BindingFlags.NonPublic);
            var before = (MatchResult)method.Invoke(form, null);
            Field<CheckBox>(form, "errorAnalysisCheckBox").Checked = true;
            Invoke(form, "SetZoom", 0.75F);
            Check(!list.Items[0].Checked && ReferenceEquals(before, method.Invoke(form, null)) && before.TruePositiveCount == 1,
                "zoom and comparison mode retain visibility and matching cache");
            Invoke(form, "SelectAnnotation", 0);
            list.Focus();
            SendMessage(list.Handle, 0x100, new IntPtr(32), IntPtr.Zero);
            SendMessage(list.Handle, 0x101, new IntPtr(32), IntPtr.Zero);
            Check(list.Items[0].Checked, "Space restores checked annotation");
            list.Items[0].Checked = false;
            Check(shapes.Count == 2 && File.ReadAllBytes(records[0].AnnotationPath).SequenceEqual(original) &&
                StatisticsService.BuildStatsRows(dataset, records).Single(s => s.IsTotal).BoxCount == 2,
                "visibility never edits annotation files or statistics");
            Field<TextBox>(form, "predFolderText").Clear();
            SaveForm(form, "ui-annotation-visibility.png");
            files.SelectedIndex = 1;
            Check(list.Items.Count == 0, "empty annotation image remains supported");
            files.SelectedIndex = 0;
            Check(list.Items.Cast<ListViewItem>().All(i => i.Checked) && Field<HashSet<int>>(form, "hiddenAnnotationIndexes").Count == 0,
                "switching images resets annotation visibility to all checked");
            Field<Button>(form, "fitButton").PerformClick();
        }
    }
}
