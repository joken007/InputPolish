using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// 一次性截图工具：通过 IPC 让正在跑的 InputPolish 弹出右键菜单，然后抓菜单窗口。
//   menushot.exe <out.png>
internal static class MenuShot
{
    private const string PipeName = "InputPolish_cmd_7F3A21";

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static void Main(string[] args)
    {
        string outPng = args.Length > 0 ? args[0] : "menu.png";
        try
        {
            if (!Send("menu")) { Console.WriteLine("IPC send failed (app running?)"); return; }
            Thread.Sleep(900);

            IntPtr menu = FindMenu();
            if (menu == IntPtr.Zero) { Console.WriteLine("menu window not found"); return; }
            RECT r;
            GetWindowRect(menu, out r);
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            Console.WriteLine("menu rect=" + r.Left + "," + r.Top + " " + w + "x" + h);

            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h));
                }
                bmp.Save(outPng, ImageFormat.Png);
            }
            Console.WriteLine("saved " + outPng);

            // 点空白处关掉菜单
            SetCursorPos(4, 4);
            Thread.Sleep(150);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERR " + ex.Message);
        }
    }

    private static bool Send(string command)
    {
        try
        {
            using (System.IO.Pipes.NamedPipeClientStream c = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeDirection.Out))
            {
                c.Connect(2500);
                byte[] payload = Encoding.UTF8.GetBytes(command);
                c.Write(payload, 0, payload.Length);
                c.Flush();
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("send: " + ex.Message);
            return false;
        }
    }

    private static IntPtr FindMenu()
    {
        foreach (Process p in Process.GetProcessesByName("InputPolish"))
        {
            int pid = p.Id;
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint wp;
                GetWindowThreadProcessId(h, out wp);
                if ((int)wp != pid) return true;
                if (!IsWindowVisible(h)) return true;
                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                int w = r.Right - r.Left;
                int hh = r.Bottom - r.Top;
                if (w > 120 && hh > 90 && hh < 600)
                {
                    StringBuilder cn = new StringBuilder(256);
                    GetClassNameW(h, cn, 256);
                    if (cn.ToString().IndexOf("WindowsForms", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = h;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) return found;
        }
        return IntPtr.Zero;
    }
}
