using System;
using System.IO;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                using (ViewerForm form = new ViewerForm())
                {
                    if (args.Length > 0 && args[0] == "--self-test") return 0;
                    if (args.Length > 0 && Directory.Exists(args[0]))
                        form.Shown += async delegate { await form.OpenFolderAsync(args[0]); };
                    Application.Run(form);
                }
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "MedVision 无法启动", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
