using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MedVision.AnnotationViewer
{
    internal sealed class FolderAnnotationStats
    {
        public FolderAnnotationStats(string folderName)
        {
            FolderName = folderName;
            ImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AnnotationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AnnotationKinds = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            LabelStats = new Dictionary<string, LabelAnnotationStats>(StringComparer.CurrentCultureIgnoreCase);
        }

        public string FolderName { get; private set; }

        public HashSet<string> ImagePaths { get; private set; }

        public HashSet<string> AnnotationPaths { get; private set; }

        public HashSet<string> AnnotationKinds { get; private set; }

        public Dictionary<string, LabelAnnotationStats> LabelStats { get; private set; }

        public LabelAnnotationStats GetOrCreateLabelStats(string labelName)
        {
            LabelAnnotationStats labelStats;
            if (!LabelStats.TryGetValue(labelName, out labelStats))
            {
                labelStats = new LabelAnnotationStats(labelName);
                LabelStats[labelName] = labelStats;
            }

            return labelStats;
        }
    }
}
