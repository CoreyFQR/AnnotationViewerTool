using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace MedVision.AnnotationViewer
{
    internal static class ComparisonExporter
    {
        public static int Export(IList<ImageRecord> records, string predictionFolder, float iou,
            string outputRoot, CancellationToken cancellation, IProgress<string> progress)
        {
            PredictionResolver.ValidateMapping(records, predictionFolder);
            int count = 0;
            foreach (ImageRecord record in records)
            {
                cancellation.ThrowIfCancellationRequested();
                using (Image image = AnnotationReader.LoadImageWithoutLock(record.ImagePath))
                {
                    var gt = AnnotationReader.LoadAnnotations(record, image.Width, image.Height);
                    var pred = PredictionResolver.LoadPredictionsFromFolder(record, predictionFolder, image.Width, image.Height);
                    AnnotationRenderer.Save(image, gt, pred, ViewMode.Overlay, iou,
                        BuildExportPath(Path.Combine(outputRoot, "gt_pred_overlay"), record.DisplayName));
                    cancellation.ThrowIfCancellationRequested();
                    AnnotationRenderer.Save(image, gt, pred, ViewMode.Errors, iou,
                        BuildExportPath(Path.Combine(outputRoot, "tp_fp_fn_analysis"), record.DisplayName));
                }
                count++;
                if (progress != null) progress.Report(string.Format("正在导出对比图  {0} / {1}", count, records.Count));
            }
            return count;
        }

        public static string BuildExportPath(string outputRoot, string displayName)
        {
            string prefix = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            // Preserve the source extension so a.jpg and a.png cannot overwrite one another.
            string path = Path.GetFullPath(Path.Combine(outputRoot,
                displayName.Replace('/', Path.DirectorySeparatorChar) + ".jpg"));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("导出路径超出输出目录。");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path;
        }
    }
}
