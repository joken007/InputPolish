using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class E2E
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint GetWindowThreadProcessIdPid(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr h, bool alt);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, IntPtr vparam, uint winini);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    private static string _out = "C:\\Users\\Administrator\\AppData\\Local\\InputPolishTest\\e2e.txt";
    private static StringBuilder _log = new StringBuilder();

    private static void Say(string s)
    {
        _log.AppendLine(s);
        try { File.WriteAllText(_out, _log.ToString(), Encoding.UTF8); } catch { }
    }

    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public ushort wVk;
        [FieldOffset(10)] public ushort wScan;
        [FieldOffset(12)] public uint dwFlags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(24)] public IntPtr extra;
    }

    private static void SendSpace()
    {
        INPUT[] inputs = new INPUT[2];
        inputs[0].type = 1; inputs[0].wVk = 0x20;
        inputs[1].type = 1; inputs[1].wVk = 0x20; inputs[1].dwFlags = 2;
        uint n = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        if (n != 2) Say("sendinput returned " + n);
    }

    private static bool SendIpc(string cmd)
    {
        try
        {
            using (NamedPipeClientStream c = new NamedPipeClientStream(".", "InputPolish_cmd_7F3A21", PipeDirection.Out))
            {
                c.Connect(2500);
                byte[] b = Encoding.UTF8.GetBytes(cmd);
                c.Write(b, 0, b.Length);
                c.Flush();
            }
            return true;
        }
        catch (Exception ex)
        {
            Say("ipc error: " + ex.Message);
            return false;
        }
    }

    private static void UnlockForeground()
    {
        // 关闭前台锁定超时，让后台测试进程也能把窗口切到前台
        SystemParametersInfo(0x2004, 0, IntPtr.Zero, 0);
    }

    private static void ForceForeground(Form form)
    {
        IntPtr hwnd = form.Handle;
        for (int i = 0; i < 12; i++)
        {
            if (GetForegroundWindow() == hwnd) break;
            IntPtr fgw = GetForegroundWindow();
            uint pid;
            uint fgThread = GetWindowThreadProcessIdPid(fgw, out pid);
            uint myThread = GetCurrentThreadId();
            bool attached = false;
            if (fgThread != myThread) attached = AttachThreadInput(myThread, fgThread, true);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            form.Activate();
            if (attached) AttachThreadInput(myThread, fgThread, false);
            Application.DoEvents();
            Thread.Sleep(120);
        }
    }

    // 真实鼠标点击窗口中心，用来夺前台（后台进程 SetForegroundWindow 会被前台锁拒绝）
    private static void ClickToFocus(Form form)
    {
        POINT old;
        GetCursorPos(out old);
        Point c = form.PointToScreen(new Point(form.Width / 2, form.Height / 2));
        SetCursorPos(c.X, c.Y);
        Thread.Sleep(120);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(200);
        SetCursorPos(old.X, old.Y);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        string draft = "这个方案有点问题主要是成本太高而且交付周期长";
        bool useUndo = args.Length > 0 && args[0] == "undo";

        Form form = new Form();
        form.Text = "InputPolish E2E Harness";
        form.Width = 900;
        form.Height = 300;
        form.StartPosition = FormStartPosition.CenterScreen;
        TextBox tb = new TextBox();
        tb.Multiline = true;
        tb.Dock = DockStyle.Fill;
        tb.Font = new Font("Microsoft YaHei", 11f);
        tb.Text = draft;
        form.Controls.Add(tb);
        form.TopMost = true;
        UnlockForeground();
        form.Show();
        Application.DoEvents();
        ForceForeground(form);
        Application.DoEvents();
        Thread.Sleep(300);
        ClickToFocus(form);
        Application.DoEvents();
        Thread.Sleep(400);
        Application.DoEvents();
        tb.Focus();
        tb.SelectionStart = tb.Text.Length;
        Application.DoEvents();

        IntPtr fg2 = GetForegroundWindow();
        Say("harness ready, draft len=" + draft.Length);
        Say("foreground is our form: " + (fg2 == form.Handle) + " (hwnd=" + form.Handle.ToInt64() + " fg=" + fg2.ToInt64() + ")");

        bool ok = true;
        if (useUndo) { SendIpc("undo"); }
        else
        {
            for (int i = 0; i < 3; i++) { SendSpace(); Thread.Sleep(120); }
            Say("sent 3 injected spaces");
        }
        Say("ipc sent=" + ok);

        DateTime deadline = DateTime.UtcNow.AddSeconds(50);
        bool changed = false;
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(100);
            if (tb.Text.Trim() != draft.Trim()) { changed = true; break; }
        }

        // 等文本稳定 900ms，避免把「刚敲进去的三个空格」当成润色结果
        string prev = tb.Text;
        int stable = 0;
        while (stable < 9 && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(100);
            if (tb.Text == prev) stable++; else { prev = tb.Text; stable = 0; }
        }

        Say("changed=" + changed);
        Say("final=[" + tb.Text.Replace("\r\n", " / ") + "]");
        Say("finalLen=" + tb.Text.Length);

        if (!useUndo)
        {
            Thread.Sleep(600);
            Application.DoEvents();
            Say("--- triggering undo ---");
            SendIpc("undo");
            DateTime d2 = DateTime.UtcNow.AddSeconds(15);
            bool restored = false;
            while (DateTime.UtcNow < d2)
            {
                Application.DoEvents();
                Thread.Sleep(100);
                if (tb.Text.Trim() == draft.Trim()) { restored = true; break; }
            }
            Say("undo restored=" + restored);
            Say("after undo=[" + tb.Text.Replace("\r\n", " / ") + "]");
            // 给宿主留出回读校验的时间，不要立刻销毁窗口
            DateTime d3 = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < d3) { Application.DoEvents(); Thread.Sleep(50); }
        }

        Say("=== e2e done ===");
        form.Close();
    }
}
