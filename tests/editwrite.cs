using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Automation;

// 绔埌绔獙璇侊細鎶婅崏绋垮啓杩涖€孌eepSeek Harness銆嶇獥鍙ｇ殑杈撳叆妗?-> 瑙﹀彂 IPC polish ->
// 绛夋鼎鑹茬粨鏋滃啓鍥?-> 鍥炶鍐呭锛岀‘璁ゅ啓鍥炲眰鏄惁鐪熺殑鐢熸晥銆?internal static class EditWrite
{
    private static string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 80 ? s.Substring(0, 80) + "..." : s;
    }

    private static void SendIpc(string cmd)
    {
        using (NamedPipeClientStream c = new NamedPipeClientStream(".", "InputPolish_cmd_7F3A21", PipeDirection.Out))
        {
            c.Connect(3000);
            byte[] b = Encoding.UTF8.GetBytes(cmd);
            c.Write(b, 0, b.Length);
            c.Flush();
        }
    }

    private static AutomationElement FindEdit(string procName)
    {
        Condition procCond = new PropertyCondition(AutomationElement.ProcessIdProperty, 0);
        AutomationElement root = AutomationElement.RootElement;
        Condition winCond = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
            new PropertyCondition(AutomationElement.IsEnabledProperty, true));
        AutomationElementCollection wins = root.FindAll(TreeScope.Children, winCond);
        foreach (AutomationElement w in wins)
        {
            string pn = "";
            int pid = 0;
            try { pid = w.Current.ProcessId; pn = Process.GetProcessById(pid).ProcessName; }
            catch { }
            if (pn.IndexOf(procName, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Condition editCond = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.IsEnabledProperty, true));
            AutomationElementCollection edits = w.FindAll(TreeScope.Descendants, editCond);
            foreach (AutomationElement e in edits)
            {
                object vp;
                if (!e.TryGetCurrentPattern(ValuePattern.Pattern, out vp)) continue;
                ValuePattern v = (ValuePattern)vp;
                if (v.Current.IsReadOnly) continue;
                Console.WriteLine("  found edit pid=" + pid + " name=[" + Short(e.Current.Name) + "] cls=" + e.Current.ClassName +
                                  " value=[" + Short(v.Current.Value) + "]");
                return e;
            }
        }
        return null;
    }

    private static int Main(string[] args)
    {
        string draft = args.Length > 0 ? args[0] : "杩欎釜鏂规鏈夌偣闂涓昏鏄垚鏈お楂樿€屼笖浜や粯鍛ㄦ湡闀?;
        string procName = args.Length > 1 ? args[1] : "DeepSeek";

        AutomationElement el = FindEdit(procName);
        if (el == null) { Console.WriteLine("FAIL: 娌℃壘鍒板彲鍐欒緭鍏ユ锛堣繘绋?" + procName + "锛?); return 2; }

        ValuePattern vp = (ValuePattern)el.GetCurrentPattern(ValuePattern.Pattern);
        try { el.SetFocus(); } catch (Exception ex) { Console.WriteLine("  SetFocus: " + ex.Message); }
        Thread.Sleep(400);
        vp.SetValue(draft);
        Thread.Sleep(600);
        Console.WriteLine("after set draft = [" + Short(vp.Current.Value) + "]");
        Console.WriteLine("focused now = [" + Short(AutomationElement.FocusedElement.Current.Name) + "] type=" +
                          AutomationElement.FocusedElement.Current.ControlType.ProgrammaticName);

        Console.WriteLine("--- ipc polish ---");
        SendIpc("polish");

        string expect = "";
        DateTime end = DateTime.UtcNow.AddSeconds(70);
        string last = null;
        int changes = 0;
        while (DateTime.UtcNow < end)
        {
            Thread.Sleep(500);
            string cur = null;
            try { cur = vp.Current.Value; } catch { }
            if (cur != null && cur != last)
            {
                last = cur;
                changes++;
                Console.WriteLine("+" + (int)(70 - (end - DateTime.UtcNow).TotalSeconds) + "s value=[" + Short(cur) + "]");
            }
            if (cur != null && cur.Length > 0 && cur.IndexOf(draft, StringComparison.Ordinal) < 0 && cur.Length > 5 &&
                (cur.IndexOf("锛?) >= 0 || cur.IndexOf(",") >= 0 || cur.IndexOf("銆?) >= 0))
            {
                Console.WriteLine("OK: 鏂囨湰宸茶鏀瑰啓");
                expect = cur;
                break;
            }
        }
        if (expect.Length == 0) Console.WriteLine("TIMEOUT: 娌℃湁绛夊洖璇诲埌鏀瑰啓缁撴灉");
        return expect.Length > 0 ? 0 : 1;
    }
}

