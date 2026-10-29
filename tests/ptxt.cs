using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

class PTxt
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr wp, string lp);
    [DllImport("user32.dll")] static extern IntPtr SendMessageA(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

    delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    static readonly List<IntPtr> Kids = new List<IntPtr>();

    static IntPtr Find(string frag)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder sb = new StringBuilder(512);
            GetWindowTextW(h, sb, 512);
            if (sb.ToString().IndexOf(frag, StringComparison.Ordinal) >= 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static int Main(string[] a)
    {
        try { return Real(a); }
        catch (Exception ex) { Console.WriteLine("ERR " + ex.GetType().Name + ": " + ex.Message); return 1; }
    }

    static int Real(string[] a)
    {
        if (a.Length < 1) { Console.WriteLine("usage: ptxt.exe <titleFrag> [list|set <i> <text>|setall <name> <body>]"); return 2; }
        IntPtr win = Find(a[0]);
        if (win == IntPtr.Zero) { Console.WriteLine("window not found: " + a[0]); return 1; }
        Kids.Clear();
        EnumChildWindows(win, delegate(IntPtr h, IntPtr p)
        {
            StringBuilder c = new StringBuilder(256);
            GetClassNameW(h, c, 256);
            string cls = c.ToString().ToUpperInvariant();
            if (cls.IndexOf("EDIT") >= 0) Kids.Add(h);
            return true;
        }, IntPtr.Zero);
        Console.WriteLine("window=" + win.ToInt64() + " edits=" + Kids.Count);

        string cmd = a.Length > 1 ? a[1] : "list";
        if (cmd == "list")
        {
            for (int i = 0; i < Kids.Count; i++)
            {
                RECT r; GetWindowRect(Kids[i], out r);
                StringBuilder t = new StringBuilder(4096);
                GetWindowTextW(Kids[i], t, 4096);
                string s = t.ToString();
                if (s.Length > 60) s = s.Substring(0, 60) + "…";
                Console.WriteLine("  #" + i + " rect=" + r.Left + "," + r.Top + " " + (r.Right-r.Left) + "x" + (r.Bottom-r.Top) + " text=[" + s + "]");
            }
            return 0;
        }
        if (cmd == "set")
        {
            int i = int.Parse(a[2]);
            if (i < 0 || i >= Kids.Count) { Console.WriteLine("bad index"); return 1; }
            SendMessageW(Kids[i], 0x000C, IntPtr.Zero, a[3]);
            Console.WriteLine("set #" + i + " len=" + a[3].Length);
            return 0;
        }
        if (cmd == "setall")
        {
            // 名称 = 第一个单行 EDIT，正文 = 最高的那个 EDIT
            int nameI = -1, bodyI = -1, bodyH = 0;
            for (int i = 0; i < Kids.Count; i++)
            {
                RECT r; GetWindowRect(Kids[i], out r);
                int hh = r.Bottom - r.Top;
                if (nameI < 0 && hh < 40) nameI = i;
                if (hh > bodyH) { bodyH = hh; bodyI = i; }
            }
            if (nameI >= 0) SendMessageW(Kids[nameI], 0x000C, IntPtr.Zero, a[2]);
            if (bodyI >= 0) SendMessageW(Kids[bodyI], 0x000C, IntPtr.Zero, a[3]);
            Console.WriteLine("setall name#" + nameI + " body#" + bodyI + " bodyH=" + bodyH);
            return 0;
        }
        Console.WriteLine("unknown cmd " + cmd);
        return 2;
    }
}
