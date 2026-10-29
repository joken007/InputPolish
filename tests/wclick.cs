using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class WidgetClick
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int c);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);

    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static IntPtr _found = IntPtr.Zero;

    private static void Say(string s)
    {
        Console.WriteLine(s);
    }

    private static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(90);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    private static IntPtr FindWindowOf(int pid, string titlePart)
    {
        _found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            uint wp;
            GetWindowThreadProcessId(h, out wp);
            if (wp != pid) return true;
            StringBuilder sb = new StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            string t = sb.ToString();
            if (titlePart.Length > 0 && t.IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) < 0) return true;
            RECT r;
            GetWindowRect(h, out r);
            Say("window hwnd=" + h.ToInt64() + " title=[" + t + "] rect=" + r.Left + "," + r.Top + "," + (r.Right - r.Left) + "x" + (r.Bottom - r.Top));
            _found = h;
            return false;
        }, IntPtr.Zero);
        return _found;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        string action = args.Length > 0 ? args[0] : "settings";
        Process[] ps = Process.GetProcessesByName("InputPolish");
        if (ps.Length == 0) { Say("no InputPolish process"); return; }
        int pid = ps[0].Id;

        IntPtr w = FindWindowOf(pid, "");
        if (w == IntPtr.Zero) { Say("widget window not found"); return; }
        RECT wr;
        GetWindowRect(w, out wr);
        int cx = wr.Left + (wr.Right - wr.Left) / 2;
        int cy = wr.Top + (wr.Bottom - wr.Top) / 2;

        SetCursorPos(cx, cy);
        Thread.Sleep(900);
        GetWindowRect(w, out wr);
        Say("after hover rect=" + wr.Left + "," + wr.Top + "," + (wr.Right - wr.Left) + "x" + (wr.Bottom - wr.Top));

        // 展开后按钮顺序：暂停/启用、译页、总结、设置、撤销、退出
        int wid = wr.Right - wr.Left;
        int bh = wr.Bottom - wr.Top;
        string[] names = new string[] { "toggle", "page", "summary", "settings", "undo", "exit" };
        int idx = 0;
        for (int i = 0; i < names.Length; i++) if (names[i] == action) idx = i;
        double scale = wid / 460.0;
        int x0 = (int)(64 * scale);
        int bw = (int)(58 * scale);
        int gap = (int)(8 * scale);
        int btnX = wr.Left + x0 + idx * (bw + gap) + bw / 2;
        int btnY = wr.Top + bh / 2;
        Say("clicking " + action + " (idx " + idx + ") at " + btnX + "," + btnY + " expandedW=" + wid + " scale=" + scale);
        Click(btnX, btnY);
        Thread.Sleep(1200);

        IntPtr st = FindWindowOf(pid, "设置");
        Say("settings window found=" + (st != IntPtr.Zero));
    }
}
