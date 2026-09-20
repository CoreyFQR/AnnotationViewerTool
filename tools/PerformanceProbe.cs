using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// Standalone probe: load any release assembly without changing its code.
internal static class PerformanceProbe
{
    private static int layouts, paints;
    private static void Watch(Control c)
    {
        c.Layout += delegate { layouts++; };
        c.Paint += delegate { paints++; };
        foreach (Control child in c.Controls) Watch(child);
    }
    private static void Pump(int milliseconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(1); }
    }
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Assembly app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Stopwatch clock = Stopwatch.StartNew();
        using (Form form = (Form)Activator.CreateInstance(app.GetType("MedVision.AnnotationViewer.ViewerForm"), true))
        {
            long construction = clock.ElapsedMilliseconds;
            bool earlyHandle = form.IsHandleCreated;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.ShowInTaskbar = false;
            Watch(form);
            form.Show();
            form.WindowState = FormWindowState.Normal;
            Application.DoEvents();
            long startup = clock.ElapsedMilliseconds;
            int startupLayouts = layouts;
            Pump(250);
            layouts = paints = 0;
            Pump(400);
            Console.WriteLine("construct_ms={0} startup_ms={1} early_handle={2} startup_layouts={3} idle_layouts={4} idle_paints={5}",
                construction, startup, earlyHandle, startupLayouts, layouts, paints);
            Size initial = form.Size;
            layouts = paints = 0;
            clock.Restart();
            for (int i = 0; i < 40; i++)
            {
                form.Size = new Size(initial.Width + i * 3, initial.Height + i * 2);
                Application.DoEvents();
                form.Update();
            }
            Console.WriteLine("resize_40_ms={0} resize_layouts={1} resize_paints={2} final_size={3}", clock.ElapsedMilliseconds, layouts, paints, form.Size);
            form.Close();
        }
    }
}
