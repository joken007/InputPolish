using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

class PClick
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool f);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d);

    delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    static IntPtr Find(string frag)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder sb = new StringBuilder(512);
            GetWindowTextW(h, sb, 512);
            if (sb.ToString().IndexOf(frag, StringComparison.Ordinal) >= 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static void Focus(IntPtr h)
    {
        SystemParametersInfo(0x2004, 0, IntPtr.Zero, 0);
        uint pid; uint tid = GetWindowThreadProcessId(h, out pid);
        uint me = GetCurrentThreadId();
        for (int i = 0; i < 12; i++)
        {
            AttachThreadInput(me, tid, true);
            ShowWindow(h, 9);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            AttachThreadInput(me, tid, false);
            System.Threading.Thread.Sleep(80);
        }
    }

    static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(160);
    }

    static int Main(string[] a)
    {
        try { Real(a); return 0; }
        catch (Exception ex) { Console.WriteLine("ERR " + ex.GetType().Name + ": " + ex.Message); return 1; }
    }

    static void Real(string[] a)
    {
        string cmd = a.Length > 0 ? a[0] : "info";
        IntPtr h = Find("快捷面板");
        if (h == IntPtr.Zero) h = Find("编辑提示词");
        if (h == IntPtr.Zero) h = Find("新建提示词");
        if (h == IntPtr.Zero) { Console.WriteLine("window not found"); return; }
        RECT wr; GetWindowRect(h, out wr);
        RECT cr; GetClientRect(h, out cr);
        POINT o = new POINT(); ClientToScreen(h, ref o);
        StringBuilder tt = new StringBuilder(512); GetWindowTextW(h, tt, 512);
        Console.WriteLine("win=[" + tt + "] rect=" + wr.Left + "," + wr.Top + " " + (wr.Right-wr.Left) + "x" + (wr.Bottom-wr.Top) + " client=" + cr.Right + "x" + cr.Bottom + " origin=" + o.X + "," + o.Y);

        if (cmd == "info") return;
        if (cmd == "click")
        {
            int dx = int.Parse(a[1]), dy = int.Parse(a[2]);
            Focus(h);
            Click(o.X + dx, o.Y + dy);
            System.Threading.Thread.Sleep(300);
            Console.WriteLine("clicked " + (o.X + dx) + "," + (o.Y + dy));
            return;
        }
        if (cmd == "hover")
        {
            int dx = int.Parse(a[1]), dy = int.Parse(a[2]);
            Focus(h);
            SetCursorPos(o.X + dx, o.Y + dy);
            System.Threading.Thread.Sleep(400);
            Console.WriteLine("hovered");
            return;
        }
        if (cmd == "drag")
        {
            int x1 = int.Parse(a[1]), y1 = int.Parse(a[2]), x2 = int.Parse(a[3]), y2 = int.Parse(a[4]);
            Focus(h);
            SetCursorPos(o.X + x1, o.Y + y1);
            System.Threading.Thread.Sleep(120);
            mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
            for (int i = 1; i <= 10; i++)
            {
                SetCursorPos(o.X + x1 + (x2 - x1) * i / 10, o.Y + y1 + (y2 - y1) * i / 10);
                System.Threading.Thread.Sleep(25);
            }
            mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(250);
            Console.WriteLine("dragged");
            return;
        }
        if (cmd == "wheel")
        {
            int dx = int.Parse(a[1]), dy = int.Parse(a[2]), times = int.Parse(a[3]);
            Focus(h);
            SetCursorPos(o.X + dx, o.Y + dy);
            System.Threading.Thread.Sleep(200);
            for (int i = 0; i < Math.Abs(times); i++)
            {
                mouse_event(0x0800, 0, 0, (uint)(times > 0 ? -120 : 120), IntPtr.Zero);
                System.Threading.Thread.Sleep(80);
            }
            System.Threading.Thread.Sleep(250);
            Console.WriteLine("wheeled");
            return;
        }
        Console.WriteLine("unknown cmd " + cmd);
    }
}
