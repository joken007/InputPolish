using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class CloseWin
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int c);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [STAThread]
    private static void Main(string[] args)
    {
        string procName = args.Length > 0 ? args[0] : "InputPolish";
        string titlePart = args.Length > 1 ? args[1] : "设置";
        Process[] ps = Process.GetProcessesByName(procName);
        if (ps.Length == 0) { Console.WriteLine("no process"); return; }
        int pid = ps[0].Id;
        int closed = 0;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            uint wp;
            GetWindowThreadProcessId(h, out wp);
            if (wp != pid) return true;
            StringBuilder sb = new StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            string t = sb.ToString();
            if (t.IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);
                Console.WriteLine("closed [" + t + "]");
                closed++;
            }
            return true;
        }, IntPtr.Zero);
        Console.WriteLine("closed=" + closed);
        Thread.Sleep(300);
    }
}
