using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace MedVision.AnnotationViewer
{
    internal sealed class FileChange
    {
        internal readonly string Path;
        internal readonly byte[] Before;
        internal readonly byte[] After;
        internal FileChange(string path, byte[] before, byte[] after)
        { Path = System.IO.Path.GetFullPath(path); Before = before; After = after; }
    }

    // Plans preserve the complete source document; only selected shapes/lines are removed.
    internal static class AnnotationEditor
    {
        internal static FileChange RemoveShapes(ImageRecord record, IEnumerable<int> indexes, byte[] expected)
        {
            EditTransaction.Verify(record.AnnotationPath, expected);
            var remove = new HashSet<int>(indexes);
            string source;
            Encoding encoding;
            using (var reader = new StreamReader(new MemoryStream(expected), Encoding.UTF8, true))
            { source = reader.ReadToEnd(); encoding = reader.CurrentEncoding; }
            byte[] preamble = encoding.GetPreamble();
            bool hasBom = preamble.Length > 0 && expected.Take(preamble.Length).SequenceEqual(preamble);
            string edited;
            int count;
            if (record.AnnotationKind == AnnotationFormats.LabelMe)
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = serializer.DeserializeObject(source) as Dictionary<string, object>;
                object value;
                if (root == null || !root.TryGetValue("shapes", out value) || !(value is object[]))
                    throw new InvalidDataException("JSON 缺少有效的 shapes 数组。");
                var shapes = (object[])value;
                count = shapes.Length;
                root["shapes"] = shapes.Where((shape, i) => !remove.Contains(i)).ToArray();
                edited = serializer.Serialize(root);
            }
            else
            {
                int index = 0;
                var result = new StringBuilder();
                foreach (Match line in Regex.Matches(source, @"[^\r\n]*(?:\r\n|\r|\n|$)"))
                {
                    bool blank = string.IsNullOrWhiteSpace(line.Value);
                    if (blank || !remove.Contains(index)) result.Append(line.Value);
                    if (!blank) index++;
                }
                count = index;
                edited = result.ToString();
            }
            if (remove.Count == 0 || remove.Any(i => i < 0 || i >= count))
                throw new InvalidOperationException("标注选择已失效，请重新选择。");
            byte[] body = encoding.GetBytes(edited);
            return new FileChange(record.AnnotationPath, expected, hasBom ? preamble.Concat(body).ToArray() : body);
        }

        internal static List<FileChange> RemoveImage(ImageRecord record, IList<ImageRecord> records)
        {
            if (records.Any(r => !string.Equals(r.ImagePath, record.ImagePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.AnnotationPath, record.AnnotationPath, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("该标注文件被其他图片共用，不能成对删除。请先处理同名图片。");
            return new List<FileChange> {
                new FileChange(record.ImagePath, File.ReadAllBytes(record.ImagePath), null),
                new FileChange(record.AnnotationPath, File.ReadAllBytes(record.AnnotationPath), null)
            };
        }

        internal static List<FileChange> RemoveCategories(string root, IList<ImageRecord> records, IList<StatsRow> rows,
            CancellationToken cancellation)
        {
            if (rows.Count == 0 || rows.Any(r => r.IsTotal || r.LabelName == StatsRow.TotalLabelName))
                throw new InvalidOperationException("不能删除“（全部）”类别。");
            var changes = new List<FileChange>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in records)
            {
                cancellation.ThrowIfCancellationRequested();
                string folder = StatisticsService.BuildStatsFolderName(root, record.ImagePath);
                var labels = new HashSet<string>(rows.Where(r => r.FolderName == folder).Select(r => r.LabelName), StringComparer.OrdinalIgnoreCase);
                if (labels.Count == 0 || !seen.Add(record.AnnotationPath)) continue;
                byte[] before = File.ReadAllBytes(record.AnnotationPath);
                var shapes = AnnotationReader.LoadAnnotations(record, 1, 1);
                EditTransaction.Verify(record.AnnotationPath, before);
                var indexes = Enumerable.Range(0, shapes.Count).Where(i => labels.Contains(StatisticsService.GetShapeStatsLabel(shapes[i]))).ToList();
                if (indexes.Count > 0) changes.Add(RemoveShapes(record, indexes, before));
            }
            return changes;
        }
    }

    internal sealed class EditTransaction
    {
        private sealed class SavedChange
        {
            internal string Path;
            internal string BeforeHash;
            internal string AfterHash;
        }
        private readonly List<SavedChange> changes;
        internal readonly string BackupFolder;
        internal string RecoveryWarning { get; private set; }
        private EditTransaction(List<FileChange> changes, string folder)
        {
            this.changes = changes.Select(c => new SavedChange {
                Path = c.Path, BeforeHash = Digest(c.Before), AfterHash = Digest(c.After)
            }).ToList();
            BackupFolder = folder;
        }

        private static string Digest(byte[] bytes)
        {
            if (bytes == null) return null;
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        internal static void Verify(string path, byte[] expected)
        {
            bool matches = expected == null ? !File.Exists(path) : File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(expected);
            if (!matches) throw new IOException("文件已被其他程序修改，请刷新后重试：" + path);
        }

        internal static EditTransaction Apply(string root, List<FileChange> changes, CancellationToken cancellation,
            Action<int> beforeWrite = null)
        {
            if (changes.Count == 0) return null;
            if (changes.Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changes.Count)
                throw new InvalidOperationException("同一文件不能在一次操作中重复修改。");
            foreach (var change in changes) { cancellation.ThrowIfCancellationRequested(); Verify(change.Path, change.Before); }
            string folder = System.IO.Path.Combine(root, ".medvision-history", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            for (int i = 0; i < changes.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (changes[i].Before != null) File.WriteAllBytes(System.IO.Path.Combine(folder, i + ".original"), changes[i].Before);
            }
            var transaction = new EditTransaction(changes, folder);
            transaction.WriteManifest("prepared");
            Commit(changes, cancellation, beforeWrite);
            transaction.TryWriteManifest("applied");
            return transaction;
        }

        internal void Undo()
        {
            var reverse = new List<FileChange>();
            for (int i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                byte[] current = File.Exists(change.Path) ? File.ReadAllBytes(change.Path) : null;
                if (Digest(current) != change.AfterHash)
                    throw new IOException("文件已被其他程序修改，请刷新后重试：" + change.Path);
                byte[] original = change.BeforeHash == null ? null : File.ReadAllBytes(System.IO.Path.Combine(BackupFolder, i + ".original"));
                if (Digest(original) != change.BeforeHash)
                    throw new IOException("原文件备份已损坏，无法撤销：" + BackupFolder);
                reverse.Add(new FileChange(change.Path, current, original));
            }
            Commit(reverse, CancellationToken.None, null);
            TryWriteManifest("reverted");
        }

        private void TryWriteManifest(string state)
        {
            RecoveryWarning = null;
            try { WriteManifest(state); }
            catch (IOException) { RecoveryWarning = "备份状态未更新：" + BackupFolder; }
            catch (UnauthorizedAccessException) { RecoveryWarning = "备份状态未更新：" + BackupFolder; }
        }

        private void WriteManifest(string state)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string json = serializer.Serialize(new {
                state = state, files = changes.Select((c, i) => new { path = c.Path, original = i + ".original", deleted = c.AfterHash == null }).ToArray()
            });
            var encoding = new UTF8Encoding(true);
            Write(System.IO.Path.Combine(BackupFolder, "manifest.json"), encoding.GetPreamble().Concat(encoding.GetBytes(json)).ToArray());
        }

        private static void Commit(List<FileChange> plan, CancellationToken cancellation, Action<int> beforeWrite)
        {
            foreach (var change in plan) Verify(change.Path, change.Before);
            var applied = new List<FileChange>();
            try
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (beforeWrite != null) beforeWrite(i);
                    Verify(plan[i].Path, plan[i].Before);
                    Write(plan[i].Path, plan[i].After);
                    applied.Add(plan[i]);
                }
            }
            catch (Exception failure)
            {
                var failures = new List<Exception> { failure };
                foreach (var change in applied.AsEnumerable().Reverse())
                {
                    try { Verify(change.Path, change.After); Write(change.Path, change.Before); }
                    catch (Exception rollback) { failures.Add(rollback); }
                }
                if (failures.Count > 1) throw new AggregateException("操作中断，部分文件无法自动还原。原文件备份位于数据目录的 .medvision-history。", failures);
                throw;
            }
        }

        private static void Write(string path, byte[] bytes)
        {
            if (bytes == null) { File.Delete(path); return; }
            string temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), ".medvision-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
