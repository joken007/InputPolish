using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

internal static class MClick
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    private const uint LEFTDOWN = 0x0002;
    private const uint LEFTUP = 0x0004;

    private static string _out = "";
    private static void W(string s) { _out += s + "\r\n"; }

    private static void Main(string[] args)
    {
        try { Body(args); }
        catch (Exception ex) { W("EX " + ex.Message); }
        try { File.WriteAllText(@"C:\Users\Administrator\AppData\Local\InputPolishTest\mclick.txt", _out, Encoding.UTF8); } catch { }
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
        catch (Exception ex)
        {
            W("ipc failed: " + ex.Message);
            return false;
        }
    }

    private static void Body(string[] args)
    {
        string want = args.Length > 0 ? args[0] : "打开配置目录";
        W("ipc menu sent=" + SendIpc("menu"));
        Thread.Sleep(900);

        // find the DropDown menu window
        IntPtr menu = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder c = new StringBuilder(128);
            GetClassNameW(h, c, 128);
            string cls = c.ToString();
            if (cls == "#32768" || cls.IndexOf("WindowsForms10.Window", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                string pn = "";
                try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
                W("candidate hwnd=" + h.ToInt64() + " class=" + cls + " proc=" + pn);
                if (string.Equals(pn, "InputPolish", StringComparison.OrdinalIgnoreCase))
                {
                    menu = h;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        W("menu hwnd=" + menu.ToInt64());
        if (menu == IntPtr.Zero) { W("menu window not found (open it first)"); return; }

        AutomationElement root = AutomationElement.FromHandle(menu);
        ItemContainerPattern icp = null;
        object icpObj;
        if (root.TryGetCurrentPattern(ItemContainerPattern.Pattern, out icpObj)) icp = (ItemContainerPattern)icpObj;

        AutomationElement item = null;
        if (icp != null)
        {
            try { item = icp.FindItemByProperty(null, AutomationElement.NameProperty, want); }
            catch (Exception ex) { W("icp failed: " + ex.Message); }
        }
        if (item == null)
        {
            item = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, want));
        }
        W("item=" + (item == null ? "null" : "ok"));
        if (item == null) return;

        System.Windows.Rect r = item.Current.BoundingRectangle;
        W("item rect=" + (int)r.Left + "," + (int)r.Top + " " + (int)r.Width + "x" + (int)r.Height);
        SetCursorPos((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2));
        Thread.Sleep(150);
        mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
        W("clicked [" + want + "]");
        Thread.Sleep(1500);
    }
}
