using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace MedVision.AnnotationViewer
{
    internal sealed class StatisticsForm : Form
    {
        private readonly List<string[]> data;
        private readonly string[] headers;
        private DataGridView grid;
        private FlowLayoutPanel actions;
        private Label summaryLabel;
        private Button deleteButton;
        private bool editing;

        private StatisticsForm(string title, string summary, string[] headers, List<string[]> data, IList<bool> totals)
        {
            SuspendLayout();
            this.data = data;
            this.headers = headers;
            Text = title + " · Annatation Viewer " + AppInfo.Version;
            Size = new Size(1120, 700);
            MinimumSize = new Size(850, 500);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Theme.Background;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            TableLayoutPanel root = Theme.Rows(40, 56, -100, 54);
            root.Padding = new Padding(18);
            root.Controls.Add(Theme.Label(title, 16, Theme.Ink), 0, 0);
            summaryLabel = Theme.Label(summary, 9, Theme.Muted);
            root.Controls.Add(summaryLabel, 0, 1);
            grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Theme.Border, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Ink;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersHeight = 42;
            grid.RowTemplate.Height = 34;
            grid.DefaultCellStyle.SelectionBackColor = Theme.SoftAccent;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            foreach (string header in headers) grid.Columns.Add(header, header);
            grid.Columns[0].FillWeight = 170;
            for (int i = 0; i < data.Count; i++)
            {
                int index = grid.Rows.Add(data[i].Cast<object>().ToArray());
                if (totals[i]) grid.Rows[index].DefaultCellStyle.BackColor = Theme.SoftAccent;
            }
            root.Controls.Add(grid, 0, 2);
            actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 10, 0, 0) };
            Button close = new ModernButton { Width = 84, DialogResult = DialogResult.Cancel };
            Button export = new ModernButton { Width = 112 };
            Button copy = new ModernButton { Width = 104 };
            Theme.Button(close, "关闭", false);
            Theme.Button(export, "导出 CSV", true);
            Theme.Button(copy, "复制表格", false);
            actions.Controls.Add(close);
            actions.Controls.Add(export);
            actions.Controls.Add(copy);
            copy.Click += delegate { TryAction(() => Clipboard.SetText(TableSerializer.Serialize(AllRows(), false))); };
            export.Click += delegate { ExportCsv(); };
            root.Controls.Add(actions, 0, 3);
            Controls.Add(root);
            CancelButton = close;
            ResumeLayout(true);
        }

        private IEnumerable<string[]> AllRows() { return new[] { headers }.Concat(data); }
        private void TryAction(Action action)
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void ExportCsv()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv",
                FileName = "MedVision_stats_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    TryAction(() => File.WriteAllText(dialog.FileName, TableSerializer.Serialize(AllRows(), true), new UTF8Encoding(true)));
            }
        }
        private static string N(int value) { return value.ToString(CultureInfo.InvariantCulture); }
        private static string Rate(int numerator, int denominator)
        {
            return denominator == 0 ? "—" : (100.0 * numerator / denominator).ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        public static StatisticsForm Annotations(IList<StatsRow> rows, string root, Func<IList<StatsRow>, Task<IList<StatsRow>>> delete = null)
        {
            var form = new StatisticsForm("标注统计", root + "\n" + rows.Count(r => r.IsTotal) + " 个文件夹 · " +
                rows.Where(r => r.IsTotal).Sum(r => r.ImageCount) + " 张图 · " + rows.Where(r => r.IsTotal).Sum(r => r.BoxCount) + " 个框",
                new[] { "文件夹", "图片数", "标注文件数", "标注格式", "类别", "标注框数" },
                rows.Select(r => new[] { r.FolderName, N(r.ImageCount), N(r.AnnotationCount), r.AnnotationKinds, r.LabelName, N(r.BoxCount) }).ToList(),
                rows.Select(r => r.IsTotal).ToList());
            if (delete != null) form.EnableCategoryDeletion(rows, root, delete);
            return form;
        }

        private static bool CanDelete(StatsRow row)
        { return row != null && !row.IsTotal && row.LabelName != StatsRow.TotalLabelName; }

        private void EnableCategoryDeletion(IList<StatsRow> rows, string root, Func<IList<StatsRow>, Task<IList<StatsRow>>> delete)
        {
            for (int i = 0; i < rows.Count; i++) grid.Rows[i].Tag = rows[i];
            deleteButton = new ModernButton { Width = (int)(150 * DeviceScale()), Enabled = false };
            Theme.Button(deleteButton, "删除类别标注", false);
            deleteButton.Height = actions.Controls[0].Height;
            deleteButton.Margin = actions.Controls[0].Margin;
            actions.Controls.Add(deleteButton);
            deleteButton.AccessibleDescription = "删除所选行对应文件夹中的该类别标注；不删除图片。Ctrl+Z 可在主窗口撤销。";
            Action update = delegate
            {
                deleteButton.Enabled = !editing && grid.SelectedRows.Count > 0 &&
                    grid.SelectedRows.Cast<DataGridViewRow>().All(row => CanDelete(row.Tag as StatsRow));
            };
            grid.SelectionChanged += delegate { update(); };
            Func<Task> remove = async delegate
            {
                if (!deleteButton.Enabled || editing) return;
                var selected = grid.SelectedRows.Cast<DataGridViewRow>().Select(row => (StatsRow)row.Tag).ToList();
                editing = true;
                actions.Enabled = grid.Enabled = false;
                try
                {
                    var refreshed = await delete(selected);
                    grid.Rows.Clear(); data.Clear();
                    foreach (var row in refreshed)
                    {
                        var cells = new[] { row.FolderName, N(row.ImageCount), N(row.AnnotationCount), row.AnnotationKinds, row.LabelName, N(row.BoxCount) };
                        data.Add(cells);
                        int index = grid.Rows.Add(cells.Cast<object>().ToArray());
                        grid.Rows[index].Tag = row;
                        if (row.IsTotal) grid.Rows[index].DefaultCellStyle.BackColor = Theme.SoftAccent;
                    }
                    grid.ClearSelection();
                    summaryLabel.Text = root + "\n" + refreshed.Count(r => r.IsTotal) + " 个文件夹 · " +
                        refreshed.Where(r => r.IsTotal).Sum(r => r.ImageCount) + " 张图 · " +
                        refreshed.Where(r => r.IsTotal).Sum(r => r.BoxCount) + " 个框";
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { editing = false; actions.Enabled = grid.Enabled = true; update(); }
            };
            deleteButton.Click += async delegate { await remove(); };
            grid.KeyDown += async delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None)
                { e.SuppressKeyPress = true; await remove(); }
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (editing) e.Cancel = true; };
            update();
        }

        private float DeviceScale() { using (Graphics graphics = CreateGraphics()) return graphics.DpiX / 96F; }

        public static StatisticsForm Comparison(IList<ComparisonStatsRow> rows, string root, string pred, float iou)
        {
            return new StatisticsForm("预测误差统计", "GT: " + root + "\nPred: " + pred + "    IoU " + iou.ToString("0.00"),
                new[] { "文件夹", "类别", "图片数", "GT", "Pred", "TP", "FP", "FN", "Precision", "Recall", "F1" },
                rows.Select(r => new[] { r.FolderName, r.LabelName, N(r.ImageCount), N(r.GtCount), N(r.PredCount),
                    N(r.TruePositiveCount), N(r.FalsePositiveCount), N(r.FalseNegativeCount),
                    Rate(r.TruePositiveCount, r.TruePositiveCount + r.FalsePositiveCount),
                    Rate(r.TruePositiveCount, r.TruePositiveCount + r.FalseNegativeCount),
                    Rate(2 * r.TruePositiveCount, 2 * r.TruePositiveCount + r.FalsePositiveCount + r.FalseNegativeCount) }).ToList(),
                rows.Select(r => r.IsTotal).ToList());
        }
    }
}
