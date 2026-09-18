using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MedVision.AnnotationViewer
{
    /// <summary>Discovers image/annotation pairs; no UI state or image decoding.</summary>
    internal static class DatasetScanner
    {
        private static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

        internal static bool IsImageFile(string path) { return Extensions.Contains(Path.GetExtension(path)); }

        internal static List<ImageRecord> BuildRecordsRecursively(string folder, CancellationToken cancellation = default(CancellationToken))
        {
            string root = Path.GetFullPath(folder);
            List<ImageRecord> result = new List<ImageRecord>();
            Dictionary<string, Dictionary<int, string>> classCache = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string current in EnumerateFolders(root, cancellation))
            {
                foreach (string image in Directory.EnumerateFiles(current).Where(IsImageFile))
                {
                    cancellation.ThrowIfCancellationRequested();
                    string annotation = FindAnnotation(image, root);
                    if (annotation == null) continue;
                    string classesFile = FindClassesFile(current) ?? FindClassesFile(Path.GetDirectoryName(annotation)) ?? string.Empty;
                    Dictionary<int, string> names;
                    if (!classCache.TryGetValue(classesFile, out names))
                    {
                        names = LoadClassNames(classesFile);
                        classCache[classesFile] = names;
                    }
                    string format = Path.GetExtension(annotation).Equals(".json", StringComparison.OrdinalIgnoreCase)
                        ? AnnotationFormats.LabelMe : AnnotationFormats.Yolo;
                    int count;
                    try { count = AnnotationReader.CountAnnotationShapes(annotation, format); }
                    catch (Exception ex) { throw new InvalidDataException("无法读取标注 " + annotation + "：" + ex.Message, ex); }
                    result.Add(new ImageRecord(image, annotation, format, count, BuildDisplayName(root, image), names));
                }
            }
            return result.OrderBy(record => record.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static string FindAnnotation(string image, string root)
        {
            string json = Path.ChangeExtension(image, ".json");
            if (File.Exists(json)) return json;
            string text = Path.ChangeExtension(image, ".txt");
            if (File.Exists(text)) return text;
            // Handles train/images/a.jpg and images/train/a.jpg, including nested image directories.
            DirectoryInfo current = new DirectoryInfo(Path.GetDirectoryName(image));
            while (current != null && current.Parent != null)
            {
                if (string.Equals(current.Name, "images", StringComparison.OrdinalIgnoreCase))
                {
                    string relative = image.Substring(current.FullName.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                    string labelBase = Path.Combine(current.Parent.FullName, "labels", relative);
                    string labelJson = Path.ChangeExtension(labelBase, ".json");
                    if (File.Exists(labelJson)) return labelJson;
                    string labelText = Path.ChangeExtension(labelBase, ".txt");
                    if (File.Exists(labelText)) return labelText;
                }
                current = current.Parent;
            }
            // Also accept arbitrary image-folder names with sibling annotation folders.
            // Preserve relative subdirectories, never guess by basename across different splits.
            current = new DirectoryInfo(Path.GetDirectoryName(image));
            string boundary = new DirectoryInfo(root).Parent == null ? root : new DirectoryInfo(root).Parent.FullName;
            while (current != null && current.Parent != null &&
                !string.Equals(current.FullName, boundary, StringComparison.OrdinalIgnoreCase))
            {
                string relative = image.Substring(current.FullName.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                var matches = new List<string>();
                foreach (string name in new[] { "labels", "annotations", "annotation", "标注", "标签", "json", "txt" })
                {
                    string candidate = Path.Combine(current.Parent.FullName, name, relative);
                    string candidateJson = Path.ChangeExtension(candidate, ".json");
                    string candidateText = Path.ChangeExtension(candidate, ".txt");
                    if (File.Exists(candidateJson)) matches.Add(candidateJson);
                    else if (File.Exists(candidateText)) matches.Add(candidateText);
                }
                if (matches.Count > 1) throw new InvalidDataException("图片存在多个相邻标注目录，无法确定配对：" + image + " → " + string.Join("、", matches));
                if (matches.Count == 1) return matches[0];
                current = current.Parent;
            }
            return null;
        }

        private static IEnumerable<string> EnumerateFolders(string root, CancellationToken cancellation)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                string current = pending.Pop();
                yield return current;
                foreach (string child in Directory.EnumerateDirectories(current))
                {
                    cancellation.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(child);
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0 ||
                        name.StartsWith(".", StringComparison.Ordinal) ||
                        name.StartsWith("annotation_viewer", StringComparison.OrdinalIgnoreCase) ||
                        name == "__pycache__" || name == "venv" || name == "comparison_exports" ||
                        name == "gt_pred_overlay" || name == "tp_fp_fn_analysis") continue;
                    pending.Push(child);
                }
            }
        }

        internal static string BuildDisplayName(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/') : Path.GetFileName(full);
        }

        internal static string FindClassesFile(string folder)
        {
            DirectoryInfo current = new DirectoryInfo(folder);
            while (current != null)
            {
                string path = Path.Combine(current.FullName, "classes.txt");
                if (File.Exists(path)) return path;
                current = current.Parent;
            }
            return null;
        }

        internal static Dictionary<int, string> LoadClassNames(string path)
        {
            Dictionary<int, string> names = new Dictionary<int, string>();
            if (string.IsNullOrEmpty(path)) return names;
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Trim().Length > 0) names[i] = lines[i].Trim();
            return names;
        }
    }
}
