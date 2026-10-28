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
using System.Windows.Automation.Text;

internal static class Tabbit
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint GetWindowThreadProcessIdPid(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool f);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint a, uint p, IntPtr v, uint w);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

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

    private const uint LEFTDOWN = 0x0002;
    private const uint LEFTUP = 0x0004;
    private static string _out = @"C:\Users\Administrator\AppData\Local\InputPolishTest\tabbit.txt";
    private static StringBuilder _log = new StringBuilder();

    private static void Say(string s)
    {
        _log.AppendLine(s);
        try { File.WriteAllText(_out, _log.ToString(), Encoding.UTF8); } catch { }
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
        catch (Exception ex) { Say("ipc error: " + ex.Message); return false; }
    }

    private static IntPtr FindTabbit(out string title)
    {
        IntPtr best = IntPtr.Zero;
        int area = 0;
        string t = "";
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            uint pid;
            GetWindowThreadProcessIdPid(h, out pid);
            string pn = "";
            try { pn = Process.GetProcessById((int)pid).ProcessName; } catch { }
            if (pn.IndexOf("Tabbit", StringComparison.OrdinalIgnoreCase) < 0) return true;
            RECT r;
            GetWindowRect(h, out r);
            int a = (r.Right - r.Left) * (r.Bottom - r.Top);
            if (a > area)
            {
                area = a;
                best = h;
                AutomationElement el = AutomationElement.FromHandle(h);
                try { t = el.Current.Name; } catch { }
            }
            return true;
        }, IntPtr.Zero);
        title = t;
        return best;
    }

    private static void UnlockForeground()
    {
        SystemParametersInfo(0x2004, 0, IntPtr.Zero, 0);
    }

    private static bool ForceForeground(IntPtr h)
    {
        UnlockForeground();
        if (IsIconic(h)) ShowWindow(h, 9);
        uint dummy;
        uint fgThread = GetWindowThreadProcessIdPid(GetForegroundWindow(), out dummy);
        uint myThread = GetCurrentThreadId();
        for (int i = 0; i < 12; i++)
        {
            AttachThreadInput(myThread, fgThread, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            AttachThreadInput(myThread, fgThread, false);
            Thread.Sleep(140);
            if (GetForegroundWindow() == h) return true;
            fgThread = GetWindowThreadProcessIdPid(GetForegroundWindow(), out dummy);
        }
        return GetForegroundWindow() == h;
    }

    private static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(140);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(260);
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
        Thread.Sleep(220);
    }

    private static void SendVk(ushort vk, bool up)
    {
        INPUT[] i = new INPUT[1];
        i[0].type = 1;
        i[0].wVk = vk;
        i[0].dwFlags = up ? 2u : 0u;
        SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
    }

    private static void SendCombo(ushort vk1, ushort vk2)
    {
        SendVk(0x11, false); Thread.Sleep(45);
        SendVk(0x12, false); Thread.Sleep(45);
        SendVk(vk2, false); Thread.Sleep(70);
        SendVk(vk2, true); Thread.Sleep(45);
        SendVk(0x12, true); Thread.Sleep(45);
        SendVk(0x11, true); Thread.Sleep(180);
    }

    private static void TypeText(string text)
    {
        for (int n = 0; n < text.Length; n++)
        {
            char ch = text[n];
            INPUT[] i = new INPUT[2];
            i[0].type = 1; i[0].wScan = ch; i[0].dwFlags = 4;
            i[1].type = 1; i[1].wScan = ch; i[1].dwFlags = 4 | 2;
            SendInput(2, i, Marshal.SizeOf(typeof(INPUT)));
            Thread.Sleep(12);
        }
        Thread.Sleep(320);
    }

    private static void Shot(Pen holder, IntPtr hwnd, string name, int pad)
    {
        RECT r;
        GetWindowRect(hwnd, out r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) { Say("shot skipped (bad rect) " + name); return; }
        using (Bitmap bmp = new Bitmap(w + pad * 2, h + pad * 2))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(30, 30, 34));
                IntPtr hdc = g.GetHdc();
                bool ok = PrintWindow(hwnd, hdc, 2);
                g.ReleaseHdc(hdc);
                if (!ok) Say("PrintWindow failed for " + name);
            }
            string path = @"C:\Users\Administrator\AppData\Local\InputPolishTest\" + name;
            bmp.Save(path, ImageFormat.Png);
            Say("shot " + path + " " + bmp.Width + "x" + bmp.Height);
        }
    }

    private struct Pen { }

    private static void ClearValue(AutomationElement el)
    {
        try
        {
            object vpObj;
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out vpObj))
            {
                ValuePattern vp = (ValuePattern)vpObj;
                if (!vp.Current.IsReadOnly) { vp.SetValue(""); return; }
            }
        }
        catch (Exception ex) { Say("clear value failed: " + ex.Message); }
    }

    private static AutomationElement FindComposer(AutomationElement root)
    {
        Condition c = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
        AutomationElementCollection all = root.FindAll(TreeScope.Descendants, c);
        foreach (AutomationElement e in all)
        {
            string cls = "";
            try { cls = e.Current.ClassName; } catch { }
            if (cls == "composer-input") return e;
        }
        return null;
    }

    private static string ReadValue(AutomationElement el)
    {
        try
        {
            object o;
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out o))
                return ((ValuePattern)o).Current.Value;
        }
        catch { }
        try
        {
            object o;
            if (el.TryGetCurrentPattern(TextPattern.Pattern, out o))
                return ((TextPattern)o).DocumentRange.GetText(-1);
        }
        catch { }
        return null;
    }

    private static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "composer";
        try { Body(mode); }
        catch (Exception ex) { Say("EX " + ex); }
        Say("=== done " + mode + " ===");
    }

    private static void Body(string mode)
    {
        string title;
        IntPtr win = FindTabbit(out title);
        Say("tabbit hwnd=" + win.ToInt64() + " title=[" + title + "]");
        if (win == IntPtr.Zero) { Say("tabbit not found"); return; }
        AutomationElement root = AutomationElement.FromHandle(win);

        if (mode == "composer")
        {
            AutomationElement comp = FindComposer(root);
            Say("composer=" + (comp == null ? "null" : "ok"));
            if (comp == null) return;
            System.Windows.Rect r = comp.Current.BoundingRectangle;
            Say("composer rect=" + (int)r.Left + "," + (int)r.Top + " " + (int)r.Width + "x" + (int)r.Height);
            Say("composer before=[" + ReadValue(comp) + "]");

            bool fg = ForceForeground(win);
            Say("foreground=" + fg);
            Thread.Sleep(300);
            // 输入框右下角有 chip 按钮（连接/模型等），点正中容易点到按钮上，
            // 改成从左侧 20px 处开始试几个点，直到焦点落在 composer-input 上。
            AutomationElement focused = null;
            int[] offsets = new int[] { 20, 40, 70, 110 };
            for (int k = 0; k < offsets.Length; k++)
            {
                ClickAt((int)(r.Left + offsets[k]), (int)(r.Top + r.Height / 2));
                Thread.Sleep(400);
                try { focused = AutomationElement.FocusedElement; } catch { focused = null; }
                string cls = "";
                try { if (focused != null) cls = focused.Current.ClassName; } catch { }
                Say("try click x+" + offsets[k] + " -> " + Describe(focused));
                if (cls == "composer-input") break;
            }
            Say("focused after click: " + Describe(focused));

            // 先清空输入框，避免上一轮残留文本被追加（会读成双份草稿）。
            // 注意：Web 内容里的 contenteditable 会拦 Ctrl+A，键盘删除只删一个字符，
            // 所以这里直接用 ValuePattern.SetValue("") 清空（和 InputPolish 写回走同一条通道）。
            ClearValue(comp);
            Thread.Sleep(350);
            Say("composer cleared=[" + ReadValue(comp) + "]");

            string draft = "这个方案有点问题主要是成本太高而且交付周期长";
            TypeText(draft);
            Thread.Sleep(400);
            string typed = ReadValue(comp);
            Say("composer after typing=[" + typed + "]");

            Say("ipc polish sent=" + SendIpc("polish"));
            string final = typed;
            DateTime deadline = DateTime.UtcNow.AddSeconds(70);
            DateTime lastChange = DateTime.UtcNow;
            string prev = typed;
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(200);
                string v = ReadValue(comp);
                if (v != null && v != prev) { prev = v; lastChange = DateTime.UtcNow; }
                if (v != null && v.Trim() != draft.Trim() && (DateTime.UtcNow - lastChange).TotalMilliseconds > 1200 && v.Trim().Length > 0)
                {
                    final = v;
                    if ((DateTime.UtcNow - lastChange).TotalMilliseconds > 1600) break;
                }
            }
            Say("changed=" + (final != null && final.Trim() != typed.Trim()));
            Say("final=[" + final + "]");
            Shot(new Pen(), win, "tabbit_polished.png", 8);

            Say("ipc undo sent=" + SendIpc("undo"));
            Thread.Sleep(2500);
            string back = ReadValue(comp);
            Say("after undo=[" + back + "]");
            Say("undo restored=" + (back != null && back.Trim() == typed.Trim()));
        }
        else if (mode == "page")
        {
            bool fg = ForceForeground(win);
            Say("foreground=" + fg);
            Thread.Sleep(500);
            Say("ipc page sent=" + SendIpc("page"));
            IntPtr pane = IntPtr.Zero;
            DateTime deadline = DateTime.UtcNow.AddSeconds(40);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(200);
                pane = FindOwned("网页翻译");
                if (pane != IntPtr.Zero) break;
            }
            Say("pane=" + pane.ToInt64());
            if (pane == IntPtr.Zero) return;
            DateTime wait = DateTime.UtcNow.AddSeconds(240);
            while (DateTime.UtcNow < wait)
            {
                Thread.Sleep(1000);
                AutomationElement pe = AutomationElement.FromHandle(pane);
                string t = "";
                try { t = pe.Current.Name; } catch { }
                if (t.IndexOf("完成") >= 0) break;
            }
            Thread.Sleep(1200);
            Shot(new Pen(), pane, "tabbit_page.png", 10);
            Say("page done at " + DateTime.Now.ToString("HH:mm:ss"));
        }
        else if (mode == "selection")
        {
            bool fg = ForceForeground(win);
            Say("foreground=" + fg);
            Thread.Sleep(400);
            RECT wr;
            GetWindowRect(win, out wr);
            int cx = (wr.Left + wr.Right) / 2;
            int cy = (wr.Top + wr.Bottom) / 2;
            ClickAt(cx, cy);
            Thread.Sleep(400);
            // 鼠标拖选一段正文（Chromium 页面没有 caret browsing，键盘选不中）
            int dy = wr.Top + 420;
            DragSelect(wr.Left + 260, dy, wr.Left + 1300, dy);
            Thread.Sleep(600);
            Say("ipc translate sent=" + SendIpc("translate"));
            IntPtr popup = IntPtr.Zero;
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(200);
                popup = FindOwned("");
                if (popup != IntPtr.Zero) break;
            }
            Say("popup=" + popup.ToInt64());
            if (popup != IntPtr.Zero) { Thread.Sleep(700); Shot(new Pen(), popup, "tabbit_popup.png", 16); }
        }
        else if (mode == "hotkey-page")
        {
            bool fg = ForceForeground(win);
            Say("foreground=" + fg);
            Thread.Sleep(500);
            SendCombo(0x11, 0x57);
            IntPtr pane = IntPtr.Zero;
            DateTime deadline = DateTime.UtcNow.AddSeconds(40);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(200);
                pane = FindOwned("网页翻译");
                if (pane != IntPtr.Zero) break;
            }
            Say("pane via hotkey=" + pane.ToInt64());
        }
    }

    private static string Describe(AutomationElement e)
    {
        if (e == null) return "(null)";
        try
        {
            string ct = "", cls = "", aid = "";
            System.Windows.Rect r = new System.Windows.Rect(0, 0, 0, 0);
            try { ct = e.Current.ControlType.ProgrammaticName; } catch { }
            try { cls = e.Current.ClassName; } catch { }
            try { aid = e.Current.AutomationId; } catch { }
            try { r = e.Current.BoundingRectangle; } catch { }
            return "ct=" + ct + " cls=" + cls + " aid=" + aid + " rect=" + (int)r.Left + "," + (int)r.Top + " " + (int)r.Width + "x" + (int)r.Height;
        }
        catch (Exception ex) { return "(unreadable " + ex.Message + ")"; }
    }

    private static IntPtr FindOwned(string titlePart)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            uint pid;
            GetWindowThreadProcessIdPid(h, out pid);
            string pn = "";
            try { pn = Process.GetProcessById((int)pid).ProcessName; } catch { }
            if (pn != "InputPolish") return true;
            AutomationElement el;
            try { el = AutomationElement.FromHandle(h); } catch { return true; }
            string t = "";
            try { t = el.Current.Name; } catch { }
            RECT r;
            GetWindowRect(h, out r);
            int w = r.Right - r.Left;
            if (titlePart.Length == 0)
            {
                if (w >= 240 && w <= 1100 && t.Length == 0) { found = h; return false; }
            }
            else if (t.IndexOf(titlePart) >= 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
