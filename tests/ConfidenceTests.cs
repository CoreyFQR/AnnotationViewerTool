using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static partial class Tests
    {
        private sealed class ConfidenceFixture
        {
            public string Root;
            public string Predictions;
            public List<ImageRecord> Records;
        }

        private static ConfidenceFixture MakeConfidenceFixture()
        {
            string folder = Path.Combine(qa, "confidence-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(folder, "dataset");
            string pred = Path.Combine(folder, "predictions");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(pred);
            File.WriteAllText(Path.Combine(root, "classes.txt"), "cell");
            string[] scores = { "0", "0.004", "0.01", "0.249", "0.25", "0.9", "1", null };
            var predRows = new List<string>();
            var gtRows = new List<string>();
            for (int i = 0; i < scores.Length; i++)
            {
                string row = "0 " + (0.10M + 0.11M * i).ToString("0.00", CultureInfo.InvariantCulture) + " 0.5 0.06 0.12";
                predRows.Add(row + (scores[i] == null ? string.Empty : " " + scores[i]));
                if (i == 2 || i == 4 || i == 7) gtRows.Add(row);
            }
            foreach (string name in new[] { "a", "b" })
            {
                File.Copy(Path.Combine(dataset, "train", "images", "sample_001.png"), Path.Combine(root, name + ".png"));
                File.WriteAllLines(Path.Combine(root, name + ".txt"), gtRows.ToArray());
                File.WriteAllLines(Path.Combine(pred, name + ".txt"), predRows.ToArray());
            }
            return new ConfidenceFixture { Root = root, Predictions = pred, Records = DatasetScanner.BuildRecordsRecursively(root) };
        }

        private static void TestConfidenceServices()
        {
            var fixture = MakeConfidenceFixture();
            ImageRecord record = fixture.Records[0];
            var raw = PredictionResolver.LoadPredictionsFromFolder(record, fixture.Predictions, 960, 720);
            var filtered = PredictionResolver.FilterByConfidence(raw, 0.25F);
            Check(raw.Count == 8 && filtered.Count == 4 && filtered[0].Confidence == 0.25F &&
                filtered[1].Confidence == 0.9F && filtered[2].Confidence == 1F && !filtered[3].Confidence.HasValue,
                "confidence filter removes zero and low scores, includes the exact threshold and retains unscored predictions");
            Check(PredictionResolver.FilterByConfidence(raw, 0F).Count == 8 &&
                PredictionResolver.FilterByConfidence(raw, 1F).Count == 2,
                "confidence endpoints allow all scores at zero and only score one or missing scores at one");
            Check(raw.Count == 8 && ReferenceEquals(filtered[0], raw[4]) && ReferenceEquals(filtered[3], raw[7]),
                "confidence filtering leaves original shapes and ordering intact");
            foreach (float invalid in new[] { -0.01F, 1.01F, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Throws<ArgumentOutOfRangeException>(() => PredictionResolver.FilterByConfidence(raw, invalid),
                    "invalid confidence threshold rejected: " + invalid.ToString(CultureInfo.InvariantCulture));
            Check(PredictionResolver.LoadPredictionsFromFolder(record, fixture.Predictions, 960, 720, 0.25F).Count == 4,
                "prediction file loader uses the same confidence threshold");
            var gt = AnnotationReader.LoadAnnotations(record, 960, 720);
            MatchResult match = DetectionMatcher.MatchDetections(gt, filtered, 0.5F);
            Check(match.TruePositiveCount == 2 && match.FalsePositiveCount == 2 && match.FalseNegativeCount == 1,
                "a removed low-confidence detection becomes FN rather than affecting retained TP or FP");
            var rows = StatisticsService.BuildComparisonStatsRows(new[] { record }, fixture.Predictions, 0.5F,
                fixture.Root, CancellationToken.None, 0.25F);
            var total = rows.Single(r => r.IsTotal);
            Check(total.GtCount == 3 && total.PredCount == 4 && total.TruePositiveCount == 2 &&
                total.FalsePositiveCount == 2 && total.FalseNegativeCount == 1,
                "comparison statistics count the same confidence-filtered predictions as matching");
            var unfiltered = StatisticsService.BuildComparisonStatsRows(new[] { record }, fixture.Predictions, 0.5F, fixture.Root)
                .Single(r => r.IsTotal);
            Check(unfiltered.PredCount == 8 && unfiltered.TruePositiveCount == 3 && unfiltered.FalsePositiveCount == 5,
                "existing service callers retain the complete prediction set when confidence is omitted");
            string export = Path.Combine(fixture.Root, "export-check");
            Check(ComparisonExporter.Export(new[] { record }, fixture.Predictions, 0.5F, export,
                CancellationToken.None, null, 0.25F) == 1, "confidence-filtered comparison export completes");
            using (Image image = AnnotationReader.LoadImageWithoutLock(record.ImagePath))
            foreach (ViewMode mode in new[] { ViewMode.Overlay, ViewMode.Errors })
            {
                string subfolder = mode == ViewMode.Overlay ? "gt_pred_overlay" : "tp_fp_fn_analysis";
                string actual = ComparisonExporter.BuildExportPath(Path.Combine(export, subfolder), record.DisplayName);
                string expected = Path.Combine(export, "reference-" + mode + ".jpg");
                AnnotationRenderer.Save(image, gt, filtered, mode, 0.5F, expected);
                Check(File.ReadAllBytes(actual).SequenceEqual(File.ReadAllBytes(expected)),
                    "exported " + mode + " exactly matches rendering the filtered prediction set");
            }
        }

        private static async Task TestConfidenceUi(ViewerForm form)
        {
            var fixture = MakeConfidenceFixture();
            var numeric = Field<NumericUpDown>(form, "confidenceNumeric");
            var iou = Field<NumericUpDown>(form, "iouNumeric");
            decimal originalConfidence = numeric.Value;
            decimal originalIou = iou.Value;
            Size originalSize = form.Size;
            FormWindowState originalState = form.WindowState;
            Check(numeric.Value == 0.25M && numeric.Minimum == 0M && numeric.Maximum == 1M && numeric.Increment == 0.05M,
                "confidence input defaults to 0.25 with full zero-to-one range and IoU-style steps");
            await form.OpenFolderAsync(fixture.Root);
            try
            {
                var editor = numeric.Parent as NumericField;
                var iouEditor = iou.Parent as NumericField;
                var label = Field<Label>(form, "confidenceLabel");
                Check(editor != null && iouEditor != null && editor.Width == iouEditor.Width && editor.Height == iouEditor.Height &&
                    editor.PointToScreen(Point.Empty).X == iouEditor.PointToScreen(Point.Empty).X &&
                    editor.PointToScreen(Point.Empty).Y >= iouEditor.PointToScreen(new Point(0, iouEditor.Height)).Y &&
                    label is OptionLabel,
                    "confidence field shares IoU styling, width and alignment in the row immediately below");
                Check(!numeric.Enabled && !editor.Input.Enabled && !label.Enabled,
                    "confidence filter is disabled until a prediction folder is loaded");
                Field<TextBox>(form, "predFolderText").Text = fixture.Predictions;
                Check(numeric.Enabled && editor.Input.Enabled && label.Enabled &&
                    Field<List<AnnotationShape>>(form, "currentPredShapes").Count == 4,
                    "loading predictions immediately applies the displayed confidence threshold");
                var matchMethod = typeof(ViewerForm).GetMethod("GetCurrentMatch", BindingFlags.Instance | BindingFlags.NonPublic);
                var first = (MatchResult)matchMethod.Invoke(form, null);
                Check(first.TruePositiveCount == 2 && first.FalsePositiveCount == 2 && first.FalseNegativeCount == 1,
                    "live canvas matching agrees with confidence-filtered statistics");
                Invoke(form, "SetZoom", 0.75F);
                Check(ReferenceEquals(first, matchMethod.Invoke(form, null)),
                    "zoom reuses confidence-filtered matching rather than recalculating");
                using (var locked = new FileStream(Path.Combine(fixture.Predictions, "a.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    numeric.Value = 0M;
                    Check(Field<List<AnnotationShape>>(form, "currentPredShapes").Count == 8 &&
                        ((MatchResult)matchMethod.Invoke(form, null)).TruePositiveCount == 3,
                        "lowering confidence restores cached low-score predictions without rereading the source file");
                    numeric.Value = 1M;
                    Check(Field<List<AnnotationShape>>(form, "currentPredShapes").Count == 2 &&
                        ((MatchResult)matchMethod.Invoke(form, null)).TruePositiveCount == 1,
                        "raising confidence invalidates the match cache and retains exact-one and unscored predictions");
                }
                numeric.Value = 0.25M;
                editor.Increase.PerformClick();
                Check(numeric.Value == 0.30M && Field<List<AnnotationShape>>(form, "currentPredShapes").Count == 3,
                    "confidence plus button updates visible prediction count");
                editor.Decrease.PerformClick();
                Check(numeric.Value == 0.25M && Field<List<AnnotationShape>>(form, "currentPredShapes").Count == 4,
                    "confidence minus button restores an exact-boundary detection");
                editor.Input.Text = 0.90M.ToString("F2", CultureInfo.CurrentCulture);
                Check(numeric.Value == 0.90M && Field<List<AnnotationShape>>(form, "currentPredShapes").Count == 3,
                    "direct confidence entry immediately updates prediction filtering");
                numeric.Value = 0.25M;
                Check(iou.Value == originalIou, "confidence changes leave the independent IoU threshold unchanged");
                AssertConfidenceCanvas(form, fixture);
                byte[] annotationBytes = File.ReadAllBytes(fixture.Records[0].AnnotationPath);
                byte[] predictionBytes = File.ReadAllBytes(Path.Combine(fixture.Predictions, "a.txt"));
                Field<ListView>(form, "annotationList").Items[0].Checked = false;
                numeric.Value = 0.9M;
                numeric.Value = 0.25M;
                Check(!Field<ListView>(form, "annotationList").Items[0].Checked &&
                    File.ReadAllBytes(fixture.Records[0].AnnotationPath).SequenceEqual(annotationBytes) &&
                    File.ReadAllBytes(Path.Combine(fixture.Predictions, "a.txt")).SequenceEqual(predictionBytes),
                    "confidence edits preserve annotation visibility and original GT and prediction files");
                var files = Field<ListBox>(form, "fileList");
                numeric.Value = 0.9M;
                files.SelectedIndex = 1;
                Check(numeric.Value == 0.9M && Field<List<AnnotationShape>>(form, "currentPredShapes").Count == 3,
                    "switching images retains confidence threshold and applies it to new predictions");
                files.SelectedIndex = 0;
                numeric.Value = 0.25M;
                Field<Button>(form, "fitButton").PerformClick();
                SaveForm(form, "ui-confidence-threshold.png");
                form.WindowState = FormWindowState.Normal;
                form.Size = new Size(1120, 720);
                form.PerformLayout();
                await Task.Delay(50);
                var list = Field<ListView>(form, "annotationList");
                Check(editor.Parent.ClientRectangle.Contains(editor.Bounds) && editor.ClientRectangle.Contains(editor.Input.Bounds) &&
                    iouEditor.Parent.ClientRectangle.Contains(iouEditor.Bounds) &&
                    label.Parent.ClientRectangle.Contains(label.Bounds) &&
                    list.Parent.ClientRectangle.Contains(list.Bounds) && list.Height >= list.Font.Height * 2,
                    "confidence row and annotation list remain readable in the minimum normal window");
                SaveForm(form, "ui-confidence-threshold-compact.png");
                Field<TextBox>(form, "predFolderText").Clear();
                Check(!numeric.Enabled && !editor.Input.Enabled && Field<List<AnnotationShape>>(form, "currentPredShapes").Count == 0,
                    "removing prediction folder disables confidence input and clears filtered detections");
            }
            finally
            {
                numeric.Value = originalConfidence;
                iou.Value = originalIou;
                form.WindowState = FormWindowState.Normal;
                form.Size = originalSize;
                form.WindowState = originalState;
            }
            await form.OpenFolderAsync(dataset);
        }

        private static void AssertConfidenceCanvas(ViewerForm form, ConfidenceFixture fixture)
        {
            var picture = Field<PictureBox>(form, "pictureBox");
            var source = Field<Image>(form, "sourceImage");
            float scale = Field<float>(form, "zoom");
            var expectedPredictions = PredictionResolver.LoadPredictionsFromFolder(fixture.Records[0], fixture.Predictions,
                source.Width, source.Height, 0.25F);
            using (var actual = new Bitmap(picture.Width, picture.Height))
            using (var expected = new Bitmap(picture.Width, picture.Height))
            using (var actualGraphics = Graphics.FromImage(actual))
            using (var expectedGraphics = Graphics.FromImage(expected))
            {
                Invoke(form, "PaintImage", picture, new PaintEventArgs(actualGraphics, picture.ClientRectangle));
                expectedGraphics.InterpolationMode = scale > 2 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                expectedGraphics.DrawImage(source, new Rectangle(Point.Empty, picture.Size));
                AnnotationRenderer.Draw(expectedGraphics, Field<List<AnnotationShape>>(form, "currentShapes"), expectedPredictions,
                    scale, ViewMode.Overlay, Field<ModernButton>(form, "labelsButton").Checked, (float)Field<NumericUpDown>(form, "iouNumeric").Value);
                Check(RegionEquals(actual, expected, picture.ClientRectangle),
                    "canvas displays exactly the same confidence-filtered overlay used by export");
            }
        }
    }
}
