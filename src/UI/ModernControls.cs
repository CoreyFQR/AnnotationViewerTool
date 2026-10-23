using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static class ControlGeometry
    {
        // The application is system-DPI aware. Reading this once avoids acquiring window DCs
        // and creating native control handles during preferred-size/layout calculations.
        public static readonly float SystemScale = ReadSystemScale();
        private static float ReadSystemScale()
        {
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) return graphics.DpiX / 96F;
        }
        public static void PaintCorners(Graphics graphics, Rectangle bounds, bool top, bool bottom)
        {
            if ((!top && !bottom) || bounds.Width < 2 || bounds.Height < 2) return;
            GraphicsState state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (!top) graphics.SetClip(new Rectangle(0, bounds.Height / 2, bounds.Width, bounds.Height), CombineMode.Intersect);
            if (!bottom) graphics.SetClip(new Rectangle(0, 0, bounds.Width, bounds.Height / 2), CombineMode.Intersect);
            using (GraphicsPath mask = new GraphicsPath(FillMode.Alternate))
            using (GraphicsPath outline = Rounded(bounds, 14 * graphics.DpiX / 96F))
            using (Brush background = new SolidBrush(Theme.Background))
            {
                mask.AddRectangle(bounds);
                mask.AddPath(outline, false);
                graphics.FillPath(background, mask);
            }
            graphics.Restore(state);
        }
        public static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class ModernButton : Button
    {
        private bool hovered;
        private bool pressed;
        public bool Primary { get; set; }
        public bool Borderless { get; set; }
        private bool isChecked;
        public bool Checked
        {
            get { return isChecked; }
            set { isChecked = value; AccessibleDescription = value ? "已开启" : "已关闭"; Invalidate(); }
        }

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { pressed = false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            float dpi = e.Graphics.DpiX / 96F;
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor.A == 0 ? Color.White : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Theme.Disabled : Primary ?
                (pressed ? Theme.AccentPressed : hovered ? Theme.AccentHover : Theme.Accent) :
                (pressed ? Color.FromArgb(214, 234, 247) : hovered ? Theme.SoftAccent : Borderless && Parent != null ? Parent.BackColor : Color.White);
            using (GraphicsPath shape = ControlGeometry.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 8 * dpi))
            using (Brush background = new SolidBrush(fill))
            using (Pen border = new Pen(!Enabled ? Theme.Border : Primary ? fill : Theme.Border, dpi))
            {
                e.Graphics.FillPath(background, shape);
                if (!Borderless) e.Graphics.DrawPath(border, shape);
            }
            Color ink = !Enabled ? Theme.Muted : Primary ? Color.White : Theme.Ink;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                using (GraphicsPath outline = ControlGeometry.Rounded(new RectangleF(4 * dpi, 4 * dpi,
                    Width - 8 * dpi, Height - 8 * dpi), 5 * dpi))
                using (Pen focus = new Pen(Primary && Enabled ? Color.White : Theme.Accent) { DashStyle = DashStyle.Dot })
                    e.Graphics.DrawPath(focus, outline);
        }
    }

    /// <summary>Native CheckBox behavior and accessibility, with a switch presentation.</summary>
    internal sealed class ToggleSwitch : CheckBox
    {
        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            AutoSize = true;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            float dpi = ControlGeometry.SystemScale;
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
            return new Size(text.Width + (int)(48 * dpi), Math.Max(text.Height + (int)(8 * dpi), (int)(30 * dpi)));
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null || Parent.BackColor.A == 0 ? Color.White : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float dpi = e.Graphics.DpiX / 96F;
            float height = 20 * dpi;
            RectangleF track = new RectangleF(1, (Height - height) / 2, 36 * dpi, height);
            Color fill = !Enabled ? Theme.Disabled : Checked ? Theme.Accent : Theme.SwitchOff;
            using (GraphicsPath shape = ControlGeometry.Rounded(track, height / 2))
            using (Brush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, shape);
            float knob = height - 4 * dpi;
            float x = Checked ? track.Right - knob - 2 * dpi : track.Left + 2 * dpi;
            e.Graphics.FillEllipse(Brushes.White, x, track.Top + 2 * dpi, knob, knob);
            Rectangle text = new Rectangle((int)(44 * dpi), 0, Width - (int)(44 * dpi), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? Theme.Ink : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(text, -1, -3));
        }
    }

    internal sealed class TextField : Panel
    {
        private readonly TextBox editor;
        public TextField(TextBox editor)
        {
            this.editor = editor;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Dock = DockStyle.Fill;
            Margin = new Padding(3);
            BackColor = Color.White;
            editor.BorderStyle = BorderStyle.None;
            editor.Dock = DockStyle.None;
            editor.BackColor = BackColor;
            editor.ForeColor = Theme.Ink;
            editor.Margin = Padding.Empty;
            Controls.Add(editor);
            editor.GotFocus += delegate { Invalidate(); };
            editor.LostFocus += delegate { Invalidate(); };
            Click += delegate { editor.Focus(); };
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (editor == null) return;
            int inset = (int)(11 * ControlGeometry.SystemScale);
            editor.SetBounds(inset, Math.Max(0, (ClientSize.Height - editor.PreferredHeight) / 2),
                Math.Max(1, ClientSize.Width - inset * 2), editor.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Parent == null || Parent.BackColor.A == 0 ? Color.White : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float dpi = e.Graphics.DpiX / 96F;
            using (GraphicsPath outline = ControlGeometry.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 8 * dpi))
            using (Pen pen = new Pen(editor.Focused ? Theme.Accent : Theme.Border, dpi))
            {
                e.Graphics.FillPath(Brushes.White, outline);
                e.Graphics.DrawPath(pen, outline);
            }
        }
    }

    // Keep NumericUpDown as the value/range model; only borderless text and modern buttons are visible.
    internal sealed class NumericField : Panel
    {
        private readonly NumericUpDown model;
        internal readonly TextBox Input = new TextBox();
        internal readonly ModernButton Decrease = new ModernButton();
        internal readonly ModernButton Increase = new ModernButton();
        private bool syncing;
        private bool updatingValue;

        public NumericField(NumericUpDown model)
        {
            this.model = model;
            DoubleBuffered = ResizeRedraw = true;
            Dock = DockStyle.Fill;
            Margin = new Padding(3, 2, 0, 2);
            BackColor = Color.White;
            model.Visible = false;
            model.TabStop = false;
            Controls.Add(model);
            Input.BorderStyle = BorderStyle.None;
            Input.TextAlign = HorizontalAlignment.Center;
            Input.ForeColor = Theme.Ink;
            Input.AccessibleName = model.AccessibleName;
            Theme.Button(Decrease, "−", false);
            Theme.Button(Increase, "+", false);
            Decrease.Borderless = Increase.Borderless = true;
            Input.Margin = Decrease.Margin = Increase.Margin = Padding.Empty;
            Decrease.AccessibleName = "减小 IoU 匹配阈值";
            Increase.AccessibleName = "增大 IoU 匹配阈值";
            Controls.Add(Input); Controls.Add(Decrease); Controls.Add(Increase);
            Decrease.Click += delegate { Step(-1); };
            Increase.Click += delegate { Step(1); };
            Input.TextChanged += delegate {
                decimal value;
                if (!syncing && decimal.TryParse(Input.Text, out value) && value >= model.Minimum && value <= model.Maximum)
                {
                    updatingValue = true;
                    try { model.Value = value; } finally { updatingValue = false; }
                }
            };
            Input.Leave += delegate { CommitText(); Sync(); };
            Input.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) { Step(e.KeyCode == Keys.Up ? 1 : -1); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape) { if (e.KeyCode == Keys.Enter) CommitText(); Sync(); e.SuppressKeyPress = true; }
            };
            Input.MouseWheel += delegate(object sender, MouseEventArgs e) { Step(e.Delta > 0 ? 1 : -1); };
            Input.GotFocus += delegate { Invalidate(); };
            Input.LostFocus += delegate { Invalidate(); };
            model.ValueChanged += delegate { if (!updatingValue) Sync(); };
            model.EnabledChanged += delegate { Sync(); };
            Sync();
        }

        private void CommitText()
        {
            decimal value;
            if (decimal.TryParse(Input.Text, out value)) model.Value = Math.Max(model.Minimum, Math.Min(model.Maximum, value));
        }
        private void Step(int direction)
        {
            if (!model.Enabled) return;
            CommitText();
            model.Value = Math.Max(model.Minimum, Math.Min(model.Maximum, model.Value + direction * model.Increment));
            Sync();
        }
        private void Sync()
        {
            syncing = true;
            Input.Text = model.Value.ToString("F" + model.DecimalPlaces);
            Input.Enabled = Decrease.Enabled = Increase.Enabled = model.Enabled;
            Input.BackColor = model.Enabled ? Color.White : Theme.Disabled;
            BackColor = Input.BackColor;
            syncing = false;
            Invalidate();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Input == null) return;
            float dpi = ControlGeometry.SystemScale;
            int inset = (int)Math.Ceiling(6 * dpi);
            int side = (int)(24 * dpi);
            int gap = (int)(2 * dpi);
            Decrease.SetBounds(inset, inset, side, Math.Max(1, Height - inset * 2));
            Increase.SetBounds(Width - side - inset, inset, side, Math.Max(1, Height - inset * 2));
            Input.SetBounds(inset + side + gap, Math.Max(0, (Height - Input.PreferredHeight) / 2),
                Math.Max(1, Width - 2 * (inset + side + gap)), Input.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Parent == null || Parent.BackColor.A == 0 ? Color.White : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float dpi = e.Graphics.DpiX / 96F;
            using (GraphicsPath shape = ControlGeometry.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 8 * dpi))
            using (Brush fill = new SolidBrush(model.Enabled ? Color.White : Theme.Disabled))
            using (Pen border = new Pen(ContainsFocus ? Theme.Accent : Theme.Border, dpi))
            { e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape); }
        }
    }

    internal sealed class SingleLineLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle bounds = new Rectangle(Padding.Left, Padding.Top,
                Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - Padding.Vertical));
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // Use the same text rendering and disabled ink as the comparison switches.
    internal sealed class OptionLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? Theme.Ink : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
    {
        public bool RoundTop { get; set; }
        public bool RoundBottom { get; set; }
        public BufferedTableLayoutPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            ControlGeometry.PaintCorners(e.Graphics, ClientRectangle, RoundTop, RoundBottom);
        }
    }

    internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public bool RoundBottom { get; set; }
        public BufferedFlowLayoutPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            ControlGeometry.PaintCorners(e.Graphics, ClientRectangle, false, RoundBottom);
        }
    }

    internal sealed class ModernAnnotationList : ListView
    {
        private readonly ImageList rowHeight = new ImageList();
        private bool scaledColumns;
        public ModernAnnotationList()
        {
            DoubleBuffered = true;
            OwnerDraw = true;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            GridLines = false;
            ShowItemToolTips = true;
            rowHeight.ImageSize = new Size(1, 34);
            SmallImageList = rowHeight;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            rowHeight.ImageSize = new Size(1, (int)(34 * ControlGeometry.SystemScale));
            if (!scaledColumns)
            {
                foreach (ColumnHeader column in Columns) column.Width = (int)(column.Width * ControlGeometry.SystemScale);
                scaledColumns = true;
            }
        }
        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using (Brush fill = new SolidBrush(Theme.Background)) e.Graphics.FillRectangle(fill, e.Bounds);
            Rectangle text = Rectangle.Inflate(e.Bounds, -8, 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, text, Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (Pen line = new Pen(Theme.Border)) e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }
        protected override void OnDrawItem(DrawListViewItemEventArgs e) { }
        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item.Selected;
            using (Brush fill = new SolidBrush(selected ? Theme.SoftAccent : e.ItemIndex % 2 == 0 ? Color.White : Theme.Background))
                e.Graphics.FillRectangle(fill, e.Bounds);
            if (selected && e.ColumnIndex == 0)
                using (Brush accent = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top + 5, 3, e.Bounds.Height - 10);
            Rectangle text = Rectangle.Inflate(e.Bounds, -8, 0);
            if (CheckBoxes && e.ColumnIndex == 0)
            {
                var state = e.Item.Checked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal :
                    System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
                Size glyph = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
                CheckBoxRenderer.DrawCheckBox(e.Graphics,
                    new Point(e.Bounds.Left + (int)(4 * ControlGeometry.SystemScale), e.Bounds.Top + (e.Bounds.Height - glyph.Height) / 2), state);
                int indent = (int)(20 * ControlGeometry.SystemScale);
                text.X += indent;
                text.Width = Math.Max(0, text.Width - indent);
            }
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, text, selected ? Theme.Accent : Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (e.Item.Focused && Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -1, -1));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { SmallImageList = null; rowHeight.Dispose(); }
            base.Dispose(disposing);
        }
    }
    internal sealed class CanvasPanel : Panel
    {
        public CanvasPanel() { DoubleBuffered = true; ResizeRedraw = true; SetStyle(ControlStyles.Selectable, true); TabStop = true; }
    }

    internal sealed class FileListBox : ListBox
    {
        internal int TextLineHeight { get; private set; }
        internal int TextLineGap { get; private set; }

        public FileListBox() { UpdateRowHeight(); }

        private void UpdateRowHeight()
        {
            float scale = ControlGeometry.SystemScale;
            TextLineHeight = TextRenderer.MeasureText("国Ag", Font, Size.Empty, TextFormatFlags.SingleLine).Height;
            TextLineGap = (int)Math.Ceiling(2 * scale);
            // Owner-drawn rows must reserve two complete lines at the actual font size.
            ItemHeight = Math.Max((int)Math.Ceiling(57 * scale),
                2 * TextLineHeight + TextLineGap + (int)Math.Ceiling(14 * scale));
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateRowHeight();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRowHeight();
        }

        [DllImport("user32.dll")]
        private static extern int GetScrollPos(IntPtr window, int bar);

        public int HorizontalOffset { get { return IsHandleCreated ? GetScrollPos(Handle, 0) : 0; } }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x114) Invalidate(); // Owner-drawn rows must use the native scroll offset.
        }
    }
}
