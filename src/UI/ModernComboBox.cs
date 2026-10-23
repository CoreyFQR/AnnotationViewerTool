using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    /// <summary>Native dropdown behavior with the same rounded field styling as the other controls.</summary>
    internal sealed class ModernComboBox : ComboBox
    {
        private bool hovered;

        public ModernComboBox()
        {
            // Keep native mouse capture and dropdown tracking. Only replace the closed field's paint.
            SetStyle(ControlStyles.ResizeRedraw, true);
            DropDownStyle = ComboBoxStyle.DropDownList;
            IntegralHeight = false;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = Color.White;
            ForeColor = Theme.Ink;
            Cursor = Cursors.Hand;
            UpdateItemHeight();
        }

        private void UpdateItemHeight()
        {
            ItemHeight = Math.Max(Font.Height + (int)Math.Ceiling(8 * ControlGeometry.SystemScale),
                (int)Math.Ceiling(28 * ControlGeometry.SystemScale));
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateItemHeight();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateItemHeight();
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }

        protected override void OnDropDown(EventArgs e)
        {
            DropDownHeight = Math.Max(1, Math.Min(Items.Count, MaxDropDownItems)) * ItemHeight + 2;
            int width = Width;
            foreach (object item in Items)
                width = Math.Max(width, TextRenderer.MeasureText(GetItemText(item), Font).Width +
                    (int)Math.Ceiling(48 * ControlGeometry.SystemScale));
            DropDownWidth = Math.Min(width, Screen.FromControl(this).WorkingArea.Width);
            base.OnDropDown(e);
            Invalidate();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            // WM_PAINT / WM_PRINT / WM_PRINTCLIENT: decorate after native processing, including snapshots.
            if (message.Msg == 0x000F && IsHandleCreated)
            {
                using (Graphics graphics = Graphics.FromHwnd(Handle)) DrawField(graphics);
            }
            else if ((message.Msg == 0x0317 || message.Msg == 0x0318) && message.WParam != IntPtr.Zero)
            {
                using (Graphics graphics = Graphics.FromHdc(message.WParam)) DrawField(graphics);
            }
        }

        private void DrawField(Graphics graphics)
        {
            float scale = graphics.DpiX / 96F;
            if (Width < 3 || Height < 3) return;
            // A print DC can be shared with the whole form: paint only this control's rectangle.
            using (Brush surface = new SolidBrush(Parent == null || Parent.BackColor.A == 0 ? Color.White : Parent.BackColor))
                graphics.FillRectangle(surface, ClientRectangle);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Theme.Disabled : hovered || DroppedDown ? Theme.SoftAccent : Color.White;
            Color edge = Enabled && (Focused || DroppedDown) ? Theme.Accent : Theme.Border;
            using (GraphicsPath outline = ControlGeometry.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 8 * scale))
            using (Brush background = new SolidBrush(fill))
            using (Pen border = new Pen(edge, scale))
            {
                graphics.FillPath(background, outline);
                graphics.DrawPath(border, outline);
            }
            int inset = (int)Math.Ceiling(11 * scale);
            Rectangle textBounds = new Rectangle(inset, 0, Math.Max(0, Width - inset - (int)(34 * scale)), Height);
            TextRenderer.DrawText(graphics, Text, Font, textBounds, Enabled ? Theme.Ink : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            float x = Width - 17 * scale;
            float y = Height / 2F;
            float direction = DroppedDown ? -1 : 1;
            using (Pen arrow = new Pen(Enabled ? Theme.Accent : Theme.Muted, 1.5F * scale)
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                graphics.DrawLines(arrow, new[] { new PointF(x - 4 * scale, y - direction * 2 * scale),
                    new PointF(x, y + direction * 2 * scale), new PointF(x + 4 * scale, y - direction * 2 * scale) });
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            float scale = e.Graphics.DpiX / 96F;
            bool highlighted = (e.State & DrawItemState.Selected) != 0;
            using (Brush background = new SolidBrush(highlighted ? Theme.SoftAccent : Color.White))
                e.Graphics.FillRectangle(background, e.Bounds);
            int inset = (int)Math.Ceiling(11 * scale);
            Rectangle textBounds = new Rectangle(e.Bounds.Left + inset, e.Bounds.Top,
                Math.Max(0, e.Bounds.Width - inset - (int)(30 * scale)), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, textBounds,
                highlighted ? Theme.Accent : Theme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (e.Index == SelectedIndex)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float x = e.Bounds.Right - 17 * scale;
                float y = e.Bounds.Top + e.Bounds.Height / 2F;
                using (Pen check = new Pen(Theme.Accent, 1.5F * scale)
                    { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    e.Graphics.DrawLines(check, new[] { new PointF(x - 4 * scale, y),
                        new PointF(x - scale, y + 3 * scale), new PointF(x + 4 * scale, y - 3 * scale) });
            }
        }
    }
}
