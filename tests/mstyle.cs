using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Automation;

internal static class MStyle
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    private const uint LEFTDOWN = 0x0002;
    private const uint LEFTUP = 0x0004;

    private static string _out = "";
    private static void W(string s) { _out += s + "\r\n"; }

    private static void Main(string[] args)
    {
        string parent = args.Length > 0 ? args[0] : "润色风格";
        string child = args.Length > 1 ? args[1] : "更正式";
        try { Body(parent, child); }
        catch (Exception ex) { W("EX " + ex.Message); }
        try { File.WriteAllText(@"C:\Users\Administrator\AppData\Local\InputPolishTest\mstyle.txt", _out, Encoding.UTF8); } catch { }
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

    private static void Body(string parent, string child)
    {
        W("ipc menu sent=" + SendIpc("menu"));
        Thread.Sleep(900);

        AutomationElement root = FindMenu();
        W("menu root=" + (root == null ? "null" : root.Current.NativeWindowHandle.ToString()));
        if (root == null) return;

        AutomationElement item = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, parent));
        W("parent item=" + (item == null ? "null" : "ok"));
        if (item == null) return;
        System.Windows.Rect r = item.Current.BoundingRectangle;
        W("parent rect=" + (int)r.Left + "," + (int)r.Top + " " + (int)r.Width + "x" + (int)r.Height);
        // hover to open the submenu (no click)
        SetCursorPos((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2));
        Thread.Sleep(1200);

        AutomationElement sub = FindMenu();
        W("sub root=" + (sub == null ? "null" : sub.Current.NativeWindowHandle.ToString()));
        AutomationElement want = null;
        if (sub != null)
            want = sub.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, child));
        if (want == null && sub != null)
        {
            AutomationElementCollection all = sub.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            W("sub candidates=" + all.Count);
            foreach (AutomationElement e in all)
            {
                string nm = "";
                try { nm = e.Current.Name; } catch { }
                string ct = "";
                try { ct = e.Current.ControlType.ProgrammaticName; } catch { }
                System.Windows.Rect rr = new System.Windows.Rect(0, 0, 0, 0);
                try { rr = e.Current.BoundingRectangle; } catch { }
                W("  sub node ct=" + ct + " name=[" + nm + "] rect=" + (int)rr.Left + "," + (int)rr.Top + " " + (int)rr.Width + "x" + (int)rr.Height);
            }
            return;
        }
        System.Windows.Rect sr = want.Current.BoundingRectangle;
        W("child rect=" + (int)sr.Left + "," + (int)sr.Top + " " + (int)sr.Width + "x" + (int)sr.Height);
        SetCursorPos((int)(sr.Left + sr.Width / 2), (int)(sr.Top + sr.Height / 2));
        Thread.Sleep(200);
        mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
        W("clicked [" + parent + " > " + child + "]");
        Thread.Sleep(1500);
    }

    private static AutomationElement FindMenu()
    {
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
                    menu = h;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        if (menu == IntPtr.Zero) return null;
        return AutomationElement.FromHandle(menu);
    }
}
