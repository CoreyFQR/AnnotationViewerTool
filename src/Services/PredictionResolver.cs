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
            }
            return null;
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

        internal static List<AnnotationShape> LoadPredictionsFromFolder(ImageRecord record, string predLabelFolder, int imageWidth, int imageHeight)
        {
            if (string.IsNullOrEmpty(predLabelFolder) || !Directory.Exists(predLabelFolder))
            {
                return new List<AnnotationShape>();
            }

            string predPath = ResolvePath(record, predLabelFolder);
            if (predPath == null)
            {
                return new List<AnnotationShape>();
            }

            return AnnotationReader.LoadYoloAnnotations(predPath, imageWidth, imageHeight, record.ClassNames, true);
        }
    }
}
