using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;


namespace MedVision.AnnotationViewer
{
    internal static class AnnotationReader
    {
        internal static Image LoadImageWithoutLock(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image temp = Image.FromStream(stream))
            {
                return new Bitmap(temp);
            }
        }

        internal static Dictionary<string, object> LoadJsonObject(string jsonPath)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = serializer.DeserializeObject(File.ReadAllText(jsonPath)) as Dictionary<string, object>;
            if (root == null)
            {
                throw new InvalidDataException("JSON 根节点不是对象。");
            }

            return root;
        }

        internal static List<AnnotationShape> LoadAnnotations(ImageRecord record, int imageWidth, int imageHeight)
        {
            if (record.AnnotationKind == AnnotationFormats.Yolo)
            {
                return LoadYoloAnnotations(record.AnnotationPath, imageWidth, imageHeight, record.ClassNames, false);
            }

            return LoadLabelMeAnnotations(record.AnnotationPath);
        }

        internal static List<AnnotationShape> LoadLabelMeAnnotations(string jsonPath)
        {
            List<AnnotationShape> shapes = new List<AnnotationShape>();
            Dictionary<string, object> root = LoadJsonObject(jsonPath);

            object shapesObject;
            if (!root.TryGetValue("shapes", out shapesObject))
            {
                throw new InvalidDataException(Path.GetFileName(jsonPath) + "：缺少 shapes 数组。");
            }

            object[] shapeItems = shapesObject as object[];
            if (shapeItems == null)
            {
                throw new InvalidDataException(Path.GetFileName(jsonPath) + "：shapes 不是数组。");
            }

            foreach (object shapeObject in shapeItems)
            {
                Dictionary<string, object> shapeMap = shapeObject as Dictionary<string, object>;
                if (shapeMap == null)
                {
                    throw new InvalidDataException(Path.GetFileName(jsonPath) + "：shapes 含有非对象条目。");
                }

                string label = GetString(shapeMap, "label");
                string shapeType = GetString(shapeMap, "shape_type");
                List<PointF> points = LoadPoints(shapeMap);
                if (points.Count < 2 || points.Any(p => !Finite(p.X) || !Finite(p.Y)))
                    throw new InvalidDataException(Path.GetFileName(jsonPath) + "：标注点不足或包含无效坐标。");
                shapes.Add(new AnnotationShape(label, shapeType, points));
            }

            return shapes;
        }

        internal static List<AnnotationShape> LoadYoloAnnotations(
            string txtPath,
            int imageWidth,
            int imageHeight,
            Dictionary<int, string> classNames,
            bool isPrediction)
        {
            List<AnnotationShape> shapes = new List<AnnotationShape>();
            string[] lines = File.ReadAllLines(txtPath);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 5 && parts.Length != 6)
                {
                    throw new InvalidDataException(Path.GetFileName(txtPath) + " 第 " + (i + 1) + " 行：需要 5 或 6 列 YOLO 检测框数据。");
                }

                int classId;
                float centerX;
                float centerY;
                float width;
                float height;
                float confidence = 0;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out classId) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out centerX) ||
                    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out centerY) ||
                    !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out width) ||
                    !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out height))
                {
                    throw new InvalidDataException(Path.GetFileName(txtPath) + " 第 " + (i + 1) + " 行：无效的类别或坐标。");
                }
                if (classId < 0 || !Finite(centerX) || !Finite(centerY) || !Finite(width) || !Finite(height) ||
                    centerX < 0 || centerX > 1 || centerY < 0 || centerY > 1 || width <= 0 || width > 1 || height <= 0 || height > 1)
                    throw new InvalidDataException(Path.GetFileName(txtPath) + " 第 " + (i + 1) + " 行：坐标须归一化到 0–1，宽高须大于 0。");

                bool hasConfidence = parts.Length >= 6 &&
                    float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out confidence);
                if (parts.Length == 6 && (!hasConfidence || !Finite(confidence) || confidence < 0 || confidence > 1))
                    throw new InvalidDataException(Path.GetFileName(txtPath) + " 第 " + (i + 1) + " 行：置信度须为 0–1。");
                float pixelWidth = width * imageWidth;
                float pixelHeight = height * imageHeight;
                float left = (centerX - width / 2.0f) * imageWidth;
                float top = (centerY - height / 2.0f) * imageHeight;

                List<PointF> points = new List<PointF>();
                points.Add(new PointF(left, top));
                points.Add(new PointF(left + pixelWidth, top + pixelHeight));

                string className;
                string label = classNames.TryGetValue(classId, out className)
                    ? string.Format("{0}: {1}", classId, className)
                    : string.Format("class {0}", classId);
                shapes.Add(new AnnotationShape(label, isPrediction ? AnnotationFormats.Pred : AnnotationFormats.Yolo, points, classId, hasConfidence ? (float?)confidence : null));
            }

            return shapes;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        internal static string GetString(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value);
        }

        internal static List<PointF> LoadPoints(Dictionary<string, object> shapeMap)
        {
            List<PointF> points = new List<PointF>();
            object pointsObject;
            if (!shapeMap.TryGetValue("points", out pointsObject))
            {
                return points;
            }

            object[] pointItems = pointsObject as object[];
            if (pointItems == null)
            {
                return points;
            }

            foreach (object pointObject in pointItems)
            {
                object[] values = pointObject as object[];
                if (values == null || values.Length < 2)
                {
                    continue;
                }

                points.Add(new PointF(Convert.ToSingle(values[0]), Convert.ToSingle(values[1])));
            }

            return points;
        }

        internal static int CountAnnotationShapes(string annotationPath, string annotationKind)
        {
            if (annotationKind == AnnotationFormats.Yolo)
            {
                return CountYoloShapes(annotationPath);
            }

            return CountLabelMeShapes(annotationPath);
        }

        internal static int CountLabelMeShapes(string jsonPath)
        {
            return LoadLabelMeAnnotations(jsonPath).Count;
        }

        internal static int CountYoloShapes(string txtPath)
        {
            return LoadYoloAnnotations(txtPath, 1, 1, new Dictionary<int, string>(), false).Count;
        }

        internal static Dictionary<string, int> CountLabelsByClass(ImageRecord record)
        {
            if (record.AnnotationKind == AnnotationFormats.Yolo)
            {
                return CountYoloLabels(record.AnnotationPath, record.ClassNames);
            }

            return CountLabelMeLabels(record.AnnotationPath);
        }

        internal static Dictionary<string, int> CountLabelMeLabels(string jsonPath)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (AnnotationShape shape in LoadLabelMeAnnotations(jsonPath))
                AddCount(counts, StatisticsService.GetShapeStatsLabel(shape), 1);
            return counts;
        }

        internal static Dictionary<string, int> CountYoloLabels(string txtPath, Dictionary<int, string> classNames)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (AnnotationShape shape in LoadYoloAnnotations(txtPath, 1, 1, classNames, false))
                AddCount(counts, StatisticsService.GetShapeStatsLabel(shape), 1);
            return counts;
        }

        internal static void AddCount(Dictionary<string, int> counts, string key, int value)
        {
            int current;
            counts.TryGetValue(key, out current);
            counts[key] = current + value;
        }
    }
}
