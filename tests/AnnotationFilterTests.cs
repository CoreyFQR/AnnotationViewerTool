using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static partial class Tests
    {
        private static async Task TestAnnotationFilterUi(ViewerForm form)
        {
            string folder = Path.Combine(qa, "annotation-filter-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string source = Path.Combine(dataset, "train", "images", "sample_001.png");
            File.Copy(source, Path.Combine(folder, "a.png"));
            File.Copy(source, Path.Combine(folder, "empty.png"));
            string path = Path.Combine(folder, "a.json");
            File.WriteAllText(path, "{\"shapes\":[" +
                "{\"label\":\"菌丝\",\"shape_type\":\"rectangle\",\"points\":[[50,80],[150,180]]}," +
                "{\"label\":\"细胞\",\"shape_type\":\"rectangle\",\"points\":[[250,80],[350,180]]}," +
                "{\"label\":\"菌丝\",\"shape_type\":\"rectangle\",\"points\":[[450,80],[550,180]]}," +
                "{\"label\":\"细胞核\",\"shape_type\":\"rectangle\",\"points\":[[650,80],[750,180]]}]}");
            File.WriteAllText(Path.Combine(folder, "empty.json"), "{\"shapes\":[]}");
            byte[] original = File.ReadAllBytes(path);
            await form.OpenFolderAsync(folder);
            var filter = Field<ComboBox>(form, "annotationLabelFilter");
            var list = Field<ListView>(form, "annotationList");
            var all = Field<Button>(form, "selectAllAnnotationsButton");
            var none = Field<Button>(form, "selectNoAnnotationsButton");
            var hidden = Field<HashSet<int>>(form, "hiddenAnnotationIndexes");
            var files = Field<ListBox>(form, "fileList");
            Check(filter.SelectedIndex == 0 && filter.Items.Count == 4 && list.Items.Count == 4,
                "label filter defaults to all and lists each current label once");
            await TestDropdownInteraction(form);
            none.PerformClick();
            Check(list.CheckedItems.Count == 0 && hidden.SetEquals(new[] { 0, 1, 2, 3 }),
                "all-label deselect hides every annotation");
            filter.SelectedIndex = filter.FindStringExact("菌丝");
            Check(list.Items.Count == 2 && list.Items[0].Text == "1" && list.Items[1].Text == "3" && list.CheckedItems.Count == 0,
                "filtered rows retain original numbers and unchecked state");
            all.PerformClick();
            Check(list.CheckedItems.Count == 2 && hidden.SetEquals(new[] { 1, 3 }),
                "filtered select-all shows both matching labels without changing other labels");
            list.Items[1].Checked = false;
            Check(hidden.SetEquals(new[] { 1, 2, 3 }), "filtered checkbox updates original annotation index");
            filter.SelectedIndex = 0;
            Check(list.Items[0].Checked && !list.Items[1].Checked && !list.Items[2].Checked && !list.Items[3].Checked,
                "returning to all labels preserves each checkbox state");
            all.PerformClick();
            filter.SelectedIndex = filter.FindStringExact("菌丝");
            none.PerformClick();
            Check(hidden.SetEquals(new[] { 0, 2 }), "filtered deselect-all hides only matching labels");
            filter.SelectedIndex = filter.FindStringExact("细胞");
            Check(list.Items.Count == 1 && list.Items[0].Text == "2" && list.Items[0].Checked,
                "label filtering is exact and does not include similar label names");
            list.Items[0].Selected = true;
            Check(Field<int>(form, "selectedAnnotationIndex") == 1,
                "filtered row selection targets original annotation for editing");
            filter.SelectedIndex = filter.FindStringExact("菌丝");
            Check(Field<int>(form, "selectedAnnotationIndex") == -1,
                "filtering out the selected row clears the deletion target");
            float zoom = Field<float>(form, "zoom");
            Invoke(form, "SelectCanvasAnnotation", new Point((int)(300 * zoom), (int)(130 * zoom)));
            Check(filter.SelectedIndex == 0 && list.SelectedItems.Count == 1 && list.SelectedItems[0].Text == "2",
                "canvas selection outside filter reveals the correct original row");
            Check(File.ReadAllBytes(path).SequenceEqual(original) &&
                StatisticsService.BuildStatsRows(folder, Field<List<ImageRecord>>(form, "records")).Single(r => r.IsTotal).BoxCount == 4,
                "label filtering and bulk visibility do not change files or statistics");
            filter.SelectedIndex = filter.FindStringExact("细胞");
            filter.Focus();
            var right = new KeyEventArgs(Keys.Right);
            typeof(Form).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(form, new object[] { right });
            Check(!right.Handled && files.SelectedIndex == 0, "arrow keys in label filter never switch images");
            SaveForm(form, "ui-annotation-filter.png");
            list.Items[0].Selected = true;
            list.Focus();
            FormKey(form, Keys.Delete);
            await WaitForEdit(form);
            Check(AnnotationReader.LoadLabelMeAnnotations(path).Select(s => s.Label).SequenceEqual(new[] { "菌丝", "菌丝", "细胞核" }),
                "Delete on a filtered row removes the intended original annotation only");
            FormKey(form, Keys.Control | Keys.Z);
            await WaitForEdit(form);
            Check(File.ReadAllBytes(path).SequenceEqual(original) && list.Items.Count == 4,
                "undo restores exact annotation bytes after filtered deletion");
            filter.SelectedIndex = filter.FindStringExact("菌丝");
            none.PerformClick();
            files.SelectedIndex = 1;
            Check(!filter.Enabled && !all.Enabled && !none.Enabled && list.Items.Count == 0 && filter.Items.Count == 1,
                "empty image clears label options and disables bulk actions");
            files.SelectedIndex = 0;
            Check(filter.SelectedIndex == 0 && list.CheckedItems.Count == 4 && hidden.Count == 0,
                "new image restores all labels and default visibility");
            Size before = form.Size;
            form.Size = new Size(1120, 720);
            form.PerformLayout();
            Check(all.Parent.ClientRectangle.Contains(all.Bounds) && none.Parent.ClientRectangle.Contains(none.Bounds) &&
                filter.Parent.ClientRectangle.Contains(filter.Bounds) && list.Height >= list.Font.Height * 2,
                "filter and bulk actions fit the minimum window with readable annotation rows");
            SaveForm(form, "ui-annotation-filter-compact.png");
            form.Size = before;
        }
    }
}
