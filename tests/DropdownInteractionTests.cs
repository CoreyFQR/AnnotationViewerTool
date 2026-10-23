using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MedVision.AnnotationViewer
{
    internal static partial class Tests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ComboWindowInfo
        {
            public int Size;
            public NativeRect Item, Button;
            public int ButtonState;
            public IntPtr Combo, Edit, List;
        }
        [DllImport("user32.dll")]
        private static extern bool GetComboBoxInfo(IntPtr window, ref ComboWindowInfo info);
        [DllImport("user32.dll")]
        private static extern IntPtr GetCapture();
        [DllImport("user32.dll")]
        private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref Point point, uint count);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr GetListItemRect(IntPtr window, int message, IntPtr index, ref NativeRect rect);

        private static void ClickWithMouseCapture(IntPtr window, Point point)
        {
            SendMessage(window, 0x201, new IntPtr(1), new IntPtr((point.Y << 16) | (point.X & 0xffff)));
            IntPtr recipient = GetCapture();
            if (recipient == IntPtr.Zero) recipient = window;
            MapWindowPoints(window, recipient, ref point, 1);
            SendMessage(recipient, 0x202, IntPtr.Zero, new IntPtr((point.Y << 16) | (point.X & 0xffff)));
        }

        private static async Task TestDropdownInteraction(ViewerForm form)
        {
            var filter = Field<ComboBox>(form, "annotationLabelFilter");
            int opened = 0, closed = 0;
            EventHandler onOpen = delegate { opened++; };
            EventHandler onClose = delegate { closed++; };
            filter.DropDown += onOpen;
            filter.DropDownClosed += onClose;
            try
            {
                filter.Focus();
                Point arrow = new Point(filter.Width - (int)(17 * ControlGeometry.SystemScale), filter.Height / 2);
                ClickWithMouseCapture(filter.Handle, arrow);
                await Task.Delay(200);
                Check(filter.DroppedDown && opened == 1 && closed == 0,
                    "dropdown stays open after mouse press and captured release in the actual filter");
                filter.Refresh();
                await Task.Delay(1000);
                Check(filter.DroppedDown && closed == 0, "dropdown remains open while waiting and repainting");
                var info = new ComboWindowInfo { Size = Marshal.SizeOf(typeof(ComboWindowInfo)) };
                Check(GetComboBoxInfo(filter.Handle, ref info) && info.List != IntPtr.Zero,
                    "native dropdown list is available for label selection");
                var item = new NativeRect();
                Check(GetListItemRect(info.List, 0x198, new IntPtr(1), ref item).ToInt64() != -1,
                    "native label option has a valid click target");
                Point target = new Point((item.Left + item.Right) / 2, (item.Top + item.Bottom) / 2);
                SendMessage(info.List, 0x200, IntPtr.Zero, new IntPtr((target.Y << 16) | (target.X & 0xffff)));
                ClickWithMouseCapture(info.List, target);
                await Task.Delay(100);
                Check(filter.SelectedIndex == 1 && !filter.DroppedDown && closed == 1 &&
                    Field<ListView>(form, "annotationList").Items.Count > 0,
                    "clicking an actual dropdown option selects its label and then closes the menu");
                ClickWithMouseCapture(filter.Handle, new Point(20, filter.Height / 2));
                await Task.Delay(100);
                Check(filter.DroppedDown && opened == 2, "clicking the text reopens the dropdown after selection");
                SendMessage(filter.Handle, 0x100, new IntPtr((int)Keys.Escape), IntPtr.Zero);
                SendMessage(filter.Handle, 0x101, new IntPtr((int)Keys.Escape), IntPtr.Zero);
                await Task.Delay(100);
                Check(!filter.DroppedDown && filter.SelectedIndex == 1,
                    "Escape closes the dropdown without changing the chosen label");
            }
            finally
            {
                filter.DroppedDown = false;
                filter.SelectedIndex = 0;
                filter.DropDown -= onOpen;
                filter.DropDownClosed -= onClose;
            }
        }
    }
}
