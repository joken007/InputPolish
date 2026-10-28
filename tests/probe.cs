using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Automation;

internal static class Probe
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO info);
    [DllImport("user32.dll")] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, IntPtr vparam, uint winini);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize; public int flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    private static string _out = "C:\\Users\\Administrator\\AppData\\Local\\InputPolishTest\\probe.txt";
    private static StringBuilder _log = new StringBuilder();
    private static void Say(string s) { _log.AppendLine(s); try { File.WriteAllText(_out, _log.ToString(), Encoding.UTF8); } catch { } }

    [STAThread]
    private static void Main(string[] args)
    {
        Form form = new Form();
        form.Text = "InputPolish Focus Probe";
        form.Width = 800;
        form.Height = 260;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(120, 120);
        TextBox tb = new TextBox();
        tb.Multiline = true;
        tb.Dock = DockStyle.Fill;
        tb.Text = "probe draft";
        form.Controls.Add(tb);
        form.TopMost = true;
        // 关闭前台锁定超时，让后台进程也能抢到前台
        IntPtr oldTimeout = Marshal.AllocHGlobal(8);
        SystemParametersInfo(0x2003, 0, oldTimeout, 0);   // SPI_GETFOREGROUNDLOCKTIMEOUT
        SystemParametersInfo(0x2004, 0, IntPtr.Zero, 0);  // SPI_SETFOREGROUNDLOCKTIMEOUT = 0

        form.Show();
        Application.DoEvents();
        {
            uint me = GetCurrentThreadId();
            uint pid;
            uint fgt = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            if (fgt != me) AttachThreadInput(me, fgt, true);
            BringWindowToTop(form.Handle);
            SetForegroundWindow(form.Handle);
            form.Activate();
            if (fgt != me) AttachThreadInput(me, fgt, false);
        }
        Application.DoEvents();
        int oldVal = Marshal.ReadInt32(oldTimeout);
        Say("old foreground lock timeout=" + oldVal);
        Point c = form.PointToScreen(new Point(form.Width / 2, form.Height / 2));
        SetCursorPos(c.X, c.Y);
        Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(300);
        tb.Focus();
        Application.DoEvents();
        Thread.Sleep(300);
        Application.DoEvents();

        IntPtr fg = GetForegroundWindow();
        Say("form hwnd=" + form.Handle.ToInt64() + " fg=" + fg.ToInt64() + " isForeground=" + (fg == form.Handle));

        uint tid = GetWindowThreadProcessId(fg, IntPtr.Zero);
        GUITHREADINFO gi = new GUITHREADINFO();
        gi.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
        bool ok = GetGUIThreadInfo(tid, ref gi);
        Say("guiinfo ok=" + ok + " focusHwnd=" + gi.hwndFocus.ToInt64() + " active=" + gi.hwndActive.ToInt64());
        StringBuilder cn = new StringBuilder(256);
        GetClassName(gi.hwndFocus, cn, 256);
        Say("focus class=" + cn.ToString());

        try
        {
            AutomationElement fe = AutomationElement.FocusedElement;
            Say("uia focused: name=[" + fe.Current.Name + "] class=" + fe.Current.ClassName + " type=" + fe.Current.ControlType.ProgrammaticName + " pid=" + fe.Current.ProcessId);
            object vp;
            bool hasValue = fe.TryGetCurrentPattern(ValuePattern.Pattern, out vp);
            Say("uia valuePattern=" + hasValue);
            if (hasValue) Say("uia value=[" + ((ValuePattern)vp).Current.Value + "] readonly=" + ((ValuePattern)vp).Current.IsReadOnly);
            object tp;
            bool hasText = fe.TryGetCurrentPattern(TextPattern.Pattern, out tp);
            Say("uia textPattern=" + hasText);
            AutomationElement parent = TreeWalker.ControlViewWalker.GetParent(fe);
            if (parent != null) Say("parent: " + parent.Current.ControlType.ProgrammaticName + " class=" + parent.Current.ClassName);
        }
        catch (Exception ex)
        {
            Say("uia error: " + ex.Message);
        }

        DateTime d = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < d) { Application.DoEvents(); Thread.Sleep(100); }
        form.Close();
    }
}
