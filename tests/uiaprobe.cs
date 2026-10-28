using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

internal static class UiaProbe
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int c);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    private static readonly StringBuilder _o = new StringBuilder();
    private static void Say(string s) { _o.AppendLine(s); }

    private static IntPtr FindChrome()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder sb = new StringBuilder(512);
            GetWindowTextW(h, sb, sb.Capacity);
            if (sb.ToString().IndexOf("InputPolish Page Test", StringComparison.OrdinalIgnoreCase) < 0) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static string Name(AutomationElement e)
    {
        try { string n = e.Current.Name; if (n.Length > 40) n = n.Substring(0, 40) + "..."; return n; }
        catch { return "?"; }
    }

    private static void Main()
    {
        try
        {
            IntPtr h = FindChrome();
            Say("chrome hwnd=" + h.ToInt64());
            if (h == IntPtr.Zero) { Flush(); return; }
            AutomationElement root = AutomationElement.FromHandle(h);
            Say("root type=" + root.Current.ControlType.ProgrammaticName + " name=" + Name(root));

            Condition docCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
            AutomationElementCollection docs = root.FindAll(TreeScope.Descendants, docCond);
            Say("docs(ControlType.Document)=" + docs.Count);
            for (int i = 0; i < docs.Count; i++)
            {
                object dummy;
                bool tp = docs[i].TryGetCurrentPattern(TextPattern.Pattern, out dummy);
                Say("  doc[" + i + "] name=" + Name(docs[i]) + " textPattern=" + tp);
            }

            Condition tpCond = new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true);
            AutomationElementCollection tps = root.FindAll(TreeScope.Descendants, tpCond);
            Say("textPatternAvailable elements=" + tps.Count);

            AutomationElementCollection all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            Say("all descendants=" + all.Count);
            int shown = 0;
            for (int i = 0; i < all.Count && shown < 80; i++)
            {
                string ct = "";
                try { ct = all[i].Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); } catch { ct = "?"; }
                if (ct == "Pane" || ct == "Group" || ct == "Custom") continue;
                Say("  [" + i + "] " + ct + " name=" + Name(all[i]));
                shown++;
            }
        }
        catch (Exception ex) { Say("EX: " + ex); }
        Flush();
    }

    private static void Flush()
    {
        try { System.IO.File.WriteAllText(@"C:\Users\Administrator\AppData\Local\InputPolishTest\uiaprobe.txt", _o.ToString(), Encoding.UTF8); } catch { }
    }
}
