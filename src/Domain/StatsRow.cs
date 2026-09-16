using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MedVision.AnnotationViewer
{
    internal sealed class StatsRow
    {
        public const string TotalLabelName = "（全部）";

        public StatsRow(
            string folderName,
            int imageCount,
            int annotationCount,
            string annotationKinds,
            string labelName,
            int boxCount,
            bool isTotal)
        {
            FolderName = folderName;
            ImageCount = imageCount;
            AnnotationCount = annotationCount;
            AnnotationKinds = annotationKinds;
            LabelName = labelName;
            BoxCount = boxCount;
            IsTotal = isTotal;
        }

        public string FolderName { get; private set; }

        public int ImageCount { get; private set; }

        public int AnnotationCount { get; private set; }

        public string AnnotationKinds { get; private set; }

        public string LabelName { get; private set; }

        public int BoxCount { get; private set; }

        public bool IsTotal { get; private set; }
    }
}
