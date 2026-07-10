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

[assembly: AssemblyTitle("AnnotationViewer")]
[assembly: AssemblyDescription("MedVision annotation visualizer")]
[assembly: AssemblyCompany("MedVision")]
[assembly: AssemblyProduct("MedVision Annotation Viewer")]
[assembly: AssemblyVersion("1.5.2.0")]
[assembly: AssemblyFileVersion("1.5.2.0")]
[assembly: AssemblyInformationalVersion("1.5.2")]

namespace MedVision.AnnotationViewer
{
    internal static class Program
    {
        public const string AppVersion = "1.5.2";

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (args != null && args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
                {
                    using (ViewerForm form = new ViewerForm())
                    {
                        Console.WriteLine("SELF_TEST_OK");
                    }

                    return;
                }

                Application.Run(new ViewerForm());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STARTUP_ERROR_TYPE: " + ex.GetType().FullName);
                Console.Error.WriteLine("STARTUP_ERROR_MESSAGE: " + ex.Message);
                Console.Error.WriteLine("STARTUP_ERROR_STACK: " + ex.StackTrace);
                throw;
            }
        }
    }

    internal sealed class ViewerForm : Form
    {
        private readonly TextBox folderText = new TextBox();
        private readonly Button browseButton = new Button();
        private readonly Button reloadButton = new Button();
        private readonly Button statsButton = new Button();
        private readonly Button previousButton = new Button();
        private readonly Button nextButton = new Button();
        private readonly Button fitButton = new Button();
        private readonly Button zoomInButton = new Button();
        private readonly Button zoomOutButton = new Button();
        private readonly Button actualSizeButton = new Button();
        private readonly CheckBox labelsCheckBox = new CheckBox();
        private readonly CheckBox fitCheckBox = new CheckBox();
        private readonly ListBox fileList = new ListBox();
        private readonly ListView annotationList = new ListView();
        private readonly Panel imagePanel = new Panel();
        private readonly PictureBox pictureBox = new PictureBox();
        private readonly Label statusLabel = new Label();
        private readonly Label fileInfoLabel = new Label();
        private readonly TextBox predFolderText = new TextBox();
        private readonly Button predBrowseButton = new Button();
        private readonly Label iouLabel = new Label();
        private readonly NumericUpDown iouNumeric = new NumericUpDown();
        private readonly CheckBox gtOnlyCheckBox = new CheckBox();
        private readonly CheckBox errorAnalysisCheckBox = new CheckBox();
        private readonly Button comparisonStatsButton = new Button();
        private readonly Button exportCompareButton = new Button();

        private readonly List<ImageRecord> records = new List<ImageRecord>();
        private Image sourceImage;
        private List<AnnotationShape> currentShapes = new List<AnnotationShape>();
        private List<AnnotationShape> currentPredShapes = new List<AnnotationShape>();
        private float zoom = 1.0f;
        private bool changingSelection;

        private const string AnnotationKindLabelMe = "LabelMe JSON";
        private const string AnnotationKindYolo = "YOLO TXT";
        private const string AnnotationKindPred = "Pred YOLO TXT";

        public ViewerForm()
        {
            Text = "MedVision 标注框可视化工具 v" + Program.AppVersion;
            Width = 1500;
            Height = 940;
            MinimumSize = new Size(1120, 680);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Icon windowIcon = LoadWindowIcon();
            if (windowIcon != null)
            {
                Icon = windowIcon;
            }

            BuildLayout();
            WireEvents();

            string defaultFolder = FindDefaultDataFolder();
            if (!string.IsNullOrEmpty(defaultFolder))
            {
                folderText.Text = defaultFolder;
                LoadFolder(defaultFolder);
            }
            else
            {
                SetStatus("请选择包含图片和 JSON/TXT 标注文件的文件夹。");
            }

            UpdatePredictionControlsState();
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.Padding = new Padding(10);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(root);

            TableLayoutPanel headerPanel = new TableLayoutPanel();
            headerPanel.Dock = DockStyle.Fill;
            headerPanel.ColumnCount = 1;
            headerPanel.RowCount = 2;
            headerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            headerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.Controls.Add(headerPanel, 0, 0);

            TableLayoutPanel topBar = new TableLayoutPanel();
            topBar.Dock = DockStyle.Fill;
            topBar.ColumnCount = 5;
            topBar.RowCount = 1;
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            headerPanel.Controls.Add(topBar, 0, 0);

            folderText.Dock = DockStyle.Fill;
            folderText.ReadOnly = true;
            topBar.Controls.Add(folderText, 0, 0);

            browseButton.Text = "选择文件夹";
            browseButton.Dock = DockStyle.Fill;
            topBar.Controls.Add(browseButton, 1, 0);

            reloadButton.Text = "刷新";
            reloadButton.Dock = DockStyle.Fill;
            topBar.Controls.Add(reloadButton, 2, 0);

            statsButton.Text = "统计标注";
            statsButton.Dock = DockStyle.Fill;
            topBar.Controls.Add(statsButton, 3, 0);

            fileInfoLabel.Dock = DockStyle.Fill;
            fileInfoLabel.TextAlign = ContentAlignment.MiddleRight;
            topBar.Controls.Add(fileInfoLabel, 4, 0);

            TableLayoutPanel compareBar = new TableLayoutPanel();
            compareBar.Dock = DockStyle.Fill;
            compareBar.ColumnCount = 10;
            compareBar.RowCount = 1;
            compareBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            compareBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            compareBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            compareBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
            compareBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            compareBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
            compareBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            compareBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
            compareBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            compareBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));
            headerPanel.Controls.Add(compareBar, 0, 1);

            Label predLabel = new Label();
            predLabel.Dock = DockStyle.Fill;
            predLabel.Text = "Pred labels";
            predLabel.TextAlign = ContentAlignment.MiddleLeft;
            compareBar.Controls.Add(predLabel, 0, 0);

            predFolderText.Dock = DockStyle.Fill;
            predFolderText.ReadOnly = false;
            compareBar.Controls.Add(predFolderText, 1, 0);

            predBrowseButton.Text = "选择Pred";
            predBrowseButton.Dock = DockStyle.Fill;
            compareBar.Controls.Add(predBrowseButton, 2, 0);

            iouLabel.Dock = DockStyle.Fill;
            iouLabel.Text = "IoU";
            iouLabel.TextAlign = ContentAlignment.MiddleRight;
            compareBar.Controls.Add(iouLabel, 3, 0);

            iouNumeric.Dock = DockStyle.Fill;
            iouNumeric.DecimalPlaces = 2;
            iouNumeric.Minimum = 0.01M;
            iouNumeric.Maximum = 0.95M;
            iouNumeric.Increment = 0.05M;
            iouNumeric.Value = 0.50M;
            compareBar.Controls.Add(iouNumeric, 4, 0);

            gtOnlyCheckBox.Text = "仅显示GT";
            gtOnlyCheckBox.Dock = DockStyle.Fill;
            gtOnlyCheckBox.TextAlign = ContentAlignment.MiddleLeft;
            compareBar.Controls.Add(gtOnlyCheckBox, 5, 0);

            errorAnalysisCheckBox.Text = "错误分析";
            errorAnalysisCheckBox.Dock = DockStyle.Fill;
            errorAnalysisCheckBox.TextAlign = ContentAlignment.MiddleLeft;
            compareBar.Controls.Add(errorAnalysisCheckBox, 6, 0);

            comparisonStatsButton.Text = "统计误差";
            comparisonStatsButton.Dock = DockStyle.Fill;
            compareBar.Controls.Add(comparisonStatsButton, 7, 0);

            exportCompareButton.Text = "导出对比图";
            exportCompareButton.Dock = DockStyle.Fill;
            compareBar.Controls.Add(exportCompareButton, 8, 0);

            SplitContainer mainSplit = new SplitContainer();
            mainSplit.Dock = DockStyle.Fill;
            mainSplit.Orientation = Orientation.Vertical;
            mainSplit.SplitterDistance = 250;
            mainSplit.SplitterWidth = 6;
            mainSplit.Panel1MinSize = 220;
            root.Controls.Add(mainSplit, 0, 1);

            TableLayoutPanel leftPanel = new TableLayoutPanel();
            leftPanel.Dock = DockStyle.Fill;
            leftPanel.ColumnCount = 1;
            leftPanel.RowCount = 2;
            leftPanel.Padding = new Padding(0, 0, 6, 0);
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
            mainSplit.Panel1.Controls.Add(leftPanel);

            fileList.Dock = DockStyle.Fill;
            fileList.IntegralHeight = false;
            fileList.HorizontalScrollbar = true;
            leftPanel.Controls.Add(fileList, 0, 0);

            annotationList.Dock = DockStyle.Fill;
            annotationList.View = View.Details;
            annotationList.FullRowSelect = true;
            annotationList.GridLines = true;
            annotationList.Columns.Add("#", 34);
            annotationList.Columns.Add("标签", 82);
            annotationList.Columns.Add("类型", 76);
            annotationList.Columns.Add("坐标", 150);
            leftPanel.Controls.Add(annotationList, 0, 1);

            TableLayoutPanel rightPanel = new TableLayoutPanel();
            rightPanel.Dock = DockStyle.Fill;
            rightPanel.ColumnCount = 1;
            rightPanel.RowCount = 2;
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainSplit.Panel2.Controls.Add(rightPanel);

            FlowLayoutPanel toolBar = new FlowLayoutPanel();
            toolBar.Dock = DockStyle.Fill;
            toolBar.FlowDirection = FlowDirection.LeftToRight;
            toolBar.WrapContents = false;
            rightPanel.Controls.Add(toolBar, 0, 0);

            previousButton.Text = "上一张";
            nextButton.Text = "下一张";
            fitButton.Text = "适应窗口";
            zoomOutButton.Text = "-";
            zoomInButton.Text = "+";
            actualSizeButton.Text = "100%";

            Button[] buttons = new Button[] { previousButton, nextButton, fitButton, zoomOutButton, zoomInButton, actualSizeButton };
            foreach (Button button in buttons)
            {
                button.Width = button == zoomOutButton || button == zoomInButton ? 42 : 82;
                button.Height = 30;
                toolBar.Controls.Add(button);
            }

            fitCheckBox.Text = "自动适应";
            fitCheckBox.Checked = true;
            fitCheckBox.AutoSize = true;
            fitCheckBox.Padding = new Padding(8, 6, 0, 0);
            toolBar.Controls.Add(fitCheckBox);

            labelsCheckBox.Text = "显示标签";
            labelsCheckBox.Checked = true;
            labelsCheckBox.AutoSize = true;
            labelsCheckBox.Padding = new Padding(8, 6, 0, 0);
            toolBar.Controls.Add(labelsCheckBox);

            imagePanel.Dock = DockStyle.Fill;
            imagePanel.AutoScroll = true;
            imagePanel.BackColor = Color.FromArgb(34, 34, 34);
            rightPanel.Controls.Add(imagePanel, 0, 1);

            pictureBox.SizeMode = PictureBoxSizeMode.Normal;
            pictureBox.Location = new Point(0, 0);
            imagePanel.Controls.Add(pictureBox);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(statusLabel, 0, 2);
        }

        private void WireEvents()
        {
            browseButton.Click += delegate { BrowseForFolder(); };
            reloadButton.Click += delegate { LoadFolder(folderText.Text); };
            statsButton.Click += delegate { ShowAnnotationStats(); };
            predBrowseButton.Click += delegate { BrowseForPredictionFolder(); };
            predFolderText.TextChanged += delegate { OnPredictionFolderChanged(); };
            comparisonStatsButton.Click += delegate { ShowComparisonStats(); };
            exportCompareButton.Click += delegate { ExportComparisonImages(); };
            iouNumeric.ValueChanged += delegate
            {
                RenderCurrent();
                UpdateCurrentStatus();
            };
            gtOnlyCheckBox.CheckedChanged += delegate
            {
                RenderCurrent();
                UpdateCurrentStatus();
            };
            errorAnalysisCheckBox.CheckedChanged += delegate
            {
                RenderCurrent();
                UpdateCurrentStatus();
            };
            previousButton.Click += delegate { MoveSelection(-1); };
            nextButton.Click += delegate { MoveSelection(1); };
            fitButton.Click += delegate { FitToWindow(); };
            zoomInButton.Click += delegate { SetZoom(zoom * 1.25f); };
            zoomOutButton.Click += delegate { SetZoom(zoom / 1.25f); };
            actualSizeButton.Click += delegate
            {
                fitCheckBox.Checked = false;
                SetZoom(1.0f);
            };
            labelsCheckBox.CheckedChanged += delegate { RenderCurrent(); };
            fitCheckBox.CheckedChanged += delegate
            {
                if (fitCheckBox.Checked)
                {
                    FitToWindow();
                }
            };
            fileList.SelectedIndexChanged += delegate
            {
                if (!changingSelection)
                {
                    LoadSelectedRecord();
                }
            };
            imagePanel.Resize += delegate
            {
                if (fitCheckBox.Checked)
                {
                    FitToWindow();
                }
            };
            imagePanel.MouseWheel += delegate(object sender, MouseEventArgs e)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    fitCheckBox.Checked = false;
                    SetZoom(e.Delta > 0 ? zoom * 1.15f : zoom / 1.15f);
                }
            };
            imagePanel.MouseEnter += delegate { imagePanel.Focus(); };
            pictureBox.MouseEnter += delegate { imagePanel.Focus(); };
            pictureBox.MouseWheel += delegate(object sender, MouseEventArgs e)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    fitCheckBox.Checked = false;
                    SetZoom(e.Delta > 0 ? zoom * 1.15f : zoom / 1.15f);
                }
            };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Left)
                {
                    MoveSelection(-1);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Right)
                {
                    MoveSelection(1);
                    e.Handled = true;
                }
                else if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add))
                {
                    fitCheckBox.Checked = false;
                    SetZoom(zoom * 1.25f);
                    e.Handled = true;
                }
                else if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract))
                {
                    fitCheckBox.Checked = false;
                    SetZoom(zoom / 1.25f);
                    e.Handled = true;
                }
            };
        }

        private static string FindDefaultDataFolder()
        {
            string[] candidates = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "New mycelium in square frame"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "New mycelium in square frame"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "New mycelium in square frame"),
                Path.Combine(Environment.CurrentDirectory, "New mycelium in square frame"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "New mycelium in square frame"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "New mycelium in square frame")
            };

            foreach (string candidate in candidates)
            {
                string fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return null;
        }

        private void BrowseForFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择包含图片和同名 JSON 标注文件的文件夹";
                dialog.SelectedPath = Directory.Exists(folderText.Text) ? folderText.Text : Environment.CurrentDirectory;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    folderText.Text = dialog.SelectedPath;
                    LoadFolder(dialog.SelectedPath);
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
            UpdatePredictionControlsState();
            ReloadCurrentPredictions();
            RenderCurrent();
            UpdateCurrentStatus();
        }

        private void UpdatePredictionControlsState()
        {
            bool hasPredictionFolder = HasPredictionFolder();
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
            if (string.IsNullOrWhiteSpace(predFolderText.Text))
            {
                return false;
            }

            string predLabelFolder = ResolvePredictionLabelFolder(predFolderText.Text);
            return !string.IsNullOrEmpty(predLabelFolder) && Directory.Exists(predLabelFolder);
        }

        private void ShowAnnotationStats()
        {
            string folder = folderText.Text;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show(this, "请先选择一个有效的数据文件夹。", "无法统计", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Cursor previousCursor = Cursor.Current;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                List<ImageRecord> statsRecords = BuildRecordsRecursively(folder);
                List<StatsRow> rows = BuildStatsRows(folder, statsRecords);
                Cursor.Current = previousCursor;

                if (rows.Count == 0)
                {
                    MessageBox.Show(this, "没有找到可统计的图片与 JSON/TXT 标注对。", "统计结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (StatsForm dialog = new StatsForm(rows, folder))
                {
                    dialog.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                Cursor.Current = previousCursor;
                MessageBox.Show(this, "统计失败：" + ex.Message, "统计失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = previousCursor;
            }
        }

        private void ShowComparisonStats()
        {
            if (records.Count == 0)
            {
                MessageBox.Show(this, "请先加载包含 GT 标注的数据集。", "无法统计", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string predLabelFolder = ResolvePredictionLabelFolder(predFolderText.Text);
            if (string.IsNullOrEmpty(predLabelFolder) || !Directory.Exists(predLabelFolder))
            {
                MessageBox.Show(this, "请选择 YOLO 预测 labels 文件夹，或包含 labels 子目录的预测结果文件夹。", "缺少 Pred labels", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Cursor previousCursor = Cursor.Current;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                float iouThreshold = (float)iouNumeric.Value;
                List<ComparisonStatsRow> rows = BuildComparisonStatsRows(new List<ImageRecord>(records), predLabelFolder, iouThreshold, folderText.Text);
                Cursor.Current = previousCursor;

                if (rows.Count == 0)
                {
                    MessageBox.Show(this, "没有可统计的 GT / Pred 对比结果。", "统计结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (ComparisonStatsForm dialog = new ComparisonStatsForm(rows, folderText.Text, predLabelFolder, iouThreshold))
                {
                    dialog.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                Cursor.Current = previousCursor;
                MessageBox.Show(this, "统计失败：" + ex.Message, "统计失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = previousCursor;
            }
        }

        private void ExportComparisonImages()
        {
            List<ImageRecord> exportRecords = new List<ImageRecord>(records);
            if (exportRecords.Count == 0)
            {
                MessageBox.Show(this, "请先加载包含 GT 标注的数据集。", "无法导出", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string predLabelFolder = ResolvePredictionLabelFolder(predFolderText.Text);
            if (string.IsNullOrEmpty(predLabelFolder) || !Directory.Exists(predLabelFolder))
            {
                MessageBox.Show(this, "请选择 YOLO 预测 labels 文件夹，或包含 labels 子目录的预测结果文件夹。", "缺少 Pred labels", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            float iouThreshold = (float)iouNumeric.Value;
            string exportRoot = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "comparison_exports",
                DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            string overlayDir = Path.Combine(exportRoot, "gt_pred_overlay");
            string errorDir = Path.Combine(exportRoot, "tp_fp_fn_analysis");
            Directory.CreateDirectory(overlayDir);
            Directory.CreateDirectory(errorDir);

            int processed = 0;
            int imagesWithPred = 0;
            int totalTp = 0;
            int totalFp = 0;
            int totalFn = 0;

            try
            {
                foreach (ImageRecord record in exportRecords)
                {
                    using (Image image = LoadImageWithoutLock(record.ImagePath))
                    {
                        List<AnnotationShape> gtShapes = LoadAnnotations(record, image.Width, image.Height);
                        string predPath = Path.Combine(predLabelFolder, Path.GetFileNameWithoutExtension(record.ImagePath) + ".txt");
                        List<AnnotationShape> predShapes = File.Exists(predPath)
                            ? LoadYoloAnnotations(predPath, image.Width, image.Height, record.ClassNames, true)
                            : new List<AnnotationShape>();

                        if (predShapes.Count > 0)
                        {
                            imagesWithPred++;
                        }

                        MatchResult match = MatchDetections(gtShapes, predShapes, iouThreshold);
                        totalTp += match.TruePositiveCount;
                        totalFp += match.FalsePositiveCount;
                        totalFn += match.FalseNegativeCount;

                        string overlayPath = BuildExportPath(overlayDir, record.DisplayName);
                        string errorPath = BuildExportPath(errorDir, record.DisplayName);
                        SaveOverlayImage(image, gtShapes, predShapes, overlayPath);
                        SaveErrorAnalysisImage(image, gtShapes, predShapes, match, errorPath);
                    }

                    processed++;
                    if (processed % 10 == 0 || processed == exportRecords.Count)
                    {
                        SetStatus(string.Format("导出对比图 {0}/{1} ...", processed, exportRecords.Count));
                        Application.DoEvents();
                    }
                }

                SetStatus(string.Format("导出完成：{0} 张图，TP {1} / FP {2} / FN {3}。输出：{4}", processed, totalTp, totalFp, totalFn, exportRoot));
                MessageBox.Show(
                    this,
                    string.Format(
                        "导出完成。\n\n图片数：{0}\n有 Pred 的图片：{1}\nTP：{2}\nFP：{3}\nFN：{4}\n\n输出目录：\n{5}",
                        processed,
                        imagesWithPred,
                        totalTp,
                        totalFp,
                        totalFn,
                        exportRoot),
                    "导出完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                SetStatus("导出失败：" + ex.Message);
                MessageBox.Show(this, "导出失败：" + ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string ResolvePredictionLabelFolder(string selectedFolder)
        {
            if (string.IsNullOrEmpty(selectedFolder) || !Directory.Exists(selectedFolder))
            {
                return null;
            }

            string directLabels = Path.Combine(selectedFolder, "labels");
            if (Directory.Exists(directLabels))
            {
                return directLabels;
            }

            if (Directory.GetFiles(selectedFolder, "*.txt").Length > 0)
            {
                return selectedFolder;
            }

            DirectoryInfo nestedLabels = Directory.GetDirectories(selectedFolder, "labels", SearchOption.AllDirectories)
                .Select(path => new DirectoryInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault();

            return nestedLabels == null ? selectedFolder : nestedLabels.FullName;
        }

        private static string BuildExportPath(string outputRoot, string displayName)
        {
            string relative = displayName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(outputRoot, relative);
            fullPath = Path.ChangeExtension(fullPath, ".jpg");
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return fullPath;
        }

        private static void SaveOverlayImage(Image image, IList<AnnotationShape> gtShapes, IList<AnnotationShape> predShapes, string outputPath)
        {
            using (Bitmap bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = new Font("Arial", Math.Max(12.0f, image.Width / 180.0f), FontStyle.Bold))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
                DrawShapeSet(graphics, gtShapes, Color.FromArgb(0, 210, 90), "GT", font, false);
                DrawShapeSet(graphics, predShapes, Color.FromArgb(255, 145, 0), "Pred", font, true);
                DrawLegend(graphics, new[] { "GT: green", "Pred: orange dashed" }, font);
                bitmap.Save(outputPath, ImageFormat.Jpeg);
            }
        }

        private static void SaveErrorAnalysisImage(
            Image image,
            IList<AnnotationShape> gtShapes,
            IList<AnnotationShape> predShapes,
            MatchResult match,
            string outputPath)
        {
            using (Bitmap bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = new Font("Arial", Math.Max(12.0f, image.Width / 180.0f), FontStyle.Bold))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));

                for (int i = 0; i < predShapes.Count; i++)
                {
                    if (match.MatchedPredIndexes.Contains(i))
                    {
                        DrawOneShape(graphics, predShapes[i], Color.FromArgb(0, 210, 90), "TP", font, false);
                    }
                    else
                    {
                        DrawOneShape(graphics, predShapes[i], Color.FromArgb(235, 45, 45), "FP", font, false);
                    }
                }

                for (int i = 0; i < gtShapes.Count; i++)
                {
                    if (!match.MatchedGtIndexes.Contains(i))
                    {
                        DrawOneShape(graphics, gtShapes[i], Color.FromArgb(20, 125, 255), "FN", font, true);
                    }
                }

                DrawLegend(graphics, new[] { "TP: green", "FP: red", "FN: blue dashed" }, font);
                bitmap.Save(outputPath, ImageFormat.Jpeg);
            }
        }

        private static void DrawShapeSet(Graphics graphics, IList<AnnotationShape> shapes, Color color, string prefix, Font font, bool dashed)
        {
            foreach (AnnotationShape shape in shapes)
            {
                DrawOneShape(graphics, shape, color, prefix, font, dashed);
            }
        }

        private static void DrawOneShape(Graphics graphics, AnnotationShape shape, Color color, string prefix, Font font, bool dashed)
        {
            RectangleF? boundsOrNull = shape.Bounds;
            if (!boundsOrNull.HasValue)
            {
                return;
            }

            RectangleF bounds = boundsOrNull.Value;
            using (Pen pen = new Pen(color, Math.Max(3.0f, bounds.Width / 120.0f)))
            {
                if (dashed)
                {
                    pen.DashStyle = DashStyle.Dash;
                }

                if (shape.Points.Count > 2 && string.Equals(shape.ShapeType, "polygon", StringComparison.OrdinalIgnoreCase))
                {
                    graphics.DrawPolygon(pen, shape.Points.ToArray());
                }
                else
                {
                    graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                }
            }

            string text = BuildShapeText(prefix, shape);
            DrawLabel(graphics, text, bounds, color, font);
        }

        private static string BuildShapeText(string prefix, AnnotationShape shape)
        {
            string text = string.IsNullOrEmpty(shape.Label) ? prefix : prefix + " " + shape.Label;
            if (shape.Confidence.HasValue)
            {
                text += string.Format(CultureInfo.InvariantCulture, " {0:0.00}", shape.Confidence.Value);
            }

            return text;
        }

        private static void DrawLegend(Graphics graphics, string[] lines, Font font)
        {
            float width = 0;
            float height = 8;
            foreach (string line in lines)
            {
                SizeF size = graphics.MeasureString(line, font);
                width = Math.Max(width, size.Width);
                height += size.Height + 2;
            }

            RectangleF background = new RectangleF(12, 12, width + 18, height + 8);
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            {
                graphics.FillRectangle(brush, background);
                float y = 20;
                foreach (string line in lines)
                {
                    graphics.DrawString(line, font, textBrush, 20, y);
                    y += graphics.MeasureString(line, font).Height + 2;
                }
            }
        }

        private static MatchResult MatchDetections(IList<AnnotationShape> gtShapes, IList<AnnotationShape> predShapes, float iouThreshold)
        {
            MatchResult result = new MatchResult();
            HashSet<int> usedGt = new HashSet<int>();
            List<int> predOrder = Enumerable.Range(0, predShapes.Count)
                .OrderByDescending(index => predShapes[index].Confidence.HasValue ? predShapes[index].Confidence.Value : 1.0f)
                .ToList();

            foreach (int predIndex in predOrder)
            {
                AnnotationShape pred = predShapes[predIndex];
                int bestGtIndex = -1;
                float bestIou = 0;

                for (int gtIndex = 0; gtIndex < gtShapes.Count; gtIndex++)
                {
                    if (usedGt.Contains(gtIndex) || !ClassMatches(gtShapes[gtIndex], pred))
                    {
                        continue;
                    }

                    RectangleF? gtBounds = gtShapes[gtIndex].Bounds;
                    RectangleF? predBounds = pred.Bounds;
                    if (!gtBounds.HasValue || !predBounds.HasValue)
                    {
                        continue;
                    }

                    float iou = ComputeIou(gtBounds.Value, predBounds.Value);
                    if (iou >= iouThreshold && iou > bestIou)
                    {
                        bestIou = iou;
                        bestGtIndex = gtIndex;
                    }
                }

                if (bestGtIndex >= 0)
                {
                    usedGt.Add(bestGtIndex);
                    result.MatchedGtIndexes.Add(bestGtIndex);
                    result.MatchedPredIndexes.Add(predIndex);
                    result.PredToGtIndexes[predIndex] = bestGtIndex;
                }
            }

            for (int i = 0; i < predShapes.Count; i++)
            {
                if (!result.MatchedPredIndexes.Contains(i))
                {
                    result.FalsePositiveIndexes.Add(i);
                }
            }

            for (int i = 0; i < gtShapes.Count; i++)
            {
                if (!result.MatchedGtIndexes.Contains(i))
                {
                    result.FalseNegativeIndexes.Add(i);
                }
            }

            return result;
        }

        private static bool ClassMatches(AnnotationShape gt, AnnotationShape pred)
        {
            if (gt.ClassId.HasValue && pred.ClassId.HasValue)
            {
                return gt.ClassId.Value == pred.ClassId.Value;
            }

            if (!gt.ClassId.HasValue && !pred.ClassId.HasValue)
            {
                return string.Equals(gt.Label, pred.Label, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private static float ComputeIou(RectangleF a, RectangleF b)
        {
            float left = Math.Max(a.Left, b.Left);
            float top = Math.Max(a.Top, b.Top);
            float right = Math.Min(a.Right, b.Right);
            float bottom = Math.Min(a.Bottom, b.Bottom);
            float intersectionWidth = Math.Max(0, right - left);
            float intersectionHeight = Math.Max(0, bottom - top);
            float intersection = intersectionWidth * intersectionHeight;
            float union = a.Width * a.Height + b.Width * b.Height - intersection;
            return union <= 0 ? 0 : intersection / union;
        }

        private void LoadFolder(string folder)
        {
            records.Clear();
            fileList.Items.Clear();
            annotationList.Items.Clear();
            ClearCurrentImage();

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                SetStatus("请选择有效的数据文件夹。");
                fileInfoLabel.Text = string.Empty;
                return;
            }

            try
            {
                records.AddRange(BuildRecords(folder));

                foreach (ImageRecord record in records)
                {
                    fileList.Items.Add(string.Format("{0}  [{1}]  ({2})", record.DisplayName, record.AnnotationKind, record.ShapeCount));
                }

                int totalBoxes = records.Sum(record => record.ShapeCount);
                fileInfoLabel.Text = string.Format("{0} 张图 / {1} 个框", records.Count, totalBoxes);

                if (records.Count == 0)
                {
                    SetStatus("没有找到图片与 JSON/TXT 标注对。可选择原始 LabelMe 文件夹、YOLO 数据集根目录、split 目录或 images 目录。");
                    return;
                }

                changingSelection = true;
                fileList.SelectedIndex = 0;
                changingSelection = false;
                LoadSelectedRecord();
            }
            catch (Exception ex)
            {
                SetStatus("读取文件夹失败：" + ex.Message);
            }
        }

        private static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".tif" || ext == ".tiff";
        }

        private List<ImageRecord> BuildRecords(string folder)
        {
            List<ImageRecord> result = new List<ImageRecord>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<int, string> classNames = LoadClassNames(FindClassesFile(folder));

            AddRecordsFromDirectFolder(folder, classNames, result, seen, folder);
            AddRecordsFromFolderPatterns(folder, classNames, result, seen, folder);

            return result
                .OrderBy(record => record.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private List<ImageRecord> BuildRecordsRecursively(string folder)
        {
            List<ImageRecord> result = new List<ImageRecord>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string scanFolder in EnumerateStatsFolders(folder))
            {
                Dictionary<int, string> classNames = LoadClassNames(FindClassesFile(scanFolder));
                AddRecordsFromDirectFolder(scanFolder, classNames, result, seen, folder);
                AddRecordsFromFolderPatterns(scanFolder, classNames, result, seen, folder);
            }

            return result
                .OrderBy(record => record.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static void AddRecordsFromFolderPatterns(
            string folder,
            Dictionary<int, string> classNames,
            List<ImageRecord> result,
            HashSet<string> seen,
            string displayBaseFolder)
        {
            string trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string folderName = Path.GetFileName(trimmed);
            if (string.Equals(folderName, "images", StringComparison.OrdinalIgnoreCase))
            {
                string parent = Directory.GetParent(trimmed) == null ? null : Directory.GetParent(trimmed).FullName;
                string labelsFolder = parent == null ? null : Path.Combine(parent, "labels");
                if (!string.IsNullOrEmpty(labelsFolder) && Directory.Exists(labelsFolder))
                {
                    AddRecordsFromImageLabelFolders(trimmed, labelsFolder, classNames, result, seen, displayBaseFolder);
                }
            }

            string childImages = Path.Combine(folder, "images");
            string childLabels = Path.Combine(folder, "labels");
            if (Directory.Exists(childImages) && Directory.Exists(childLabels))
            {
                AddRecordsFromImageLabelFolders(childImages, childLabels, classNames, result, seen, displayBaseFolder);
            }

            string[] splits = new[] { "train", "val", "test" };
            foreach (string split in splits)
            {
                string splitImages = Path.Combine(folder, split, "images");
                string splitLabels = Path.Combine(folder, split, "labels");
                if (Directory.Exists(splitImages) && Directory.Exists(splitLabels))
                {
                    AddRecordsFromImageLabelFolders(splitImages, splitLabels, classNames, result, seen, displayBaseFolder);
                }
            }
        }

        private static List<string> EnumerateStatsFolders(string rootFolder)
        {
            List<string> folders = new List<string>();
            Stack<string> pending = new Stack<string>();
            pending.Push(rootFolder);

            while (pending.Count > 0)
            {
                string folder = pending.Pop();
                folders.Add(folder);

                string[] children;
                try
                {
                    children = Directory.GetDirectories(folder);
                }
                catch
                {
                    continue;
                }

                Array.Sort(children, StringComparer.CurrentCultureIgnoreCase);
                for (int i = children.Length - 1; i >= 0; i--)
                {
                    if (!ShouldSkipStatsFolder(children[i]))
                    {
                        pending.Push(children[i]);
                    }
                }
            }

            return folders;
        }

        private static bool ShouldSkipStatsFolder(string folder)
        {
            string name = Path.GetFileName(folder);
            return string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ".venv", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "venv", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "__pycache__", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ".agents", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ".codex", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "annotation_viewer_tool", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "comparison_exports", StringComparison.OrdinalIgnoreCase);
        }

        private static List<StatsRow> BuildStatsRows(string rootFolder, IList<ImageRecord> statsRecords)
        {
            Dictionary<string, FolderAnnotationStats> folderStats =
                new Dictionary<string, FolderAnnotationStats>(StringComparer.CurrentCultureIgnoreCase);

            foreach (ImageRecord record in statsRecords)
            {
                string folderName = BuildStatsFolderName(rootFolder, record.ImagePath);
                FolderAnnotationStats stats;
                if (!folderStats.TryGetValue(folderName, out stats))
                {
                    stats = new FolderAnnotationStats(folderName);
                    folderStats[folderName] = stats;
                }

                stats.ImagePaths.Add(record.ImagePath);
                stats.AnnotationPaths.Add(record.AnnotationPath);
                stats.AnnotationKinds.Add(record.AnnotationKind);

                Dictionary<string, int> labelCounts = CountLabelsByClass(record);
                foreach (KeyValuePair<string, int> item in labelCounts)
                {
                    AddCount(stats.LabelCounts, item.Key, item.Value);
                }
            }

            List<StatsRow> rows = new List<StatsRow>();
            foreach (FolderAnnotationStats stats in folderStats.Values.OrderBy(item => item.FolderName, StringComparer.CurrentCultureIgnoreCase))
            {
                int totalBoxes = stats.LabelCounts.Values.Sum();
                string kinds = string.Join(", ", stats.AnnotationKinds.OrderBy(kind => kind, StringComparer.CurrentCultureIgnoreCase).ToArray());
                rows.Add(new StatsRow(
                    stats.FolderName,
                    stats.ImagePaths.Count,
                    stats.AnnotationPaths.Count,
                    kinds,
                    StatsRow.TotalLabelName,
                    totalBoxes,
                    true));

                foreach (KeyValuePair<string, int> labelCount in stats.LabelCounts
                    .OrderByDescending(item => item.Value)
                    .ThenBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
                {
                    rows.Add(new StatsRow(
                        stats.FolderName,
                        stats.ImagePaths.Count,
                        stats.AnnotationPaths.Count,
                        kinds,
                        labelCount.Key,
                        labelCount.Value,
                        false));
                }
            }

            return rows;
        }

        private static string BuildStatsFolderName(string rootFolder, string imagePath)
        {
            string imageFolder = Path.GetDirectoryName(imagePath);
            if (string.IsNullOrEmpty(imageFolder))
            {
                return GetDisplayRootFolderName(rootFolder);
            }

            try
            {
                string fullRoot = Path.GetFullPath(rootFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullImageFolder = Path.GetFullPath(imageFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (fullImageFolder.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    string relative = fullImageFolder.Substring(fullRoot.Length).Replace(Path.DirectorySeparatorChar, '/');
                    if (string.IsNullOrEmpty(relative))
                    {
                        return GetDisplayRootFolderName(rootFolder);
                    }

                    if (relative.EndsWith("/images", StringComparison.OrdinalIgnoreCase))
                    {
                        relative = relative.Substring(0, relative.Length - "/images".Length);
                    }
                    else if (string.Equals(relative, "images", StringComparison.OrdinalIgnoreCase))
                    {
                        relative = GetDisplayRootFolderName(rootFolder);
                    }

                    return string.IsNullOrEmpty(relative) ? GetDisplayRootFolderName(rootFolder) : relative;
                }
            }
            catch
            {
            }

            return Path.GetFileName(imageFolder);
        }

        private static string GetDisplayRootFolderName(string folder)
        {
            string trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? "根目录" : name;
        }

        private static Dictionary<string, int> CountLabelsByClass(ImageRecord record)
        {
            if (record.AnnotationKind == AnnotationKindYolo)
            {
                return CountYoloLabels(record.AnnotationPath, record.ClassNames);
            }

            return CountLabelMeLabels(record.AnnotationPath);
        }

        private static Dictionary<string, int> CountLabelMeLabels(string jsonPath)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            try
            {
                Dictionary<string, object> root = LoadJsonObject(jsonPath);
                object shapesObject;
                if (!root.TryGetValue("shapes", out shapesObject))
                {
                    return counts;
                }

                object[] shapeItems = shapesObject as object[];
                if (shapeItems == null)
                {
                    return counts;
                }

                foreach (object shapeObject in shapeItems)
                {
                    Dictionary<string, object> shapeMap = shapeObject as Dictionary<string, object>;
                    if (shapeMap == null)
                    {
                        continue;
                    }

                    string label = GetString(shapeMap, "label").Trim();
                    AddCount(counts, label.Length == 0 ? "（空标签）" : label, 1);
                }
            }
            catch
            {
                return counts;
            }

            return counts;
        }

        private static Dictionary<string, int> CountYoloLabels(string txtPath, Dictionary<int, string> classNames)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            try
            {
                string[] lines = File.ReadAllLines(txtPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5)
                    {
                        continue;
                    }

                    int classId;
                    if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out classId))
                    {
                        continue;
                    }

                    string className;
                    string label = classNames.TryGetValue(classId, out className) && className.Trim().Length > 0
                        ? className.Trim()
                        : string.Format(CultureInfo.InvariantCulture, "class {0}", classId);
                    AddCount(counts, label, 1);
                }
            }
            catch
            {
                return counts;
            }

            return counts;
        }

        private static void AddCount(Dictionary<string, int> counts, string key, int value)
        {
            int current;
            counts.TryGetValue(key, out current);
            counts[key] = current + value;
        }

        private static List<ComparisonStatsRow> BuildComparisonStatsRows(
            IList<ImageRecord> compareRecords,
            string predLabelFolder,
            float iouThreshold,
            string rootFolder)
        {
            Dictionary<string, ComparisonStatsBucket> buckets =
                new Dictionary<string, ComparisonStatsBucket>(StringComparer.CurrentCultureIgnoreCase);

            foreach (ImageRecord record in compareRecords)
            {
                using (Image image = LoadImageWithoutLock(record.ImagePath))
                {
                    List<AnnotationShape> gtShapes = LoadAnnotations(record, image.Width, image.Height);
                    List<AnnotationShape> predShapes = LoadPredictionsFromFolder(record, predLabelFolder, image.Width, image.Height);
                    MatchResult match = MatchDetections(gtShapes, predShapes, iouThreshold);
                    string folderName = BuildStatsFolderName(rootFolder, record.ImagePath);

                    AddComparisonCounts(
                        buckets,
                        folderName,
                        ComparisonStatsRow.TotalLabelName,
                        record.ImagePath,
                        gtShapes.Count,
                        predShapes.Count,
                        match.TruePositiveCount,
                        match.FalsePositiveCount,
                        match.FalseNegativeCount);

                    foreach (AnnotationShape gtShape in gtShapes)
                    {
                        AddComparisonCounts(buckets, folderName, GetShapeStatsLabel(gtShape), record.ImagePath, 1, 0, 0, 0, 0);
                    }

                    foreach (AnnotationShape predShape in predShapes)
                    {
                        AddComparisonCounts(buckets, folderName, GetShapeStatsLabel(predShape), record.ImagePath, 0, 1, 0, 0, 0);
                    }

                    foreach (KeyValuePair<int, int> pair in match.PredToGtIndexes)
                    {
                        AddComparisonCounts(buckets, folderName, GetShapeStatsLabel(gtShapes[pair.Value]), record.ImagePath, 0, 0, 1, 0, 0);
                    }

                    foreach (int predIndex in match.FalsePositiveIndexes)
                    {
                        AddComparisonCounts(buckets, folderName, GetShapeStatsLabel(predShapes[predIndex]), record.ImagePath, 0, 0, 0, 1, 0);
                    }

                    foreach (int gtIndex in match.FalseNegativeIndexes)
                    {
                        AddComparisonCounts(buckets, folderName, GetShapeStatsLabel(gtShapes[gtIndex]), record.ImagePath, 0, 0, 0, 0, 1);
                    }
                }
            }

            List<ComparisonStatsRow> rows = new List<ComparisonStatsRow>();
            List<string> folderNames = buckets.Values
                .Select(bucket => bucket.FolderName)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (string folderName in folderNames)
            {
                ComparisonStatsBucket totalBucket;
                if (buckets.TryGetValue(BuildComparisonStatsKey(folderName, ComparisonStatsRow.TotalLabelName), out totalBucket))
                {
                    rows.Add(totalBucket.ToRow(true));
                }

                foreach (ComparisonStatsBucket bucket in buckets.Values
                    .Where(item => string.Equals(item.FolderName, folderName, StringComparison.CurrentCultureIgnoreCase) &&
                        !string.Equals(item.LabelName, ComparisonStatsRow.TotalLabelName, StringComparison.CurrentCultureIgnoreCase))
                    .OrderByDescending(item => item.GtCount + item.PredCount)
                    .ThenBy(item => item.LabelName, StringComparer.CurrentCultureIgnoreCase))
                {
                    rows.Add(bucket.ToRow(false));
                }
            }

            return rows;
        }

        private static void AddComparisonCounts(
            Dictionary<string, ComparisonStatsBucket> buckets,
            string folderName,
            string labelName,
            string imagePath,
            int gtCount,
            int predCount,
            int truePositiveCount,
            int falsePositiveCount,
            int falseNegativeCount)
        {
            string key = BuildComparisonStatsKey(folderName, labelName);
            ComparisonStatsBucket bucket;
            if (!buckets.TryGetValue(key, out bucket))
            {
                bucket = new ComparisonStatsBucket(folderName, labelName);
                buckets[key] = bucket;
            }

            bucket.ImagePaths.Add(imagePath);
            bucket.GtCount += gtCount;
            bucket.PredCount += predCount;
            bucket.TruePositiveCount += truePositiveCount;
            bucket.FalsePositiveCount += falsePositiveCount;
            bucket.FalseNegativeCount += falseNegativeCount;
        }

        private static string BuildComparisonStatsKey(string folderName, string labelName)
        {
            return folderName + "||" + labelName;
        }

        private static string GetShapeStatsLabel(AnnotationShape shape)
        {
            string label = shape.Label == null ? string.Empty : shape.Label.Trim();
            if (shape.ClassId.HasValue)
            {
                int colonIndex = label.IndexOf(':');
                if (colonIndex >= 0 && colonIndex < label.Length - 1)
                {
                    label = label.Substring(colonIndex + 1).Trim();
                }
            }

            return label.Length == 0 ? "（空标签）" : label;
        }

        private static void AddRecordsFromDirectFolder(
            string folder,
            Dictionary<int, string> classNames,
            List<ImageRecord> result,
            HashSet<string> seen,
            string displayBaseFolder)
        {
            List<string> imageFiles = Directory.GetFiles(folder)
                .Where(IsImageFile)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (string imagePath in imageFiles)
            {
                string jsonPath = Path.ChangeExtension(imagePath, ".json");
                string txtPath = Path.ChangeExtension(imagePath, ".txt");

                if (File.Exists(jsonPath))
                {
                    AddRecord(imagePath, jsonPath, AnnotationKindLabelMe, classNames, result, seen, displayBaseFolder);
                }
                else if (File.Exists(txtPath))
                {
                    AddRecord(imagePath, txtPath, AnnotationKindYolo, classNames, result, seen, displayBaseFolder);
                }
            }
        }

        private static void AddRecordsFromImageLabelFolders(
            string imageFolder,
            string labelFolder,
            Dictionary<int, string> classNames,
            List<ImageRecord> result,
            HashSet<string> seen,
            string displayBaseFolder)
        {
            List<string> imageFiles = Directory.GetFiles(imageFolder)
                .Where(IsImageFile)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (string imagePath in imageFiles)
            {
                string txtPath = Path.Combine(labelFolder, Path.GetFileNameWithoutExtension(imagePath) + ".txt");
                if (File.Exists(txtPath))
                {
                    AddRecord(imagePath, txtPath, AnnotationKindYolo, classNames, result, seen, displayBaseFolder);
                }
            }
        }

        private static void AddRecord(
            string imagePath,
            string annotationPath,
            string annotationKind,
            Dictionary<int, string> classNames,
            List<ImageRecord> result,
            HashSet<string> seen,
            string displayBaseFolder)
        {
            string key = imagePath + "|" + annotationPath;
            if (seen.Contains(key))
            {
                return;
            }

            seen.Add(key);
            int shapeCount = CountAnnotationShapes(annotationPath, annotationKind);
            string displayName = BuildDisplayName(displayBaseFolder, imagePath);
            result.Add(new ImageRecord(imagePath, annotationPath, annotationKind, shapeCount, displayName, classNames));
        }

        private static string BuildDisplayName(string baseFolder, string imagePath)
        {
            try
            {
                string fullBase = Path.GetFullPath(baseFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullImage = Path.GetFullPath(imagePath);
                if (fullImage.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
                {
                    return fullImage.Substring(fullBase.Length).Replace(Path.DirectorySeparatorChar, '/');
                }
            }
            catch
            {
            }

            return Path.GetFileName(imagePath);
        }

        private static string FindClassesFile(string folder)
        {
            DirectoryInfo current = new DirectoryInfo(folder);
            while (current != null)
            {
                string classesPath = Path.Combine(current.FullName, "classes.txt");
                if (File.Exists(classesPath))
                {
                    return classesPath;
                }

                current = current.Parent;
            }

            return null;
        }

        private static Dictionary<int, string> LoadClassNames(string classesPath)
        {
            Dictionary<int, string> names = new Dictionary<int, string>();
            if (string.IsNullOrEmpty(classesPath) || !File.Exists(classesPath))
            {
                return names;
            }

            string[] lines = File.ReadAllLines(classesPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string name = lines[i].Trim();
                if (name.Length > 0)
                {
                    names[i] = name;
                }
            }

            return names;
        }

        private static int CountAnnotationShapes(string annotationPath, string annotationKind)
        {
            if (annotationKind == AnnotationKindYolo)
            {
                return CountYoloShapes(annotationPath);
            }

            return CountLabelMeShapes(annotationPath);
        }

        private static int CountLabelMeShapes(string jsonPath)
        {
            try
            {
                Dictionary<string, object> root = LoadJsonObject(jsonPath);
                object shapesObject;
                if (root.TryGetValue("shapes", out shapesObject))
                {
                    object[] shapes = shapesObject as object[];
                    return shapes == null ? 0 : shapes.Length;
                }
            }
            catch
            {
                return 0;
            }

            return 0;
        }

        private static int CountYoloShapes(string txtPath)
        {
            try
            {
                return File.ReadAllLines(txtPath).Count(line => line.Trim().Length > 0);
            }
            catch
            {
                return 0;
            }
        }

        private void LoadSelectedRecord()
        {
            if (fileList.SelectedIndex < 0 || fileList.SelectedIndex >= records.Count)
            {
                return;
            }

            ImageRecord record = records[fileList.SelectedIndex];

            try
            {
                ClearCurrentImage();
                sourceImage = LoadImageWithoutLock(record.ImagePath);
                currentShapes = LoadAnnotations(record, sourceImage.Width, sourceImage.Height);
                currentPredShapes = LoadPredictionsForRecord(record, sourceImage.Width, sourceImage.Height);
                PopulateAnnotationList(currentShapes);

                if (fitCheckBox.Checked)
                {
                    FitToWindow();
                }
                else
                {
                    RenderCurrent();
                }

                SetStatus(string.Format("{0}/{1}  {2}  |  {3}  |  {4} 个标注框",
                    fileList.SelectedIndex + 1,
                    records.Count,
                    record.DisplayName,
                    record.AnnotationKind,
                    currentShapes.Count));
                UpdateCurrentStatus();
            }
            catch (Exception ex)
            {
                SetStatus("加载失败：" + Path.GetFileName(record.ImagePath) + " - " + ex.Message);
            }
        }

        private void ReloadCurrentPredictions()
        {
            currentPredShapes = new List<AnnotationShape>();
            if (sourceImage == null || fileList.SelectedIndex < 0 || fileList.SelectedIndex >= records.Count)
            {
                return;
            }

            ImageRecord record = records[fileList.SelectedIndex];
            currentPredShapes = LoadPredictionsForRecord(record, sourceImage.Width, sourceImage.Height);
        }

        private List<AnnotationShape> LoadPredictionsForRecord(ImageRecord record, int imageWidth, int imageHeight)
        {
            string predLabelFolder = ResolvePredictionLabelFolder(predFolderText.Text);
            if (string.IsNullOrEmpty(predLabelFolder) || !Directory.Exists(predLabelFolder))
            {
                return new List<AnnotationShape>();
            }

            return LoadPredictionsFromFolder(record, predLabelFolder, imageWidth, imageHeight);
        }

        private static List<AnnotationShape> LoadPredictionsFromFolder(ImageRecord record, string predLabelFolder, int imageWidth, int imageHeight)
        {
            if (string.IsNullOrEmpty(predLabelFolder) || !Directory.Exists(predLabelFolder))
            {
                return new List<AnnotationShape>();
            }

            string predPath = Path.Combine(predLabelFolder, Path.GetFileNameWithoutExtension(record.ImagePath) + ".txt");
            if (!File.Exists(predPath))
            {
                return new List<AnnotationShape>();
            }

            return LoadYoloAnnotations(predPath, imageWidth, imageHeight, record.ClassNames, true);
        }

        private void UpdateCurrentStatus()
        {
            if (fileList.SelectedIndex < 0 || fileList.SelectedIndex >= records.Count)
            {
                return;
            }

            ImageRecord record = records[fileList.SelectedIndex];
            string compareText = string.Empty;
            string predLabelFolder = ResolvePredictionLabelFolder(predFolderText.Text);
            if (!gtOnlyCheckBox.Checked && !string.IsNullOrEmpty(predLabelFolder) && Directory.Exists(predLabelFolder))
            {
                MatchResult match = MatchDetections(currentShapes, currentPredShapes, (float)iouNumeric.Value);
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
                records.Count,
                record.DisplayName,
                record.AnnotationKind,
                currentShapes.Count,
                compareText));
        }

        private static Image LoadImageWithoutLock(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image temp = Image.FromStream(stream))
            {
                return new Bitmap(temp);
            }
        }

        private static Dictionary<string, object> LoadJsonObject(string jsonPath)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = serializer.DeserializeObject(File.ReadAllText(jsonPath)) as Dictionary<string, object>;
            if (root == null)
            {
                throw new InvalidDataException("JSON 根节点不是对象。");
            }

            return root;
        }

        private static List<AnnotationShape> LoadAnnotations(ImageRecord record, int imageWidth, int imageHeight)
        {
            if (record.AnnotationKind == AnnotationKindYolo)
            {
                return LoadYoloAnnotations(record.AnnotationPath, imageWidth, imageHeight, record.ClassNames, false);
            }

            return LoadLabelMeAnnotations(record.AnnotationPath);
        }

        private static List<AnnotationShape> LoadLabelMeAnnotations(string jsonPath)
        {
            List<AnnotationShape> shapes = new List<AnnotationShape>();
            Dictionary<string, object> root = LoadJsonObject(jsonPath);

            object shapesObject;
            if (!root.TryGetValue("shapes", out shapesObject))
            {
                return shapes;
            }

            object[] shapeItems = shapesObject as object[];
            if (shapeItems == null)
            {
                return shapes;
            }

            foreach (object shapeObject in shapeItems)
            {
                Dictionary<string, object> shapeMap = shapeObject as Dictionary<string, object>;
                if (shapeMap == null)
                {
                    continue;
                }

                string label = GetString(shapeMap, "label");
                string shapeType = GetString(shapeMap, "shape_type");
                List<PointF> points = LoadPoints(shapeMap);
                shapes.Add(new AnnotationShape(label, shapeType, points));
            }

            return shapes;
        }

        private static List<AnnotationShape> LoadYoloAnnotations(
            string txtPath,
            int imageWidth,
            int imageHeight,
            Dictionary<int, string> classNames,
            bool isPrediction)
        {
            List<AnnotationShape> shapes = new List<AnnotationShape>();
            string[] lines = File.ReadAllLines(txtPath);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                {
                    continue;
                }

                int classId;
                float centerX;
                float centerY;
                float width;
                float height;
                float confidence = 0;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out classId) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out centerX) ||
                    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out centerY) ||
                    !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out width) ||
                    !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out height))
                {
                    continue;
                }

                bool hasConfidence = parts.Length >= 6 &&
                    float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out confidence);
                float pixelWidth = width * imageWidth;
                float pixelHeight = height * imageHeight;
                float left = (centerX - width / 2.0f) * imageWidth;
                float top = (centerY - height / 2.0f) * imageHeight;

                List<PointF> points = new List<PointF>();
                points.Add(new PointF(left, top));
                points.Add(new PointF(left + pixelWidth, top + pixelHeight));

                string className;
                string label = classNames.TryGetValue(classId, out className)
                    ? string.Format("{0}: {1}", classId, className)
                    : string.Format("class {0}", classId);
                shapes.Add(new AnnotationShape(label, isPrediction ? AnnotationKindPred : AnnotationKindYolo, points, classId, hasConfidence ? (float?)confidence : null));
            }

            return shapes;
        }

        private static string GetString(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value);
        }

        private static List<PointF> LoadPoints(Dictionary<string, object> shapeMap)
        {
            List<PointF> points = new List<PointF>();
            object pointsObject;
            if (!shapeMap.TryGetValue("points", out pointsObject))
            {
                return points;
            }

            object[] pointItems = pointsObject as object[];
            if (pointItems == null)
            {
                return points;
            }

            foreach (object pointObject in pointItems)
            {
                object[] values = pointObject as object[];
                if (values == null || values.Length < 2)
                {
                    continue;
                }

                points.Add(new PointF(Convert.ToSingle(values[0]), Convert.ToSingle(values[1])));
            }

            return points;
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
                annotationList.Items.Add(item);
            }
        }

        private void FitToWindow()
        {
            if (sourceImage == null)
            {
                return;
            }

            int visibleWidth = Math.Max(1, imagePanel.ClientSize.Width - 24);
            int visibleHeight = Math.Max(1, imagePanel.ClientSize.Height - 24);
            float widthScale = visibleWidth / (float)sourceImage.Width;
            float heightScale = visibleHeight / (float)sourceImage.Height;
            zoom = Clamp(Math.Min(widthScale, heightScale), 0.05f, 8.0f);
            RenderCurrent();
        }

        private void SetZoom(float value)
        {
            zoom = Clamp(value, 0.05f, 8.0f);
            RenderCurrent();
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private void RenderCurrent()
        {
            if (sourceImage == null)
            {
                return;
            }

            int displayWidth = Math.Max(1, (int)Math.Round(sourceImage.Width * zoom));
            int displayHeight = Math.Max(1, (int)Math.Round(sourceImage.Height * zoom));
            Bitmap bitmap = new Bitmap(displayWidth, displayHeight, PixelFormat.Format24bppRgb);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(sourceImage, new Rectangle(0, 0, displayWidth, displayHeight));
                DrawAnnotations(graphics);
            }

            Image old = pictureBox.Image;
            pictureBox.Image = bitmap;
            pictureBox.Width = displayWidth;
            pictureBox.Height = displayHeight;
            if (old != null)
            {
                old.Dispose();
            }

            statusLabel.Text = AppendZoom(statusLabel.Text, string.Format("{0:0}%", zoom * 100));
        }

        private void DrawAnnotations(Graphics graphics)
        {
            string predLabelFolder = ResolvePredictionLabelFolder(predFolderText.Text);
            if (!string.IsNullOrEmpty(predLabelFolder) && Directory.Exists(predLabelFolder))
            {
                using (Font labelFont = new Font(Font.FontFamily, Math.Max(9.0f, 10.5f * Math.Min(1.5f, Math.Max(0.8f, zoom))), FontStyle.Bold))
                {
                    if (gtOnlyCheckBox.Checked)
                    {
                        DrawPreviewShapeSet(graphics, currentShapes, Color.FromArgb(0, 210, 90), "GT", labelFont, false);
                    }
                    else if (errorAnalysisCheckBox.Checked)
                    {
                        MatchResult match = MatchDetections(currentShapes, currentPredShapes, (float)iouNumeric.Value);
                        DrawPreviewErrorAnalysis(graphics, currentShapes, currentPredShapes, match, labelFont);
                    }
                    else
                    {
                        DrawPreviewShapeSet(graphics, currentShapes, Color.FromArgb(0, 210, 90), "GT", labelFont, false);
                        DrawPreviewShapeSet(graphics, currentPredShapes, Color.FromArgb(255, 145, 0), "Pred", labelFont, true);
                    }

                    DrawPreviewLegend(graphics, labelFont);
                }

                return;
            }

            Color[] palette = new Color[]
            {
                Color.FromArgb(255, 52, 91, 235),
                Color.FromArgb(255, 244, 67, 54),
                Color.FromArgb(255, 0, 150, 136),
                Color.FromArgb(255, 255, 152, 0),
                Color.FromArgb(255, 156, 39, 176),
                Color.FromArgb(255, 76, 175, 80)
            };

            using (Font labelFont = new Font(Font.FontFamily, Math.Max(9.0f, 10.5f * Math.Min(1.5f, Math.Max(0.8f, zoom))), FontStyle.Bold))
            {
                for (int i = 0; i < currentShapes.Count; i++)
                {
                    AnnotationShape shape = currentShapes[i];
                    RectangleF? boundsOrNull = shape.Bounds;
                    if (!boundsOrNull.HasValue)
                    {
                        continue;
                    }

                    Color color = palette[i % palette.Length];
                    using (Pen pen = new Pen(color, Math.Max(2.0f, 3.0f * Math.Min(1.2f, Math.Max(0.7f, zoom)))))
                    {
                        RectangleF scaledBounds = Scale(boundsOrNull.Value);
                        if (shape.Points.Count > 2 && string.Equals(shape.ShapeType, "polygon", StringComparison.OrdinalIgnoreCase))
                        {
                            PointF[] points = shape.Points.Select(Scale).ToArray();
                            if (points.Length > 1)
                            {
                                graphics.DrawPolygon(pen, points);
                            }
                        }
                        else
                        {
                            graphics.DrawRectangle(pen, scaledBounds.X, scaledBounds.Y, scaledBounds.Width, scaledBounds.Height);
                        }

                        if (labelsCheckBox.Checked)
                        {
                            DrawLabel(graphics, string.Format("{0}: {1}", i + 1, shape.Label), scaledBounds, color, labelFont);
                        }
                    }
                }
            }
        }

        private void DrawPreviewErrorAnalysis(
            Graphics graphics,
            IList<AnnotationShape> gtShapes,
            IList<AnnotationShape> predShapes,
            MatchResult match,
            Font font)
        {
            Color truePositiveColor = Color.FromArgb(0, 210, 90);
            Color falsePositiveColor = Color.FromArgb(235, 64, 52);
            Color falseNegativeColor = Color.FromArgb(46, 134, 255);

            for (int i = 0; i < predShapes.Count; i++)
            {
                if (match.MatchedPredIndexes.Contains(i))
                {
                    DrawPreviewShape(graphics, predShapes[i], truePositiveColor, string.Format("TP {0}", i + 1), font, false);
                }
            }

            for (int i = 0; i < predShapes.Count; i++)
            {
                if (match.FalsePositiveIndexes.Contains(i))
                {
                    DrawPreviewShape(graphics, predShapes[i], falsePositiveColor, string.Format("FP {0}", i + 1), font, false);
                }
            }

            for (int i = 0; i < gtShapes.Count; i++)
            {
                if (match.FalseNegativeIndexes.Contains(i))
                {
                    DrawPreviewShape(graphics, gtShapes[i], falseNegativeColor, string.Format("FN {0}", i + 1), font, true);
                }
            }
        }

        private void DrawPreviewShapeSet(Graphics graphics, IList<AnnotationShape> shapes, Color color, string prefix, Font font, bool dashed)
        {
            for (int i = 0; i < shapes.Count; i++)
            {
                DrawPreviewShape(graphics, shapes[i], color, string.Format("{0} {1}", prefix, i + 1), font, dashed);
            }
        }

        private void DrawPreviewShape(Graphics graphics, AnnotationShape shape, Color color, string prefix, Font font, bool dashed)
        {
            RectangleF? boundsOrNull = shape.Bounds;
            if (!boundsOrNull.HasValue)
            {
                return;
            }

            using (Pen pen = new Pen(color, Math.Max(2.0f, 3.0f * Math.Min(1.2f, Math.Max(0.7f, zoom)))))
            {
                if (dashed)
                {
                    pen.DashStyle = DashStyle.Dash;
                }

                RectangleF scaledBounds = Scale(boundsOrNull.Value);
                if (shape.Points.Count > 2 && string.Equals(shape.ShapeType, "polygon", StringComparison.OrdinalIgnoreCase))
                {
                    PointF[] points = shape.Points.Select(Scale).ToArray();
                    if (points.Length > 1)
                    {
                        graphics.DrawPolygon(pen, points);
                    }
                }
                else
                {
                    graphics.DrawRectangle(pen, scaledBounds.X, scaledBounds.Y, scaledBounds.Width, scaledBounds.Height);
                }

                if (labelsCheckBox.Checked)
                {
                    string label = prefix + ": " + shape.Label;
                    if (shape.Confidence.HasValue)
                    {
                        label += string.Format(CultureInfo.InvariantCulture, " {0:0.00}", shape.Confidence.Value);
                    }

                    DrawLabel(graphics, label, scaledBounds, color, font);
                }
            }
        }

        private void DrawPreviewLegend(Graphics graphics, Font font)
        {
            if (!labelsCheckBox.Checked)
            {
                return;
            }

            string text;
            if (gtOnlyCheckBox.Checked)
            {
                text = "GT only: green solid";
            }
            else if (errorAnalysisCheckBox.Checked)
            {
                text = string.Format(CultureInfo.InvariantCulture, "TP: green    FP: red    FN: blue dashed    IoU: {0:0.00}", (float)iouNumeric.Value);
            }
            else
            {
                text = string.Format(CultureInfo.InvariantCulture, "GT: green solid    Pred: orange dashed    IoU: {0:0.00}", (float)iouNumeric.Value);
            }
            SizeF size = graphics.MeasureString(text, font);
            RectangleF rect = new RectangleF(10, 10, size.Width + 16, size.Height + 10);
            using (SolidBrush background = new SolidBrush(Color.FromArgb(210, 20, 20, 20)))
            using (SolidBrush foreground = new SolidBrush(Color.White))
            {
                graphics.FillRectangle(background, rect);
                graphics.DrawString(text, font, foreground, rect.X + 8, rect.Y + 5);
            }
        }

        private RectangleF Scale(RectangleF rectangle)
        {
            return new RectangleF(rectangle.X * zoom, rectangle.Y * zoom, rectangle.Width * zoom, rectangle.Height * zoom);
        }

        private PointF Scale(PointF point)
        {
            return new PointF(point.X * zoom, point.Y * zoom);
        }

        private static void DrawLabel(Graphics graphics, string text, RectangleF bounds, Color color, Font font)
        {
            SizeF size = graphics.MeasureString(text, font);
            RectangleF labelRect = new RectangleF(bounds.X, Math.Max(0, bounds.Y - size.Height - 4), size.Width + 8, size.Height + 4);

            using (SolidBrush background = new SolidBrush(Color.FromArgb(220, color)))
            using (SolidBrush foreground = new SolidBrush(Color.White))
            {
                graphics.FillRectangle(background, labelRect);
                graphics.DrawString(text, font, foreground, labelRect.X + 4, labelRect.Y + 2);
            }
        }

        private static string AppendZoom(string status, string zoomText)
        {
            string marker = "  |  缩放：";
            int index = status.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                status = status.Substring(0, index);
            }

            return status + marker + zoomText;
        }

        private void MoveSelection(int direction)
        {
            if (records.Count == 0)
            {
                return;
            }

            int next = Math.Max(0, Math.Min(records.Count - 1, fileList.SelectedIndex + direction));
            if (next != fileList.SelectedIndex)
            {
                fileList.SelectedIndex = next;
            }
        }

        private void ClearCurrentImage()
        {
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearCurrentImage();
            }

            base.Dispose(disposing);
        }
    }

    internal sealed class ImageRecord
    {
        public ImageRecord(
            string imagePath,
            string annotationPath,
            string annotationKind,
            int shapeCount,
            string displayName,
            Dictionary<int, string> classNames)
        {
            ImagePath = imagePath;
            AnnotationPath = annotationPath;
            AnnotationKind = annotationKind;
            ShapeCount = shapeCount;
            DisplayName = displayName;
            ClassNames = new Dictionary<int, string>(classNames);
        }

        public string ImagePath { get; private set; }

        public string AnnotationPath { get; private set; }

        public string AnnotationKind { get; private set; }

        public int ShapeCount { get; private set; }

        public string DisplayName { get; private set; }

        public Dictionary<int, string> ClassNames { get; private set; }
    }

    internal sealed class FolderAnnotationStats
    {
        public FolderAnnotationStats(string folderName)
        {
            FolderName = folderName;
            ImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AnnotationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AnnotationKinds = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            LabelCounts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        }

        public string FolderName { get; private set; }

        public HashSet<string> ImagePaths { get; private set; }

        public HashSet<string> AnnotationPaths { get; private set; }

        public HashSet<string> AnnotationKinds { get; private set; }

        public Dictionary<string, int> LabelCounts { get; private set; }
    }

    internal sealed class StatsRow
    {
        public const string TotalLabelName = "（全部）";

        public StatsRow(
            string folderName,
            int imageCount,
            int annotationCount,
            string annotationKinds,
            string labelName,
            int boxCount,
            bool isTotal)
        {
            FolderName = folderName;
            ImageCount = imageCount;
            AnnotationCount = annotationCount;
            AnnotationKinds = annotationKinds;
            LabelName = labelName;
            BoxCount = boxCount;
            IsTotal = isTotal;
        }

        public string FolderName { get; private set; }

        public int ImageCount { get; private set; }

        public int AnnotationCount { get; private set; }

        public string AnnotationKinds { get; private set; }

        public string LabelName { get; private set; }

        public int BoxCount { get; private set; }

        public bool IsTotal { get; private set; }
    }

    internal sealed class StatsForm : Form
    {
        private readonly IList<StatsRow> rows;
        private readonly DataGridView grid = new DataGridView();
        private readonly Button copyButton = new Button();
        private readonly Button exportButton = new Button();
        private readonly Button closeButton = new Button();

        public StatsForm(IList<StatsRow> rows, string rootFolder)
        {
            this.rows = rows;
            Text = "标注统计";
            Width = 980;
            Height = 650;
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BuildLayout(rootFolder);
            FillRows();
        }

        private void BuildLayout(string rootFolder)
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            Controls.Add(root);

            int folderCount = rows.Select(row => row.FolderName).Distinct(StringComparer.CurrentCultureIgnoreCase).Count();
            int imageCount = rows.Where(row => row.IsTotal).Sum(row => row.ImageCount);
            int boxCount = rows.Where(row => row.IsTotal).Sum(row => row.BoxCount);

            Label summaryLabel = new Label();
            summaryLabel.Dock = DockStyle.Fill;
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            summaryLabel.Padding = new Padding(10, 0, 10, 0);
            summaryLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "统计范围：{0}    文件夹 {1} 个 / 图片 {2} 张 / 标注框 {3} 个",
                rootFolder,
                folderCount,
                imageCount,
                boxCount);
            root.Controls.Add(summaryLabel, 0, 0);

            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.RowHeadersVisible = false;
            grid.BackgroundColor = Color.White;
            grid.Columns.Add("FolderName", "文件夹");
            grid.Columns.Add("ImageCount", "图片数");
            grid.Columns.Add("AnnotationCount", "标注文件数");
            grid.Columns.Add("AnnotationKinds", "标注格式");
            grid.Columns.Add("LabelName", "类别");
            grid.Columns.Add("BoxCount", "标注框数");
            grid.Columns["FolderName"].FillWeight = 190;
            grid.Columns["ImageCount"].FillWeight = 58;
            grid.Columns["AnnotationCount"].FillWeight = 76;
            grid.Columns["AnnotationKinds"].FillWeight = 90;
            grid.Columns["LabelName"].FillWeight = 130;
            grid.Columns["BoxCount"].FillWeight = 70;
            root.Controls.Add(grid, 0, 1);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Padding = new Padding(0, 7, 8, 0);
            root.Controls.Add(buttons, 0, 2);

            closeButton.Text = "关闭";
            closeButton.Width = 86;
            closeButton.Height = 30;
            closeButton.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(closeButton);

            exportButton.Text = "导出 CSV";
            exportButton.Width = 96;
            exportButton.Height = 30;
            exportButton.Click += delegate { ExportCsv(); };
            buttons.Controls.Add(exportButton);

            copyButton.Text = "复制表格";
            copyButton.Width = 96;
            copyButton.Height = 30;
            copyButton.Click += delegate { CopyTable(); };
            buttons.Controls.Add(copyButton);

            CancelButton = closeButton;
        }

        private void FillRows()
        {
            foreach (StatsRow row in rows)
            {
                int index = grid.Rows.Add(
                    row.FolderName,
                    row.ImageCount,
                    row.AnnotationCount,
                    row.AnnotationKinds,
                    row.LabelName,
                    row.BoxCount);

                if (row.IsTotal)
                {
                    DataGridViewRow gridRow = grid.Rows[index];
                    gridRow.DefaultCellStyle.BackColor = Color.FromArgb(236, 244, 255);
                    gridRow.DefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold);
                }
            }
        }

        private void CopyTable()
        {
            Clipboard.SetText(BuildSeparatedText("\t", false));
            MessageBox.Show(this, "统计表已复制到剪贴板。", "已复制", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExportCsv()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV UTF-8 (*.csv)|*.csv|文本文件 (*.txt)|*.txt";
                dialog.FileName = "annotation_stats_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                File.WriteAllText(dialog.FileName, BuildSeparatedText(",", true), new UTF8Encoding(true));
                MessageBox.Show(this, "统计表已导出。", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private string BuildSeparatedText(string separator, bool csv)
        {
            StringBuilder builder = new StringBuilder();
            AppendSeparatedLine(builder, separator, csv, new string[] { "文件夹", "图片数", "标注文件数", "标注格式", "类别", "标注框数" });
            foreach (StatsRow row in rows)
            {
                AppendSeparatedLine(
                    builder,
                    separator,
                    csv,
                    new string[]
                    {
                        row.FolderName,
                        row.ImageCount.ToString(CultureInfo.InvariantCulture),
                        row.AnnotationCount.ToString(CultureInfo.InvariantCulture),
                        row.AnnotationKinds,
                        row.LabelName,
                        row.BoxCount.ToString(CultureInfo.InvariantCulture)
                    });
            }

            return builder.ToString();
        }

        private static void AppendSeparatedLine(StringBuilder builder, string separator, bool csv, string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(separator);
                }

                builder.Append(csv ? EscapeCsv(values[i]) : values[i]);
            }

            builder.AppendLine();
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }

    internal sealed class ComparisonStatsBucket
    {
        public ComparisonStatsBucket(string folderName, string labelName)
        {
            FolderName = folderName;
            LabelName = labelName;
            ImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public string FolderName { get; private set; }

        public string LabelName { get; private set; }

        public HashSet<string> ImagePaths { get; private set; }

        public int GtCount { get; set; }

        public int PredCount { get; set; }

        public int TruePositiveCount { get; set; }

        public int FalsePositiveCount { get; set; }

        public int FalseNegativeCount { get; set; }

        public ComparisonStatsRow ToRow(bool isTotal)
        {
            return new ComparisonStatsRow(
                FolderName,
                LabelName,
                ImagePaths.Count,
                GtCount,
                PredCount,
                TruePositiveCount,
                FalsePositiveCount,
                FalseNegativeCount,
                isTotal);
        }
    }

    internal sealed class ComparisonStatsRow
    {
        public const string TotalLabelName = "（全部）";

        public ComparisonStatsRow(
            string folderName,
            string labelName,
            int imageCount,
            int gtCount,
            int predCount,
            int truePositiveCount,
            int falsePositiveCount,
            int falseNegativeCount,
            bool isTotal)
        {
            FolderName = folderName;
            LabelName = labelName;
            ImageCount = imageCount;
            GtCount = gtCount;
            PredCount = predCount;
            TruePositiveCount = truePositiveCount;
            FalsePositiveCount = falsePositiveCount;
            FalseNegativeCount = falseNegativeCount;
            IsTotal = isTotal;
        }

        public string FolderName { get; private set; }

        public string LabelName { get; private set; }

        public int ImageCount { get; private set; }

        public int GtCount { get; private set; }

        public int PredCount { get; private set; }

        public int TruePositiveCount { get; private set; }

        public int FalsePositiveCount { get; private set; }

        public int FalseNegativeCount { get; private set; }

        public bool IsTotal { get; private set; }
    }

    internal sealed class ComparisonStatsForm : Form
    {
        private readonly IList<ComparisonStatsRow> rows;
        private readonly string rootFolder;
        private readonly string predFolder;
        private readonly float iouThreshold;
        private readonly DataGridView grid = new DataGridView();
        private readonly Button copyButton = new Button();
        private readonly Button exportButton = new Button();
        private readonly Button closeButton = new Button();

        public ComparisonStatsForm(IList<ComparisonStatsRow> rows, string rootFolder, string predFolder, float iouThreshold)
        {
            this.rows = rows;
            this.rootFolder = rootFolder;
            this.predFolder = predFolder;
            this.iouThreshold = iouThreshold;
            Text = "TP / FP / FN 统计";
            Width = 1120;
            Height = 680;
            MinimumSize = new Size(860, 500);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BuildLayout();
            FillRows();
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            Controls.Add(root);

            int folderCount = rows.Where(row => row.IsTotal).Count();
            int imageCount = rows.Where(row => row.IsTotal).Sum(row => row.ImageCount);
            int gtCount = rows.Where(row => row.IsTotal).Sum(row => row.GtCount);
            int predCount = rows.Where(row => row.IsTotal).Sum(row => row.PredCount);
            int tpCount = rows.Where(row => row.IsTotal).Sum(row => row.TruePositiveCount);
            int fpCount = rows.Where(row => row.IsTotal).Sum(row => row.FalsePositiveCount);
            int fnCount = rows.Where(row => row.IsTotal).Sum(row => row.FalseNegativeCount);

            Label summaryLabel = new Label();
            summaryLabel.Dock = DockStyle.Fill;
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            summaryLabel.Padding = new Padding(10, 0, 10, 0);
            summaryLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "GT：{0}    Pred：{1}    IoU：{2:0.00}    文件夹 {3} 个 / 图片 {4} 张 / TP {5} FP {6} FN {7} / P {8} R {9} F1 {10}",
                rootFolder,
                predFolder,
                iouThreshold,
                folderCount,
                imageCount,
                tpCount,
                fpCount,
                fnCount,
                FormatRate(tpCount, tpCount + fpCount),
                FormatRate(tpCount, tpCount + fnCount),
                FormatF1(tpCount, fpCount, fnCount));
            root.Controls.Add(summaryLabel, 0, 0);

            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.RowHeadersVisible = false;
            grid.BackgroundColor = Color.White;
            grid.Columns.Add("FolderName", "文件夹");
            grid.Columns.Add("LabelName", "类别");
            grid.Columns.Add("ImageCount", "图片数");
            grid.Columns.Add("GtCount", "GT框数");
            grid.Columns.Add("PredCount", "Pred框数");
            grid.Columns.Add("TruePositiveCount", "TP");
            grid.Columns.Add("FalsePositiveCount", "FP");
            grid.Columns.Add("FalseNegativeCount", "FN");
            grid.Columns.Add("Precision", "Precision");
            grid.Columns.Add("Recall", "Recall");
            grid.Columns.Add("F1", "F1");
            grid.Columns["FolderName"].FillWeight = 170;
            grid.Columns["LabelName"].FillWeight = 120;
            grid.Columns["ImageCount"].FillWeight = 58;
            grid.Columns["GtCount"].FillWeight = 58;
            grid.Columns["PredCount"].FillWeight = 64;
            grid.Columns["TruePositiveCount"].FillWeight = 44;
            grid.Columns["FalsePositiveCount"].FillWeight = 44;
            grid.Columns["FalseNegativeCount"].FillWeight = 44;
            grid.Columns["Precision"].FillWeight = 72;
            grid.Columns["Recall"].FillWeight = 72;
            grid.Columns["F1"].FillWeight = 72;
            root.Controls.Add(grid, 0, 1);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Padding = new Padding(0, 7, 8, 0);
            root.Controls.Add(buttons, 0, 2);

            closeButton.Text = "关闭";
            closeButton.Width = 86;
            closeButton.Height = 30;
            closeButton.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(closeButton);

            exportButton.Text = "导出 CSV";
            exportButton.Width = 96;
            exportButton.Height = 30;
            exportButton.Click += delegate { ExportCsv(); };
            buttons.Controls.Add(exportButton);

            copyButton.Text = "复制表格";
            copyButton.Width = 96;
            copyButton.Height = 30;
            copyButton.Click += delegate { CopyTable(); };
            buttons.Controls.Add(copyButton);

            CancelButton = closeButton;
        }

        private void FillRows()
        {
            foreach (ComparisonStatsRow row in rows)
            {
                int index = grid.Rows.Add(
                    row.FolderName,
                    row.LabelName,
                    row.ImageCount,
                    row.GtCount,
                    row.PredCount,
                    row.TruePositiveCount,
                    row.FalsePositiveCount,
                    row.FalseNegativeCount,
                    FormatRate(row.TruePositiveCount, row.TruePositiveCount + row.FalsePositiveCount),
                    FormatRate(row.TruePositiveCount, row.TruePositiveCount + row.FalseNegativeCount),
                    FormatF1(row.TruePositiveCount, row.FalsePositiveCount, row.FalseNegativeCount));

                if (row.IsTotal)
                {
                    DataGridViewRow gridRow = grid.Rows[index];
                    gridRow.DefaultCellStyle.BackColor = Color.FromArgb(236, 244, 255);
                    gridRow.DefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold);
                }
            }
        }

        private void CopyTable()
        {
            Clipboard.SetText(BuildSeparatedText("\t", false));
            MessageBox.Show(this, "统计表已复制到剪贴板。", "已复制", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExportCsv()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV UTF-8 (*.csv)|*.csv|文本文件 (*.txt)|*.txt";
                dialog.FileName = "comparison_stats_iou" + iouThreshold.ToString("0.00", CultureInfo.InvariantCulture).Replace(".", "_") + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                File.WriteAllText(dialog.FileName, BuildSeparatedText(",", true), new UTF8Encoding(true));
                MessageBox.Show(this, "统计表已导出。", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private string BuildSeparatedText(string separator, bool csv)
        {
            StringBuilder builder = new StringBuilder();
            AppendSeparatedLine(builder, separator, csv, new string[] { "文件夹", "类别", "图片数", "GT框数", "Pred框数", "TP", "FP", "FN", "Precision", "Recall", "F1" });
            foreach (ComparisonStatsRow row in rows)
            {
                AppendSeparatedLine(
                    builder,
                    separator,
                    csv,
                    new string[]
                    {
                        row.FolderName,
                        row.LabelName,
                        row.ImageCount.ToString(CultureInfo.InvariantCulture),
                        row.GtCount.ToString(CultureInfo.InvariantCulture),
                        row.PredCount.ToString(CultureInfo.InvariantCulture),
                        row.TruePositiveCount.ToString(CultureInfo.InvariantCulture),
                        row.FalsePositiveCount.ToString(CultureInfo.InvariantCulture),
                        row.FalseNegativeCount.ToString(CultureInfo.InvariantCulture),
                        FormatRate(row.TruePositiveCount, row.TruePositiveCount + row.FalsePositiveCount),
                        FormatRate(row.TruePositiveCount, row.TruePositiveCount + row.FalseNegativeCount),
                        FormatF1(row.TruePositiveCount, row.FalsePositiveCount, row.FalseNegativeCount)
                    });
            }

            return builder.ToString();
        }

        private static void AppendSeparatedLine(StringBuilder builder, string separator, bool csv, string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(separator);
                }

                builder.Append(csv ? EscapeCsv(values[i]) : values[i]);
            }

            builder.AppendLine();
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string FormatRate(int numerator, int denominator)
        {
            if (denominator <= 0)
            {
                return "-";
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.00}%", numerator * 100.0 / denominator);
        }

        private static string FormatF1(int truePositiveCount, int falsePositiveCount, int falseNegativeCount)
        {
            int denominator = 2 * truePositiveCount + falsePositiveCount + falseNegativeCount;
            if (denominator <= 0)
            {
                return "-";
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.00}%", 2.0 * truePositiveCount * 100.0 / denominator);
        }
    }

    internal sealed class MatchResult
    {
        public MatchResult()
        {
            MatchedGtIndexes = new HashSet<int>();
            MatchedPredIndexes = new HashSet<int>();
            FalsePositiveIndexes = new HashSet<int>();
            FalseNegativeIndexes = new HashSet<int>();
            PredToGtIndexes = new Dictionary<int, int>();
        }

        public HashSet<int> MatchedGtIndexes { get; private set; }

        public HashSet<int> MatchedPredIndexes { get; private set; }

        public HashSet<int> FalsePositiveIndexes { get; private set; }

        public HashSet<int> FalseNegativeIndexes { get; private set; }

        public Dictionary<int, int> PredToGtIndexes { get; private set; }

        public int TruePositiveCount { get { return MatchedPredIndexes.Count; } }

        public int FalsePositiveCount { get { return FalsePositiveIndexes.Count; } }

        public int FalseNegativeCount { get { return FalseNegativeIndexes.Count; } }
    }

    internal sealed class AnnotationShape
    {
        public AnnotationShape(string label, string shapeType, IList<PointF> points)
            : this(label, shapeType, points, null, null)
        {
        }

        public AnnotationShape(string label, string shapeType, IList<PointF> points, int? classId, float? confidence)
        {
            Label = label;
            ShapeType = shapeType;
            Points = new List<PointF>(points);
            ClassId = classId;
            Confidence = confidence;
        }

        public string Label { get; private set; }

        public string ShapeType { get; private set; }

        public List<PointF> Points { get; private set; }

        public int? ClassId { get; private set; }

        public float? Confidence { get; private set; }

        public RectangleF? Bounds
        {
            get
            {
                if (Points.Count == 0)
                {
                    return null;
                }

                float minX = Points.Min(point => point.X);
                float minY = Points.Min(point => point.Y);
                float maxX = Points.Max(point => point.X);
                float maxY = Points.Max(point => point.Y);
                return new RectangleF(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
            }
        }
    }
}
