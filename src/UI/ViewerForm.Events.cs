using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;


namespace MedVision.AnnotationViewer
{
    internal sealed partial class ViewerForm : Form
    {
        private void WireEvents()
        {
            undoButton.Click += async delegate { await UndoEditAsync(); };
            annotationList.SelectedIndexChanged += delegate
            {
                if (syncingAnnotationSelection) return;
                selectedAnnotationIndex = annotationList.SelectedIndices.Count == 0 ? -1 : annotationList.SelectedIndices[0];
                pictureBox.Invalidate();
            };
            pictureBox.MouseDown += delegate(object sender, MouseEventArgs e)
            { if (e.Button == MouseButtons.Left) SelectCanvasAnnotation(e.Location); };
            imagePanel.MouseDown += delegate(object sender, MouseEventArgs e)
            { if (e.Button == MouseButtons.Left) { imagePanel.Focus(); SelectAnnotation(-1); } };
            browseButton.Click += delegate { BrowseForFolder(); };
            reloadButton.Click += async delegate { await OpenFolderAsync(folderText.Text); };
            folderText.KeyDown += async delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    await OpenFolderAsync(folderText.Text);
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    folderText.Text = loadedFolder ?? string.Empty;
                    e.SuppressKeyPress = true;
                }
            };
            statsButton.Click += async delegate { await ShowAnnotationStatsAsync(); };
            searchText.TextChanged += delegate { ApplyFileFilter(); };
            searchText.KeyDown += SearchTextKeyDown;
            clearSearchButton.Click += delegate { searchText.Clear(); };
            predBrowseButton.Click += delegate { BrowseForPredictionFolder(); };
            predFolderText.TextChanged += delegate { OnPredictionFolderChanged(); };
            comparisonStatsButton.Click += async delegate { await ShowComparisonStatsAsync(); };
            exportCompareButton.Click += async delegate { await ExportComparisonImagesAsync(); };
            iouNumeric.ValueChanged += delegate
            {
                RenderCurrent();
                UpdateCurrentStatus();
            };
            gtOnlyCheckBox.CheckedChanged += delegate
            {
                if (gtOnlyCheckBox.Checked) errorAnalysisCheckBox.Checked = false;
                RenderCurrent();
                UpdateCurrentStatus();
            };
            errorAnalysisCheckBox.CheckedChanged += delegate
            {
                if (errorAnalysisCheckBox.Checked) gtOnlyCheckBox.Checked = false;
                RenderCurrent();
                UpdateCurrentStatus();
            };
            previousButton.Click += delegate { MoveSelection(-1); };
            nextButton.Click += delegate { MoveSelection(1); };
            fitButton.Click += delegate { fitCheckBox.Checked = true; FitToWindow(); };
            zoomInButton.Click += delegate { SetZoom(zoom * 1.25f); };
            zoomOutButton.Click += delegate { SetZoom(zoom / 1.25f); };
            actualSizeButton.Click += delegate
            {
                fitCheckBox.Checked = false;
                SetZoom(1.0f);
            };
            labelsCheckBox.CheckedChanged += delegate { RenderCurrent(); };
            fitCheckBox.CheckedChanged += delegate
            {
                if (fitCheckBox.Checked)
                {
                    FitToWindow();
                }
            };
            fileList.SelectedIndexChanged += delegate
            {
                if (!changingSelection)
                {
                    LoadSelectedRecord();
                }
            };
            imagePanel.Resize += delegate
            {
                imagePanel.Invalidate();
                if (fitCheckBox.Checked)
                {
                    FitToWindow();
                }
                else RenderCurrent();
            };
            imagePanel.MouseWheel += delegate(object sender, MouseEventArgs e)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    fitCheckBox.Checked = false;
                    SetZoom(e.Delta > 0 ? zoom * 1.15f : zoom / 1.15f);
                }
            };


            pictureBox.MouseWheel += delegate(object sender, MouseEventArgs e)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    fitCheckBox.Checked = false;
                    SetZoom(e.Delta > 0 ? zoom * 1.15f : zoom / 1.15f);
                }
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!isBusy) return;
                e.Cancel = true;
                closeAfterWork = true;
                if (workCancellation != null) workCancellation.Cancel();
            };
            KeyDown += async delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.F)
                {
                    searchText.Focus();
                    searchText.SelectAll();
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.Control && e.KeyCode == Keys.O)
                {
                    if (!isBusy) BrowseForFolder();
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.Control && e.KeyCode == Keys.L)
                {
                    folderText.Focus();
                    folderText.SelectAll();
                    e.SuppressKeyPress = true;
                    return;
                }
                if (isBusy || folderText.ContainsFocus || searchText.ContainsFocus ||
                    predFolderText.ContainsFocus || iouNumeric.ContainsFocus) return;
                if (e.Control && e.KeyCode == Keys.Z)
                {
                    e.SuppressKeyPress = true;
                    await UndoEditAsync();
                    return;
                }
                if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None)
                {
                    if (fileList.ContainsFocus) { e.SuppressKeyPress = true; await DeleteImageAsync(); }
                    else if (annotationList.ContainsFocus || imagePanel.ContainsFocus)
                    { e.SuppressKeyPress = true; await DeleteAnnotationAsync(); }
                    return;
                }
                if (e.KeyCode == Keys.Left)
                {
                    MoveSelection(-1);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Right)
                {
                    MoveSelection(1);
                    e.Handled = true;
                }
                else if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add))
                {
                    fitCheckBox.Checked = false;
                    SetZoom(zoom * 1.25f);
                    e.Handled = true;
                }
                else if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract))
                {
                    fitCheckBox.Checked = false;
                    SetZoom(zoom / 1.25f);
                    e.Handled = true;
                }
            };
        }
    }
}
