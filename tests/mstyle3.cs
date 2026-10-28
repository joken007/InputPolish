using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Automation;

internal static class MStyle3
{
    private static string _out = "";
    private static void W(string s) { _out += s + "\r\n"; }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    private static void Main(string[] args)
    {
        try { Body(args); }
        catch (Exception ex) { W("EX " + ex); }
        try { File.WriteAllText(@"C:\Users\Administrator\AppData\Local\InputPolishTest\mstyle3.txt", _out, Encoding.UTF8); } catch { }
    }

    private static bool SendIpc(string command)
    {
        try
        {
            using (System.IO.Pipes.NamedPipeClientStream pipe =
                new System.IO.Pipes.NamedPipeClientStream(".", "InputPolish_cmd_7F3A21", System.IO.Pipes.PipeDirection.Out))
            {
                pipe.Connect(2500);
                byte[] b = Encoding.UTF8.GetBytes(command);
                pipe.Write(b, 0, b.Length);
                pipe.Flush();
                return true;
            }
        }
        catch (Exception ex) { W("ipc failed: " + ex.Message); return false; }
    }

    private static List<IntPtr> FindMenus()
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder c = new StringBuilder(128);
            GetClassNameW(h, c, 128);
            if (c.ToString().IndexOf("WindowsForms10.Window", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                string pn = "";
                try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
                if (string.Equals(pn, "InputPolish", StringComparison.OrdinalIgnoreCase)) list.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static void Body(string[] args)
    {
        W("ipc menu sent=" + SendIpc("menu"));
        Thread.Sleep(1000);
        List<IntPtr> wins = FindMenus();
        W("windows=" + wins.Count);
        AutomationElement parent = null;
        for (int i = 0; i < wins.Count; i++)
        {
            AutomationElement el = AutomationElement.FromHandle(wins[i]);
            AutomationElementCollection all = el.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            W("--- hwnd " + wins[i].ToInt64() + " nodes=" + all.Count);
            foreach (AutomationElement e in all)
            {
                string nm = "", ct = "", pats = "";
                try { nm = e.Current.Name; } catch { }
                try { ct = e.Current.ControlType.ProgrammaticName; } catch { }
                try
                {
                    AutomationPattern[] ps = e.GetSupportedPatterns();
                    for (int k = 0; k < ps.Length; k++) pats += ps[k].ProgrammaticName.Replace("PatternIdentifiers.Pattern", "") + ",";
                }
                catch { }
                W("  ct=" + ct + " name=[" + nm + "] pats=" + pats);
                if (nm == "润色风格" && parent == null) parent = e;
            }
        }
        if (parent == null) { W("parent not found"); return; }

        object ec;
        if (parent.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out ec))
        {
            try { ((ExpandCollapsePattern)ec).Expand(); W("expand ok"); }
            catch (Exception ex) { W("expand failed: " + ex.Message); }
        }
        else W("no ExpandCollapsePattern");
        Thread.Sleep(1200);

        if (args.Length > 1)
        {
            string want = args[1];
            for (int i = 0; i < wins.Count; i++)
            {
                AutomationElement el = AutomationElement.FromHandle(wins[i]);
                AutomationElement item = el.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, want));
                if (item == null) continue;
                W("child found [" + want + "] in hwnd " + wins[i].ToInt64());
                object ip;
                if (item.TryGetCurrentPattern(InvokePattern.Pattern, out ip))
                {
                    try { ((InvokePattern)ip).Invoke(); W("invoked"); }
                    catch (Exception ex) { W("invoke failed: " + ex.Message); }
                }
                else if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out ip))
                {
                    try { ((SelectionItemPattern)ip).Select(); W("selected"); }
                    catch (Exception ex) { W("select failed: " + ex.Message); }
                }
                else W("no Invoke/SelectionItem pattern on child");
                break;
            }
        }
        Thread.Sleep(1200);
    }
}
