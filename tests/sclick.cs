using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

internal static class SClick
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);

    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    private static string _out = "";
    private static void W(string s) { _out += s + "\r\n"; }

    private static IntPtr FindWindow(string procName, string titlePart)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            string name = "";
            try { name = Process.GetProcessById((int)pid).ProcessName; } catch { return true; }
            if (!string.Equals(name, procName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!IsWindowVisible(h)) return true;
            StringBuilder t = new StringBuilder(256);
            GetWindowTextW(h, t, 256);
            if (t.ToString().IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static void ForceForeground(IntPtr h)
    {
        for (int i = 0; i < 12; i++)
        {
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint myThread = GetCurrentThreadId();
            bool attached = AttachThreadInput(myThread, fgThread, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (attached) AttachThreadInput(myThread, fgThread, false);
            Thread.Sleep(80);
            if (GetForegroundWindow() == h) return;
        }
    }

    private static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(80);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private static void DumpTexts(IntPtr hwnd)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(hwnd);
            Walk(root, 0);
        }
        catch (Exception ex)
        {
            W("uia dump failed: " + ex.Message);
        }
    }

    private static void Walk(AutomationElement el, int depth)
    {
        if (depth > 6) return;
        AutomationElementCollection kids = null;
        try { kids = el.FindAll(TreeScope.Children, Condition.TrueCondition); }
        catch { return; }
        foreach (AutomationElement k in kids)
        {
            string type = "";
            string name = "";
            try { type = k.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); } catch { }
            try { name = k.Current.Name; } catch { }
            if (name != null && name.Length > 0 && (type == "Text" || type == "Button" || type == "Edit"))
                W("  " + type + " [" + name + "]");
            Walk(k, depth + 1);
        }
    }

    private static void Main(string[] args)
    {
        string dir = @"C:\Users\Administrator\AppData\Local\InputPolishTest";
        string logPath = Path.Combine(dir, "sclick.txt");
        try
        {
            Body(args);
        }
        catch (Exception ex)
        {
            W("EX " + ex.ToString());
        }
        try { File.WriteAllText(logPath, _out, Encoding.UTF8); } catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    private delegate bool EnumChildProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, string l);
    private const uint WM_SETTEXT = 0x000C;
    private const uint WM_GETTEXT = 0x000D;
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    private const uint WM_COMMAND = 0x0111;

    private static List<IntPtr> FindDialogs(string procName)
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            string name = "";
            try { name = Process.GetProcessById((int)pid).ProcessName; } catch { return true; }
            if (!string.Equals(name, procName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!IsWindowVisible(h)) return true;
            StringBuilder c = new StringBuilder(128);
            GetClassNameW(h, c, 128);
            if (c.ToString() == "#32770") list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static void Body(string[] args)
    {
        if (args.Length > 1 && args[0] == "ask2")
        {
            // 全程不走 UIA：WM_SETTEXT 填问题，再按布局常量点击「问」按钮。
            IntPtr ph = IntPtr.Zero;
            for (int tries = 0; tries < 12 && ph == IntPtr.Zero; tries++)
            {
                IntPtr found = IntPtr.Zero;
                EnumWindows(delegate(IntPtr hh, IntPtr pp)
                {
                    if (!IsWindowVisible(hh)) return true;
                    StringBuilder tt = new StringBuilder(256);
                    GetWindowTextW(hh, tt, 256);
                    if (tt.ToString().IndexOf("快捷面板") >= 0) { found = hh; return false; }
                    return true;
                }, IntPtr.Zero);
                ph = found;
                if (ph == IntPtr.Zero) Thread.Sleep(400);
            }
            W("window(ask2)=" + ph.ToInt64());
            if (ph == IntPtr.Zero) return;
            ForceForeground(ph);
            Thread.Sleep(300);

            List<IntPtr> edits = new List<IntPtr>();
            EnumChildWindows(ph, delegate(IntPtr c, IntPtr p2)
            {
                StringBuilder cc = new StringBuilder(160);
                GetClassNameW(c, cc, 160);
                if (cc.ToString().IndexOf("EDIT", StringComparison.OrdinalIgnoreCase) >= 0) edits.Add(c);
                return true;
            }, IntPtr.Zero);
            W("child edits=" + edits.Count);
            if (edits.Count == 0) { W("no edit control"); return; }
            // 面板里有两个 EDIT：「问这一页」（单行，高 26）和自定义提示词（多行，高 62）。
            // 取高度最小的那个，别靠创建顺序。
            IntPtr askEdit = edits[0];
            int best = int.MaxValue;
            foreach (IntPtr e in edits)
            {
                RECT er;
                GetClientRect(e, out er);
                int hgt = er.Bottom - er.Top;
                W("edit " + e.ToInt64() + " h=" + hgt);
                if (hgt < best) { best = hgt; askEdit = e; }
            }
            SendMessageW(askEdit, WM_SETTEXT, IntPtr.Zero, args[1]);
            W("wm_settext sent to " + askEdit.ToInt64() + " h=" + best);

            RECT cr2;
            GetClientRect(ph, out cr2);
            POINT o = new POINT();
            ClientToScreen(ph, ref o);
            int bx = o.X + (cr2.Right - 102) + 37;
            int by = o.Y + 244 + 13;
            Click(bx, by);
            W("clicked ask2 at " + bx + "," + by);
            Thread.Sleep(1500);
            return;
        }

        if (args.Length > 0 && args[0] == "btns")
        {
            string t2 = args.Length > 1 ? args[1] : "快捷面板";
            IntPtr bh = FindWindow("InputPolish", t2);
            W("window(btns)=" + bh.ToInt64());
            if (bh == IntPtr.Zero) return;
            AutomationElement broot = AutomationElement.FromHandle(bh);
            AutomationElementCollection bs = broot.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement b in bs)
            {
                System.Windows.Rect br = b.Current.BoundingRectangle;
                W("btn [" + b.Current.Name + "] aid=" + b.Current.AutomationId + " rect=" + (int)br.Left + "," + (int)br.Top + " " + (int)br.Width + "x" + (int)br.Height);
            }
            return;
        }

        if (args.Length > 1 && args[0] == "ask")
        {
            IntPtr ah = FindWindow("InputPolish", "快捷面板");
            W("window(ask)=" + ah.ToInt64());
            if (ah == IntPtr.Zero) return;
            ForceForeground(ah);
            SetEditText(ah, 0, args[1]);
            Thread.Sleep(900);
            AutomationElement aroot = AutomationElement.FromHandle(ah);
            AutomationElement ab = null;
            AutomationElementCollection abs = aroot.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement cand in abs)
            {
                string nm = cand.Current.Name;
                if (nm == "问") { ab = cand; break; }
            }
            if (ab == null) { W("ask button not found among " + abs.Count + " buttons"); return; }
            System.Windows.Rect ar = ab.Current.BoundingRectangle;
            int ax = (int)(ar.Left + ar.Width / 2);
            int ay = (int)(ar.Top + ar.Height / 2);
            Click(ax, ay);
            W("clicked ask at " + ax + "," + ay);
            Thread.Sleep(1500);
            return;
        }

        if (args.Length > 3 && args[0] == "text")
        {
            IntPtr th = FindWindow("InputPolish", args[3]);
            W("window(text)=" + th.ToInt64());
            if (th == IntPtr.Zero) return;
            SetEditText(th, int.Parse(args[1]), args[2]);
            return;
        }

        if (args.Length > 0 && args[0] == "ok")
        {
            List<IntPtr> dlgs = FindDialogs("InputPolish");
            W("dialogs=" + dlgs.Count);
            foreach (IntPtr d in dlgs)
            {
                StringBuilder t = new StringBuilder(256);
                GetWindowTextW(d, t, 256);
                W("dialog hwnd=" + d.ToInt64() + " title=[" + t + "] -> IDOK");
                PostMessageW(d, WM_COMMAND, new IntPtr(1), IntPtr.Zero);
            }
            Thread.Sleep(600);
            return;
        }

        string btn = null;
        bool invokeMode = false;
        string title = "设置";
        if (args.Length > 2) title = args[2];
        if (args.Length > 0 && args[0] == "invoke")
        {
            btn = args[1];
            invokeMode = true;
        }
        else if (args.Length > 0 && args[0] == "btn")
        {
            btn = args[1];
        }
        IntPtr h = FindWindow("InputPolish", title);
        W("window=" + h.ToInt64());
        if (h == IntPtr.Zero) return;

        RECT cr;
        GetClientRect(h, out cr);
        POINT p = new POINT();
        p.X = 0; p.Y = 0;
        ClientToScreen(h, ref p);
        W("client origin=" + p.X + "," + p.Y + " size=" + cr.Right + "x" + cr.Bottom);

        ForceForeground(h);
        W("foreground is target=" + (GetForegroundWindow() == h));

        if (btn != null)
        {
            try
            {
                AutomationElement root = AutomationElement.FromHandle(h);
                Condition cond = new PropertyCondition(AutomationElement.NameProperty, btn);
                AutomationElement b = root.FindFirst(TreeScope.Descendants, cond);
                if (b == null)
                {
                    W("button not found: " + btn);
                }
                else
                {
                    if (invokeMode)
                    {
                        object ip;
                        if (b.TryGetCurrentPattern(InvokePattern.Pattern, out ip))
                        {
                            ((InvokePattern)ip).Invoke();
                            W("invoked button [" + btn + "]");
                            Thread.Sleep(1500);
                            DumpTexts(h);
                            return;
                        }
                        W("no invoke pattern on [" + btn + "]");
                        return;
                    }
                    System.Windows.Rect r = b.Current.BoundingRectangle;
                    int tx = (int)(r.Left + r.Width / 2);
                    int ty = (int)(r.Top + r.Height / 2);
                    Click(tx, ty);
                    Thread.Sleep(250);
                    ForceForeground(h);
                    Click(tx, ty);
                    W("clicked button [" + btn + "] at " + tx + "," + ty + " rect=" + r.Left + "," + r.Top + "," + r.Width + "x" + r.Height);
                }
            }
            catch (Exception ex)
            {
                W("button click failed: " + ex.Message);
            }
            Thread.Sleep(1200);
            DumpTexts(h);
            return;
        }

        if (args.Length >= 2)
        {
            int cx = int.Parse(args[0]);
            int cy = int.Parse(args[1]);
            Click(p.X + cx, p.Y + cy);
            W("clicked client " + cx + "," + cy);
            Thread.Sleep(1200);
        }

        DumpTexts(h);
    }

    /// <summary>把窗口里第 idx 个 Edit 控件的文本设为 value（ValuePattern，跨进程可用）。</summary>
    private static void SetEditText(IntPtr h, int idx, string value)
    {
        AutomationElement root = AutomationElement.FromHandle(h);
        Condition cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
        AutomationElementCollection all = root.FindAll(TreeScope.Descendants, cond);
        W("edits=" + all.Count);
        if (idx < 0 || idx >= all.Count) { W("edit index out of range"); return; }
        AutomationElement e = all[idx];
        object vp;
        if (!e.TryGetCurrentPattern(ValuePattern.Pattern, out vp)) { W("no ValuePattern on edit " + idx); return; }
        ((ValuePattern)vp).SetValue(value);
        W("set edit[" + idx + "] = [" + value + "] readback=[" + ((ValuePattern)vp).Current.Value + "]");
    }
}
