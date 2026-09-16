using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MedVision.AnnotationViewer
{
    internal static class TableSerializer
    {
        public static string Serialize(IEnumerable<string[]> rows, bool csv)
        {
            StringBuilder result = new StringBuilder();
            foreach (string[] row in rows)
                result.AppendLine(string.Join(csv ? "," : "\t", row.Select(value => Escape(value, csv)).ToArray()));
            return result.ToString();
        }

        private static string Escape(string value, bool csv)
        {
            value = value ?? string.Empty;
            if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
            return csv ? "\"" + value.Replace("\"", "\"\"") + "\"" : value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
