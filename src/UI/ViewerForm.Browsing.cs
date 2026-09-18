using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;


namespace MedVision.AnnotationViewer
{
    internal sealed partial class ViewerForm : Form
    {
        private void SearchTextKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && fileList.Items.Count > 0)
            {
                fileList.Focus();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape && searchText.TextLength > 0)
            {
                searchText.Clear();
                e.SuppressKeyPress = true;
            }
        }

        private void ApplyFileFilter()
        {
            ImageRecord previousRecord = GetSelectedRecord();
            string searchTerm = searchText.Text.Trim();
            clearSearchButton.Enabled = searchTerm.Length > 0;

            visibleRecords.Clear();
            foreach (ImageRecord record in records)
            {
                if (MatchesSearch(record, searchTerm))
                {
                    visibleRecords.Add(record);
                }
            }

            int selectedIndex = previousRecord == null ? -1 : visibleRecords.IndexOf(previousRecord);
            if (selectedIndex < 0 && visibleRecords.Count > 0)
            {
                selectedIndex = 0;
            }

            changingSelection = true;
            fileList.BeginUpdate();
            try
            {
                fileList.Items.Clear();
                foreach (ImageRecord record in visibleRecords)
                {
                    fileList.Items.Add(FormatFileListItem(record));
                }
                fileList.HorizontalExtent = visibleRecords.Count == 0 ? 0 : visibleRecords.Max(record =>
                    TextRenderer.MeasureText(record.DisplayName, fileList.Font).Width + 32);
                hoveredFile = -1;
                fileToolTip.SetToolTip(fileList, null);

                fileList.SelectedIndex = selectedIndex;
            }
            finally
            {
                fileList.EndUpdate();
                changingSelection = false;
            }

            UpdateFileInfo();
            if (selectedIndex >= 0)
            {
                LoadSelectedRecord();
            }
            else
            {
                ClearCurrentImage();
                annotationList.Items.Clear();
                if (records.Count > 0)
                {
                    SetStatus("没有找到匹配的文件名。可输入图片名或 JSON/TXT 标注文件名的一部分。");
                }
            }
        }

        private static bool MatchesSearch(ImageRecord record, string searchTerm)
        {
            if (searchTerm.Length == 0)
            {
                return true;
            }

            return ContainsIgnoreCase(record.DisplayName, searchTerm) ||
                ContainsIgnoreCase(Path.GetFileName(record.ImagePath), searchTerm) ||
                ContainsIgnoreCase(Path.GetFileName(record.AnnotationPath), searchTerm) ||
                ContainsIgnoreCase(record.AnnotationPath, searchTerm);
        }

        private static bool ContainsIgnoreCase(string value, string searchTerm)
        {
            return !string.IsNullOrEmpty(value) &&
                value.IndexOf(searchTerm, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static string FormatFileListItem(ImageRecord record)
        {
            return string.Format("{0}  [{1}]  ({2})", record.DisplayName, record.AnnotationKind, record.ShapeCount);
        }

        private void UpdateFileInfo()
        {
            if (searchText.Text.Trim().Length == 0)
            {
                int totalBoxes = records.Sum(record => record.ShapeCount);
                fileInfoLabel.Text = string.Format("{0} 张图 / {1} 个框", records.Count, totalBoxes);
                return;
            }

            int visibleBoxes = visibleRecords.Sum(record => record.ShapeCount);
            fileInfoLabel.Text = string.Format(
                "筛选 {0}/{1} 张图 / {2} 个框",
                visibleRecords.Count,
                records.Count,
                visibleBoxes);
        }

        private ImageRecord GetSelectedRecord()
        {
            if (fileList.SelectedIndex < 0 || fileList.SelectedIndex >= visibleRecords.Count)
            {
                return null;
            }

            return visibleRecords[fileList.SelectedIndex];
        }

        private async void BrowseForFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择包含图片与 LabelMe JSON / YOLO TXT 的数据文件夹";
                dialog.SelectedPath = Directory.Exists(folderText.Text) ? folderText.Text : Environment.CurrentDirectory;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    await OpenFolderAsync(dialog.SelectedPath);
                }
            }
        }

        private void BrowseForPredictionFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 YOLO 预测 labels 文件夹，或包含 labels 子目录的预测结果文件夹";
                dialog.SelectedPath = Directory.Exists(predFolderText.Text) ? predFolderText.Text : Environment.CurrentDirectory;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    predFolderText.Text = dialog.SelectedPath;
                }
            }
        }

        private void OnPredictionFolderChanged()
        {
            try
            {
                resolvedPredictionFolder = PredictionResolver.ResolvePredictionLabelFolder(predFolderText.Text.Trim());
                PredictionResolver.ValidateMapping(records, resolvedPredictionFolder);
                UpdatePredictionControlsState();
                ReloadCurrentPredictions();
                RenderCurrent();
                UpdateCurrentStatus();
            }
            catch (Exception ex)
            {
                resolvedPredictionFolder = null;
                currentPredShapes.Clear();
                UpdatePredictionControlsState();
                RenderCurrent();
                SetStatus("预测目录未加载：" + ex.Message);
            }
        }

        private void UpdatePredictionControlsState()
        {
            bool hasPredictionFolder = HasPredictionFolder();
            clearPredButton.Enabled = predFolderText.TextLength > 0;
            if (!hasPredictionFolder)
            {
                if (gtOnlyCheckBox.Checked)
                {
                    gtOnlyCheckBox.Checked = false;
                }

                if (errorAnalysisCheckBox.Checked)
                {
                    errorAnalysisCheckBox.Checked = false;
                }
            }

            iouLabel.Enabled = hasPredictionFolder;
            iouNumeric.Enabled = hasPredictionFolder;
            gtOnlyCheckBox.Enabled = hasPredictionFolder;
            errorAnalysisCheckBox.Enabled = hasPredictionFolder;
            comparisonStatsButton.Enabled = hasPredictionFolder;
            exportCompareButton.Enabled = hasPredictionFolder;
        }

        private bool HasPredictionFolder()
        {
            return !string.IsNullOrEmpty(resolvedPredictionFolder) && Directory.Exists(resolvedPredictionFolder);
        }

        private void LoadSelectedRecord()
        {
            ImageRecord record = GetSelectedRecord();
            if (record == null)
            {
                return;
            }

            try
            {
                ClearCurrentImage();
                annotationList.Items.Clear();
                sourceImage = AnnotationReader.LoadImageWithoutLock(record.ImagePath);
                currentAnnotationBytes = File.ReadAllBytes(record.AnnotationPath);
                currentShapes = AnnotationReader.LoadAnnotations(record, sourceImage.Width, sourceImage.Height);
                EditTransaction.Verify(record.AnnotationPath, currentAnnotationBytes);
                currentPredShapes = LoadPredictionsForRecord(record, sourceImage.Width, sourceImage.Height);
                PopulateAnnotationList(currentShapes);

                if (autoFit)
                {
                    FitToWindow();
                }
                else
                {
                    RenderCurrent();
                }

                SetStatus(string.Format("{0}/{1}  {2}  |  {3}  |  {4} 个标注框",
                    fileList.SelectedIndex + 1,
                    visibleRecords.Count,
                    record.DisplayName,
                    record.AnnotationKind,
                    currentShapes.Count));
                UpdateCurrentStatus();
            }
            catch (Exception ex)
            {
                ClearCurrentImage();
                annotationList.Items.Clear();
                SetStatus("加载失败：" + Path.GetFileName(record.ImagePath) + " - " + ex.Message);
            }
        }

        private void ReloadCurrentPredictions()
        {
            currentPredShapes = new List<AnnotationShape>();
            if (sourceImage == null)
            {
                return;
            }

            ImageRecord record = GetSelectedRecord();
            if (record == null)
            {
                return;
            }

            currentPredShapes = LoadPredictionsForRecord(record, sourceImage.Width, sourceImage.Height);
        }

        private List<AnnotationShape> LoadPredictionsForRecord(ImageRecord record, int imageWidth, int imageHeight)
        {
            string predLabelFolder = resolvedPredictionFolder;
            if (string.IsNullOrEmpty(predLabelFolder) || !Directory.Exists(predLabelFolder))
            {
                return new List<AnnotationShape>();
            }

            return PredictionResolver.LoadPredictionsFromFolder(record, predLabelFolder, imageWidth, imageHeight);
        }

        private void UpdateCurrentStatus()
        {
            ImageRecord record = GetSelectedRecord();
            if (record == null)
            {
                return;
            }
            string compareText = string.Empty;
            string predLabelFolder = resolvedPredictionFolder;
            if (!gtOnlyCheckBox.Checked && !string.IsNullOrEmpty(predLabelFolder) && Directory.Exists(predLabelFolder))
            {
                MatchResult match = DetectionMatcher.MatchDetections(currentShapes, currentPredShapes, (float)iouNumeric.Value);
                compareText = string.Format(
                    "  |  Pred {0}  |  TP {1} FP {2} FN {3} @ IoU {4:0.00}  |  {5}",
                    currentPredShapes.Count,
                    match.TruePositiveCount,
                    match.FalsePositiveCount,
                    match.FalseNegativeCount,
                    (float)iouNumeric.Value,
                    errorAnalysisCheckBox.Checked ? "错误分析" : "GT+Pred");
            }

            SetStatus(string.Format(
                "{0}/{1}  {2}  |  {3}  |  GT {4}{5}",
                fileList.SelectedIndex + 1,
                visibleRecords.Count,
                record.DisplayName,
                record.AnnotationKind,
                currentShapes.Count,
                compareText));
        }

        private void PopulateAnnotationList(IList<AnnotationShape> shapes)
        {
            annotationList.Items.Clear();
            for (int i = 0; i < shapes.Count; i++)
            {
                AnnotationShape shape = shapes[i];
                ListViewItem item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(shape.Label);
                item.SubItems.Add(shape.ShapeType);
                RectangleF? bounds = shape.Bounds;
                item.SubItems.Add(bounds.HasValue
                    ? string.Format("{0:0},{1:0},{2:0},{3:0}", bounds.Value.X, bounds.Value.Y, bounds.Value.Width, bounds.Value.Height)
                      : "-");
                item.ToolTipText = string.Join("  ·  ", item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text).ToArray());
                annotationList.Items.Add(item);
            }
        }

        private void MoveSelection(int direction)
        {
            if (visibleRecords.Count == 0)
            {
                return;
            }

            int next = Math.Max(0, Math.Min(visibleRecords.Count - 1, fileList.SelectedIndex + direction));
            if (next != fileList.SelectedIndex)
            {
                fileList.SelectedIndex = next;
            }
        }

        private void ClearCurrentImage()
        {
            selectedAnnotationIndex = -1;
            currentAnnotationBytes = null;
            pictureBox.Visible = false;
            canvasTitle.Text = "标注查看";
            annotationCount.Text = "当前标注";
            zoomLabel.Text = "—";
            imagePanel.AutoScrollPosition = Point.Empty;
            imagePanel.Invalidate();
            Image oldPicture = pictureBox.Image;
            pictureBox.Image = null;
            if (oldPicture != null)
            {
                oldPicture.Dispose();
            }

            if (sourceImage != null)
            {
                sourceImage.Dispose();
                sourceImage = null;
            }

            currentShapes = new List<AnnotationShape>();
            currentPredShapes = new List<AnnotationShape>();
        }

        private void SetStatus(string text)
        {
            statusLabel.Text = text;
        }

        private static Icon LoadWindowIcon()
        {
            try
            {
                Icon associatedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (associatedIcon != null)
                {
                    return associatedIcon;
                }
            }
            catch
            {
            }

            string[] candidates = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AnnotationViewer.ico"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "AnnotationViewer.ico"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "AnnotationViewer.ico")
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    string fullPath = Path.GetFullPath(candidate);
                    if (File.Exists(fullPath))
                    {
                        return new Icon(fullPath);
                    }
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
