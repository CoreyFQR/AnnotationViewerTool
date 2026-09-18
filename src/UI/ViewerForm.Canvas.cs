using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal sealed partial class ViewerForm
    {
        private bool updatingCanvas;

        private void FitToWindow()
        {
            if (sourceImage == null || updatingCanvas) return;
            zoom = Math.Min((imagePanel.ClientSize.Width - 32F) / sourceImage.Width,
                (imagePanel.ClientSize.Height - 32F) / sourceImage.Height);
            zoom = Clamp(zoom, 0.005F, 8F);
            imagePanel.AutoScrollPosition = Point.Empty;
            RenderCurrent();
        }

        private void SetZoom(float value)
        {
            autoFit = false;
            zoom = Clamp(value, 0.005F, 8F);
            RenderCurrent();
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private ViewMode CurrentViewMode()
        {
            if (!HasPredictionFolder()) return ViewMode.Annotations;
            if (gtOnlyCheckBox.Checked) return ViewMode.GroundTruth;
            return errorAnalysisCheckBox.Checked ? ViewMode.Errors : ViewMode.Overlay;
        }

        private void RenderCurrent()
        {
            if (updatingCanvas) return;
            if (sourceImage == null) { imagePanel.Invalidate(); return; }
            updatingCanvas = true;
            try
            {
            imagePanel.AutoScroll = !autoFit;
            if (autoFit)
                zoom = Clamp(Math.Min((imagePanel.ClientSize.Width - 32F) / sourceImage.Width,
                    (imagePanel.ClientSize.Height - 32F) / sourceImage.Height), 0.005F, 8F);
            pictureBox.Size = new Size(Math.Max(1, (int)Math.Round(sourceImage.Width * zoom)),
                Math.Max(1, (int)Math.Round(sourceImage.Height * zoom)));
            Point scroll = imagePanel.AutoScrollPosition;
            pictureBox.Location = new Point(Math.Max(0, (imagePanel.ClientSize.Width - pictureBox.Width) / 2) + scroll.X,
                Math.Max(0, (imagePanel.ClientSize.Height - pictureBox.Height) / 2) + scroll.Y);
            pictureBox.Visible = true;
            pictureBox.Invalidate();
            zoomLabel.Text = string.Format("{0:0.#}%", zoom * 100);
            canvasTitle.Text = GetSelectedRecord() == null ? "标注查看" : "标注查看   ·   " + GetSelectedRecord().DisplayName +
                string.Format("   ·   {0} × {1}", sourceImage.Width, sourceImage.Height);
            annotationCount.Text = "当前标注   ·   " + currentShapes.Count;
            }
            finally { updatingCanvas = false; }
        }

        private void PaintImage(object sender, PaintEventArgs e)
        {
            if (sourceImage == null) return;
            // Paint only the invalidated viewport. Never allocate a zoom-sized bitmap.
            e.Graphics.InterpolationMode = zoom > 2 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(sourceImage, new Rectangle(Point.Empty, pictureBox.Size));
            AnnotationRenderer.Draw(e.Graphics, currentShapes, currentPredShapes, zoom,
                CurrentViewMode(), labelsButton.Checked, (float)iouNumeric.Value);
            PaintAnnotationSelection(e.Graphics);
        }

        private void PaintEmptyCanvas(object sender, PaintEventArgs e)
        {
            if (sourceImage != null) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = imagePanel.ClientSize.Width / 2;
            int cy = imagePanel.ClientSize.Height / 2;
            using (Pen frame = new Pen(Theme.CanvasGlyph, 3))
            {
                g.DrawLine(frame, cx - 30, cy - 30, cx - 30, cy - 10);
                g.DrawLine(frame, cx - 30, cy - 30, cx - 10, cy - 30);
                g.DrawLine(frame, cx + 30, cy - 30, cx + 30, cy - 10);
                g.DrawLine(frame, cx + 30, cy - 30, cx + 10, cy - 30);
                g.DrawLine(frame, cx - 30, cy + 30, cx - 30, cy + 10);
                g.DrawLine(frame, cx - 30, cy + 30, cx - 10, cy + 30);
                g.DrawLine(frame, cx + 30, cy + 30, cx + 30, cy + 10);
                g.DrawLine(frame, cx + 30, cy + 30, cx + 10, cy + 30);
                g.DrawEllipse(frame, cx - 10, cy - 10, 20, 20);
            }
            if (records.Count > 0 && visibleRecords.Count == 0)
            {
                using (Brush ink = new SolidBrush(Theme.Muted))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString("没有匹配的图像", Font, ink, new RectangleF(0, cy + 48, imagePanel.Width, 40), format);
            }
        }
    }
}
