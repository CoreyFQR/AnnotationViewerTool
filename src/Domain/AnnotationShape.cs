using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MedVision.AnnotationViewer
{
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
