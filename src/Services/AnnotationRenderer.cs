using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace MedVision.AnnotationViewer
{
    internal enum ViewMode { Annotations, Overlay, GroundTruth, Errors }

    /// <summary>One renderer shared by the live canvas and full resolution exports.</summary>
    internal static class AnnotationRenderer
    {
        private static readonly Color[] Palette = {
            Color.FromArgb(82, 158, 255), Color.FromArgb(255, 115, 138), Color.FromArgb(36, 216, 186),
            Color.FromArgb(255, 199, 80), Color.FromArgb(189, 148, 255), Color.FromArgb(146, 220, 99) };
        private static readonly Color Green = Color.FromArgb(42, 216, 144);
        private static readonly Color Orange = Color.FromArgb(255, 170, 67);
        private static readonly Color Red = Color.FromArgb(255, 87, 111);
        private static readonly Color Blue = Color.FromArgb(91, 160, 255);

        public static void Draw(Graphics g, IList<AnnotationShape> gt, IList<AnnotationShape> pred,
            float scale, ViewMode mode, bool labels, float iou)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Font font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold))
            {
                if (mode == ViewMode.Annotations)
                {
                    for (int i = 0; i < gt.Count; i++) DrawShape(g, gt[i], scale, Palette[i % Palette.Length],
                        (i + 1).ToString(), false, labels, font);
                    return;
                }
                if (mode == ViewMode.Errors)
                {
                    MatchResult match = DetectionMatcher.MatchDetections(gt, pred, iou);
                    foreach (int index in match.MatchedPredIndexes) DrawShape(g, pred[index], scale, Green, "TP", false, labels, font);
                    foreach (int index in match.FalsePositiveIndexes) DrawShape(g, pred[index], scale, Red, "FP", false, labels, font);
                    foreach (int index in match.FalseNegativeIndexes) DrawShape(g, gt[index], scale, Blue, "FN", true, labels, font);
                    Legend(g, "TP 正确预测   ·   FP 误检   ·   FN 漏检", font);
                }
                else
                {
                    foreach (AnnotationShape shape in gt) DrawShape(g, shape, scale, Green, "GT", false, labels, font);
                    if (mode == ViewMode.Overlay)
                        foreach (AnnotationShape shape in pred) DrawShape(g, shape, scale, Orange, "Pred", true, labels, font);
                    Legend(g, mode == ViewMode.GroundTruth ? "GT 真实标注" : "GT 绿色实线   ·   Pred 橙色虚线", font);
                }
            }
        }

        private static void DrawShape(Graphics g, AnnotationShape shape, float scale, Color color,
            string prefix, bool dashed, bool labels, Font font)
        {
            if (!shape.Bounds.HasValue) return;
            RectangleF bounds = shape.Bounds.Value;
            RectangleF box = new RectangleF(bounds.X * scale, bounds.Y * scale, bounds.Width * scale, bounds.Height * scale);
            using (Pen pen = new Pen(color, 2F))
            {
                if (dashed) pen.DashStyle = DashStyle.Dash;
                if (shape.Points.Count > 2 && string.Equals(shape.ShapeType, "polygon", StringComparison.OrdinalIgnoreCase))
                    g.DrawPolygon(pen, shape.Points.Select(p => new PointF(p.X * scale, p.Y * scale)).ToArray());
                else g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            }
            if (!labels) return;
            string text = prefix + " · " + shape.Label;
            if (shape.Confidence.HasValue) text += " " + shape.Confidence.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            SizeF size = g.MeasureString(text, font);
            float x = Math.Max(0, box.X);
            float y = Math.Max(0, box.Y - size.Height - 5);
            using (Brush background = new SolidBrush(Color.FromArgb(230, color)))
            using (Brush ink = new SolidBrush(Color.FromArgb(18, 29, 40)))
            {
                g.FillRectangle(background, x, y, size.Width + 8, size.Height + 4);
                g.DrawString(text, font, ink, x + 4, y + 2);
            }
        }

        private static void Legend(Graphics g, string text, Font font)
        {
            SizeF size = g.MeasureString(text, font);
            using (Brush background = new SolidBrush(Color.FromArgb(220, 20, 30, 43)))
            {
                g.FillRectangle(background, 12, 12, size.Width + 20, size.Height + 14);
                g.DrawString(text, font, Brushes.White, 22, 19);
            }
        }

        public static void Save(Image image, IList<AnnotationShape> gt, IList<AnnotationShape> pred,
            ViewMode mode, float iou, string outputPath)
        {
            using (Bitmap bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImageUnscaled(image, 0, 0);
                Draw(graphics, gt, pred, 1F, mode, true, iou);
                bitmap.Save(outputPath, ImageFormat.Jpeg);
            }
        }
    }
}
