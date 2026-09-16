using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MedVision.AnnotationViewer
{
    internal sealed class ComparisonStatsRow
    {
        public const string TotalLabelName = "（全部）";

        public ComparisonStatsRow(
            string folderName,
            string labelName,
            int imageCount,
            int gtCount,
            int predCount,
            int truePositiveCount,
            int falsePositiveCount,
            int falseNegativeCount,
            bool isTotal)
        {
            FolderName = folderName;
            LabelName = labelName;
            ImageCount = imageCount;
            GtCount = gtCount;
            PredCount = predCount;
            TruePositiveCount = truePositiveCount;
            FalsePositiveCount = falsePositiveCount;
            FalseNegativeCount = falseNegativeCount;
            IsTotal = isTotal;
        }

        public string FolderName { get; private set; }

        public string LabelName { get; private set; }

        public int ImageCount { get; private set; }

        public int GtCount { get; private set; }

        public int PredCount { get; private set; }

        public int TruePositiveCount { get; private set; }

        public int FalsePositiveCount { get; private set; }

        public int FalseNegativeCount { get; private set; }

        public bool IsTotal { get; private set; }
    }
}
