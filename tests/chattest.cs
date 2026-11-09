using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

// 定向验证：DSH 聊天输入框（ProseMirror contenteditable）
// 这个控件的 ValuePattern.SetValue 是「空操作」，正是用户看到的「按三下空格没反应」的原因。
internal static class ChatTest
{
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

    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);

    private static void SendCtrl(int vk)
    {
        INPUT[] a = new INPUT[4];
        a[0].type = 1; a[0].wVk = 0x11;
        a[1].type = 1; a[1].wVk = (ushort)vk;
        a[2].type = 1; a[2].wVk = (ushort)vk; a[2].dwFlags = 2;
        a[3].type = 1; a[3].wVk = 0x11; a[3].dwFlags = 2;
        SendInput(4, a, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(120);
    }

    private static void SendKey(int vk)
    {
        INPUT[] a = new INPUT[2];
        a[0].type = 1; a[0].wVk = (ushort)vk;
        a[1].type = 1; a[1].wVk = (ushort)vk; a[1].dwFlags = 2;
        SendInput(2, a, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(60);
    }

    private static void Typec(char ch)
    {
        INPUT[] a = new INPUT[2];
        a[0].type = 1; a[0].wScan = ch; a[0].dwFlags = 4;
        a[1].type = 1; a[1].wScan = ch; a[1].dwFlags = 6;
        SendInput(2, a, Marshal.SizeOf(typeof(INPUT)));
    }

    private static void TypeText(string s)
    {
        foreach (char c in s) Typec(c);
    }

    private static string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 90 ? s.Substring(0, 90) + "..." : s;
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

    private static int Main(string[] args)
    {
        string cls = args.Length > 0 ? args[0] : "GRgSLq_input";
        string draft = args.Length > 1 ? args[1] : "这个方案有点问题主要是成本太高而且交付周期长";
        bool doUndo = args.Length > 2 && args[2] == "undo";

        AutomationElement target = null;
        AutomationElementCollection wins = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
        foreach (AutomationElement w in wins)
        {
            string pn = "";
            try { pn = Process.GetProcessById(w.Current.ProcessId).ProcessName; } catch { }
            if (pn.IndexOf("DeepSeek", StringComparison.OrdinalIgnoreCase) < 0) continue;
            foreach (AutomationElement e in w.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)))
            {
                if (e.Current.ClassName == cls) { target = e; break; }
            }
            if (target != null) break;
        }
        if (target == null) { Console.WriteLine("FAIL: 没找到 " + cls); return 2; }

        ValuePattern vp = (ValuePattern)target.GetCurrentPattern(ValuePattern.Pattern);
        try { target.SetFocus(); } catch (Exception ex) { Console.WriteLine("SetFocus: " + ex.Message); }
        Thread.Sleep(500);

        // 全选 + 删除清空，再用键盘键入草稿（value pattern 对这个控件是空操作）
        SendCtrl(0x41);
        SendKey(0x2E);
        Thread.Sleep(250);
        TypeText(draft);
        Thread.Sleep(700);
        Console.WriteLine("draft in box = [" + Short(vp.Current.Value) + "]");
        Console.WriteLine("focused = [" + Short(AutomationElement.FocusedElement.Current.Name) + "] cls=" +
                          AutomationElement.FocusedElement.Current.ClassName);

        Console.WriteLine("--- ipc polish ---");
        SendIpc("polish");

        string result = "";
        DateTime end = DateTime.UtcNow.AddSeconds(95);
        string last = null;
        while (DateTime.UtcNow < end)
        {
            Thread.Sleep(500);
            string cur = null;
            try { cur = vp.Current.Value; } catch { }
            if (cur != null && cur != last)
            {
                last = cur;
                Console.WriteLine("value=[" + Short(cur) + "]");
            }
            if (cur != null && cur.Length > 0 && cur.IndexOf(draft, StringComparison.Ordinal) < 0 &&
                (cur.IndexOf("，") >= 0 || cur.IndexOf("。") >= 0))
            {
                result = cur;
                break;
            }
        }
        Console.WriteLine(result.Length > 0 ? "OK: 聊天框文本已被改写" : "TIMEOUT: 没等到改写");

        if (doUndo && result.Length > 0)
        {
            Console.WriteLine("--- ipc undo ---");
            SendIpc("undo");
            DateTime e2 = DateTime.UtcNow.AddSeconds(40);
            bool restored = false;
            while (DateTime.UtcNow < e2)
            {
                Thread.Sleep(400);
                string cur = null;
                try { cur = vp.Current.Value; } catch { }
                if (cur != null && cur.Trim() == draft.Trim()) { restored = true; break; }
            }
            Console.WriteLine(restored ? "OK: 撤销已还原原文" : "FAIL: 撤销没有还原");
        }

        // 收尾：清空输入框，不给用户留草稿
        try
        {
            target.SetFocus();
            Thread.Sleep(200);
            SendCtrl(0x41);
            SendKey(0x2E);
        }
        catch { }
        return result.Length > 0 ? 0 : 1;
    }
}
