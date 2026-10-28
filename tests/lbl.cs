using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Automation;

internal static class Lbl
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    private static string _out = "";
    private static void W(string s) { _out += s + "\r\n"; }

    private static void Main()
    {
        try { Body(); } catch (Exception ex) { W("EX " + ex.Message); }
        try { File.WriteAllText(@"C:\Users\Administrator\AppData\Local\InputPolishTest\lbl.txt", _out, Encoding.UTF8); } catch { }
    }

    private static void Body()
    {
        IntPtr target = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder sb = new StringBuilder(256);
            GetWindowTextW(h, sb, 256);
            if (sb.ToString().IndexOf("InputPolish 设置") >= 0) { target = h; return false; }
            return true;
        }, IntPtr.Zero);
        W("settings hwnd=" + target.ToInt64());
        if (target == IntPtr.Zero) return;
        AutomationElement root = AutomationElement.FromHandle(target);
        AutomationElementCollection all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        W("nodes=" + all.Count);
        foreach (AutomationElement e in all)
        {
            string ct = "", nm = "";
            try { ct = e.Current.ControlType.ProgrammaticName; } catch { }
            try { nm = e.Current.Name; } catch { }
            if (ct.IndexOf("Text") < 0 && ct.IndexOf("Edit") < 0 && ct.IndexOf("Combo") < 0) continue;
            System.Windows.Rect r = new System.Windows.Rect(0,0,0,0);
            try { r = e.Current.BoundingRectangle; } catch { }
            W(ct + " rect=" + (int)r.Left + "," + (int)r.Top + " " + (int)r.Width + "x" + (int)r.Height + " name=[" + nm + "]");
        }
    }
}
