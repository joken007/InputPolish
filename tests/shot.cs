using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class Shot
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static List<IntPtr> FindWindows(string procName)
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            string name = "";
            try { name = Process.GetProcessById((int)pid).ProcessName; } catch { return true; }
            if (string.Equals(name, procName, StringComparison.OrdinalIgnoreCase) && IsWindowVisible(h))
                list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "C:\\Users\\Administrator\\AppData\\Local\\InputPolishTest\\shot.png";
        string target = args.Length > 1 ? args[1] : "InputPolish";
        if (args.Length > 3)
        {
            int cx = int.Parse(args[2]);
            int cy = int.Parse(args[3]);
            SetCursorPos(cx, cy);
            System.Threading.Thread.Sleep(1200);
        }

        List<IntPtr> wins = FindWindows(target);
        StringBuilder rep = new StringBuilder();
        rep.AppendLine("windows=" + wins.Count);
        Rectangle bounds = Rectangle.Empty;
        foreach (IntPtr h in wins)
        {
            RECT r;
            if (!GetWindowRect(h, out r)) continue;
            StringBuilder t = new StringBuilder(256);
            GetWindowTextW(h, t, 256);
            rep.AppendLine("hwnd=" + h.ToInt64() + " rect=" + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom +
                           " title=[" + t + "] style=0x" + GetWindowLong(h, -16).ToString("X8") +
                           " exStyle=0x" + GetWindowLong(h, -20).ToString("X8"));
            Rectangle cur = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (cur.Width > 0 && cur.Height > 0) bounds = bounds.IsEmpty ? cur : Rectangle.Union(bounds, cur);
        }
        rep.AppendLine("union=" + bounds.Left + "," + bounds.Top + "," + bounds.Width + "x" + bounds.Height);

        if (!bounds.IsEmpty)
        {
            int pad = 60;
            Rectangle cap = Rectangle.Inflate(bounds, pad, pad);
            Bitmap bmp = new Bitmap(cap.Width, cap.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(cap.Left, cap.Top, 0, 0, new Size(cap.Width, cap.Height));
            }
            bmp.Save(outPath, ImageFormat.Png);
            rep.AppendLine("saved=" + outPath + " " + cap.Width + "x" + cap.Height);
        }
        File.WriteAllText(outPath + ".txt", rep.ToString(), Encoding.UTF8);
        Console.WriteLine(rep.ToString());
    }
}
