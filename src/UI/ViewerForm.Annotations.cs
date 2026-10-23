using System;
using System.Linq;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal sealed partial class ViewerForm
    {
        private readonly ComboBox annotationLabelFilter = new ModernComboBox();
        private readonly Button selectAllAnnotationsButton = new ModernButton();
        private readonly Button selectNoAnnotationsButton = new ModernButton();
        private bool updatingAnnotationList;

        private sealed class AnnotationLabelOption
        {
            public readonly string Label;
            public AnnotationLabelOption(string label) { Label = label; }
            public override string ToString()
            {
                if (Label == null) return "全部标签";
                if (Label.Length == 0) return "（空标签）";
                return Label == "全部标签" ? "标签：" + Label : Label;
            }
        }

        private void ResetAnnotationLabelFilter()
        {
            updatingAnnotationList = true;
            annotationLabelFilter.BeginUpdate();
            try
            {
                annotationLabelFilter.Items.Clear();
                annotationLabelFilter.Items.Add(new AnnotationLabelOption(null));
                foreach (string label in currentShapes.Select(s => s.Label ?? string.Empty)
                    .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
                    annotationLabelFilter.Items.Add(new AnnotationLabelOption(label));
                annotationLabelFilter.SelectedIndex = 0;
                annotationLabelFilter.Enabled = currentShapes.Count > 0;
                selectAllAnnotationsButton.Enabled = selectNoAnnotationsButton.Enabled = currentShapes.Count > 0;
            }
            finally
            {
                annotationLabelFilter.EndUpdate();
                updatingAnnotationList = false;
            }
        }

        private bool MatchesAnnotationLabel(AnnotationShape shape)
        {
            var option = annotationLabelFilter.SelectedItem as AnnotationLabelOption;
            return option == null || option.Label == null ||
                string.Equals(shape.Label ?? string.Empty, option.Label, StringComparison.Ordinal);
        }

        private static int AnnotationIndex(ListViewItem item)
        {
            return item != null && item.Tag is int ? (int)item.Tag : -1;
        }

        private void SetFilteredAnnotationsChecked(bool isChecked)
        {
            annotationList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in annotationList.Items) item.Checked = isChecked;
            }
            finally { annotationList.EndUpdate(); }
            pictureBox.Invalidate();
        }

        private void UpdateAnnotationCount()
        {
            annotationCount.Text = sourceImage == null ? "当前标注" : "当前标注 · " +
                (annotationLabelFilter.SelectedIndex > 0 ? annotationList.Items.Count + "/" : string.Empty) + currentShapes.Count;
            fileToolTip.SetToolTip(annotationCount, annotationCount.Text);
        }
    }
}
