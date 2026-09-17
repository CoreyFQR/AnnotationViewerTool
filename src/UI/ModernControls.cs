using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static class ControlGeometry
    {
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
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Theme.Disabled : Primary ?
                (pressed ? Theme.AccentPressed : hovered ? Theme.AccentHover : Theme.Accent) :
                (pressed ? Color.FromArgb(214, 234, 247) : hovered ? Theme.SoftAccent : Color.White);
            using (GraphicsPath shape = ControlGeometry.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 8 * dpi))
            using (Brush background = new SolidBrush(fill))
            using (Pen border = new Pen(!Enabled ? Theme.Border : Primary ? fill : Theme.Border, dpi))
            {
                e.Graphics.FillPath(background, shape);
                e.Graphics.DrawPath(border, shape);
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
            using (Graphics g = CreateGraphics())
            {
                float dpi = g.DpiX / 96F;
                Size text = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.SingleLine);
                return new Size(text.Width + (int)(48 * dpi), Math.Max(text.Height + (int)(8 * dpi), (int)(30 * dpi)));
            }
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
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
            using (Graphics g = CreateGraphics())
            {
                int inset = (int)(11 * g.DpiX / 96F);
                editor.SetBounds(inset, Math.Max(0, (ClientSize.Height - editor.PreferredHeight) / 2),
                    Math.Max(1, ClientSize.Width - inset * 2), editor.PreferredHeight);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
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

    internal sealed class NumericField : Panel
    {
        private readonly NumericUpDown editor;
        private int hoveredSpin;

        public NumericField(NumericUpDown editor)
        {
            this.editor = editor;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Dock = DockStyle.Fill;
            Margin = new Padding(3, 2, 0, 2);
            BackColor = Color.White;
            editor.BorderStyle = BorderStyle.None;
            editor.BackColor = BackColor;
            editor.ForeColor = Theme.Ink;
            editor.TextAlign = HorizontalAlignment.Center;
            editor.Margin = Padding.Empty;
            Controls.Add(editor);
            HideNativeButtons();
            editor.GotFocus += delegate { Invalidate(); };
            editor.LostFocus += delegate { Invalidate(); };
            editor.ValueChanged += delegate { Invalidate(); };
            Click += delegate { editor.Focus(); };
        }

        private int SpinWidth { get { return Math.Max(26, Height - 2); } }

        private void HideNativeButtons()
        {
            foreach (Control child in editor.Controls)
                if (child.GetType().Name.IndexOf("Buttons", StringComparison.OrdinalIgnoreCase) >= 0)
                    child.Visible = false;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (editor == null) return;
            using (Graphics g = CreateGraphics())
            {
                int horizontal = (int)(9 * g.DpiX / 96F);
                editor.SetBounds(horizontal, Math.Max(0, (ClientSize.Height - editor.PreferredHeight) / 2),
                    Math.Max(1, ClientSize.Width - horizontal - SpinWidth - 2), editor.PreferredHeight);
                HideNativeButtons();
                foreach (Control child in editor.Controls)
                    if (child.Visible) child.SetBounds(0, 0, editor.Width, editor.Height);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int next = e.X >= Width - SpinWidth ? (e.Y < Height / 2 ? 1 : -1) : 0;
            if (hoveredSpin != next) { hoveredSpin = next; Invalidate(); }
            Cursor = next == 0 ? Cursors.Default : Cursors.Hand;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoveredSpin = 0;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.X >= Width - SpinWidth)
            {
                editor.Focus();
                decimal change = e.Y < Height / 2 ? editor.Increment : -editor.Increment;
                editor.Value = Math.Max(editor.Minimum, Math.Min(editor.Maximum, editor.Value + change));
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float dpi = e.Graphics.DpiX / 96F;
            Color border = editor.Focused ? Theme.Accent : Theme.Border;
            int spinLeft = Width - SpinWidth;
            using (GraphicsPath outline = ControlGeometry.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 8 * dpi))
            using (Brush fill = new SolidBrush(editor.Enabled ? Color.White : Theme.Disabled))
            using (Pen pen = new Pen(border, editor.Focused ? 1.5F * dpi : dpi))
            {
                e.Graphics.FillPath(fill, outline);
                if (editor.Enabled && hoveredSpin != 0)
                {
                    GraphicsState state = e.Graphics.Save();
                    e.Graphics.SetClip(outline);
                    using (Brush hover = new SolidBrush(Theme.SoftAccent))
                        e.Graphics.FillRectangle(hover, spinLeft, hoveredSpin > 0 ? 1 : Height / 2,
                            SpinWidth, Height / 2);
                    e.Graphics.Restore(state);
                }
                using (Pen divider = new Pen(Theme.Border, dpi))
                {
                    e.Graphics.DrawLine(divider, spinLeft, 5 * dpi, spinLeft, Height - 5 * dpi);
                    e.Graphics.DrawLine(divider, spinLeft + 5 * dpi, Height / 2F,
                        Width - 5 * dpi, Height / 2F);
                }
                e.Graphics.DrawPath(pen, outline);
            }
            float centerX = spinLeft + SpinWidth / 2F;
            using (Pen arrow = new Pen(editor.Enabled ? Theme.Accent : Theme.Muted, 1.6F * dpi))
            {
                arrow.StartCap = LineCap.Round;
                arrow.EndCap = LineCap.Round;
                float offset = 3 * dpi;
                float upper = Height * 0.27F;
                float lower = Height * 0.73F;
                e.Graphics.DrawLines(arrow, new[] { new PointF(centerX - offset, upper + offset / 2),
                    new PointF(centerX, upper - offset / 2), new PointF(centerX + offset, upper + offset / 2) });
                e.Graphics.DrawLines(arrow, new[] { new PointF(centerX - offset, lower - offset / 2),
                    new PointF(centerX, lower + offset / 2), new PointF(centerX + offset, lower - offset / 2) });
            }
        }
    }

    internal sealed class CanvasPanel : Panel
    {
        public CanvasPanel() { DoubleBuffered = true; ResizeRedraw = true; SetStyle(ControlStyles.Selectable, true); TabStop = true; }
    }

    internal sealed class FileListBox : ListBox
    {
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
