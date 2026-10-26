using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace MedVision.AnnotationViewer
{
    internal static partial class Tests
    {
        private static void TestDegeneratePredictions()
        {
            string folder = Path.Combine(qa, "degenerate-predictions-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(folder, "dataset");
            string pred = Path.Combine(folder, "predictions");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(pred);
            File.Copy(Path.Combine(dataset, "train", "images", "sample_001.png"), Path.Combine(root, "sample.png"));
            File.WriteAllText(Path.Combine(root, "classes.txt"), "cell");
            string annotation = Path.Combine(root, "sample.txt");
            File.WriteAllText(annotation, "0 0.2 0.2 0.1 0.1\n0 0.6 0.6 0.1 0.1\n");
            string path = Path.Combine(pred, "sample.txt");
            // This exact edge-clipped row previously prevented the entire prediction file from loading.
            const string clippedHeight = "2 0.664916 1 0.0275548 0 0.00210271";
            const string clippedWidth = "0 0 0.542219 0 0.223251 0.00102918";
            File.WriteAllText(path, "0 0.2 0.2 0.1 0.1 0.9\n" + clippedHeight + "\n" +
                "0 0.6 0.6 0.1 0.1 0.1\n" + clippedWidth + "\n0 1 1 0 0\n0 0 1 1 1\n");
            byte[] originalPrediction = File.ReadAllBytes(path);
            byte[] originalAnnotation = File.ReadAllBytes(annotation);
            var records = DatasetScanner.BuildRecordsRecursively(root);
            var raw = PredictionResolver.LoadPredictionsFromFolder(records[0], pred, 960, 720);
            Check(raw.Count == 3 && raw[0].Confidence == 0.9F && raw[1].Confidence == 0.1F && !raw[2].Confidence.HasValue,
                "zero-height and zero-width predictions are skipped while valid rows retain order and confidence");
            Check(raw[2].Bounds.HasValue && raw[2].Bounds.Value.Width == 960 && raw[2].Bounds.Value.Height == 720,
                "normalized endpoint centers and full-size positive predictions remain valid");
            var filtered = PredictionResolver.LoadPredictionsFromFolder(records[0], pred, 960, 720, 0.25F);
            Check(filtered.Count == 2 && filtered[0].Confidence == 0.9F && !filtered[1].Confidence.HasValue,
                "confidence filtering follows zero-area removal and retains legacy unscored predictions");
            Throws<InvalidDataException>(() => AnnotationReader.LoadYoloAnnotations(path, 960, 720, Classes, false),
                "zero-area ground truth remains an error even when the same rows are tolerated as predictions");

            string line42 = Path.Combine(folder, "line42.txt");
            File.WriteAllLines(line42, Enumerable.Repeat("0 0.2 0.2 0.1 0.1 0.9", 41)
                .Concat(new[] { clippedHeight, "0 0.6 0.6 0.1 0.1 0.1" }).ToArray());
            var after42 = AnnotationReader.LoadYoloAnnotations(line42, 960, 720, Classes, true);
            Check(after42.Count == 42 && after42[41].Confidence == 0.1F,
                "the real zero-height row at line 42 does not discard predictions before or after it");
            string invalid = Path.Combine(folder, "invalid.txt");
            foreach (string row in new[]
            {
                "0 0.5 0.5 -0.1 0.1 0.7", "0 0.5 0.5 0.1 -0.1 0.7",
                "0 NaN 0.5 0 0.1 0.7", "0 0.5 Infinity 0.1 0 0.7",
                "0 1.1 0.5 0 0.1 0.7", "0 0.5 0.5 1.1 0 0.7",
                "0 0.5 0.5 0 0.1 1.1", "0 0.5 0.5 0.1 0 -0.1",
                "0 0.5 0.5 0 0.1 NaN", "0 0.5 0.5 0.1 0 Infinity",
                "0 0.5 0.5 0 0 bad", "-1 0.5 0.5 0 0 0.7"
            })
            {
                File.WriteAllText(invalid, row);
                Throws<InvalidDataException>(() => AnnotationReader.LoadYoloAnnotations(invalid, 960, 720, Classes, true),
                    "degenerate prediction tolerance never accepts malformed row: " + row);
            }
            foreach (string row in new[] { clippedHeight, clippedWidth, "0 0.5 0.5 0 0" })
            {
                File.WriteAllText(invalid, row);
                Check(AnnotationReader.LoadYoloAnnotations(invalid, 960, 720, Classes, true).Count == 0,
                    "prediction file containing only a zero-area row loads as empty");
                Throws<InvalidDataException>(() => AnnotationReader.LoadYoloAnnotations(invalid, 960, 720, Classes, false),
                    "ground-truth validation still rejects every zero-area form");
            }

            var total = StatisticsService.BuildComparisonStatsRows(records, pred, 0.5F, root, CancellationToken.None, 0.25F)
                .Single(r => r.IsTotal);
            Check(total.GtCount == 2 && total.PredCount == 2 && total.TruePositiveCount == 1 &&
                total.FalsePositiveCount == 1 && total.FalseNegativeCount == 1,
                "statistics process the original file and exclude degenerate and low-score predictions");
            var all = StatisticsService.BuildComparisonStatsRows(records, pred, 0.5F, root).Single(r => r.IsTotal);
            Check(all.PredCount == 3 && all.TruePositiveCount == 2 && all.FalsePositiveCount == 1 && all.FalseNegativeCount == 0,
                "zero confidence threshold still excludes zero-area boxes while retaining low-score valid boxes");
            string output = Path.Combine(folder, "exports");
            Check(ComparisonExporter.Export(records, pred, 0.5F, output, CancellationToken.None, null, 0.25F) == 1,
                "comparison export accepts the original prediction file with degenerate rows");
            using (Image image = AnnotationReader.LoadImageWithoutLock(records[0].ImagePath))
            {
                var gt = AnnotationReader.LoadAnnotations(records[0], image.Width, image.Height);
                foreach (ViewMode mode in new[] { ViewMode.Overlay, ViewMode.Errors })
                {
                    string subfolder = mode == ViewMode.Overlay ? "gt_pred_overlay" : "tp_fp_fn_analysis";
                    string actual = ComparisonExporter.BuildExportPath(Path.Combine(output, subfolder), records[0].DisplayName);
                    string expected = Path.Combine(folder, "reference-" + mode + ".jpg");
                    AnnotationRenderer.Save(image, gt, filtered, mode, 0.5F, expected);
                    Check(File.ReadAllBytes(actual).SequenceEqual(File.ReadAllBytes(expected)),
                        "degenerate-row " + mode + " export contains exactly the valid filtered predictions");
                }
            }
            Check(File.ReadAllBytes(path).SequenceEqual(originalPrediction) &&
                File.ReadAllBytes(annotation).SequenceEqual(originalAnnotation),
                "loading, statistics and export never rewrite original prediction or GT files");
        }
    }
}
