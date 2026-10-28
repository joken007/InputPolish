using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Automation;

internal static class MStyle2
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint LEFTDOWN = 0x0002;
    private const uint LEFTUP = 0x0004;

    private static string _out = "";
    private static void W(string s) { _out += s + "\r\n"; }

    private static void Main(string[] args)
    {
        int index = args.Length > 0 ? int.Parse(args[0]) : 2;
        try { Body(index); }
        catch (Exception ex) { W("EX " + ex); }
        try { File.WriteAllText(@"C:\Users\Administrator\AppData\Local\InputPolishTest\mstyle2.txt", _out, Encoding.UTF8); } catch { }
    }

    private static bool SendIpc(string command)
    {
        try
        {
            using (System.IO.Pipes.NamedPipeClientStream pipe =
                new System.IO.Pipes.NamedPipeClientStream(".", "InputPolish_cmd_7F3A21", System.IO.Pipes.PipeDirection.Out))
            {
                pipe.Connect(2500);
                byte[] b = Encoding.UTF8.GetBytes(command);
                pipe.Write(b, 0, b.Length);
                pipe.Flush();
                return true;
            }
        }
        catch (Exception ex) { W("ipc failed: " + ex.Message); return false; }
    }

    private static void Body(int index)
    {
        W("ipc menu sent=" + SendIpc("menu"));
        Thread.Sleep(900);

        System.Collections.Generic.List<IntPtr> wins = FindMenus();
        W("candidate windows=" + wins.Count);
        AutomationElement parent = null;
        for (int k = 0; k < wins.Count; k++)
        {
            AutomationElement el = AutomationElement.FromHandle(wins[k]);
            AutomationElement found = el.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "润色风格"));
            if (found != null) { parent = found; W("parent found in hwnd " + wins[k].ToInt64()); break; }
        }
        if (parent == null) { W("parent not found"); return; }
        System.Windows.Rect pr = parent.Current.BoundingRectangle;
        W("parent rect=" + (int)pr.Left + "," + (int)pr.Top + " " + (int)pr.Width + "x" + (int)pr.Height);
        SetCursorPos((int)(pr.Left + pr.Width / 2), (int)(pr.Top + pr.Height / 2));
        Thread.Sleep(1300);

        // geometry: submenu opens to the right of the parent bar, first item aligned with parent item top
        int sx = (int)(pr.Left + pr.Width) + 6;
        int sy = (int)pr.Top + 11 + index * 22;
        W("submenu open at " + sx + "," + (int)pr.Top);
        SetCursorPos(sx + 60, sy);
        Thread.Sleep(900);

        string shot = @"C:\Users\Administrator\AppData\Local\InputPolishTest\tray_style_menu.png";
        try
        {
            Rectangle r = new Rectangle(Math.Max(0, (int)pr.Left - 20), Math.Max(0, (int)pr.Top - 60), 420, 200);
            using (Bitmap bmp = new Bitmap(r.Width, r.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size);
                bmp.Save(shot, ImageFormat.Png);
            }
            W("shot saved " + shot);
        }
        catch (Exception ex) { W("shot failed: " + ex.Message); }

        mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
        W("clicked submenu index " + index + " at " + (sx + 60) + "," + sy);
        Thread.Sleep(1500);
    }

    private static System.Collections.Generic.List<IntPtr> FindMenus()
    {
        System.Collections.Generic.List<IntPtr> list = new System.Collections.Generic.List<IntPtr>();
        IntPtr menu = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder c = new StringBuilder(128);
            GetClassNameW(h, c, 128);
            string cls = c.ToString();
            if (cls.IndexOf("WindowsForms10.Window", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                string pn = "";
                try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
                if (string.Equals(pn, "InputPolish", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(h);
                }
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
