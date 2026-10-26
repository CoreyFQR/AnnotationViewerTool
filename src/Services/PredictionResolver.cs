using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;


namespace MedVision.AnnotationViewer
{
    internal static class PredictionResolver
    {
        internal static string ResolvePredictionLabelFolder(string selectedFolder)
        {
            if (string.IsNullOrEmpty(selectedFolder) || !Directory.Exists(selectedFolder))
            {
                return null;
            }

            string directLabels = Path.Combine(selectedFolder, "labels");
            if (Directory.Exists(directLabels))
            {
                return directLabels;
            }

            return Path.GetFullPath(selectedFolder);
        }

        public static string ResolvePath(ImageRecord record, string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            string relative = record.DisplayName.Replace('/', Path.DirectorySeparatorChar);
            string[] parts = relative.Split(Path.DirectorySeparatorChar);
            string withLabels = string.Join(Path.DirectorySeparatorChar.ToString(),
                parts.Select(p => string.Equals(p, "images", StringComparison.OrdinalIgnoreCase) ? "labels" : p).ToArray());
            string withoutImages = string.Join(Path.DirectorySeparatorChar.ToString(),
                parts.Where(p => !string.Equals(p, "images", StringComparison.OrdinalIgnoreCase)).ToArray());
            string[] candidates = { Path.ChangeExtension(withLabels, ".txt"),
                Path.ChangeExtension(withoutImages, ".txt"), Path.ChangeExtension(relative, ".txt"),
                Path.GetFileNameWithoutExtension(record.ImagePath) + ".txt" };
            string prefix = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string candidate in candidates)
            {
                string path = Path.GetFullPath(Path.Combine(folder, candidate));
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("预测路径超出选定目录。");
                if (File.Exists(path)) return path;
                // Explicit fallback also supports case-sensitive folders and shares.
                string matched = ResolvePathIgnoringCase(prefix, path.Substring(prefix.Length));
                if (matched != null) return matched;
            }
            return null;
        }

        private static string ResolvePathIgnoringCase(string folder, string relative)
        {
            string[] parts = relative.Split(Path.DirectorySeparatorChar);
            string current = folder;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!Directory.Exists(current)) return null;
                bool isFile = i == parts.Length - 1;
                string direct = Path.Combine(current, parts[i]);
                if (isFile ? File.Exists(direct) : Directory.Exists(direct))
                {
                    current = direct;
                    continue;
                }
                string match = null;
                IEnumerable<string> entries = isFile ? Directory.EnumerateFiles(current) : Directory.EnumerateDirectories(current);
                foreach (string entry in entries)
                {
                    if (!string.Equals(Path.GetFileName(entry), parts[i], StringComparison.OrdinalIgnoreCase)) continue;
                    if (match != null)
                        throw new InvalidDataException("存在多个仅大小写不同的预测路径，无法确定配对：" + direct);
                    match = entry;
                }
                if (match == null) return null;
                current = match;
            }
            return current;
        }

        public static void ValidateMapping(IList<ImageRecord> records, string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            Dictionary<string, string> used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ImageRecord record in records)
            {
                string path = ResolvePath(record, folder);
                if (path == null) continue;
                string previous;
                if (used.TryGetValue(path, out previous))
                    throw new InvalidDataException("同名图片 " + previous + " 与 " + record.DisplayName +
                        " 指向同一预测文件。请按数据集子目录组织预测 labels。");
                used[path] = record.DisplayName;
            }
        }

        internal static void ValidateConfidenceThreshold(float minConfidence)
        {
            if (float.IsNaN(minConfidence) || float.IsInfinity(minConfidence) || minConfidence < 0F || minConfidence > 1F)
                throw new ArgumentOutOfRangeException("minConfidence", "置信度阈值须为 0–1。");
        }

        internal static List<AnnotationShape> FilterByConfidence(IList<AnnotationShape> shapes, float minConfidence)
        {
            ValidateConfidenceThreshold(minConfidence);
            if (shapes == null) throw new ArgumentNullException("shapes");
            // Five-column prediction files do not contain scores; retain their previous behavior.
            return shapes.Where(shape => !shape.Confidence.HasValue || shape.Confidence.Value >= minConfidence).ToList();
        }

        internal static List<AnnotationShape> LoadPredictionsFromFolder(ImageRecord record, string predLabelFolder,
            int imageWidth, int imageHeight, float minConfidence = 0F)
        {
            ValidateConfidenceThreshold(minConfidence);
            if (string.IsNullOrEmpty(predLabelFolder) || !Directory.Exists(predLabelFolder))
            {
                return new List<AnnotationShape>();
            }

            string predPath = ResolvePath(record, predLabelFolder);
            if (predPath == null)
            {
                return new List<AnnotationShape>();
            }

            return FilterByConfidence(
                AnnotationReader.LoadYoloAnnotations(predPath, imageWidth, imageHeight, record.ClassNames, true), minConfidence);
        }
    }
}
