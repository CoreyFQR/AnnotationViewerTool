using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MedVision.AnnotationViewer
{
    internal sealed class LabelAnnotationStats
    {
        public LabelAnnotationStats(string labelName)
        {
            LabelName = labelName;
            ImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AnnotationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public string LabelName { get; private set; }

        public HashSet<string> ImagePaths { get; private set; }

        public HashSet<string> AnnotationPaths { get; private set; }

        public int BoxCount { get; set; }
    }
}
