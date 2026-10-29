using System;
using System.Runtime.InteropServices;
using System.Text;

class Fg
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    static string Info(IntPtr h)
    {
        if (h == IntPtr.Zero) return "(null)";
        StringBuilder t = new StringBuilder(512); GetWindowTextW(h, t, 512);
        StringBuilder c = new StringBuilder(256); GetClassNameW(h, c, 256);
        uint pid; GetWindowThreadProcessId(h, out pid);
        RECT r; GetWindowRect(h, out r);
        int ex = GetWindowLong(h, -20);
        string pname = "?";
        try { pname = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
        return "hwnd=" + h + " proc=" + pname + "(" + pid + ") cls=" + c + " rect=" + r.Left + "," + r.Top + "," + (r.Right-r.Left) + "x" + (r.Bottom-r.Top) + " ex=0x" + ex.ToString("X8") + " title=[" + t + "]";
    }

    static void Main(string[] a)
    {
        Console.WriteLine("FG " + Info(GetForegroundWindow()));
        IntPtr top = GetWindow(GetForegroundWindow(), 4); // GW_HWNDPREV? use GetTopWindow instead
        IntPtr first = GetWindow(IntPtr.Zero, 0);
        int n = 0;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            int ex = GetWindowLong(h, -20);
            if ((ex & 0x8) != 0) Console.WriteLine("TOPMOST " + Info(h));
            n++;
            return n < 4000;
        }, IntPtr.Zero);
        Console.WriteLine("visible windows=" + n);
    }
}
