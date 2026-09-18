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
        private readonly TextBox folderText = new TextBox();
        private readonly Button browseButton = new ModernButton();
        private readonly Button reloadButton = new ModernButton();
        private readonly Button statsButton = new ModernButton();
        private readonly Button previousButton = new ModernButton();
        private readonly Button nextButton = new ModernButton();
        private readonly Button fitButton = new ModernButton();
        private readonly Button zoomInButton = new ModernButton();
        private readonly Button zoomOutButton = new ModernButton();
        private readonly Button actualSizeButton = new ModernButton();
        private readonly ModernButton labelsButton = new ModernButton();
        private bool autoFit = true;
        private readonly TextBox searchText = new TextBox();
        private readonly Button clearSearchButton = new ModernButton();
        private readonly ListBox fileList = new FileListBox();
        private readonly ListView annotationList = new ModernAnnotationList();
        private readonly Panel imagePanel = new CanvasPanel();
        private readonly PictureBox pictureBox = new BufferedPictureBox();
        private readonly Label statusLabel = new Label();
        private readonly Label fileInfoLabel = new Label();
        private readonly TextBox predFolderText = new TextBox();
        private readonly Button predBrowseButton = new ModernButton();
        private readonly Label iouLabel = new OptionLabel();
        private readonly NumericUpDown iouNumeric = new NumericUpDown();
        private readonly CheckBox gtOnlyCheckBox = new ToggleSwitch();
        private readonly CheckBox errorAnalysisCheckBox = new ToggleSwitch();
        private readonly Button comparisonStatsButton = new ModernButton();
        private readonly Button exportCompareButton = new ModernButton();

        private readonly List<ImageRecord> records = new List<ImageRecord>();
        private readonly List<ImageRecord> visibleRecords = new List<ImageRecord>();
        private Image sourceImage;
        private List<AnnotationShape> currentShapes = new List<AnnotationShape>();
        private List<AnnotationShape> currentPredShapes = new List<AnnotationShape>();
        private float zoom = 1.0f;
        private bool changingSelection;


        public ViewerForm()
        {
            SuspendLayout();
            Text = "Annatation Viewer · 标注工作台 · " + AppInfo.Version;
            Size = new Size(1500, 940);
            MinimumSize = new Size(1120, 720);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Theme.Background;
            Icon = LoadWindowIcon();
            BuildLayout();
            WireEvents();
            UpdatePredictionControlsState();
            SetStatus("就绪");
            ResumeLayout(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearCurrentImage();
                fileToolTip.Dispose();
                if (Icon != null) Icon.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (StartPosition == FormStartPosition.CenterScreen)
            {
                Rectangle available = Screen.FromControl(this).WorkingArea;
                if (Width > available.Width || Height > available.Height) WindowState = FormWindowState.Maximized;
            }
            InitializePanelWidths();
        }
    }
}
