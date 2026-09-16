using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static partial class Tests
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private static int checks;
        private static string qa;
        private static string dataset;
        private static string predictions;
        private static readonly Dictionary<int, string> Classes = new Dictionary<int, string> { { 0, "细胞" }, { 1, "菌丝" } };

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            checks++;
            Console.WriteLine("PASS: " + name);
        }

        private static void Throws<T>(Action action, string name) where T : Exception
        {
            try { action(); }
            catch (T) { Check(true, name); return; }
            throw new Exception("Expected " + typeof(T).Name + ": " + name);
        }

        [STAThread]
        private static int Main(string[] args)
        {
            qa = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(qa);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                MakeFixture();
                TestServices();
                TestEditServices();
                TestAuditRegressions();
                Exception failure = null;
                using (ViewerForm form = new ViewerForm())
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-20000, -20000);
                    form.ShowInTaskbar = false;
                    form.Shown += async delegate {
                        try { await TestUi(form); await TestEditingUi(form); }
                        catch (Exception ex) { failure = ex; }
                        finally { form.Close(); }
                    };
                    Application.Run(form);
                }
                if (failure != null) throw failure;
                string report = "MedVision 2.0.0: " + checks + " checks passed.\n" + DateTime.Now.ToString("s");
                File.WriteAllText(Path.Combine(qa, "test-results.txt"), report);
                Console.WriteLine(report);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static void MakeFixture()
        {
            dataset = Path.Combine(qa, "fixture", "dataset");
            predictions = Path.Combine(qa, "fixture", "predictions");
            Directory.CreateDirectory(Path.Combine(dataset, "train", "images"));
            Directory.CreateDirectory(Path.Combine(dataset, "train", "labels"));
            Directory.CreateDirectory(predictions);
            File.WriteAllLines(Path.Combine(dataset, "classes.txt"), new[] { "细胞", "菌丝" });
            using (Bitmap bitmap = new Bitmap(960, 720))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(227, 233, 232));
                Random random = new Random(170);
                for (int i = 0; i < 240; i++)
                {
                    int x = random.Next(940), y = random.Next(700), r = random.Next(3, 16);
                    using (Brush brush = new SolidBrush(Color.FromArgb(55, 120, 135, 147))) graphics.FillEllipse(brush, x, y, r, r);
                }
                using (Brush cell = new SolidBrush(Color.FromArgb(90, 90, 150)))
                using (Pen edge = new Pen(Color.FromArgb(105, 109, 151), 8))
                {
                    graphics.FillEllipse(cell, 270, 220, 135, 155);
                    graphics.DrawEllipse(edge, 270, 220, 135, 155);
                    graphics.FillEllipse(cell, 610, 390, 155, 115);
                    graphics.DrawEllipse(edge, 610, 390, 155, 115);
                }
                using (Font font = new Font("Microsoft YaHei UI", 13F))
                    graphics.DrawString("MedVision · 合成测试图像", font, Brushes.DimGray, 20, 675);
                bitmap.Save(Path.Combine(dataset, "train", "images", "sample_001.png"), ImageFormat.Png);
                bitmap.Save(Path.Combine(dataset, "train", "images", "sample_002.png"), ImageFormat.Png);
            }
            File.WriteAllText(Path.Combine(dataset, "train", "labels", "sample_001.txt"), "0 0.35 0.42 0.2 0.28\n1 0.72 0.62 0.2 0.22\n");
            File.WriteAllText(Path.Combine(dataset, "train", "labels", "sample_002.txt"), "");
            File.WriteAllText(Path.Combine(predictions, "sample_001.txt"), "0 0.35 0.42 0.2 0.28 0.94\n0 0.85 0.2 0.1 0.1 0.70\n");
        }

        private static AnnotationShape Box(string label, int? id, float confidence)
        {
            return new AnnotationShape(label, "rectangle", new[] { new PointF(0, 0), new PointF(100, 100) }, id, confidence);
        }

        private static void TestServices()
        {
            var records = DatasetScanner.BuildRecordsRecursively(dataset);
            Check(records.Count == 2, "recursive YOLO scan deduplicates records and includes empty labels");
            Check(records[0].ClassNames[1] == "菌丝", "ancestor classes.txt preserves Chinese class names");
            var gt = AnnotationReader.LoadAnnotations(records[0], 960, 720);
            Check(gt.Count == 2 && Math.Abs(gt[0].Bounds.Value.Width - 192) < 0.01, "normalized YOLO to pixel coordinates");
            var pred = PredictionResolver.LoadPredictionsFromFolder(records[0], predictions, 960, 720);
            var match = DetectionMatcher.MatchDetections(gt, pred, 0.5F);
            Check(match.TruePositiveCount == 1 && match.FalsePositiveCount == 1 && match.FalseNegativeCount == 1, "TP FP FN reference result");
            Check(DetectionMatcher.ComputeIou(new RectangleF(0, 0, 100, 100), new RectangleF(0, 0, 100, 100)) == 1, "identical box IoU");
            Check(DetectionMatcher.ComputeIou(new RectangleF(0, 0, 0, 0), new RectangleF(0, 0, 0, 0)) == 0, "zero area IoU");
            var duplicate = DetectionMatcher.MatchDetections(new[] { Box("细胞", 0, 1) },
                new[] { Box("细胞", 0, 0.2F), Box("细胞", 0, 0.9F) }, 0.5F);
            Check(duplicate.TruePositiveCount == 1 && duplicate.MatchedPredIndexes.Contains(1), "one-to-one matching favors confidence");
            Check(!DetectionMatcher.ClassMatches(Box("细胞", null, 1), Box("1: 菌丝", 1, 1)), "mixed LabelMe YOLO different classes never match");
            Check(DetectionMatcher.ClassMatches(Box("细胞", null, 1), Box("0: 细胞", 0, 1)), "mixed format class names match");
            string json = Path.Combine(qa, "rectangle.json");
            File.WriteAllText(json, "{\"shapes\":[{\"label\":\"细胞\",\"shape_type\":\"polygon\",\"points\":[[10,20],[50,20],[50,70]]}]}");
            var polygon = AnnotationReader.LoadLabelMeAnnotations(json);
            Check(polygon.Count == 1 && polygon[0].Bounds.Value == new RectangleF(10, 20, 40, 50), "LabelMe polygon bounds");
            Check(AnnotationReader.CountLabelMeShapes(json) == 1 && AnnotationReader.CountLabelMeLabels(json)["细胞"] == 1, "parser and counts agree");
            string invalid = Path.Combine(qa, "invalid.txt");
            foreach (string row in new[] { "0 NaN 0.5 0.2 0.2", "0 0.5 0.5 -0.2 0.2", "-1 0.5 0.5 0.2 0.2", "0 0.5 0.5 0.2 0.2 1.5", "0 0.5 0.5 0.2 0.2 0.9 3" })
            {
                File.WriteAllText(invalid, row);
                Throws<InvalidDataException>(() => AnnotationReader.CountYoloShapes(invalid), "invalid YOLO data: " + row);
            }
            var stats = StatisticsService.BuildStatsRows(dataset, records);
            Check(stats.Single(s => s.IsTotal).ImageCount == 2 && stats.Single(s => s.IsTotal).BoxCount == 2, "annotation total counts include empty image");
            Check(stats.Single(s => s.LabelName == "细胞").ImageCount == 1, "per-class image count");
            var comparisons = StatisticsService.BuildComparisonStatsRows(records, predictions, 0.5F, dataset);
            var total = comparisons.Single(s => s.IsTotal);
            Check(total.TruePositiveCount == 1 && total.FalsePositiveCount == 1 && total.FalseNegativeCount == 1, "comparison aggregation equals matcher");
            Check(PredictionResolver.LoadPredictionsFromFolder(records[1], predictions, 960, 720).Count == 0, "missing prediction is an empty set");
            string export = Path.Combine(qa, "exports");
            Check(ComparisonExporter.Export(records, predictions, 0.5F, export, CancellationToken.None, null) == 2, "full dataset comparison export");
            Check(Directory.GetFiles(export, "*.jpg", SearchOption.AllDirectories).Length == 4, "both export modes written");
            using (Image output = Image.FromFile(Directory.GetFiles(export, "*.jpg", SearchOption.AllDirectories)[0]))
                Check(output.Width == 960 && output.Height == 720, "export preserves source resolution");
            Check(ComparisonExporter.BuildExportPath(export, "a.jpg") != ComparisonExporter.BuildExportPath(export, "a.png"), "different input extensions cannot collide");
            Throws<InvalidDataException>(() => ComparisonExporter.BuildExportPath(export, "../escape.png"), "export path containment");
            var collisions = new[] { records[0], new ImageRecord(records[0].ImagePath, records[0].AnnotationPath,
                records[0].AnnotationKind, 2, "val/images/sample_001.png", Classes) };
            Throws<InvalidDataException>(() => PredictionResolver.ValidateMapping(collisions, predictions), "ambiguous flat prediction mapping rejected");
            string csv = TableSerializer.Serialize(new[] { new[] { "含,逗号", "引\"号", "=1+1" } }, true);
            Check(csv.Contains("\"含,逗号\"") && csv.Contains("引\"\"号") && csv.Contains("'=1+1"), "CSV quoting and text formula escaping");
            var cancel = new CancellationToken(true);
            Throws<OperationCanceledException>(() => DatasetScanner.BuildRecordsRecursively(dataset, cancel), "scan cancellation");
            Throws<OperationCanceledException>(() => StatisticsService.BuildStatsRows(dataset, records, cancel), "statistics cancellation");
            Throws<OperationCanceledException>(() => ComparisonExporter.Export(records, predictions, 0.5F, export, cancel, null), "export cancellation");
            using (Image loaded = AnnotationReader.LoadImageWithoutLock(records[0].ImagePath))
            using (FileStream access = new FileStream(records[0].ImagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(access.CanWrite, "viewer releases source image file lock");
            using (StatisticsForm dialog = StatisticsForm.Annotations(stats, dataset)) SaveForm(dialog, "statistics.png");
            using (StatisticsForm dialog = StatisticsForm.Comparison(comparisons, dataset, predictions, 0.5F)) SaveForm(dialog, "comparison-statistics.png");
            Check(Assembly.GetExecutingAssembly().GetName().Version.ToString() == "2.0.0.0", "assembly version is 2.0.0");
            string nested = Path.Combine(qa, "nested");
            Directory.CreateDirectory(Path.Combine(nested, "images", "val"));
            Directory.CreateDirectory(Path.Combine(nested, "labels", "val"));
            File.Delete(Path.Combine(nested, "labels", "val", "nested.json"));
            File.Copy(records[0].ImagePath, Path.Combine(nested, "images", "val", "nested.png"), true);
            File.WriteAllText(Path.Combine(nested, "labels", "val", "nested.txt"), "0 0.5 0.5 0.3 0.3");
            File.WriteAllText(Path.Combine(nested, "classes.txt"), "parent-class");
            File.WriteAllText(Path.Combine(nested, "images", "val", "classes.txt"), "child-class");
            var nestedRecords = DatasetScanner.BuildRecordsRecursively(nested);
            Check(nestedRecords.Count == 1 && nestedRecords[0].ClassNames[0] == "child-class", "nested images/val layout uses nearest class mapping");
            Check(PredictionResolver.ResolvePath(nestedRecords[0], nested) == nestedRecords[0].AnnotationPath,
                "hierarchical prediction mapping preserves relative directories");
            File.WriteAllText(json, "{\"shapes\":null}");
            Throws<InvalidDataException>(() => AnnotationReader.CountLabelMeShapes(json), "invalid LabelMe array cannot silently count as zero");
            string splitJson = Path.Combine(nested, "labels", "val", "nested.json");
            File.WriteAllText(splitJson, "{\"shapes\":[{\"label\":\"菌丝\",\"shape_type\":\"rectangle\",\"points\":[[10,20],[50,70]]}]}");
            var splitRecords = DatasetScanner.BuildRecordsRecursively(nested);
            Check(splitRecords.Count == 1 && splitRecords[0].AnnotationKind == AnnotationFormats.LabelMe && splitRecords[0].AnnotationPath == splitJson,
                "JSON in separate labels directory is found and preferred over sibling TXT");
            var directImages = DatasetScanner.BuildRecordsRecursively(Path.Combine(nested, "images"));
            Check(directImages.Count == 1 && directImages[0].AnnotationPath == splitJson, "opening images directory still resolves separate JSON labels");
        }

        private static T Field<T>(ViewerForm form, string name)
        {
            return (T)typeof(ViewerForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }
        private static void Invoke(ViewerForm form, string name, params object[] args)
        {
            typeof(ViewerForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args);
        }
        private static void SaveForm(Form form, string name)
        {
            bool wasVisible = form.Visible;
            if (!wasVisible)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Show();
            }
            form.PerformLayout();
            form.Refresh();
            if (name == "ui-real-dataset.png") File.WriteAllLines(Path.Combine(qa, "layout.txt"), Describe(form, 0));
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(qa, name), ImageFormat.Png);
            }
            if (!wasVisible) form.Hide();
        }

        private static IEnumerable<string> Describe(Control control, int depth)
        {
            var panel = control as TableLayoutPanel;
            yield return new string(' ', depth * 2) + control.GetType().Name + " " + control.Text + " " + control.Bounds +
                " visible=" + control.Visible + " dock=" + control.Dock + " margin=" + control.Margin + " padding=" + control.Padding + (panel == null ? "" : " rows=" + string.Join(",", panel.GetRowHeights().Select(x => x.ToString()).ToArray()));
            foreach (Control child in control.Controls)
                foreach (string line in Describe(child, depth + 1)) yield return line;
        }

        private static async Task TestUi(ViewerForm form)
        {
            CheckEmptyCenter(form);
            form.Size = new Size(1800, 1120);
            await Task.Delay(30);
            CheckEmptyCenter(form);
            SplitContainer split = Field<SplitContainer>(form, "librarySplit");
            int initialDistance = split.SplitterDistance;
            split.SplitterDistance += 100;
            Check(split.SplitterDistance == initialDistance + 100 && !split.IsSplitterFixed, "library divider can be moved");
            CheckEmptyCenter(form);
            split.SplitterDistance = initialDistance;
            SaveForm(form, "ui-empty.png");
            Check(!Field<Button>(form, "exportCompareButton").Enabled, "comparison disabled without prediction directory");
            TextBox address = Field<TextBox>(form, "folderText");
            Check(!address.ReadOnly, "dataset address is editable");
            Button browse = Field<Button>(form, "browseButton");
            Check(address.Parent.Height == browse.Height && address.Parent.Top == browse.Top, "address field and folder buttons have aligned heights");
            await form.OpenFolderAsync(dataset);
            Check(Field<ListBox>(form, "fileList").Items.Count == 2, "async folder load populates library");
            Check(Field<PictureBox>(form, "pictureBox").Image == null, "preview allocates no scaled bitmap");
            SaveForm(form, "ui-annotations.png");
            var search = Field<TextBox>(form, "searchText");
            search.Text = "sample_002";
            Check(Field<ListBox>(form, "fileList").Items.Count == 1 && Field<List<ImageRecord>>(form, "records").Count == 2, "filter leaves full dataset intact");
            search.Focus();
            var arrow = new KeyEventArgs(Keys.Right);
            typeof(Form).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { arrow });
            Check(!arrow.Handled, "arrow keys in search remain text editing keys");
            search.Text = "not-found";
            Check(Field<Image>(form, "sourceImage") == null, "empty search clears image");
            search.Clear();
            Field<TextBox>(form, "predFolderText").Text = predictions;
            Check(Field<Button>(form, "exportCompareButton").Enabled, "valid predictions enable comparison");
            CheckBox keyboardToggle = Field<CheckBox>(form, "gtOnlyCheckBox");
            keyboardToggle.Focus();
            SendMessage(keyboardToggle.Handle, 0x100, new IntPtr(32), IntPtr.Zero);
            SendMessage(keyboardToggle.Handle, 0x101, new IntPtr(32), IntPtr.Zero);
            Check(keyboardToggle.Checked, "switch responds to Space key");
            keyboardToggle.Checked = false;
            NumericUpDown numeric = Field<NumericUpDown>(form, "iouNumeric");
            Check(numeric.Parent.ClientRectangle.Contains(numeric.Bounds) && numeric.Width > 55, "IoU editor stays inside its row at system DPI");
            Field<CheckBox>(form, "errorAnalysisCheckBox").Checked = true;
            SaveForm(form, "ui-comparison.png");
            Field<CheckBox>(form, "gtOnlyCheckBox").Checked = true;
            Check(!Field<CheckBox>(form, "errorAnalysisCheckBox").Checked, "GT-only and error analysis are mutually exclusive");
            Field<TextBox>(form, "predFolderText").Clear();
            Check(!Field<Button>(form, "comparisonStatsButton").Enabled && !Field<CheckBox>(form, "gtOnlyCheckBox").Checked,
                "removing predictions restores annotation browsing");
            for (int i = 0; i < 12; i++) Invoke(form, "SetZoom", i % 2 == 0 ? 8F : 0.5F);
            Check(Field<PictureBox>(form, "pictureBox").Image == null, "repeated 800 percent zoom uses paint rendering");
            Field<CheckBox>(form, "fitCheckBox").Checked = true;
            form.Size = new Size(1120, 720);
            await Task.Delay(50);
            SaveForm(form, "ui-compact.png");
            Check(Field<Panel>(form, "imagePanel").Width > 400, "minimum window retains usable canvas");
            Check(!Field<Panel>(form, "imagePanel").HorizontalScroll.Visible && !Field<Panel>(form, "imagePanel").VerticalScroll.Visible,
                "fit mode removes stale scrollbars after large zoom and window resize");
            address.Text = Path.Combine(qa, "does-not-exist");
            await form.OpenFolderAsync(address.Text);
            Check(Field<string>(form, "loadedFolder") == Path.GetFullPath(dataset) && Field<List<ImageRecord>>(form, "records").Count == 2,
                "invalid edited path preserves the loaded dataset");
            SendKey(address, Keys.Escape);
            Check(address.Text == Path.GetFullPath(dataset), "Escape restores the loaded directory");
            address.Text = "\"" + dataset + "\"";
            SendKey(address, Keys.Enter);
            for (int i = 0; i < 500 && Field<bool>(form, "isBusy"); i++) await Task.Delay(20);
            Check(address.Text == Path.GetFullPath(dataset) && !Field<bool>(form, "isBusy"), "Enter opens and normalizes a typed quoted address");
            Check(Field<CheckBox>(form, "gtOnlyCheckBox") is ToggleSwitch && Field<CheckBox>(form, "errorAnalysisCheckBox") is ToggleSwitch,
                "comparison options use keyboard-accessible toggle switches");
            CheckBox errorToggle = Field<CheckBox>(form, "errorAnalysisCheckBox");
            File.WriteAllLines(Path.Combine(qa, "compact-layout.txt"), Describe(form, 0));
            Check(errorToggle.Parent.ClientRectangle.Contains(errorToggle.Bounds), "error-analysis switch and label stay fully inside inspector");
            form.Size = new Size(1500, 940);
            string realFolder = Path.GetFullPath(Path.Combine(qa, "..", "..", "方框新菌丝"));
            if (Directory.Exists(realFolder))
            {
                string first = Directory.GetFiles(realFolder, "*.json").FirstOrDefault();
                if (first != null)
                {
                    string image = Path.ChangeExtension(first, ".jpg");
                    if (File.Exists(image))
                    {
                        var realRecords = Field<List<ImageRecord>>(form, "records");
                        realRecords.Clear();
                        realRecords.Add(new ImageRecord(image, first, AnnotationFormats.LabelMe,
                            AnnotationReader.CountLabelMeShapes(first), Path.GetFileName(image), Classes));
                        Field<TextBox>(form, "folderText").Text = realFolder;
                        Invoke(form, "ApplyFileFilter");
                        Check(Field<Image>(form, "sourceImage") != null, "existing MedVision LabelMe image loads read-only");
                        SaveForm(form, "ui-real-dataset.png");
                    }
                }
            }
            string train = Path.GetFullPath(Path.Combine(qa, "..", "..", "train"));
            if (Directory.Exists(Path.Combine(train, "images")) && Directory.Exists(Path.Combine(train, "labels")))
            {
                await form.OpenFolderAsync(train);
                await Task.Delay(100);
                var actual = Field<List<ImageRecord>>(form, "records");
                int expected = Directory.GetFiles(Path.Combine(train, "images"), "*.jpg").Count(path =>
                    File.Exists(Path.Combine(train, "labels", Path.GetFileNameWithoutExtension(path) + ".json")));
                Check(actual.Count == expected && expected > 0 && actual.All(r => r.AnnotationKind == AnnotationFormats.LabelMe),
                    "actual MedVision train images/labels JSON pairs all load: " + actual.Count);
                var trainRows = StatisticsService.BuildStatsRows(train, actual);
                Check(trainRows.Where(r => r.IsTotal).Sum(r => r.BoxCount) == actual.Sum(r => r.ShapeCount), "actual train label statistics agree with scan");
                Check(Field<Image>(form, "sourceImage") != null && Field<List<AnnotationShape>>(form, "currentShapes").Count > 0,
                    "actual train image and JSON boxes render");
                ListBox files = Field<ListBox>(form, "fileList");
                Check(files.HorizontalScrollbar && files.HorizontalExtent > files.ClientSize.Width, "long filenames have horizontal scroll extent");
                typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(files,
                    new object[] { new MouseEventArgs(MouseButtons.None, 0, 20, 20, 0) });
                Check(Field<ToolTip>(form, "fileToolTip").GetToolTip(files) == actual[0].DisplayName, "hover reveals full filename");
                SaveForm(form, "ui-train.png");
                SendMessage(files.Handle, 0x114, new IntPtr(7), IntPtr.Zero);
                files.Refresh();
                SaveForm(form, "ui-filename-scrolled.png");
                Check(((FileListBox)files).HorizontalOffset > 0, "horizontal scrollbar moves owner-drawn filenames to reveal their ending");
                SendMessage(files.Handle, 0x114, new IntPtr(6), IntPtr.Zero);
                int old = split.SplitterDistance;
                split.SplitterDistance = Math.Min(split.Width - split.Panel2MinSize - split.SplitterWidth, old + 180);
                SaveForm(form, "ui-library-wide.png");
                split.SplitterDistance = old;
                File.WriteAllText(Path.Combine(qa, "train-results.txt"), "Images: " + actual.Count + "\nJSON annotations: " + actual.Count +
                    "\nBoxes: " + actual.Sum(r => r.ShapeCount));
            }
        }

        private static void SendKey(Control control, Keys key)
        {
            control.Focus();
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { new KeyEventArgs(key) });
        }

        private static void CheckEmptyCenter(ViewerForm form)
        {
            Panel panel = Field<Panel>(form, "imagePanel");
            panel.Refresh();
            using (Bitmap bitmap = new Bitmap(panel.Width, panel.Height))
            {
                panel.DrawToBitmap(bitmap, panel.ClientRectangle);
                int left = bitmap.Width, right = -1, top = bitmap.Height, bottom = -1;
                int color = Color.FromArgb(61, 102, 114).ToArgb();
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                        if (bitmap.GetPixel(x, y).ToArgb() == color)
                        { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                Check(right > left && Math.Abs((left + right) / 2.0 - panel.Width / 2.0) <= 1 &&
                    Math.Abs((top + bottom) / 2.0 - panel.Height / 2.0) <= 1,
                    "empty canvas glyph remains centered after layout at " + panel.Size);
            }
        }
    }
}
