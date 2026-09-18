using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace MedVision.AnnotationViewer
{
    internal static partial class Tests
    {
        private static void TestVersion210()
        {
            string root = Path.Combine(qa, "v210-" + Guid.NewGuid().ToString("N"));
            string images = Path.Combine(root, "图像", "train");
            string labels = Path.Combine(root, "annotations", "train");
            Directory.CreateDirectory(images); Directory.CreateDirectory(labels);
            File.Copy(Path.Combine(dataset, "train", "images", "sample_001.png"), Path.Combine(images, "a.png"));
            string label = Path.Combine(labels, "a.json");
            File.WriteAllText(label, "{\"shapes\":[{\"label\":\"细胞\",\"shape_type\":\"rectangle\",\"points\":[[10,20],[50,70]]}]}");
            var records = DatasetScanner.BuildRecordsRecursively(Path.Combine(root, "图像"));
            Check(records.Count == 1 && records[0].AnnotationPath == label, "arbitrary image folder resolves sibling annotations with nested paths");
            Check(DatasetScanner.BuildRecordsRecursively(root).Count == 1, "dataset root and direct image folder resolve the same pair");
            var shapes = AnnotationReader.LoadAnnotations(records[0], 960, 720);
            Check(shapes.Count == 1 && shapes[0].Label == "细胞", "sibling JSON is decoded normally");
            byte[] original = File.ReadAllBytes(label);
            var edit = EditTransaction.Apply(Path.Combine(root, "图像"),
                new System.Collections.Generic.List<FileChange> { AnnotationEditor.RemoveShapes(records[0], new[] { 0 }, original) }, CancellationToken.None);
            Check(AnnotationReader.CountLabelMeShapes(label) == 0, "delete supports annotation outside selected image directory");
            edit.Undo();
            Check(File.ReadAllBytes(label).SequenceEqual(original), "undo restores sibling annotation bytes");
            File.Delete(label);
            File.WriteAllText(Path.Combine(labels, "a.txt"), "0 0.5 0.5 0.2 0.2\n");
            File.WriteAllText(Path.Combine(root, "annotations", "classes.txt"), "细胞");
            records = DatasetScanner.BuildRecordsRecursively(Path.Combine(root, "图像"));
            Check(records.Count == 1 && records[0].ClassNames[0] == "细胞", "sibling YOLO reads classes from annotation tree");
            Directory.CreateDirectory(Path.Combine(root, "labels", "train"));
            File.WriteAllText(Path.Combine(root, "labels", "train", "a.txt"), "0 0.5 0.5 0.1 0.1");
            Throws<InvalidDataException>(() => DatasetScanner.BuildRecordsRecursively(Path.Combine(root, "图像")), "ambiguous sibling labels never silently pick the wrong annotations");
            string colocated = Path.Combine(images, "a.txt");
            File.WriteAllText(colocated, "0 0.5 0.5 0.3 0.3");
            Check(DatasetScanner.BuildRecordsRecursively(Path.Combine(root, "图像"))[0].AnnotationPath == colocated, "co-located labels retain precedence");

            var a = Box("细胞", null, 1);
            var b = new AnnotationShape("细胞", "rectangle", new[] { new PointF(150, 0), new PointF(250, 100) });
            var c = Box("菌丝", null, 1);
            Check(AnnotationRenderer.ClassColor(a) == AnnotationRenderer.ClassColor(b), "same category has one fixed color");
            Check(AnnotationRenderer.ClassColor(a) != AnnotationRenderer.ClassColor(c), "different categories have different colors");
            Check(AnnotationRenderer.ClassColor(a) == AnnotationRenderer.ClassColor(Box("0: 细胞", 0, 1)), "LabelMe and named YOLO category colors agree");
            using (Bitmap image = new Bitmap(280, 120))
            using (Graphics g = Graphics.FromImage(image))
            {
                AnnotationRenderer.Draw(g, new[] { a, b }, new AnnotationShape[0], 1, ViewMode.Annotations, false, 0.5F);
                Check(image.GetPixel(50, 0) == image.GetPixel(200, 0), "single-class boxes render with identical stroke colors");
                Color before = image.GetPixel(50, 0);
                g.Clear(Color.Transparent);
                AnnotationRenderer.Draw(g, new[] { c, b }, new AnnotationShape[0], 1, ViewMode.Annotations, false, 0.5F);
                Check(image.GetPixel(200, 0) == before, "class color remains stable with a different image ordering");
            }
        }
    }
}
