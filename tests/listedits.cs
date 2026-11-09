using System;
using System.Diagnostics;
using System.Text;
using System.Windows.Automation;

internal static class ListEdits
{
    private static string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 50 ? s.Substring(0, 50) + "..." : s;
    }

    private static int Main(string[] args)
    {
        string procName = args.Length > 0 ? args[0] : "DeepSeek";
        AutomationElementCollection wins = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                new PropertyCondition(AutomationElement.IsEnabledProperty, true)));
        foreach (AutomationElement w in wins)
        {
            string pn = "";
            int pid = 0;
            try { pid = w.Current.ProcessId; pn = Process.GetProcessById(pid).ProcessName; } catch { }
            if (pn.IndexOf(procName, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine("window pid=" + pid + " title=[" + Short(w.Current.Name) + "]");
            AutomationElementCollection edits = w.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            int i = 0;
            foreach (AutomationElement e in edits)
            {
                object vp;
                bool hasVp = e.TryGetCurrentPattern(ValuePattern.Pattern, out vp);
                string val = "";
                bool ro = false;
                if (hasVp)
                {
                    ValuePattern v = (ValuePattern)vp;
                    try { val = Short(v.Current.Value); } catch { }
                    try { ro = v.Current.IsReadOnly; } catch { }
                }
                object tp;
                bool hasTp = e.TryGetCurrentPattern(TextPattern.Pattern, out tp);
                System.Windows.Rect r = new System.Windows.Rect(0, 0, 0, 0);
                try { System.Windows.Rect rr = e.Current.BoundingRectangle; r = rr; } catch { }
                Console.WriteLine("  [" + i + "] cls=" + e.Current.ClassName + " name=[" + Short(e.Current.Name) + "]" +
                                  " vp=" + hasVp + (hasVp ? (" ro=" + ro + " val=[" + val + "]") : "") +
                                  " tp=" + hasTp +
                                  " rect=" + (int)r.X + "," + (int)r.Y + "," + (int)r.Width + "x" + (int)r.Height);
                i++;
            }
        }
        return 0;
    }
}
