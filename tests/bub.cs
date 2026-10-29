using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

internal static class Bub
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindowThreadProcessId2(IntPtr h, IntPtr pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool f);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);

    private const uint LEFTDOWN = 0x0002;
    private const uint LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public ushort wVk;
        [FieldOffset(10)] public ushort wScan;
        [FieldOffset(12)] public uint dwFlags;
        [FieldOffset(16)] public int time;
        [FieldOffset(24)] public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    private delegate bool EnumProc(IntPtr h, IntPtr l);

    private static readonly StringBuilder Out = new StringBuilder();
    private static void Say(string s)
    {
        string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s;
        Out.AppendLine(line);
        Console.WriteLine(line);
    }

    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "bubble";
        try
        {
            if (mode == "bubble") RunBubble();
            else if (mode == "panel") RunPanel();
            else if (mode == "pagebtn") RunPageBtn();
            else if (mode == "summary") RunSummary();
            else if (mode == "wbtn") RunWidgetButton(args.Length > 1 ? int.Parse(args[1]) : 1);
            else if (mode == "find") RunFind();
            else if (mode == "hotkeypanel") RunHotkeyPanel();
            else if (mode == "panelbtn") RunPanelButton(args.Length > 1 ? args[1] : "翻译本页");
            else if (mode == "wintxt") RunWinText(args.Length > 1 ? args[1] : "");
            else if (mode == "hover") RunHover();
            else Say("unknown mode");
        }
        catch (Exception ex)
        {
            Say("EXCEPTION " + ex.GetType().Name + ": " + ex.Message);
        }
        string outDir = Path.Combine(Path.GetTempPath(), "InputPolishTest");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "bub_" + mode + ".txt"), Out.ToString(), Encoding.UTF8);
        return 0;
    }

    private static void RunFind()
    {
        int ipid = PolishProcessId();
        List<IntPtr> ws = OwnWindows(ipid);
        Say("windows=" + ws.Count);
        for (int i = 0; i < ws.Count; i++)
        {
            Say("  " + Desc(ws[i]));
            RECT r;
            GetWindowRect(ws[i], out r);
            if (r.Right - r.Left >= 200) Say("    shot=" + ShotFile(ws[i], "find_" + i + ".png"));
        }
    }

    private static void RunSummary()
    {
        int ipid = PolishProcessId();
        Say("ipc summary sent=" + SendIpc("summary"));
        IntPtr pane = IntPtr.Zero;
        DateTime deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            List<IntPtr> ws = OwnWindows(ipid);
            for (int i = 0; i < ws.Count; i++)
            {
                RECT r;
                if (!GetWindowRect(ws[i], out r)) continue;
                if (r.Right - r.Left >= 700 && r.Bottom - r.Top >= 500) { pane = ws[i]; break; }
            }
            if (pane != IntPtr.Zero) break;
            Thread.Sleep(300);
        }
        Say("summary pane=" + (pane == IntPtr.Zero ? "NOT FOUND" : Desc(pane)));
        if (pane != IntPtr.Zero) { Thread.Sleep(700); Say("summary shot=" + ShotFile(pane, "summary.png")); }
    }

    private static void RunWidgetButton(int idx)
    {
        int ipid = PolishProcessId();
        IntPtr tab = FindTabbit();
        if (tab != IntPtr.Zero) Say("foreground tabbit=" + ForceForeground(tab) + " " + Desc(tab));
        IntPtr widget = IntPtr.Zero;
        List<IntPtr> ws = OwnWindows(ipid);
        for (int i = 0; i < ws.Count; i++)
        {
            RECT r;
            if (!GetWindowRect(ws[i], out r)) continue;
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w >= 40 && w <= 80 && h >= 40 && h <= 80) { widget = ws[i]; break; }
        }
        if (widget == IntPtr.Zero) { Say("widget not found"); return; }
        RECT wr;
        GetWindowRect(widget, out wr);
        Say("widget=" + Desc(widget));
        SetCursorPos((wr.Left + wr.Right) / 2, (wr.Top + wr.Bottom) / 2);
        Thread.Sleep(900);
        GetWindowRect(widget, out wr);
        Say("expanded=" + (wr.Right - wr.Left) + "x" + (wr.Bottom - wr.Top));
        int bx = wr.Left + 64 + idx * 66 + 29;
        int by = (wr.Top + wr.Bottom) / 2;
        Say("click idx=" + idx + " at " + bx + "," + by);
        ClickAt(bx, by);
        Thread.Sleep(1200);
        Say("shot=" + ShotFile(widget, "widget_after_btn" + idx + ".png"));
    }

    private static void RunHotkeyPanel()
    {
        IntPtr tab = FindTabbit();
        if (tab != IntPtr.Zero) { Say("foreground tabbit=" + ForceForeground(tab)); Thread.Sleep(400); }
        SendCtrlKey(0xC0);
        Say("sent ctrl+backtick");
        int ipid = PolishProcessId();
        IntPtr panel = IntPtr.Zero;
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            List<IntPtr> ws = OwnWindows(ipid);
            for (int i = 0; i < ws.Count; i++)
            {
                RECT r;
                if (!GetWindowRect(ws[i], out r)) continue;
                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w >= 380 && w <= 520 && h >= 180 && h <= 280) { panel = ws[i]; break; }
            }
            if (panel != IntPtr.Zero) break;
            Thread.Sleep(150);
        }
        Say("panel via hotkey=" + (panel == IntPtr.Zero ? "NOT FOUND" : Desc(panel)));
        if (panel != IntPtr.Zero) { Thread.Sleep(400); Say("shot=" + ShotFile(panel, "panel_hotkey.png")); }
    }

    private static void RunPanelButton(string name)
    {
        int ipid = PolishProcessId();
        IntPtr panel = IntPtr.Zero;
        List<IntPtr> ws = OwnWindows(ipid);
        for (int i = 0; i < ws.Count; i++)
        {
            RECT r;
            if (!GetWindowRect(ws[i], out r)) continue;
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w >= 380 && w <= 520 && h >= 180 && h <= 280) { panel = ws[i]; break; }
        }
        if (panel == IntPtr.Zero) { Say("panel not open"); return; }
        Say("panel=" + Desc(panel));
        try
        {
            AutomationElement root = AutomationElement.FromHandle(panel);
            Condition cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            AutomationElementCollection all = root.FindAll(TreeScope.Descendants, cond);
            Say("buttons=" + all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                string n = "";
                try { n = all[i].Current.Name; } catch { }
                Say("  btn=" + n);
                if (n == name)
                {
                    object pat;
                    if (all[i].TryGetCurrentPattern(InvokePattern.Pattern, out pat))
                    {
                        ((InvokePattern)pat).Invoke();
                        Say("invoked " + name);
                    }
                    break;
                }
            }
        }
        catch (Exception ex) { Say("uia error " + ex.Message); }
        Thread.Sleep(2500);
        Say("shot=" + ShotFile(panel, "panel_after_btn.png"));
    }

    private static void SendCtrlKey(int vk)
    {
        INPUT[] seq = new INPUT[4];
        seq[0] = Key(0x11, false);
        seq[1] = Key(vk, false);
        seq[2] = Key(vk, true);
        seq[3] = Key(0x11, true);
        SendInput(4, seq, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(400);
    }

    private static INPUT Key(int vk, bool up)
    {
        INPUT i = new INPUT();
        i.type = 1;
        i.wVk = (ushort)vk;
        i.dwFlags = up ? 2u : 0u;
        i.time = 0;
        i.dwExtraInfo = IntPtr.Zero;
        return i;
    }

    private static string Log2(string s) { return s == null ? "" : (s.Length > 50 ? s.Substring(0, 50) : s); }

    private static string WindowTitleOf(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(512);
        GetWindowTextW(h, sb, 512);
        return sb.ToString();
    }

    private static void RunHover()
    {
        int ipid = PolishProcessId();
        IntPtr widget = IntPtr.Zero;
        List<IntPtr> ws = OwnWindows(ipid);
        for (int i = 0; i < ws.Count; i++)
        {
            RECT r;
            if (!GetWindowRect(ws[i], out r)) continue;
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w >= 40 && w <= 80 && h >= 40 && h <= 80) { widget = ws[i]; break; }
        }
        if (widget == IntPtr.Zero) { Say("widget not found"); return; }
        RECT wr;
        GetWindowRect(widget, out wr);
        Say("widget=" + Desc(widget));
        SetCursorPos((wr.Left + wr.Right) / 2, (wr.Top + wr.Bottom) / 2);
        Thread.Sleep(1200);
        Say("shot=" + ShotFile(widget, "widget-hover.png"));
        Thread.Sleep(300);
        SetCursorPos(wr.Right + 260, wr.Top + 40);
    }

    private static void RunWinText(string frag)
    {
        int ipid = PolishProcessId();
        List<IntPtr> ws = OwnWindows(ipid);
        Say("own windows=" + ws.Count);
        for (int i = 0; i < ws.Count; i++)
        {
            string title = WindowTitleOf(ws[i]);
            Say("  win " + Desc(ws[i]) + " title=[" + Log2( title) + "]");
            if (frag.Length > 0 && title.IndexOf(frag) < 0) continue;
            if (IsIconic(ws[i])) { ShowWindow(ws[i], 9); Thread.Sleep(600); }
            RECT r;
            GetWindowRect(ws[i], out r);
            if (r.Right - r.Left < 300) continue;
            Say("window=" + Desc(ws[i]));
            try
            {
                AutomationElement root = AutomationElement.FromHandle(ws[i]);
                Condition cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
                AutomationElementCollection docs = root.FindAll(TreeScope.Descendants, cond);
                Say("docs=" + docs.Count);
                for (int d = 0; d < docs.Count; d++)
                {
                    object pat;
                    if (!docs[d].TryGetCurrentPattern(TextPattern.Pattern, out pat)) continue;
                    string t = ((TextPattern)pat).DocumentRange.GetText(60000);
                    string dir = Path.Combine(Path.GetTempPath(), "InputPolishTest");
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "wintxt_" + d + ".txt");
                    File.WriteAllText(file, t, Encoding.UTF8);
                    Say("doc " + d + " chars=" + t.Length + " -> " + file);
                }
            }
            catch (Exception ex) { Say("uia error " + ex.Message); }
        }
    }

    // ---------- targets ----------
    private static IntPtr FindTabbit()
    {
        Process[] all = Process.GetProcesses();
        for (int i = 0; i < all.Length; i++)
        {
            Process p = all[i];
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero) continue;
                string title = p.MainWindowTitle;
                string name = p.ProcessName;
                if ((title != null && title.IndexOf("Tabbit") >= 0) || (name != null && name.IndexOf("Tabbit") >= 0))
                {
                    RECT r;
                    if (!GetWindowRect(p.MainWindowHandle, out r)) continue;
                    if (r.Right - r.Left < 400) continue;
                    Say("candidate proc=" + name + " pid=" + p.Id + " title=" + title);
                    return p.MainWindowHandle;
                }
            }
            catch
            {
            }
        }
        return IntPtr.Zero;
    }

    private static IntPtr FindWindowByProc(string procName, string titlePart)
    {
        int pid = 0;
        Process[] ps = Process.GetProcessesByName(procName);
        if (ps.Length == 0) return IntPtr.Zero;
        pid = ps[0].Id;
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint wpid;
            GetWindowThreadProcessId(h, out wpid);
            if ((int)wpid != pid) return true;
            if (!IsWindowVisible(h)) return true;
            RECT r;
            if (!GetWindowRect(h, out r)) return true;
            if (r.Right - r.Left < 400) return true;
            StringBuilder sb = new StringBuilder(512);
            GetWindowTextW(h, sb, 512);
            string t = sb.ToString();
            if (titlePart.Length > 0 && t.IndexOf(titlePart) < 0) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static List<IntPtr> OwnWindows(int pid)
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint wpid;
            GetWindowThreadProcessId(h, out wpid);
            if ((int)wpid != pid) return true;
            if (!IsWindowVisible(h)) return true;
            list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static IntPtr FindSmallOwn(int pid, int minW, int maxW, int minH, int maxH)
    {
        List<IntPtr> ws = OwnWindows(pid);
        for (int i = 0; i < ws.Count; i++)
        {
            RECT r;
            if (!GetWindowRect(ws[i], out r)) continue;
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w >= minW && w <= maxW && h >= minH && h <= maxH) return ws[i];
        }
        return IntPtr.Zero;
    }

    private static string Desc(IntPtr h)
    {
        RECT r;
        GetWindowRect(h, out r);
        StringBuilder t = new StringBuilder(256);
        GetWindowTextW(h, t, 256);
        StringBuilder c = new StringBuilder(256);
        GetClassNameW(h, c, 256);
        return "hwnd=" + h.ToInt64() + " rect=" + r.Left + "," + r.Top + "," + (r.Right - r.Left) + "x" + (r.Bottom - r.Top)
            + " cls=" + c.ToString() + " title=[" + t.ToString() + "]";
    }

    private static bool ForceForeground(IntPtr h)
    {
        SystemParametersInfo(0x2004, 0, IntPtr.Zero, 0);
        for (int i = 0; i < 12; i++)
        {
            if (GetForegroundWindow() == h) return true;
            uint dummy;
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out dummy);
            uint myThread = GetCurrentThreadId();
            AttachThreadInput(myThread, fgThread, true);
            SetForegroundWindow(h);
            AttachThreadInput(myThread, fgThread, false);
            Thread.Sleep(140);
            if (GetForegroundWindow() == h) return true;
        }
        return GetForegroundWindow() == h;
    }

    private static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(160);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(300);
    }

    private static void DragSelect(int x1, int y1, int x2, int y2)
    {
        SetCursorPos(x1, y1);
        Thread.Sleep(180);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(120);
        int steps = 14;
        for (int i = 1; i <= steps; i++)
        {
            SetCursorPos(x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps);
            Thread.Sleep(28);
        }
        Thread.Sleep(160);
        mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(260);
    }

    private static IntPtr PolishPid()
    {
        Process[] ps = Process.GetProcessesByName("InputPolish");
        return IntPtr.Zero;
    }

    private static int PolishProcessId()
    {
        Process[] ps = Process.GetProcessesByName("InputPolish");
        if (ps.Length == 0) return 0;
        return ps[0].Id;
    }

    private static string ShotFile(IntPtr h, string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "InputPolishTest");
        Directory.CreateDirectory(dir);
        RECT r;
        GetWindowRect(h, out r);
        int w = r.Right - r.Left;
        int hh = r.Bottom - r.Top;
        if (w <= 0 || hh <= 0) return "";
        using (Bitmap bmp = new Bitmap(w, hh))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(h, hdc, 2);
                g.ReleaseHdc(hdc);
            }
            string path = Path.Combine(dir, name);
            bmp.Save(path, ImageFormat.Png);
            return path;
        }
    }

    // ---------- modes ----------
    private static void RunBubble()
    {
        int ipid = PolishProcessId();
        Say("InputPolish pid=" + ipid);
        if (ipid == 0) { Say("InputPolish not running"); return; }

        IntPtr win = FindTabbit();
        if (win == IntPtr.Zero) { Say("no Tabbit window"); return; }
        Say("target " + Desc(win));
        bool fg = ForceForeground(win);
        Say("foreground=" + fg);
        Thread.Sleep(500);

        RECT wr;
        GetWindowRect(win, out wr);
        int cx = (wr.Left + wr.Right) / 2;
        int cy = (wr.Top + wr.Bottom) / 2;
        ClickAt(cx, cy);
        Thread.Sleep(400);
        int dy = wr.Top + 420;
        DragSelect(wr.Left + 260, dy, wr.Left + 1300, dy);
        Say("drag done, waiting for bubble");

        IntPtr bubble = IntPtr.Zero;
        DateTime deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            bubble = FindSmallOwn(ipid, 80, 200, 20, 70);
            if (bubble != IntPtr.Zero) break;
            Thread.Sleep(150);
        }
        Say("bubble=" + (bubble == IntPtr.Zero ? "NOT FOUND" : Desc(bubble)));
        if (bubble == IntPtr.Zero)
        {
            Say("windows of InputPolish: " + OwnWindows(ipid).Count);
            foreach (IntPtr h in OwnWindows(ipid)) Say("  " + Desc(h));
            return;
        }
        string shot = ShotFile(bubble, "bubble.png");
        Say("bubble shot=" + shot);

        RECT br;
        GetWindowRect(bubble, out br);
        ClickAt((br.Left + br.Right) / 2, (br.Top + br.Bottom) / 2);
        Say("bubble clicked");

        IntPtr card = IntPtr.Zero;
        deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            List<IntPtr> ws = OwnWindows(ipid);
            for (int i = 0; i < ws.Count; i++)
            {
                RECT r;
                if (!GetWindowRect(ws[i], out r)) continue;
                if (r.Right - r.Left >= 300 && r.Bottom - r.Top >= 90) { card = ws[i]; break; }
            }
            if (card != IntPtr.Zero) break;
            Thread.Sleep(200);
        }
        Say("card=" + (card == IntPtr.Zero ? "NOT FOUND" : Desc(card)));
        if (card != IntPtr.Zero)
        {
            Thread.Sleep(500);
            Say("card shot=" + ShotFile(card, "bubble_card.png"));
        }
    }

    private static void RunPanel()
    {
        int ipid = PolishProcessId();
        Say("InputPolish pid=" + ipid);
        IntPtr before = IntPtr.Zero;
        bool sent = SendIpc("panel");
        Say("ipc panel sent=" + sent);
        IntPtr panel = IntPtr.Zero;
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            List<IntPtr> ws = OwnWindows(ipid);
            for (int i = 0; i < ws.Count; i++)
            {
                RECT r;
                if (!GetWindowRect(ws[i], out r)) continue;
                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w >= 380 && w <= 520 && h >= 180 && h <= 260) { panel = ws[i]; break; }
            }
            if (panel != IntPtr.Zero) break;
            Thread.Sleep(150);
        }
        Say("panel=" + (panel == IntPtr.Zero ? "NOT FOUND" : Desc(panel)));
        if (panel != IntPtr.Zero) { Thread.Sleep(600); Say("panel shot=" + ShotFile(panel, "quickpanel.png")); }
    }

    private static void RunPageBtn()
    {
        int ipid = PolishProcessId();
        Say("InputPolish pid=" + ipid);
        Say("ipc translate-page sent=" + SendIpc("tpage"));
    }

    private static bool SendIpc(string cmd)
    {
        try
        {
            using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", "InputPolish_cmd_7F3A21", PipeDirection.Out))
            {
                pipe.Connect(2500);
                byte[] b = Encoding.UTF8.GetBytes(cmd);
                pipe.Write(b, 0, b.Length);
                pipe.Flush();
                return true;
            }
        }
        catch (Exception ex) { Say("ipc error " + ex.Message); return false; }
    }
}
