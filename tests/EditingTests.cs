using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static partial class Tests
    {
        private static string MakeEditFixture()
        {
            string folder = Path.Combine(qa, "edit-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            string image = Directory.GetFiles(dataset, "*.png", SearchOption.AllDirectories).First();
            foreach (string name in new[] { "a", "b", "sub/c" }) File.Copy(image, Path.Combine(folder, name + ".png"));
            string json = "{\"version\":\"5.6\",\"flags\":{\"reviewed\":true},\"imagePath\":\"a.png\",\"imageData\":\"abc\",\"custom\":{\"note\":\"中文\"},\"shapes\":[" +
                "{\"label\":\"cell\",\"shape_type\":\"rectangle\",\"points\":[[80,90],[280,290]],\"flags\":{\"difficult\":true},\"group_id\":7}," +
                "{\"label\":\"fungus\",\"shape_type\":\"polygon\",\"points\":[[500,400],[700,400],[600,600]],\"description\":\"保留字段\"}]}";
            File.WriteAllText(Path.Combine(folder, "a.json"), json, new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(folder, "sub/c.json"), json, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "b.txt"), " 0 0.2 0.2 0.2 0.2 0.95\r\n\r\n\t1 0.6 0.6 0.3 0.3 0.83", new UTF8Encoding(true));
            File.WriteAllLines(Path.Combine(folder, "classes.txt"), new[] { "cell", "fungus" });
            return folder;
        }

        private static void TestEditServices()
        {
            string folder = MakeEditFixture();
            var records = DatasetScanner.BuildRecordsRecursively(folder);
            var json = records.Single(r => Path.GetFileName(r.ImagePath) == "a.png");
            var txt = records.Single(r => Path.GetFileName(r.ImagePath) == "b.png");
            byte[] original = File.ReadAllBytes(json.AnnotationPath);
            byte[] txtOriginal = File.ReadAllBytes(txt.AnnotationPath);
            var remove = AnnotationEditor.RemoveShapes(json, new[] { 0 }, original);
            var transaction = EditTransaction.Apply(folder, new List<FileChange> { remove }, CancellationToken.None);
            var document = AnnotationReader.LoadJsonObject(json.AnnotationPath);
            Check(AnnotationReader.CountLabelMeShapes(json.AnnotationPath) == 1 && (string)document["imageData"] == "abc" &&
                (bool)((Dictionary<string, object>)document["flags"])["reviewed"] &&
                (string)((Dictionary<string, object>)document["custom"])["note"] == "中文", "JSON deletion preserves document metadata and image data");
            var shape = (Dictionary<string, object>)((object[])document["shapes"])[0];
            Check((string)shape["description"] == "保留字段" && ((object[])shape["points"]).Length == 3,
                "surviving polygon keeps custom attributes and all points");
            Check(File.ReadAllBytes(Path.Combine(transaction.BackupFolder, "0.original")).SequenceEqual(original) &&
                File.Exists(Path.Combine(transaction.BackupFolder, "manifest.json")), "durable backup includes original bytes and path manifest");
            transaction.Undo();
            Check(File.ReadAllBytes(json.AnnotationPath).SequenceEqual(original), "undo restores JSON bytes including formatting and BOM");
            var txtTransaction = EditTransaction.Apply(folder, new List<FileChange> {
                AnnotationEditor.RemoveShapes(txt, new[] { 0 }, txtOriginal) }, CancellationToken.None);
            byte[] expectedTxt = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("\r\n\t1 0.6 0.6 0.3 0.3 0.83")).ToArray();
            Check(File.ReadAllBytes(txt.AnnotationPath).SequenceEqual(expectedTxt), "TXT deletion preserves BOM, CRLF, whitespace, confidence and no final newline");
            Check(File.ReadAllLines(Path.Combine(folder, "classes.txt"))[1] == "fungus" &&
                AnnotationReader.LoadAnnotations(txt, 960, 720)[0].ClassId == 1, "deleting a class never renumbers remaining YOLO IDs");
            txtTransaction.Undo();
            Check(File.ReadAllBytes(txt.AnnotationPath).SequenceEqual(txtOriginal), "TXT undo restores exact source");
            Throws<InvalidOperationException>(() => AnnotationEditor.RemoveShapes(json, new[] { 9 }, original), "stale annotation index rejected");
            File.AppendAllText(json.AnnotationPath, " ");
            Throws<IOException>(() => AnnotationEditor.RemoveShapes(json, new[] { 0 }, original), "external edits since canvas load cannot be overwritten");
            File.WriteAllBytes(json.AnnotationPath, original);

            var plan = new List<FileChange> { remove, AnnotationEditor.RemoveShapes(txt, new[] { 0 }, txtOriginal) };
            Throws<IOException>(() => EditTransaction.Apply(folder, plan, CancellationToken.None,
                i => { if (i == 1) throw new IOException("Injected write failure"); }), "failed multi-file write propagates error");
            Check(File.ReadAllBytes(json.AnnotationPath).SequenceEqual(original) && File.ReadAllBytes(txt.AnnotationPath).SequenceEqual(txtOriginal),
                "partial batch failure restores every already-written annotation");
            using (var cancellation = new CancellationTokenSource())
            {
                Throws<OperationCanceledException>(() => EditTransaction.Apply(folder, plan, cancellation.Token,
                    i => { if (i == 0) cancellation.Cancel(); }), "batch cancellation interrupts before next file");
                Check(File.ReadAllBytes(json.AnnotationPath).SequenceEqual(original), "cancelled batch rolls back earlier writes");
            }
            transaction = EditTransaction.Apply(folder, new List<FileChange> { remove }, CancellationToken.None);
            File.AppendAllText(json.AnnotationPath, " ");
            Throws<IOException>(() => transaction.Undo(), "undo refuses to overwrite later external edits");
            File.WriteAllBytes(json.AnnotationPath, remove.After);
            transaction.Undo();

            var stats = StatisticsService.BuildStatsRows(folder, records);
            var selected = stats.Single(r => r.LabelName == "cell" && r.FolderName == StatisticsService.BuildStatsFolderName(folder, json.ImagePath));
            Throws<InvalidOperationException>(() => AnnotationEditor.RemoveCategories(folder, records, stats.Where(r => r.IsTotal).ToList(), CancellationToken.None),
                "service rejects totals category independently of UI");
            var categories = AnnotationEditor.RemoveCategories(folder, records, new[] { selected }, CancellationToken.None);
            Check(categories.Count == 2, "category batch includes JSON and TXT only in the selected folder");
            transaction = EditTransaction.Apply(folder, categories, CancellationToken.None);
            Check(AnnotationReader.CountLabelMeShapes(json.AnnotationPath) == 1 && AnnotationReader.CountYoloShapes(txt.AnnotationPath) == 1 &&
                AnnotationReader.CountLabelMeShapes(Path.Combine(folder, "sub/c.json")) == 2, "same category in unselected folder is preserved");
            Check(DatasetScanner.BuildRecordsRecursively(folder).Count == 3, "backup folder is excluded from dataset scan");
            transaction.Undo();
            var empty = EditTransaction.Apply(folder, new List<FileChange> { AnnotationEditor.RemoveShapes(json, new[] { 0, 1 }, original),
                AnnotationEditor.RemoveShapes(txt, new[] { 0, 1 }, txtOriginal) }, CancellationToken.None);
            Check(AnnotationReader.CountLabelMeShapes(json.AnnotationPath) == 0 && AnnotationReader.CountYoloShapes(txt.AnnotationPath) == 0 &&
                DatasetScanner.BuildRecordsRecursively(folder).Count == 3, "deleting last annotation retains image and valid empty label file");
            empty.Undo();
            byte[] originalImage = File.ReadAllBytes(json.ImagePath);
            var pair = AnnotationEditor.RemoveImage(json, records);
            Throws<IOException>(() => EditTransaction.Apply(folder, pair, CancellationToken.None,
                i => { if (i == 1) throw new IOException("Injected label failure"); }), "image pair failure propagates error");
            Check(File.ReadAllBytes(json.ImagePath).SequenceEqual(originalImage) && File.ReadAllBytes(json.AnnotationPath).SequenceEqual(original),
                "image pair failure restores deleted image");
            transaction = EditTransaction.Apply(folder, pair, CancellationToken.None);
            Check(!File.Exists(json.ImagePath) && !File.Exists(json.AnnotationPath), "image deletion removes exactly its image and paired annotation");
            transaction.Undo();
            Check(File.ReadAllBytes(json.ImagePath).SequenceEqual(originalImage) && File.ReadAllBytes(json.AnnotationPath).SequenceEqual(original),
                "image pair undo restores both original files");
            var shared = records.Concat(new[] { new ImageRecord(Path.ChangeExtension(json.ImagePath, ".jpg"), json.AnnotationPath,
                json.AnnotationKind, 2, "a.jpg", json.ClassNames) }).ToList();
            Throws<InvalidOperationException>(() => AnnotationEditor.RemoveImage(json, shared), "shared annotation cannot be deleted with one image");
        }

        private static async Task WaitForEdit(ViewerForm form)
        {
            for (int i = 0; i < 500 && Field<bool>(form, "isBusy"); i++) await Task.Delay(20);
            Check(!Field<bool>(form, "isBusy"), "edit finishes without blocking the UI");
        }

        private static void FormKey(ViewerForm form, Keys key)
        { typeof(Form).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { new KeyEventArgs(key) }); }

        private static async Task TestEditingUi(ViewerForm form)
        {
            string folder = MakeEditFixture();
            await form.OpenFolderAsync(folder);
            var list = Field<ListView>(form, "annotationList");
            var files = Field<ListBox>(form, "fileList");
            var picture = Field<PictureBox>(form, "pictureBox");
            var canvas = Field<Panel>(form, "imagePanel");
            var label = Field<Label>(form, "zoomLabel");
            foreach (var size in new[] { new Size(1800, 1120), new Size(1120, 720), new Size(1500, 940) })
            {
                form.Size = size;
                foreach (float scale in new[] { 0.005F, 1F, 8F })
                {
                    Invoke(form, "SetZoom", scale);
                    var table = label.Parent as TableLayoutPanel;
                    Check(table != null && table.GetColumn(label) == 1 && label.TextAlign == ContentAlignment.MiddleRight &&
                        label.Right + label.Margin.Right == table.ClientSize.Width && label.Parent.ClientRectangle.Contains(label.Bounds),
                        "zoom stays at right edge for " + size + " / " + scale);
                }
            }
            Field<Button>(form, "fitButton").PerformClick();
            float zoom = Field<float>(form, "zoom");
            typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(picture,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, (int)(180 * zoom), (int)(180 * zoom), 0) });
            Check(list.SelectedIndices.Count == 1 && list.SelectedIndices[0] == 0 && canvas.ContainsFocus,
                "canvas click focuses editing surface and selects matching annotation row");
            SaveForm(form, "ui-selected-annotation.png");
            list.Items[0].Selected = false; list.Items[1].Selected = true;
            Check(Field<int>(form, "selectedAnnotationIndex") == 1, "right list selection updates highlighted canvas shape");
            Invoke(form, "SelectCanvasAnnotation", new Point((int)(20 * zoom), (int)(20 * zoom)));
            Check(list.SelectedIndices.Count == 0 && Field<int>(form, "selectedAnnotationIndex") == -1, "blank canvas clears selection");
            Invoke(form, "SelectCanvasAnnotation", new Point((int)(180 * zoom), (int)(180 * zoom)));
            var search = Field<TextBox>(form, "searchText");
            search.Focus(); FormKey(form, Keys.Delete);
            Check(AnnotationReader.CountLabelMeShapes(Path.Combine(folder, "a.json")) == 2, "Delete in text editor never deletes a selected annotation");
            canvas.Focus(); FormKey(form, Keys.Delete); await WaitForEdit(form);
            Check(AnnotationReader.CountLabelMeShapes(Path.Combine(folder, "a.json")) == 1 && list.Items.Count == 1 &&
                File.Exists(Path.Combine(folder, "a.png")), "canvas Delete writes selected annotation removal and refreshes list");
            Check(Field<int>(form, "selectedAnnotationIndex") == -1, "after deletion another box is not silently selected");
            FormKey(form, Keys.Control | Keys.Z); await WaitForEdit(form);
            Check(list.Items.Count == 2, "Ctrl+Z restores annotation and list");
            list.Focus(); list.Items[1].Selected = true;
            FormKey(form, Keys.Delete); await WaitForEdit(form);
            Check(AnnotationReader.LoadLabelMeAnnotations(Path.Combine(folder, "a.json")).Single().Label == "cell", "Delete from right list removes exact polygon");
            await (Task)typeof(ViewerForm).GetMethod("UndoEditAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            files.Focus(); FormKey(form, Keys.Delete); await WaitForEdit(form);
            Check(!File.Exists(Path.Combine(folder, "a.png")) && !File.Exists(Path.Combine(folder, "a.json")) && files.Items.Count == 2,
                "library Delete removes image pair and refreshes navigation");
            FormKey(form, Keys.Control | Keys.Z); await WaitForEdit(form);
            Check(File.Exists(Path.Combine(folder, "a.png")) && File.Exists(Path.Combine(folder, "a.json")) && files.Items.Count == 3,
                "Ctrl+Z restores deleted image pair into library");
            var stats = StatisticsService.BuildStatsRows(folder, Field<List<ImageRecord>>(form, "records"));
            IList<StatsRow> submitted = null;
            using (var dialog = StatisticsForm.Annotations(stats, folder, async selected => {
                submitted = selected;
                return await (Task<IList<StatsRow>>)typeof(ViewerForm).GetMethod("DeleteCategoriesAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { selected });
            }))
            {
                dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000); dialog.ShowInTaskbar = false; dialog.Show(form);
                var grid = (DataGridView)typeof(StatisticsForm).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                var delete = (Button)typeof(StatisticsForm).GetField("deleteButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                grid.Sort(grid.Columns[4], System.ComponentModel.ListSortDirection.Descending);
                grid.ClearSelection();
                grid.Rows.Cast<DataGridViewRow>().First(r => ((StatsRow)r.Tag).IsTotal).Selected = true;
                Check(!delete.Enabled, "statistics totals row disables deletion even after sorting");
                grid.ClearSelection();
                string scope = StatisticsService.BuildStatsFolderName(folder, Path.Combine(folder, "a.png"));
                var row = grid.Rows.Cast<DataGridViewRow>().Single(r => ((StatsRow)r.Tag).LabelName == "cell" && ((StatsRow)r.Tag).FolderName == scope);
                row.Selected = true;
                Check(delete.Enabled && delete.Height == delete.Parent.Controls[0].Height, "category deletion button enabled and aligned with other actions");
                SaveForm(dialog, "ui-category-delete.png");
                delete.PerformClick();
                for (int i = 0; i < 500 && !grid.Enabled; i++) await Task.Delay(20);
                Check(grid.Enabled && submitted.Count == 1 && submitted[0].FolderName == scope && submitted[0].LabelName == "cell",
                    "sorted statistics deletes the selected row identity");
                Check(!grid.Rows.Cast<DataGridViewRow>().Any(r => ((StatsRow)r.Tag).FolderName == scope && ((StatsRow)r.Tag).LabelName == "cell") &&
                    AnnotationReader.CountLabelMeShapes(Path.Combine(folder, "sub/c.json")) == 2 && files.Items.Count == 3,
                    "category deletion refreshes stats, preserves images and other folders");
                dialog.Close();
            }
            await (Task)typeof(ViewerForm).GetMethod("UndoEditAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            Check(AnnotationReader.CountLabelMeShapes(Path.Combine(folder, "a.json")) == 2 && AnnotationReader.CountYoloShapes(Path.Combine(folder, "b.txt")) == 2,
                "category batch can be undone as one operation");

            search.Text = "a.png";
            string predictionFolder = Path.Combine(folder, ".test-predictions");
            Directory.CreateDirectory(predictionFolder);
            string predictionPath = Path.Combine(predictionFolder, "a.txt");
            File.WriteAllText(predictionPath, "0 0.1875 0.263889 0.208333 0.277778\n1 0.9 0.1 0.05 0.05");
            byte[] predictionBytes = File.ReadAllBytes(predictionPath);
            Field<TextBox>(form, "predFolderText").Text = predictionFolder;
            Field<CheckBox>(form, "errorAnalysisCheckBox").Checked = true;
            zoom = Field<float>(form, "zoom");
            Invoke(form, "SelectCanvasAnnotation", new Point((int)(180 * zoom), (int)(180 * zoom)));
            Check(Field<int>(form, "selectedAnnotationIndex") == 0, "error view TP box selects its matched GT annotation");
            Invoke(form, "SelectCanvasAnnotation", new Point((int)(864 * zoom), (int)(72 * zoom)));
            Check(Field<int>(form, "selectedAnnotationIndex") == -1, "prediction-only FP cannot select a GT deletion target");
            Invoke(form, "SelectCanvasAnnotation", new Point((int)(180 * zoom), (int)(180 * zoom)));
            FormKey(form, Keys.Delete); await WaitForEdit(form);
            Check(search.Text == "a.png" && files.Items.Count == 1 &&
                Field<TextBox>(form, "predFolderText").Text == predictionFolder && File.ReadAllBytes(predictionPath).SequenceEqual(predictionBytes),
                "editing preserves search, prediction setting and original prediction bytes");
            await (Task)typeof(ViewerForm).GetMethod("UndoEditAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            search.Clear(); Field<TextBox>(form, "predFolderText").Clear();
            while (files.Items.Count > 0)
            {
                files.Focus(); FormKey(form, Keys.Delete); await WaitForEdit(form);
            }
            Check(Field<Image>(form, "sourceImage") == null && list.Items.Count == 0, "deleting last image clears preview and annotation selection");
            await (Task)typeof(ViewerForm).GetMethod("UndoEditAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            Check(files.Items.Count == 1 && files.SelectedIndex == 0 && Field<Image>(form, "sourceImage") != null,
                "undo from empty dataset restores a selected, usable image");
        }
    }
}
