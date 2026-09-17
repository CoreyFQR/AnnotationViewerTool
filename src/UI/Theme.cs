using System.Drawing;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static class Theme
    {
        // Screen approximations of PANTONE 294 C, 2945 C and 2905 C.
        public static readonly Color Background = Color.FromArgb(244, 248, 252);
        public static readonly Color Ink = Color.FromArgb(18, 47, 76);
        public static readonly Color Muted = Color.FromArgb(92, 116, 140);
        public static readonly Color Accent = Color.FromArgb(0, 76, 151);
        public static readonly Color AccentHover = Color.FromArgb(0, 87, 184);
        public static readonly Color AccentPressed = Color.FromArgb(0, 47, 108);
        public static readonly Color SoftAccent = Color.FromArgb(229, 243, 250);
        public static readonly Color Border = Color.FromArgb(207, 221, 233);
        public static readonly Color Disabled = Color.FromArgb(233, 240, 246);
        public static readonly Color Canvas = Color.FromArgb(0, 35, 70);
        public static readonly Color CanvasGlyph = Color.FromArgb(141, 200, 232);
        public static readonly Color SwitchOff = Color.FromArgb(159, 178, 195);
        public static readonly Color Divider = Color.FromArgb(145, 166, 185);

        public static Label Label(string text, float size, Color color)
        {
            return new Label { Text = text, AutoSize = false, Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", size), ForeColor = color,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0) };
        }

        public static void Button(Button button, string text, bool primary)
        {
            ModernButton modern = button as ModernButton;
            if (modern != null) modern.Primary = primary;
            button.Text = text;
            button.Height = 34;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : SoftAccent;
            button.BackColor = primary ? Accent : Color.White;
            button.ForeColor = primary ? Color.White : Ink;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(3);
            button.UseVisualStyleBackColor = false;
        }

        public static TableLayoutPanel Rows(params float[] heights)
        {
            TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill,
                ColumnCount = 1, RowCount = heights.Length, Margin = new Padding(0) };
            foreach (float height in heights)
                panel.RowStyles.Add(new RowStyle(height < 0 ? SizeType.Percent : SizeType.Absolute,
                    height < 0 ? -height : height));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return panel;
        }

        public static void TextBox(TextBox box)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Color.FromArgb(248, 250, 252);
            box.ForeColor = Ink;
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 6, 0, 6);
        }

        public static void ConstrainSingleRows(Control root)
        {
            TableLayoutPanel table = root as TableLayoutPanel;
            if (table != null && table.RowStyles.Count == 0)
            {
                table.RowCount = 1;
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            }
            foreach (Control child in root.Controls) ConstrainSingleRows(child);
        }
    }

    internal sealed class BufferedPictureBox : PictureBox
    {
        public BufferedPictureBox() { DoubleBuffered = true; ResizeRedraw = true; }
    }
}
