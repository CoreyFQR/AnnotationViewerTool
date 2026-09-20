using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static partial class Tests
    {
        private static void TestMatchCache(ViewerForm form)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo gtField = typeof(ViewerForm).GetField("currentShapes", flags);
            FieldInfo predField = typeof(ViewerForm).GetField("currentPredShapes", flags);
            MethodInfo matchMethod = typeof(ViewerForm).GetMethod("GetCurrentMatch", flags);
            object previousGt = gtField.GetValue(form), previousPred = predField.GetValue(form);
            NumericUpDown numeric = Field<NumericUpDown>(form, "iouNumeric");
            decimal previousThreshold = numeric.Value;
            try
            {
                var gt = new List<AnnotationShape> { Box("cell", 0, 1) };
                var pred = new List<AnnotationShape> { new AnnotationShape("cell", "rectangle",
                    new[] { new PointF(0, 0), new PointF(80, 80) }, 0, 1) };
                gtField.SetValue(form, gt); predField.SetValue(form, pred);
                numeric.Value = 0.5M;
                var first = (MatchResult)matchMethod.Invoke(form, null);
                Check(first.TruePositiveCount == 1, "cached matcher preserves reference IoU result");
                Invoke(form, "SetZoom", 0.75F);
                Check(ReferenceEquals(first, matchMethod.Invoke(form, null)), "zoom and redraw reuse matching instead of recomputing");
                numeric.Value = 0.8M;
                var changed = (MatchResult)matchMethod.Invoke(form, null);
                Check(!ReferenceEquals(first, changed) && changed.FalsePositiveCount == 1 && changed.FalseNegativeCount == 1,
                    "IoU threshold changes invalidate cache and update TP FP FN");
                predField.SetValue(form, new List<AnnotationShape> { Box("cell", 0, 1) });
                Check(((MatchResult)matchMethod.Invoke(form, null)).TruePositiveCount == 1,
                    "prediction reload invalidates cached matching even at the same item count");
                gtField.SetValue(form, new List<AnnotationShape>());
                Check(((MatchResult)matchMethod.Invoke(form, null)).FalsePositiveCount == 1,
                    "GT deletion invalidates cached matching");
            }
            finally
            {
                gtField.SetValue(form, previousGt); predField.SetValue(form, previousPred);
                numeric.Value = previousThreshold;
                Field<Button>(form, "fitButton").PerformClick();
            }
        }
    }
}
