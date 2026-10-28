using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class TransE2E
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
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);

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

    private static void SendVk(ushort vk, bool up)
    {
        INPUT inp = new INPUT();
        inp.type = 1;
        inp.wVk = vk;
        inp.dwFlags = (uint)(up ? 0x0002 : 0);
        INPUT[] arr = new INPUT[1] { inp };
        SendInput(1, arr, Marshal.SizeOf(typeof(INPUT)));
    }

    private static void SendCombo(ushort vk)
    {
        SendVk(0x11, false);
        Thread.Sleep(40);
        SendVk(0x12, false);
        Thread.Sleep(40);
        SendVk(vk, false);
        Thread.Sleep(70);
        SendVk(vk, true);
        Thread.Sleep(40);
        SendVk(0x12, true);
        Thread.Sleep(40);
        SendVk(0x11, true);
        Thread.Sleep(150);
    }

    private static void SendCtrlA()
    {
        SendVk(0x11, false);
        Thread.Sleep(40);
        SendVk(0x41, false);
        Thread.Sleep(60);
        SendVk(0x41, true);
        Thread.Sleep(40);
        SendVk(0x11, true);
        Thread.Sleep(220);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumProc(IntPtr h, IntPtr p);

    private const string OutDir = @"C:\Users\Administrator\AppData\Local\InputPolishTest";
    private static StringBuilder _log = new StringBuilder();
    private static TextBox _tb;
    private static Form _form;
    private static int _ipPid;

    private static void Say(string s) { _log.AppendLine(s); }

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
            Say("ipc sent: " + cmd);
        }
        catch (Exception ex) { Say("ipc failed: " + ex.Message); }
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

    private static IntPtr FindChrome()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            string pn = "";
            try { pn = Process.GetProcessById((int)pid).ProcessName; } catch { }
            if (!pn.Equals("chrome", StringComparison.OrdinalIgnoreCase)) return true;
            StringBuilder sb = new StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            string t = sb.ToString();
            if (t.IndexOf("InputPolish Page Test", StringComparison.OrdinalIgnoreCase) < 0 &&
                t.IndexOf("TA:", StringComparison.OrdinalIgnoreCase) < 0) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static IntPtr FindOwnedWindow(int pid, string titleWant, int minW, int minH, int maxH)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            uint wpid;
            GetWindowThreadProcessId(h, out wpid);
            if ((int)wpid != pid) return true;
            if (!IsWindowVisible(h)) return true;
            StringBuilder sb = new StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            string t = sb.ToString();
            if (titleWant != null && t.IndexOf(titleWant, StringComparison.OrdinalIgnoreCase) < 0) return true;
            if (titleWant == null && t.Length > 0) return true;
            RECT r;
            if (!GetWindowRect(h, out r)) return true;
            int w = r.Right - r.Left;
            int hh = r.Bottom - r.Top;
            if (w < minW || hh < minH || hh > maxH) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool ClickToFocus(IntPtr h)
    {
        RECT r;
        if (!GetWindowRect(h, out r)) return false;
        int x = r.Left + (r.Right - r.Left) / 2;
        int y = r.Top + (r.Bottom - r.Top) / 3;
        SetCursorPos(x, y);
        Thread.Sleep(180);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(70);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(400);
        Say("clicked " + x + "," + y);
        return GetForegroundWindow() == h;
    }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

    /// <summary>用 PrintWindow 截图，窗口被别的程序遮住也能拿到内容。</summary>
    private static bool CaptureWindow(IntPtr h, Bitmap bmp)
    {
        using (Graphics g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            bool ok = PrintWindow(h, hdc, 2);
            g.ReleaseHdc(hdc);
            return ok;
        }
    }

    private static void Shot(IntPtr h, string name, int pad)
    {
        try
        {
            RECT r;
            GetWindowRect(h, out r);
            int w = r.Right - r.Left;
            int hh = r.Bottom - r.Top;
            using (Bitmap raw = new Bitmap(w, hh))
            {
                CaptureWindow(h, raw);
                using (Bitmap bmp = new Bitmap(w + pad * 2, hh + pad * 2))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        g.DrawImage(raw, pad, pad);
                    }
                    bmp.Save(System.IO.Path.Combine(OutDir, name), ImageFormat.Png);
                }
            }
            Say("shot saved: " + name + " rect=" + r.Left + "," + r.Top + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + " pad=" + pad);
        }
        catch (Exception ex) { Say("shot failed: " + ex.Message); }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "popup";
        try
        {
            System.IO.Directory.CreateDirectory(OutDir);
            Process[] ps = Process.GetProcessesByName("InputPolish");
            if (ps.Length == 0) { Say("InputPolish not running"); Flush(mode); return; }
            _ipPid = ps[0].Id;
            Say("InputPolish pid=" + _ipPid);

            _form = new Form();
            _form.Text = "InputPolish TransTarget";
            _form.StartPosition = FormStartPosition.CenterScreen;
            _form.ClientSize = new Size(880, 520);
            _form.TopMost = true;

            _tb = new TextBox();
            _tb.Multiline = true;
            _tb.ScrollBars = ScrollBars.Vertical;
            _tb.Dock = DockStyle.Fill;
            _tb.Font = new Font("Microsoft YaHei UI", 12F);
            _tb.Text = "The deployment pipeline needs two more days because the database migration is risky. We should freeze feature work until the rollback script has been tested on staging.";
            _form.Controls.Add(_tb);

            _form.Shown += delegate
            {
                _tb.Focus();
                _tb.SelectAll();
                IntPtr h = _form.Handle;
                bool fg = ForceForeground(h);
                if (!fg) fg = ClickToFocus(h);
                if (!fg) fg = ForceForeground(h);
                if (fg)
                {
                    _tb.Focus();
                    Application.DoEvents();
                    Thread.Sleep(150);
                    SendCtrlA();
                    Application.DoEvents();
                    Thread.Sleep(150);
                    if (_tb.SelectionLength == 0)
                    {
                        _tb.SelectionStart = 0;
                        _tb.SelectionLength = _tb.TextLength;
                        Application.DoEvents();
                        Thread.Sleep(120);
                    }
                }
                Say("form hwnd=" + h.ToInt64() + " foreground=" + fg + " fgNow=" + GetForegroundWindow().ToInt64());
                Application.DoEvents();
                Say("selection len=" + _tb.SelectionLength);

                if (mode == "hotkey")
                {
                    Say("sending Ctrl+Alt+T");
                    SendCombo(0x54);
                    IntPtr popup = IntPtr.Zero;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(90);
                    while (DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(120);
                        popup = FindOwnedWindow(_ipPid, null, 240, 90, 1100);
                        if (popup != IntPtr.Zero) break;
                    }
                    Say(popup == IntPtr.Zero ? "popup NOT found via hotkey" : ("popup via hotkey hwnd=" + popup.ToInt64()));
                    if (popup != IntPtr.Zero)
                    {
                        Thread.Sleep(600);
                        Shot(popup, "popup_hotkey.png", 24);
                    }
                }
                else if (mode == "pagehotkey")
                {
                    _form.TopMost = false;
                    _form.Hide();
                    Application.DoEvents();
                    Thread.Sleep(300);
                    IntPtr ch = FindChrome();
                    if (ch == IntPtr.Zero) { Say("chrome not found"); Flush(mode); return; }
                    bool cok = ForceForeground(ch);
                    Say("chrome fg=" + cok);
                    Thread.Sleep(400);
                    Say("sending Ctrl+Alt+W");
                    SendCombo(0x57);
                    IntPtr pane = IntPtr.Zero;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(60);
                    while (DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(150);
                        pane = FindOwnedWindow(_ipPid, "网页翻译", 400, 200, 2000);
                        if (pane != IntPtr.Zero) break;
                    }
                    Say(pane == IntPtr.Zero ? "pane NOT found via hotkey" : ("pane via hotkey hwnd=" + pane.ToInt64()));
                    if (pane != IntPtr.Zero)
                    {
                        Thread.Sleep(30000);
                        Shot(pane, "pane_hotkey.png", 10);
                    }
                }
                else if (mode == "popup")
                {
                    SendIpc("translate");
                    IntPtr popup = IntPtr.Zero;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(75);
                    while (DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(120);
                        popup = FindOwnedWindow(_ipPid, null, 240, 90, 1100);
                        if (popup != IntPtr.Zero) break;
                    }
                    if (popup == IntPtr.Zero) { Say("popup NOT found"); }
                    else
                    {
                        Thread.Sleep(700);
                        Say("popup hwnd=" + popup.ToInt64() + " fgAfterShow=" + GetForegroundWindow().ToInt64() + " stillMine=" + (GetForegroundWindow() == _form.Handle));
                        Say("popup exStyle=0x" + GetWindowLong(popup, -20).ToString("X8"));
                        Shot(popup, "popup_translate.png", 24);
                    }
                }
                else if (mode == "page")
                {
                    _form.TopMost = false;
                    _form.Hide();
                    Application.DoEvents();
                    Thread.Sleep(400);

                    IntPtr ch = FindChrome();
                    if (ch == IntPtr.Zero) { Say("chrome page window not found"); Flush(mode); return; }
                    RECT cr;
                    GetWindowRect(ch, out cr);
                    Say("chrome hwnd=" + ch.ToInt64() + " rect=" + cr.Left + "," + cr.Top + " " + (cr.Right - cr.Left) + "x" + (cr.Bottom - cr.Top));
                    bool cok = ForceForeground(ch);
                    if (!cok) cok = ClickToFocus(ch);
                    Say("chrome foreground=" + cok + " fgNow=" + GetForegroundWindow().ToInt64());
                    Thread.Sleep(400);

                    SendIpc("page");
                    IntPtr pane = IntPtr.Zero;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(60);
                    while (DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(150);
                        pane = FindOwnedWindow(_ipPid, "网页翻译", 400, 200, 2000);
                        if (pane != IntPtr.Zero) break;
                    }
                    if (pane == IntPtr.Zero) Say("pane NOT found");
                    else
                    {
                        Say("pane hwnd=" + pane.ToInt64());
                        Thread.Sleep(38000);
                        Shot(pane, "pane_page.png", 10);
                        Shot(pane, "pane_page2.png", 10);
                        Say("pane captured");
                    }
                }

                Thread.Sleep(500);
                Application.Exit();
            };

            Application.Run(_form);
        }
        catch (Exception ex) { Say("EXCEPTION: " + ex.ToString()); }
        Flush(mode);
    }

    private static void Flush(string mode)
    {
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(OutDir, "transe2e_" + mode + ".txt"), _log.ToString(), Encoding.UTF8);
        }
        catch { }
    }
}
