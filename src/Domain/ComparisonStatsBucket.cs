using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MedVision.AnnotationViewer
{
    internal sealed class ComparisonStatsBucket
    {
        public ComparisonStatsBucket(string folderName, string labelName)
        {
            FolderName = folderName;
            LabelName = labelName;
            ImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public string FolderName { get; private set; }

        public string LabelName { get; private set; }

        public HashSet<string> ImagePaths { get; private set; }

        public int GtCount { get; set; }

        public int PredCount { get; set; }

        public int TruePositiveCount { get; set; }

        public int FalsePositiveCount { get; set; }

        public int FalseNegativeCount { get; set; }

        public ComparisonStatsRow ToRow(bool isTotal)
        {
            return new ComparisonStatsRow(
                FolderName,
                LabelName,
                ImagePaths.Count,
                GtCount,
                PredCount,
                TruePositiveCount,
                FalsePositiveCount,
                FalseNegativeCount,
                isTotal);
        }
    }
}
