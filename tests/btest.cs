using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class BTest
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int c);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool f);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);

    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public ushort wVk;
        [FieldOffset(10)] public ushort wScan;
        [FieldOffset(12)] public uint dwFlags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(24)] public IntPtr dwExtraInfo;
    }

    private static IntPtr _win = IntPtr.Zero;
    private static string _title = "";

    private static void Say(string s) { Console.WriteLine(s); }

    private static void SendIpc(string cmd)
    {
        try
        {
            using (System.IO.Pipes.NamedPipeClientStream pipe = new System.IO.Pipes.NamedPipeClientStream(".", "InputPolish_cmd_7F3A21", System.IO.Pipes.PipeDirection.Out))
            {
                pipe.Connect(2500);
                byte[] payload = Encoding.UTF8.GetBytes(cmd);
                pipe.Write(payload, 0, payload.Length);
                pipe.Flush();
            }
        }
        catch (Exception ex)
        {
            Say("ipc failed: " + ex.Message);
        }
    }

    private static void SendVk(ushort vk, bool up)
    {
        INPUT inp = new INPUT();
        inp.type = 1;
        inp.wVk = vk;
        inp.dwFlags = (uint)(up ? 0x0002 : 0);
        INPUT[] arr = new INPUT[1] { inp };
        SendInput(1, arr, Marshal.SizeOf(typeof(INPUT)));
    }

    private static void SendCtrlAltL()
    {
        SendVk(0x11, false);
        Thread.Sleep(40);
        SendVk(0x12, false);
        Thread.Sleep(40);
        SendVk(0x4C, false);
        Thread.Sleep(60);
        SendVk(0x4C, true);
        Thread.Sleep(40);
        SendVk(0x12, true);
        Thread.Sleep(40);
        SendVk(0x11, true);
    }

    private static string FocusControlType()
    {
        try
        {
            System.Windows.Automation.AutomationElement el = System.Windows.Automation.AutomationElement.FocusedElement;
            return el.Current.ControlType.ProgrammaticName;
        }
        catch
        {
            return "";
        }
    }

    private static bool ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(180);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(70);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(450);
        string ct = FocusControlType();
        Say("click at " + x + "," + y + " -> focus " + ct);
        return ct.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void SayFocus()
    {
        try
        {
            System.Windows.Automation.AutomationElement el = System.Windows.Automation.AutomationElement.FocusedElement;
            string ct = "", cls = "", name = "", aid = "";
            int pid = 0;
            try { ct = el.Current.ControlType.ProgrammaticName; } catch { }
            try { cls = el.Current.ClassName; } catch { }
            try { name = el.Current.Name; } catch { }
            try { aid = el.Current.AutomationId; } catch { }
            try { pid = el.Current.ProcessId; } catch { }
            string proc = "";
            try { proc = Process.GetProcessById(pid).ProcessName; } catch { }
            object vp;
            bool hasValue = el.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out vp);
            object tp;
            bool hasText = el.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out tp);
            Say("focus: " + ct + " proc=" + proc + " class=" + cls + " aid=" + aid + " value=" + hasValue + " text=" + hasText);
        }
        catch (Exception ex)
        {
            Say("focus read failed: " + ex.Message);
        }
    }

    private static IntPtr FindChrome()
    {
        _win = IntPtr.Zero;
        _title = "";
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            string pn = "";
            try { pn = Process.GetProcessById((int)pid).ProcessName; } catch { }
            if (!pn.Equals("chrome", StringComparison.OrdinalIgnoreCase)) return true;
            StringBuilder sb = new StringBuilder(1024);
            GetWindowTextW(h, sb, sb.Capacity);
            string t = sb.ToString();
            if (t.IndexOf("InputPolish Test", StringComparison.OrdinalIgnoreCase) < 0 && t.IndexOf("TA:", StringComparison.OrdinalIgnoreCase) < 0) return true;
            _win = h;
            _title = t;
            return false;
        }, IntPtr.Zero);
        return _win;
    }

    private static string TitleOf(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(1024);
        GetWindowTextW(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static void SendSpace()
    {
        INPUT[] a = new INPUT[2];
        a[0].type = 1; a[0].wVk = 0x20; a[0].wScan = 0x39;
        a[1].type = 1; a[1].wVk = 0x20; a[1].wScan = 0x39; a[1].dwFlags = 0x0002;
        uint n = SendInput(2, a, Marshal.SizeOf(typeof(INPUT)));
        if (n != 2) Say("sendinput failed n=" + n);
    }

    private static bool ForceForeground(IntPtr h)
    {
        SystemParametersInfo(0x2004, 0, IntPtr.Zero, 0);
        if (IsIconic(h)) ShowWindow(h, 9);
        for (int i = 0; i < 12; i++)
        {
            uint pid;
            uint target = GetWindowThreadProcessId(h, out pid);
            uint me = GetCurrentThreadId();
            bool att = AttachThreadInput(me, target, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (att) AttachThreadInput(me, target, false);
            Thread.Sleep(120);
            if (GetForegroundWindow() == h) return true;
        }
        return GetForegroundWindow() == h;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        try { Body(args); }
        catch (Exception ex)
        {
            try { System.IO.File.WriteAllText("C:\\Users\\Administrator\\AppData\\Local\\InputPolishTest\\btest.err.txt", ex.GetType().FullName + ": " + ex.Message + "\n" + ex.StackTrace); }
            catch { }
            throw;
        }
    }

    private static void Body(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "run";
        IntPtr h = FindChrome();
        if (h == IntPtr.Zero) { Say("chrome window not found"); return; }
        Say("window=" + h.ToInt64() + " title=[" + _title + "]");

        RECT rr;
        GetWindowRect(h, out rr);
        Say("rect=" + rr.Left + "," + rr.Top + "," + (rr.Right - rr.Left) + "x" + (rr.Bottom - rr.Top));

        int w = rr.Right - rr.Left;
        int hh = rr.Bottom - rr.Top;

        // 单击窗口中央抢前台，然后在 textarea 区域内多点重试，直到焦点落到 Edit
        bool gotEdit = ClickAt(rr.Left + w / 2, rr.Top + hh / 2);
        bool fg = ForceForeground(h);
        Say("foreground=" + fg + " fgHwnd=" + GetForegroundWindow().ToInt64());
        if (!gotEdit) gotEdit = ClickAt(rr.Left + w / 2, rr.Top + hh / 2);
        if (!gotEdit) gotEdit = ClickAt(rr.Left + w / 3, rr.Top + hh / 3);
        if (!gotEdit) gotEdit = ClickAt(rr.Left + (int)(w * 0.25), rr.Top + (int)(hh * 0.25));
        Say("focused editable=" + gotEdit);

        SayFocus();

        if (mode == "hotkey")
        {
            string hkBefore = TitleOf(h);
            SendCtrlAltL();
            Thread.Sleep(400);
            Say("sent Ctrl+Alt+L, before=[" + hkBefore + "]");
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(500);
                if (TitleOf(h) != hkBefore) { Thread.Sleep(900); break; }
            }
            Say("after=[" + TitleOf(h) + "]");
            return;
        }
        if (mode == "ipc")
        {
            SendIpc("polish");
            Say("ipc polish sent");
        }
        else if (mode == "undo")
        {
            SendIpc("undo");
            Say("ipc undo sent");
        }
        else if (mode == "run")
        {
            for (int i = 0; i < 3; i++) { SendSpace(); Thread.Sleep(130); }
            Thread.Sleep(400);
            Say("sent 3 spaces, title now=[" + TitleOf(h) + "]");
        }

        string before = _title;
        DateTime deadline = DateTime.UtcNow.AddSeconds(45);
        string now = before;
        bool changed = false;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(250);
            now = TitleOf(h);
            if (now != before) { changed = true; break; }
        }
        // 等标题稳定
        string prev = now;
        int stable = 0;
        while (stable < 6)
        {
            Thread.Sleep(250);
            string t = TitleOf(h);
            if (t == prev) stable++; else { prev = t; stable = 0; }
        }
        Say("changed=" + changed);
        Say("after=[" + TitleOf(h) + "]");
    }
}
