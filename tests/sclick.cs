using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

internal static class SClick
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);

    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    private static string _out = "";
    private static void W(string s) { _out += s + "\r\n"; }

    private static IntPtr FindWindow(string procName, string titlePart)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            string name = "";
            try { name = Process.GetProcessById((int)pid).ProcessName; } catch { return true; }
            if (!string.Equals(name, procName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!IsWindowVisible(h)) return true;
            StringBuilder t = new StringBuilder(256);
            GetWindowTextW(h, t, 256);
            if (t.ToString().IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static void ForceForeground(IntPtr h)
    {
        for (int i = 0; i < 12; i++)
        {
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint myThread = GetCurrentThreadId();
            bool attached = AttachThreadInput(myThread, fgThread, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (attached) AttachThreadInput(myThread, fgThread, false);
            Thread.Sleep(80);
            if (GetForegroundWindow() == h) return;
        }
    }

    private static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(80);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private static void DumpTexts(IntPtr hwnd)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(hwnd);
            Walk(root, 0);
        }
        catch (Exception ex)
        {
            W("uia dump failed: " + ex.Message);
        }
    }

    private static void Walk(AutomationElement el, int depth)
    {
        if (depth > 6) return;
        AutomationElementCollection kids = null;
        try { kids = el.FindAll(TreeScope.Children, Condition.TrueCondition); }
        catch { return; }
        foreach (AutomationElement k in kids)
        {
            string type = "";
            string name = "";
            try { type = k.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); } catch { }
            try { name = k.Current.Name; } catch { }
            if (name != null && name.Length > 0 && (type == "Text" || type == "Button" || type == "Edit"))
                W("  " + type + " [" + name + "]");
            Walk(k, depth + 1);
        }
    }

    private static void Main(string[] args)
    {
        string dir = @"C:\Users\Administrator\AppData\Local\InputPolishTest";
        string logPath = Path.Combine(dir, "sclick.txt");
        try
        {
            Body(args);
        }
        catch (Exception ex)
        {
            W("EX " + ex.ToString());
        }
        try { File.WriteAllText(logPath, _out, Encoding.UTF8); } catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    private const uint WM_COMMAND = 0x0111;

    private static List<IntPtr> FindDialogs(string procName)
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            string name = "";
            try { name = Process.GetProcessById((int)pid).ProcessName; } catch { return true; }
            if (!string.Equals(name, procName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!IsWindowVisible(h)) return true;
            StringBuilder c = new StringBuilder(128);
            GetClassNameW(h, c, 128);
            if (c.ToString() == "#32770") list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static void Body(string[] args)
    {
        if (args.Length > 0 && args[0] == "ok")
        {
            List<IntPtr> dlgs = FindDialogs("InputPolish");
            W("dialogs=" + dlgs.Count);
            foreach (IntPtr d in dlgs)
            {
                StringBuilder t = new StringBuilder(256);
                GetWindowTextW(d, t, 256);
                W("dialog hwnd=" + d.ToInt64() + " title=[" + t + "] -> IDOK");
                PostMessageW(d, WM_COMMAND, new IntPtr(1), IntPtr.Zero);
            }
            Thread.Sleep(600);
            return;
        }

        string btn = null;
        bool invokeMode = false;
        if (args.Length > 0 && args[0] == "invoke")
        {
            btn = args[1];
            invokeMode = true;
            args = new string[0];
        }
        else if (args.Length > 0 && args[0] == "btn")
        {
            btn = args[1];
            args = new string[0];
        }

        string title = args.Length > 2 ? args[2] : "设置";
        IntPtr h = FindWindow("InputPolish", title);
        W("window=" + h.ToInt64());
        if (h == IntPtr.Zero) return;

        RECT cr;
        GetClientRect(h, out cr);
        POINT p = new POINT();
        p.X = 0; p.Y = 0;
        ClientToScreen(h, ref p);
        W("client origin=" + p.X + "," + p.Y + " size=" + cr.Right + "x" + cr.Bottom);

        ForceForeground(h);
        W("foreground is target=" + (GetForegroundWindow() == h));

        if (btn != null)
        {
            try
            {
                AutomationElement root = AutomationElement.FromHandle(h);
                Condition cond = new PropertyCondition(AutomationElement.NameProperty, btn);
                AutomationElement b = root.FindFirst(TreeScope.Descendants, cond);
                if (b == null)
                {
                    W("button not found: " + btn);
                }
                else
                {
                    if (invokeMode)
                    {
                        object ip;
                        if (b.TryGetCurrentPattern(InvokePattern.Pattern, out ip))
                        {
                            ((InvokePattern)ip).Invoke();
                            W("invoked button [" + btn + "]");
                            Thread.Sleep(1500);
                            DumpTexts(h);
                            return;
                        }
                        W("no invoke pattern on [" + btn + "]");
                        return;
                    }
                    System.Windows.Rect r = b.Current.BoundingRectangle;
                    int tx = (int)(r.Left + r.Width / 2);
                    int ty = (int)(r.Top + r.Height / 2);
                    Click(tx, ty);
                    Thread.Sleep(250);
                    ForceForeground(h);
                    Click(tx, ty);
                    W("clicked button [" + btn + "] at " + tx + "," + ty + " rect=" + r.Left + "," + r.Top + "," + r.Width + "x" + r.Height);
                }
            }
            catch (Exception ex)
            {
                W("button click failed: " + ex.Message);
            }
            Thread.Sleep(1200);
            DumpTexts(h);
            return;
        }

        if (args.Length >= 2)
        {
            int cx = int.Parse(args[0]);
            int cy = int.Parse(args[1]);
            Click(p.X + cx, p.Y + cy);
            W("clicked client " + cx + "," + cy);
            Thread.Sleep(1200);
        }

        DumpTexts(h);
    }
}
