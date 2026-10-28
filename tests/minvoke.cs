using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Automation;

internal static class MInvoke
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
        string want = args.Length > 0 ? args[0] : "更正式";
        try { Body(want); }
        catch (Exception ex) { W("EX " + ex); }
        try { File.WriteAllText(@"C:\Users\Administrator\AppData\Local\InputPolishTest\minvoke.txt", _out, Encoding.UTF8); } catch { }
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

    private static void Body(string want)
    {
        W("ipc menu sent=" + SendIpc("menu"));
        Thread.Sleep(1100);
        List<IntPtr> wins = FindMenus();
        W("windows=" + wins.Count);
        for (int i = 0; i < wins.Count; i++)
        {
            AutomationElement el;
            try { el = AutomationElement.FromHandle(wins[i]); }
            catch (Exception ex) { W("hwnd " + wins[i].ToInt64() + " skip: " + ex.Message); continue; }

            AutomationElement item = null;
            try { item = el.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, want)); }
            catch (Exception ex) { W("hwnd " + wins[i].ToInt64() + " find failed: " + ex.Message); continue; }
            if (item == null) { W("hwnd " + wins[i].ToInt64() + " no [" + want + "]"); continue; }

            W("found [" + want + "] in hwnd " + wins[i].ToInt64());
            object ip;
            try
            {
                if (item.TryGetCurrentPattern(InvokePattern.Pattern, out ip))
                {
                    ((InvokePattern)ip).Invoke();
                    W("invoked ok");
                    Thread.Sleep(1400);
                    return;
                }
                W("no InvokePattern");
            }
            catch (Exception ex) { W("invoke failed: " + ex.Message); }
        }
    }
}
