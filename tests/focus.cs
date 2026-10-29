using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

class Focus
{
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(int a, int b, IntPtr c, int d);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool f);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    delegate bool EnumProc(IntPtr h, IntPtr p);

    static IntPtr _found = IntPtr.Zero;
    static string _want = "";

    static bool Cb(IntPtr h, IntPtr p)
    {
        if (!IsWindowVisible(h)) return true;
        var sb = new StringBuilder(512);
        GetWindowTextW(h, sb, 512);
        string t = sb.ToString();
        if (t.Length == 0) return true;
        uint pid; GetWindowThreadProcessId(h, out pid);
        string name = "";
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch { }
        if (name.IndexOf(_want, StringComparison.OrdinalIgnoreCase) >= 0 && _found == IntPtr.Zero)
        {
            _found = h;
            Console.WriteLine("candidate hwnd=" + h + " proc=" + name + " title=" + t);
        }
        return true;
    }

    static int Main(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("usage: focus <procFrag>"); return 2; }
        _want = args[0];
        EnumWindows(Cb, IntPtr.Zero);
        if (_found == IntPtr.Zero) { Console.WriteLine("not found"); return 3; }
        SystemParametersInfo(0x2004, 0, IntPtr.Zero, 0);
        for (int i = 0; i < 14; i++)
        {
            if (IsIconic(_found)) ShowWindow(_found, 9);
            uint pid; uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            uint my = GetCurrentThreadId();
            AttachThreadInput(my, fgThread, true);
            BringWindowToTop(_found);
            SetForegroundWindow(_found);
            AttachThreadInput(my, fgThread, false);
            System.Threading.Thread.Sleep(160);
            if (GetForegroundWindow() == _found) { Console.WriteLine("focused hwnd=" + _found + " attempt=" + (i + 1)); return 0; }
        }
        Console.WriteLine("failed to focus, fg=" + GetForegroundWindow());
        return 1;
    }
}
