using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MedVision.AnnotationViewer
{
    internal sealed class ImageRecord
    {
        public ImageRecord(
            string imagePath,
            string annotationPath,
            string annotationKind,
            int shapeCount,
            string displayName,
            Dictionary<int, string> classNames)
        {
            ImagePath = imagePath;
            AnnotationPath = annotationPath;
            AnnotationKind = annotationKind;
            ShapeCount = shapeCount;
            DisplayName = displayName;
            ClassNames = new Dictionary<int, string>(classNames);
        }

        public string ImagePath { get; private set; }

        public string AnnotationPath { get; private set; }

        public string AnnotationKind { get; private set; }

        public int ShapeCount { get; private set; }

        public string DisplayName { get; private set; }

        public Dictionary<int, string> ClassNames { get; private set; }
    }
}
