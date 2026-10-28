using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;

internal static class Tprobe
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static string _out = "";
    private static void W(string s) { _out += s + "\r\n"; }

    private static void Main()
    {
        try { Body(); } catch (Exception ex) { W("EX " + ex); }
        try { File.WriteAllText(@"C:\Users\Administrator\AppData\Local\InputPolishTest\tprobe.txt", _out, Encoding.UTF8); } catch { }
    }

    private static void Body()
    {
        List<IntPtr> wins = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            string pn = "";
            try { pn = Process.GetProcessById((int)pid).ProcessName; } catch { }
            if (pn.IndexOf("Tabbit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                StringBuilder t = new StringBuilder(512);
                GetWindowTextW(h, t, 512);
                StringBuilder c = new StringBuilder(256);
                GetClassNameW(h, c, 256);
                RECT r;
                GetWindowRect(h, out r);
                W("hwnd=" + h.ToInt64() + " proc=" + pn + " cls=" + c + " rect=" + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom + " title=[" + t + "]");
                wins.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        W("tabbit windows=" + wins.Count);
        if (wins.Count == 0) return;

        IntPtr best = wins[0];
        int bestArea = 0;
        for (int i = 0; i < wins.Count; i++)
        {
            RECT r;
            GetWindowRect(wins[i], out r);
            int a = (r.Right - r.Left) * (r.Bottom - r.Top);
            if (a > bestArea) { bestArea = a; best = wins[i]; }
        }
        W("choose hwnd=" + best.ToInt64());
        AutomationElement root = AutomationElement.FromHandle(best);

        AutomationElement focused = AutomationElement.FocusedElement;
        W("focused=" + Describe(focused));

        Condition cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
        AutomationElementCollection edits = root.FindAll(TreeScope.Descendants, cond);
        W("edit controls=" + edits.Count);
        int n = 0;
        foreach (AutomationElement e in edits)
        {
            W("  " + Describe(e));
            if (++n >= 25) break;
        }

        Condition cdoc = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
        AutomationElementCollection docs = root.FindAll(TreeScope.Descendants, cdoc);
        W("documents=" + docs.Count);
        foreach (AutomationElement e in docs)
        {
            object tp;
            bool hasText = e.TryGetCurrentPattern(TextPattern.Pattern, out tp);
            string txt = "";
            if (hasText)
            {
                try
                {
                    TextPattern p = (TextPattern)tp;
                    TextPatternRange r = p.DocumentRange;
                    txt = r.GetText(400);
                }
                catch (Exception ex) { txt = "(read failed " + ex.Message + ")"; }
            }
            W("  doc " + Describe(e) + " textPattern=" + hasText + " text=[" + Short(txt) + "]");
        }
    }

    private static string Short(string s)
    {
        if (s == null) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 160 ? s.Substring(0, 160) + "…|" + s.Length : s;
    }

    private static string Describe(AutomationElement e)
    {
        if (e == null) return "(null)";
        try
        {
            string name = "", cls = "", ct = "", aid = "";
            int pid = 0;
            System.Windows.Rect r = new System.Windows.Rect(0, 0, 0, 0);
            try { name = e.Current.Name; } catch { }
            try { cls = e.Current.ClassName; } catch { }
            try { ct = e.Current.ControlType.ProgrammaticName; } catch { }
            try { aid = e.Current.AutomationId; } catch { }
            try { pid = e.Current.ProcessId; } catch { }
            try { r = e.Current.BoundingRectangle; } catch { }
            bool val = false, txt = false;
            object o;
            try { val = e.TryGetCurrentPattern(ValuePattern.Pattern, out o); } catch { }
            try { txt = e.TryGetCurrentPattern(TextPattern.Pattern, out o); } catch { }
            string pn = "";
            try { pn = Process.GetProcessById(pid).ProcessName; } catch { }
            return "ct=" + ct + " proc=" + pn + " cls=" + cls + " aid=" + aid + " val=" + val + " text=" + txt +
                " rect=" + (int)r.Left + "," + (int)r.Top + " " + (int)r.Width + "x" + (int)r.Height + " name=[" + Short(name) + "]";
        }
        catch (Exception ex) { return "(unreadable " + ex.Message + ")"; }
    }
}
