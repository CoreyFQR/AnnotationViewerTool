using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal sealed partial class ViewerForm
    {
        private readonly Label canvasTitle = Theme.Label("图像预览", 10, Theme.Ink);
        private readonly Label zoomLabel = Theme.Label("—", 9, Theme.Muted);
        private readonly Label annotationCount = Theme.Label("当前标注", 10, Theme.Ink);
        private readonly Button clearPredButton = new ModernButton();
        private readonly Button cancelWorkButton = new ModernButton();
        private readonly ProgressBar progressBar = new ProgressBar();
        private Control workspace;
        private readonly SplitContainer librarySplit = new SplitContainer();
        private readonly SplitContainer inspectorSplit = new SplitContainer();
        private readonly ToolTip fileToolTip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 100 };
        private int hoveredFile = -1;

        private void InitializePanelWidths()
        {
            using (Graphics g = CreateGraphics())
            {
                float dpi = g.DpiX / 96F;
                librarySplit.Panel1MinSize = (int)(200 * dpi);
                librarySplit.Panel2MinSize = (int)(588 * dpi);
                librarySplit.SplitterDistance = Math.Max(librarySplit.Panel1MinSize,
                    Math.Min((int)(270 * dpi), librarySplit.Width - librarySplit.Panel2MinSize - librarySplit.SplitterWidth));
                inspectorSplit.Panel1MinSize = (int)(330 * dpi);
                inspectorSplit.Panel2MinSize = (int)(250 * dpi);
                inspectorSplit.SplitterDistance = Math.Max(inspectorSplit.Panel1MinSize,
                    inspectorSplit.Width - inspectorSplit.SplitterWidth - (int)(290 * dpi));
            }
        }

        private void BuildLayout()
        {
            SuspendLayout();
            TableLayoutPanel root = Theme.Rows(76, 52, -100, 34);
            root.Padding = new Padding(18, 0, 18, 8);
            Controls.Add(root);
            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildFolderBar(), 0, 1);

            librarySplit.Size = new Size(1400, 700);
            librarySplit.Dock = DockStyle.Fill;
            librarySplit.Margin = new Padding(0, 8, 0, 6);
            librarySplit.SplitterWidth = 8;
            librarySplit.SplitterDistance = 270;
            librarySplit.Panel1MinSize = 200;
            librarySplit.Panel2MinSize = 580;
            librarySplit.FixedPanel = FixedPanel.Panel1;
            librarySplit.AccessibleName = "图像库宽度";
            librarySplit.Paint += DrawSplitterGrip;
            librarySplit.Panel1.Controls.Add(BuildLibrary());
            inspectorSplit.Size = new Size(1120, 700);
            inspectorSplit.Dock = DockStyle.Fill;
            inspectorSplit.SplitterWidth = 8;
            inspectorSplit.SplitterDistance = 830;
            inspectorSplit.Panel1MinSize = 330;
            inspectorSplit.Panel2MinSize = 250;
            inspectorSplit.FixedPanel = FixedPanel.Panel2;
            inspectorSplit.AccessibleName = "检查面板宽度";
            inspectorSplit.Paint += DrawSplitterGrip;
            inspectorSplit.Panel1.Controls.Add(BuildCanvas());
            inspectorSplit.Panel2.Controls.Add(BuildInspector());
            librarySplit.Panel2.Controls.Add(inspectorSplit);
            workspace = librarySplit;
            root.Controls.Add(librarySplit, 0, 2);

            TableLayoutPanel status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.ForeColor = Theme.Muted;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.AutoEllipsis = true;
            status.Controls.Add(statusLabel, 0, 0);
            progressBar.Dock = DockStyle.Fill;
            progressBar.Margin = new Padding(8, 9, 8, 9);
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = false;
            status.Controls.Add(progressBar, 1, 0);
            Theme.Button(cancelWorkButton, "取消", false);
            cancelWorkButton.Dock = DockStyle.Fill;
            cancelWorkButton.Visible = false;
            cancelWorkButton.Click += delegate { if (workCancellation != null) workCancellation.Cancel(); };
            status.Controls.Add(cancelWorkButton, 2, 0);
            root.Controls.Add(status, 0, 3);
            AllowDrop = true;
            DragEnter += delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += async delegate(object sender, DragEventArgs e)
            {
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null && paths.Length > 0 && Directory.Exists(paths[0]))
                    await OpenFolderAsync(paths[0]);
            };
            Theme.ConstrainSingleRows(root);
            ResumeLayout(true);
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4,
                Padding = new Padding(0, 12, 0, 12), Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            PictureBox logo = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 0, 10, 0) };
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MedVision.logo.png"))
            {
                if (stream != null) using (Image original = Image.FromStream(stream)) logo.Image = new Bitmap(original);
            }
            logo.Disposed += delegate { if (logo.Image != null) logo.Image.Dispose(); };
            header.Controls.Add(logo, 0, 0);
            TableLayoutPanel brand = Theme.Rows(30, 22);
            Label title = Theme.Label("MedVision", 18, Theme.Ink);
            title.Font = new Font("Segoe UI", 19F, FontStyle.Bold);
            brand.Controls.Add(title, 0, 0);
            brand.Controls.Add(Theme.Label("标注工作台  /  ANNOTATION VIEWER   ·   " + AppInfo.Version, 8, Theme.Muted), 0, 1);
            header.Controls.Add(brand, 1, 0);
            Theme.Button(statsButton, "标注统计", false);
            Theme.Button(exportCompareButton, "导出对比图", true);
            statsButton.Dock = exportCompareButton.Dock = DockStyle.Fill;
            statsButton.Margin = exportCompareButton.Margin = new Padding(5, 8, 0, 8);
            header.Controls.Add(statsButton, 2, 0);
            header.Controls.Add(exportCompareButton, 3, 0);
            return header;
        }

        private Control BuildFolderBar()
        {
            TableLayoutPanel bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4,
                BackColor = Color.White, Padding = new Padding(12, 5, 8, 5), Margin = new Padding(0) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            bar.Controls.Add(Theme.Label("数据目录", 9, Theme.Muted), 0, 0);
            folderText.ReadOnly = false;
            folderText.AccessibleName = "数据集目录";
            bar.Controls.Add(new TextField(folderText), 1, 0);
            Theme.Button(browseButton, "打开文件夹", true);
            Theme.Button(reloadButton, "刷新", false);
            browseButton.Dock = reloadButton.Dock = DockStyle.Fill;
            bar.Controls.Add(browseButton, 2, 0);
            bar.Controls.Add(reloadButton, 3, 0);
            return bar;
        }

        private Control BuildLibrary()
        {
            TableLayoutPanel library = Theme.Rows(34, 24, 40, 30, -100);
            library.BackColor = Color.White;
            library.Padding = new Padding(14, 10, 14, 8);
            library.Margin = Padding.Empty;
            library.Controls.Add(Theme.Label("图像库", 12, Theme.Ink), 0, 0);
            library.Controls.Add(Theme.Label("搜索文件名", 9, Theme.Muted), 0, 1);
            TableLayoutPanel search = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            searchText.AccessibleName = "搜索图片或标注文件名";
            Theme.Button(clearSearchButton, "清空", false);
            clearSearchButton.Dock = DockStyle.Fill;
            clearSearchButton.Enabled = false;
            search.Controls.Add(new TextField(searchText), 0, 0);
            search.Controls.Add(clearSearchButton, 1, 0);
            library.Controls.Add(search, 0, 2);
            fileInfoLabel.Dock = DockStyle.Fill;
            fileInfoLabel.ForeColor = Theme.Muted;
            fileInfoLabel.Text = "0 张图 / 0 个框";
            fileInfoLabel.TextAlign = ContentAlignment.MiddleLeft;
            fileInfoLabel.AutoEllipsis = true;
            library.Controls.Add(fileInfoLabel, 0, 3);
            fileList.Dock = DockStyle.Fill;
            fileList.BorderStyle = BorderStyle.None;
            fileList.IntegralHeight = false;
            fileList.DrawMode = DrawMode.OwnerDrawFixed;
            fileList.ItemHeight = 57;
            fileList.DrawItem += DrawFileItem;
            fileList.HorizontalScrollbar = true;
            fileList.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                int index = fileList.IndexFromPoint(e.Location);
                if (index == hoveredFile) return;
                hoveredFile = index;
                fileToolTip.SetToolTip(fileList, index >= 0 && index < visibleRecords.Count ? visibleRecords[index].DisplayName : null);
            };
            fileList.MouseLeave += delegate { hoveredFile = -1; fileToolTip.SetToolTip(fileList, null); };
            library.Controls.Add(fileList, 0, 4);
            return library;
        }

        private Control BuildCanvas()
        {
            TableLayoutPanel area = Theme.Rows(44, -100, 48, 34);
            area.BackColor = Color.White;
            area.Margin = Padding.Empty;
            canvasTitle.Padding = new Padding(14, 0, 10, 0);
            area.Controls.Add(canvasTitle, 0, 0);
            imagePanel.Dock = DockStyle.Fill;
            imagePanel.AutoScroll = true;
            imagePanel.BackColor = Theme.Canvas;
            imagePanel.Margin = new Padding(0);
            imagePanel.Paint += PaintEmptyCanvas;
            pictureBox.SizeMode = PictureBoxSizeMode.Normal;
            pictureBox.BackColor = Theme.Canvas;
            pictureBox.Visible = false;
            pictureBox.Paint += PaintImage;
            imagePanel.Controls.Add(pictureBox);
            area.Controls.Add(imagePanel, 0, 1);
            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill,
                WrapContents = false, AutoScroll = true, Padding = new Padding(8, 5, 0, 0), Margin = new Padding(0) };
            Button[] buttons = { previousButton, nextButton, zoomOutButton, zoomInButton, actualSizeButton, fitButton };
            string[] titles = { "←", "→", "−", "+", "100%", "适应窗口" };
            for (int i = 0; i < buttons.Length; i++)
            {
                Theme.Button(buttons[i], titles[i], false);
                buttons[i].Width = i < 4 ? 38 : i == 4 ? 62 : 82;
                toolbar.Controls.Add(buttons[i]);
            }
            Theme.Button(undoButton, "撤销删除", false);
            undoButton.Width = 88;
            undoButton.Enabled = false;
            undoButton.AccessibleDescription = "Ctrl+Z 撤销上一次删除";
            toolbar.Controls.Add(undoButton);
            area.Controls.Add(toolbar, 0, 2);
            FlowLayoutPanel toggles = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
                Padding = new Padding(12, 0, 0, 0), Margin = new Padding(0) };
            fitCheckBox.Text = "自动适应";
            labelsCheckBox.Text = "显示标签";
            fitCheckBox.Checked = labelsCheckBox.Checked = true;
            fitCheckBox.AutoSize = labelsCheckBox.AutoSize = true;
            fitCheckBox.ForeColor = labelsCheckBox.ForeColor = Theme.Muted;
            toggles.Controls.Add(fitCheckBox);
            toggles.Controls.Add(labelsCheckBox);
            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            zoomLabel.Dock = DockStyle.Fill;
            zoomLabel.TextAlign = ContentAlignment.MiddleRight;
            zoomLabel.Margin = new Padding(0, 0, 12, 0);
            footer.Controls.Add(toggles, 0, 0);
            footer.Controls.Add(zoomLabel, 1, 0);
            area.Controls.Add(footer, 0, 3);
            return area;
        }

        private Control BuildInspector()
        {
            TableLayoutPanel inspector = Theme.Rows(34, 24, 40, 40, 38, 36, 40, 42, -100, 26);
            inspector.BackColor = Color.White;
            inspector.Padding = new Padding(14, 10, 14, 8);
            inspector.Controls.Add(Theme.Label("检查与对比", 12, Theme.Ink), 0, 0);
            inspector.Controls.Add(Theme.Label("预测目录", 9, Theme.Muted), 0, 1);
            predFolderText.AccessibleName = "YOLO 预测 labels 文件夹";
            inspector.Controls.Add(new TextField(predFolderText), 0, 2);
            TableLayoutPanel predButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            predButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            predButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            Theme.Button(predBrowseButton, "选择预测目录", false);
            Theme.Button(clearPredButton, "移除", false);
            predBrowseButton.Dock = clearPredButton.Dock = DockStyle.Fill;
            predButtons.Controls.Add(predBrowseButton, 0, 0);
            predButtons.Controls.Add(clearPredButton, 1, 0);
            clearPredButton.Click += delegate { predFolderText.Clear(); };
            inspector.Controls.Add(predButtons, 0, 3);
            TableLayoutPanel threshold = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            threshold.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            threshold.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            iouLabel.Text = "IoU 匹配阈值";
            iouLabel.Dock = DockStyle.Fill;
            iouLabel.TextAlign = ContentAlignment.MiddleLeft;
            iouLabel.ForeColor = Theme.Muted;
            iouNumeric.Dock = DockStyle.Fill;
            iouNumeric.DecimalPlaces = 2;
            iouNumeric.Minimum = 0.01M;
            iouNumeric.Maximum = 1M;
            iouNumeric.Increment = 0.05M;
            iouNumeric.Value = 0.5M;
            iouNumeric.Margin = Padding.Empty;
            threshold.Controls.Add(iouLabel, 0, 0);
            threshold.Controls.Add(iouNumeric, 1, 0);
            inspector.Controls.Add(threshold, 0, 4);
            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            gtOnlyCheckBox.Text = "仅显示 GT";
            errorAnalysisCheckBox.Text = "错误分析";
            gtOnlyCheckBox.AutoSize = errorAnalysisCheckBox.AutoSize = true;
            options.Controls.Add(gtOnlyCheckBox);
            options.Controls.Add(errorAnalysisCheckBox);
            inspector.Controls.Add(options, 0, 5);
            Theme.Button(comparisonStatsButton, "统计误差  ·  TP / FP / FN", false);
            comparisonStatsButton.Dock = DockStyle.Fill;
            inspector.Controls.Add(comparisonStatsButton, 0, 6);
            inspector.Controls.Add(annotationCount, 0, 7);
            annotationList.Dock = DockStyle.Fill;
            annotationList.View = View.Details;
            annotationList.FullRowSelect = true;
            annotationList.HideSelection = false;
            annotationList.MultiSelect = false;
            annotationList.BorderStyle = BorderStyle.None;
            annotationList.ForeColor = Theme.Ink;
            annotationList.Columns.Add("#", 32);
            annotationList.Columns.Add("标签", 102);
            annotationList.Columns.Add("类型", 86);
            annotationList.Columns.Add("坐标", 160);
            inspector.Controls.Add(annotationList, 0, 8);
            inspector.Controls.Add(Theme.Label("GT 真实标注  /  Pred 模型预测", 8, Theme.Muted), 0, 9);
            return inspector;
        }

        private void DrawFileItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= visibleRecords.Count) return;
            ImageRecord record = visibleRecords[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (Brush background = new SolidBrush(selected ? Theme.SoftAccent : Color.White))
                e.Graphics.FillRectangle(background, e.Bounds);
            if (selected) using (Brush accent = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y + 8, 3, e.Bounds.Height - 16);
            int scrollOffset = ((FileListBox)fileList).HorizontalOffset;
            Rectangle name = new Rectangle(e.Bounds.X + 12 - scrollOffset, e.Bounds.Y + 7, Math.Max(e.Bounds.Width, fileList.HorizontalExtent) - 22, 24);
            TextRenderer.DrawText(e.Graphics, record.DisplayName, fileList.Font, name,
                selected ? Theme.Accent : Theme.Ink, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            name.Y += 24;
            name.Height = 20;
            TextRenderer.DrawText(e.Graphics, record.AnnotationKind + "   ·   " + record.ShapeCount + " 个框",
                fileList.Font, name, Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void DrawSplitterGrip(object sender, PaintEventArgs e)
        {
            SplitContainer split = (SplitContainer)sender;
            Rectangle bounds = split.SplitterRectangle;
            using (Brush brush = new SolidBrush(Color.FromArgb(169, 185, 197)))
                for (int i = -1; i <= 1; i++) e.Graphics.FillEllipse(brush,
                    bounds.Left + bounds.Width / 2 - 1, bounds.Top + bounds.Height / 2 + i * 7, 3, 3);
        }
    }
}
