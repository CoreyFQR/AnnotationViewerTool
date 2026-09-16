using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;


namespace MedVision.AnnotationViewer
{
    internal static class StatisticsService
    {
        internal static List<StatsRow> BuildStatsRows(string rootFolder, IList<ImageRecord> statsRecords, System.Threading.CancellationToken cancellation = default(System.Threading.CancellationToken))
        {
            Dictionary<string, FolderAnnotationStats> folderStats =
                new Dictionary<string, FolderAnnotationStats>(StringComparer.CurrentCultureIgnoreCase);

            foreach (ImageRecord record in statsRecords)
            {
                cancellation.ThrowIfCancellationRequested();
                string folderName = BuildStatsFolderName(rootFolder, record.ImagePath);
                FolderAnnotationStats stats;
                if (!folderStats.TryGetValue(folderName, out stats))
                {
                    stats = new FolderAnnotationStats(folderName);
                    folderStats[folderName] = stats;
                }

                stats.ImagePaths.Add(record.ImagePath);
                stats.AnnotationPaths.Add(record.AnnotationPath);
                stats.AnnotationKinds.Add(record.AnnotationKind);

                Dictionary<string, int> labelCounts = AnnotationReader.CountLabelsByClass(record);
                foreach (KeyValuePair<string, int> item in labelCounts)
                {
                    LabelAnnotationStats labelStats = stats.GetOrCreateLabelStats(item.Key);
                    labelStats.ImagePaths.Add(record.ImagePath);
                    labelStats.AnnotationPaths.Add(record.AnnotationPath);
                    labelStats.BoxCount += item.Value;
                }
            }

            List<StatsRow> rows = new List<StatsRow>();
            foreach (FolderAnnotationStats stats in folderStats.Values.OrderBy(item => item.FolderName, StringComparer.CurrentCultureIgnoreCase))
            {
                int totalBoxes = stats.LabelStats.Values.Sum(item => item.BoxCount);
                string kinds = string.Join(", ", stats.AnnotationKinds.OrderBy(kind => kind, StringComparer.CurrentCultureIgnoreCase).ToArray());
                rows.Add(new StatsRow(
                    stats.FolderName,
                    stats.ImagePaths.Count,
                    stats.AnnotationPaths.Count,
                    kinds,
                    StatsRow.TotalLabelName,
                    totalBoxes,
                    true));

                foreach (LabelAnnotationStats labelStats in stats.LabelStats.Values
                    .OrderByDescending(item => item.BoxCount)
                    .ThenBy(item => item.LabelName, StringComparer.CurrentCultureIgnoreCase))
                {
                    rows.Add(new StatsRow(
                        stats.FolderName,
                        labelStats.ImagePaths.Count,
                        labelStats.AnnotationPaths.Count,
                        kinds,
                        labelStats.LabelName,
                        labelStats.BoxCount,
                        false));
                }
            }

            return rows;
        }

        internal static string BuildStatsFolderName(string rootFolder, string imagePath)
        {
            string imageFolder = Path.GetDirectoryName(imagePath);
            if (string.IsNullOrEmpty(imageFolder))
            {
                return GetDisplayRootFolderName(rootFolder);
            }

            try
            {
                string fullRoot = Path.GetFullPath(rootFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullImageFolder = Path.GetFullPath(imageFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (fullImageFolder.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    string relative = fullImageFolder.Substring(fullRoot.Length).Replace(Path.DirectorySeparatorChar, '/');
                    if (string.IsNullOrEmpty(relative))
                    {
                        return GetDisplayRootFolderName(rootFolder);
                    }

                    if (relative.EndsWith("/images", StringComparison.OrdinalIgnoreCase))
                    {
                        relative = relative.Substring(0, relative.Length - "/images".Length);
                    }
                    else if (string.Equals(relative, "images", StringComparison.OrdinalIgnoreCase))
                    {
                        return GetDisplayRootFolderName(rootFolder);
                    }

                    // Keep the existing images grouping, but distinguish a child named like the root.
                    if (string.Equals(relative, GetDisplayRootFolderName(rootFolder), StringComparison.CurrentCultureIgnoreCase))
                        relative = "./" + relative;
                    return string.IsNullOrEmpty(relative) ? GetDisplayRootFolderName(rootFolder) : relative;
                }
            }
            catch
            {
            }

            return Path.GetFileName(imageFolder);
        }

        internal static string GetDisplayRootFolderName(string folder)
        {
            string trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? "根目录" : name;
        }

        internal static List<ComparisonStatsRow> BuildComparisonStatsRows(
            IList<ImageRecord> compareRecords,
            string predLabelFolder,
            float iouThreshold,
            string rootFolder, System.Threading.CancellationToken cancellation = default(System.Threading.CancellationToken))
        {
            Dictionary<string, ComparisonStatsBucket> buckets =
                new Dictionary<string, ComparisonStatsBucket>(StringComparer.CurrentCultureIgnoreCase);

            foreach (ImageRecord record in compareRecords)
            {
                cancellation.ThrowIfCancellationRequested();
                using (Image image = AnnotationReader.LoadImageWithoutLock(record.ImagePath))
                {
                    List<AnnotationShape> gtShapes = AnnotationReader.LoadAnnotations(record, image.Width, image.Height);
                    List<AnnotationShape> predShapes = PredictionResolver.LoadPredictionsFromFolder(record, predLabelFolder, image.Width, image.Height);
                    MatchResult match = DetectionMatcher.MatchDetections(gtShapes, predShapes, iouThreshold);
                    string folderName = BuildStatsFolderName(rootFolder, record.ImagePath);

                    AddComparisonCounts(
                        buckets,
                        folderName,
                        ComparisonStatsRow.TotalLabelName,
                        record.ImagePath,
                        gtShapes.Count,
                        predShapes.Count,
                        match.TruePositiveCount,
                        match.FalsePositiveCount,
                        match.FalseNegativeCount);

                    foreach (AnnotationShape gtShape in gtShapes)
                    {
                        AddComparisonCounts(buckets, folderName, GetShapeStatsLabel(gtShape), record.ImagePath, 1, 0, 0, 0, 0);
                    }

                    foreach (AnnotationShape predShape in predShapes)
                    {
                        AddComparisonCounts(buckets, folderName, GetShapeStatsLabel(predShape), record.ImagePath, 0, 1, 0, 0, 0);
                    }

                    foreach (KeyValuePair<int, int> pair in match.PredToGtIndexes)
                    {
                        AddComparisonCounts(buckets, folderName, GetShapeStatsLabel(gtShapes[pair.Value]), record.ImagePath, 0, 0, 1, 0, 0);
                    }

                    foreach (int predIndex in match.FalsePositiveIndexes)
                    {
                        AddComparisonCounts(buckets, folderName, GetShapeStatsLabel(predShapes[predIndex]), record.ImagePath, 0, 0, 0, 1, 0);
                    }

                    foreach (int gtIndex in match.FalseNegativeIndexes)
                    {
                        AddComparisonCounts(buckets, folderName, GetShapeStatsLabel(gtShapes[gtIndex]), record.ImagePath, 0, 0, 0, 0, 1);
                    }
                }
            }

            List<ComparisonStatsRow> rows = new List<ComparisonStatsRow>();
            List<string> folderNames = buckets.Values
                .Select(bucket => bucket.FolderName)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (string folderName in folderNames)
            {
                ComparisonStatsBucket totalBucket;
                if (buckets.TryGetValue(BuildComparisonStatsKey(folderName, ComparisonStatsRow.TotalLabelName), out totalBucket))
                {
                    rows.Add(totalBucket.ToRow(true));
                }

                foreach (ComparisonStatsBucket bucket in buckets.Values
                    .Where(item => string.Equals(item.FolderName, folderName, StringComparison.CurrentCultureIgnoreCase) &&
                        !string.Equals(item.LabelName, ComparisonStatsRow.TotalLabelName, StringComparison.CurrentCultureIgnoreCase))
                    .OrderByDescending(item => item.GtCount + item.PredCount)
                    .ThenBy(item => item.LabelName, StringComparer.CurrentCultureIgnoreCase))
                {
                    rows.Add(bucket.ToRow(false));
                }
            }

            return rows;
        }

        internal static void AddComparisonCounts(
            Dictionary<string, ComparisonStatsBucket> buckets,
            string folderName,
            string labelName,
            string imagePath,
            int gtCount,
            int predCount,
            int truePositiveCount,
            int falsePositiveCount,
            int falseNegativeCount)
        {
            string key = BuildComparisonStatsKey(folderName, labelName);
            ComparisonStatsBucket bucket;
            if (!buckets.TryGetValue(key, out bucket))
            {
                bucket = new ComparisonStatsBucket(folderName, labelName);
                buckets[key] = bucket;
            }

            bucket.ImagePaths.Add(imagePath);
            bucket.GtCount += gtCount;
            bucket.PredCount += predCount;
            bucket.TruePositiveCount += truePositiveCount;
            bucket.FalsePositiveCount += falsePositiveCount;
            bucket.FalseNegativeCount += falseNegativeCount;
        }

        internal static string BuildComparisonStatsKey(string folderName, string labelName)
        {
            return folderName.Length.ToString(CultureInfo.InvariantCulture) + ":" + folderName + labelName;
        }

        internal static string GetShapeStatsLabel(AnnotationShape shape)
        {
            string label = shape.Label == null ? string.Empty : shape.Label.Trim();
            if (shape.ClassId.HasValue)
            {
                int colonIndex = label.IndexOf(':');
                if (colonIndex >= 0 && colonIndex < label.Length - 1)
                {
                    label = label.Substring(colonIndex + 1).Trim();
                }
            }

            return label.Length == 0 ? "（空标签）" : label;
        }
    }
}
