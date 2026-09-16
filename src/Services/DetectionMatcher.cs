using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;


namespace MedVision.AnnotationViewer
{
    internal static class DetectionMatcher
    {
        internal static MatchResult MatchDetections(IList<AnnotationShape> gtShapes, IList<AnnotationShape> predShapes, float iouThreshold)
        {
            MatchResult result = new MatchResult();
            HashSet<int> usedGt = new HashSet<int>();
            List<int> predOrder = Enumerable.Range(0, predShapes.Count)
                .OrderByDescending(index => predShapes[index].Confidence.HasValue ? predShapes[index].Confidence.Value : 1.0f)
                .ToList();

            foreach (int predIndex in predOrder)
            {
                AnnotationShape pred = predShapes[predIndex];
                int bestGtIndex = -1;
                float bestIou = 0;

                for (int gtIndex = 0; gtIndex < gtShapes.Count; gtIndex++)
                {
                    if (usedGt.Contains(gtIndex) || !ClassMatches(gtShapes[gtIndex], pred))
                    {
                        continue;
                    }

                    RectangleF? gtBounds = gtShapes[gtIndex].Bounds;
                    RectangleF? predBounds = pred.Bounds;
                    if (!gtBounds.HasValue || !predBounds.HasValue)
                    {
                        continue;
                    }

                    float iou = ComputeIou(gtBounds.Value, predBounds.Value);
                    if (iou >= iouThreshold && iou > bestIou)
                    {
                        bestIou = iou;
                        bestGtIndex = gtIndex;
                    }
                }

                if (bestGtIndex >= 0)
                {
                    usedGt.Add(bestGtIndex);
                    result.MatchedGtIndexes.Add(bestGtIndex);
                    result.MatchedPredIndexes.Add(predIndex);
                    result.PredToGtIndexes[predIndex] = bestGtIndex;
                }
            }

            for (int i = 0; i < predShapes.Count; i++)
            {
                if (!result.MatchedPredIndexes.Contains(i))
                {
                    result.FalsePositiveIndexes.Add(i);
                }
            }

            for (int i = 0; i < gtShapes.Count; i++)
            {
                if (!result.MatchedGtIndexes.Contains(i))
                {
                    result.FalseNegativeIndexes.Add(i);
                }
            }

            return result;
        }

        internal static bool ClassMatches(AnnotationShape gt, AnnotationShape pred)
        {
            if (gt.ClassId.HasValue && pred.ClassId.HasValue)
            {
                return gt.ClassId.Value == pred.ClassId.Value;
            }

            if (!gt.ClassId.HasValue && !pred.ClassId.HasValue)
            {
                return string.Equals(gt.Label, pred.Label, StringComparison.OrdinalIgnoreCase);
            }

            // LabelMe has names while YOLO may have ids. Never match unrelated classes
            // merely because their rectangles overlap.
            return string.Equals(NormalizeLabel(gt), NormalizeLabel(pred), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLabel(AnnotationShape shape)
        {
            string label = (shape.Label ?? string.Empty).Trim();
            if (shape.ClassId.HasValue)
            {
                string prefix = shape.ClassId.Value.ToString(CultureInfo.InvariantCulture) + ":";
                if (label.StartsWith(prefix, StringComparison.Ordinal)) label = label.Substring(prefix.Length).Trim();
            }
            return label;
        }

        internal static float ComputeIou(RectangleF a, RectangleF b)
        {
            float left = Math.Max(a.Left, b.Left);
            float top = Math.Max(a.Top, b.Top);
            float right = Math.Min(a.Right, b.Right);
            float bottom = Math.Min(a.Bottom, b.Bottom);
            float intersectionWidth = Math.Max(0, right - left);
            float intersectionHeight = Math.Max(0, bottom - top);
            float intersection = intersectionWidth * intersectionHeight;
            float union = a.Width * a.Height + b.Width * b.Height - intersection;
            return union <= 0 ? 0 : intersection / union;
        }
    }
}
