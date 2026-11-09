using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

internal static class UiaFocus
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int c);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO gti);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    private static string Trim(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 60 ? s.Substring(0, 60) + "..." : s;
    }

    private static string Desc(AutomationElement e)
    {
        if (e == null) return "(null)";
        StringBuilder sb = new StringBuilder();
        try
        {
            AutomationElement.AutomationElementInformation i = e.Current;
            sb.Append("proc=" + i.ProcessId);
            sb.Append(" type=" + i.ControlType.ProgrammaticName);
            sb.Append(" name=[" + Trim(i.Name) + "]");
            sb.Append(" cls=" + i.ClassName);
        }
        catch (Exception ex) { sb.Append("err=" + ex.Message); }
        return sb.ToString();
    }

    private static int Main()
    {
        IntPtr fg = GetForegroundWindow();
        StringBuilder t = new StringBuilder(300);
        GetWindowTextW(fg, t, 300);
        uint pid;
        uint tid = GetWindowThreadProcessId(fg, out pid);
        Console.WriteLine("foreground hwnd=" + fg.ToInt64() + " pid=" + pid + " title=[" + t + "]");

        GUITHREADINFO gti = new GUITHREADINFO();
        gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
        if (GetGUIThreadInfo(tid, ref gti))
        {
            Console.WriteLine("gti.hwndFocus=" + gti.hwndFocus.ToInt64() + " hwndCaret=" + gti.hwndCaret.ToInt64());
        }

        POINT p;
        GetCursorPos(out p);
        Console.WriteLine("cursor=" + p.X + "," + p.Y);

        AutomationElement fe = AutomationElement.FocusedElement;
        Console.WriteLine("focused: " + Desc(fe));

        object vp;
        if (fe != null && fe.TryGetCurrentPattern(ValuePattern.Pattern, out vp))
        {
            ValuePattern v = (ValuePattern)vp;
            Console.WriteLine("  ValuePattern readOnly=" + v.Current.IsReadOnly + " value=[" + Trim(v.Current.Value) + "]");
        }
        else
        {
            Console.WriteLine("  no ValuePattern");
        }

        object tp;
        if (fe != null && fe.TryGetCurrentPattern(TextPattern.Pattern, out tp))
        {
            TextPattern t2 = (TextPattern)tp;
            Console.WriteLine("  TextPattern len=" + t2.DocumentRange.GetText(400).Length);
        }
        else
        {
            Console.WriteLine("  no TextPattern");
        }

        AutomationElement cur = fe;
        for (int i = 0; i < 5 && cur != null; i++)
        {
            cur = TreeWalker.ControlViewWalker.GetParent(cur);
            Console.WriteLine("  parent[" + i + "]: " + Desc(cur));
        }

        return 0;
    }
}
