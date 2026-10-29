using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

class Fs
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool f);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    static void Main(string[] a)
    {
        try { Real(a); }
        catch (Exception ex) { Console.WriteLine("ERR " + ex.GetType().Name + ": " + ex.Message); }
    }

    static void Real(string[] a)
    {
        string outPng = a.Length > 0 ? a[0] : "screen.png";
        string find = a.Length > 1 ? a[1] : "";
        IntPtr target = IntPtr.Zero;
        if (find.Length > 0)
        {
            EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                if (!IsWindowVisible(h)) return true;
                StringBuilder sb = new StringBuilder(512);
                GetWindowTextW(h, sb, 512);
                if (sb.ToString().IndexOf(find, StringComparison.Ordinal) >= 0) { target = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (target != IntPtr.Zero)
            {
                uint pid; uint tid = GetWindowThreadProcessId(target, out pid);
                uint me = GetCurrentThreadId();
                AttachThreadInput(me, tid, true);
                ShowWindow(target, 9);
                BringWindowToTop(target);
                SetForegroundWindow(target);
                AttachThreadInput(me, tid, false);
                System.Threading.Thread.Sleep(500);
            }
        }
        RECT r;
        IntPtr fg = target != IntPtr.Zero ? target : GetForegroundWindow();
        GetWindowRect(fg, out r);
        int w = r.Right - r.Left, h2 = r.Bottom - r.Top;
        Bitmap bmp = new Bitmap(w, h2);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h2));
        }
        // 被别的窗口盖住时 CopyFromScreen 只会截到上层窗口，所以优先用 PrintWindow 让窗口自己重绘。
        Bitmap pw = new Bitmap(w, h2);
        using (Graphics g = Graphics.FromImage(pw))
        {
            IntPtr hdc = g.GetHdc();
            bool ok = PrintWindow(fg, hdc, 2);
            g.ReleaseHdc(hdc);
            if (!ok) { g.DrawImage(bmp, 0, 0); }
        }
        bmp.Dispose();
        pw.Save(outPng, ImageFormat.Png);
        bmp = null;
        StringBuilder tt = new StringBuilder(512); GetWindowTextW(fg, tt, 512);
        StringBuilder cc = new StringBuilder(256); GetClassNameW(fg, cc, 256);
        Console.WriteLine("hwnd=" + fg + " rect=" + r.Left + "," + r.Top + " " + w + "x" + h2 + " title=[" + tt + "] class=" + cc);
        pw.Dispose();
    }
}
