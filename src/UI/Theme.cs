using System.Drawing;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(241, 245, 248);
        public static readonly Color Ink = Color.FromArgb(28, 44, 61);
        public static readonly Color Muted = Color.FromArgb(111, 127, 144);
        public static readonly Color Accent = Color.FromArgb(0, 132, 124);
        public static readonly Color SoftAccent = Color.FromArgb(227, 245, 241);
        public static readonly Color Border = Color.FromArgb(223, 231, 237);
        public static readonly Color Disabled = Color.FromArgb(235, 240, 243);
        public static readonly Color Canvas = Color.FromArgb(20, 30, 43);

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
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0, 112, 106) : SoftAccent;
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
