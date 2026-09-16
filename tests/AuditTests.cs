using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MedVision.AnnotationViewer
{
    internal static partial class Tests
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static EditTransaction MakeCollectibleEdit(string folder, ImageRecord record, out WeakReference before, out WeakReference after)
        {
            var change = AnnotationEditor.RemoveShapes(record, new[] { 0 }, File.ReadAllBytes(record.AnnotationPath));
            before = new WeakReference(change.Before);
            after = new WeakReference(change.After);
            return EditTransaction.Apply(folder, new List<FileChange> { change }, CancellationToken.None);
        }

        private static void TestAuditRegressions()
        {
            string folder = MakeEditFixture();
            string child = Path.Combine(folder, Path.GetFileName(folder));
            Directory.CreateDirectory(child);
            File.Copy(Path.Combine(folder, "a.png"), Path.Combine(child, "child.png"));
            File.Copy(Path.Combine(folder, "a.json"), Path.Combine(child, "child.json"));
            var records = DatasetScanner.BuildRecordsRecursively(folder);
            var rootRecord = records.Single(r => r.ImagePath == Path.Combine(folder, "a.png"));
            string rootKey = StatisticsService.BuildStatsFolderName(folder, rootRecord.ImagePath);
            string childKey = StatisticsService.BuildStatsFolderName(folder, Path.Combine(child, "child.png"));
            Check(rootKey != childKey, "root and equally named child have distinct statistics identities");
            Check(rootKey == StatisticsService.BuildStatsFolderName(folder, Path.Combine(folder, "images", "sample.png")),
                "existing root/images grouping remains unchanged");
            Check(childKey == StatisticsService.BuildStatsFolderName(folder, Path.Combine(child, "images", "sample.png")),
                "child images grouping remains unchanged");
            var rows = StatisticsService.BuildStatsRows(folder, records);
            Check(rows.Count(r => r.IsTotal) == 3, "statistics does not merge same-named root and child");
            var selection = rows.Where(r => r.FolderName == rootKey && r.LabelName == "cell").ToList();
            var changes = AnnotationEditor.RemoveCategories(folder, records, selection, CancellationToken.None);
            Check(changes.Count == 2 && changes.All(c => Path.GetDirectoryName(c.Path) == folder),
                "category deletion excludes an equally named child directory");
            var transaction = EditTransaction.Apply(folder, changes, CancellationToken.None);
            Check(AnnotationReader.CountLabelMeShapes(Path.Combine(child, "child.json")) == 2,
                "unselected child's source annotations survive category deletion");
            transaction.Undo();

            WeakReference before, after;
            transaction = MakeCollectibleEdit(folder, rootRecord, out before, out after);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(!before.IsAlive && !after.IsAlive, "undo history allows full document snapshots to be collected");
            string backup = Path.Combine(transaction.BackupFolder, "0.original");
            byte[] original = File.ReadAllBytes(backup);
            byte[] edited = File.ReadAllBytes(rootRecord.AnnotationPath);
            File.WriteAllText(backup, "damaged backup");
            Throws<IOException>(() => transaction.Undo(), "corrupt backup is rejected before restoring any source file");
            Check(File.ReadAllBytes(rootRecord.AnnotationPath).SequenceEqual(edited), "failed undo preserves the current document");
            File.WriteAllBytes(backup, original);
            transaction.Undo();
            Check(File.ReadAllBytes(rootRecord.AnnotationPath).SequenceEqual(original), "disk-backed undo restores original bytes after snapshots are collected");

            string history = Path.Combine(folder, ".medvision-history");
            var existing = new HashSet<string>(Directory.GetDirectories(history));
            FileStream journalLock = null;
            try
            {
                transaction = EditTransaction.Apply(folder, new List<FileChange> {
                    AnnotationEditor.RemoveShapes(rootRecord, new[] { 0 }, original)
                }, CancellationToken.None, i => {
                    string newFolder = Directory.GetDirectories(history).Single(path => !existing.Contains(path));
                    journalLock = new FileStream(Path.Combine(newFolder, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
                });
                Check(transaction.RecoveryWarning != null && AnnotationReader.CountLabelMeShapes(rootRecord.AnnotationPath) == 1,
                    "journal update failure reports warning without losing a committed undo transaction");
                Check((string)AnnotationReader.LoadJsonObject(Path.Combine(transaction.BackupFolder, "manifest.json"))["state"] == "prepared",
                    "failed journal replacement preserves its previous valid contents");
            }
            finally { if (journalLock != null) journalLock.Dispose(); }
            transaction.Undo();
            Check(transaction.RecoveryWarning == null && File.ReadAllBytes(rootRecord.AnnotationPath).SequenceEqual(original),
                "undo recovers normally after the journal lock is released");
        }
    }
}
