using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// 一次性截图工具：
//   widgetshot.exe <out.png> idle      悬浮球待命态（光标移开，捕获折叠球）
//   widgetshot.exe <out.png> hover     悬浮球展开态（光标停在第一个按钮上）
//   widgetshot.exe <out.png> tray <ico> <size>   托盘图标（从 assets/icon.ico 取对应尺寸帧）
internal static class WidgetShot
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static void Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: widgetshot <out.png> idle|hover|tray [ico] [size]"); return; }
        try
        {
            string outPng = args[0];
            string mode = args[1].ToLowerInvariant();
            if (mode == "tray") { Tray(outPng, args.Length > 2 ? args[2] : "assets/icon.ico", args.Length > 3 ? int.Parse(args[3]) : 32); return; }

            IntPtr h = FindWidget();
            if (h == IntPtr.Zero) { Console.WriteLine("widget window not found (InputPolish running?)"); return; }

            if (mode == "idle")
            {
                SetCursorPos(4, 4);
                Thread.Sleep(1800);
                RECT r = Rect(h);
                SaveWidget(outPng, r, 45);
                Console.WriteLine("idle rect=" + Show(r) + " -> " + outPng);
                return;
            }

            // hover：先把光标移到球心，展开后再悬停到第一个按钮。
            // 桌面是活人的，光标可能被抢走，所以每一拍都重新定位一次。
            RECT c = Rect(h);
            int ballX = c.Left + 29;
            int ballY = (c.Top + c.Bottom) / 2;
            for (int i = 0; i < 30; i++)
            {
                SetCursorPos(ballX, ballY);
                Thread.Sleep(100);
                RECT s = Rect(h);
                POINT cp;
                GetCursorPos(out cp);
                Console.WriteLine("t=" + (i * 100) + "ms rect=" + Show(s) + " cursor=" + cp.X + "," + cp.Y);
                if ((s.Right - s.Left) >= 486) break;
            }
            RECT e = Rect(h);
            // 悬停到第一个按钮（暂停/启用），让截图里能看到 hover 高亮
            SetCursorPos(e.Left + 96, (e.Top + e.Bottom) / 2);
            Thread.Sleep(120);
            SetCursorPos(e.Left + 96, (e.Top + e.Bottom) / 2);
            Thread.Sleep(120);
            RECT e2 = Rect(h);
            SaveWidget(outPng, e2, 30);
            Console.WriteLine("hover rect=" + Show(e2) + " -> " + outPng);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERR " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string Show(RECT r)
    {
        return r.Left + "," + r.Top + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top);
    }

    private static RECT Rect(IntPtr h)
    {
        RECT r;
        GetWindowRect(h, out r);
        return r;
    }

    private static Rectangle Inflate(RECT r, int pad)
    {
        return new Rectangle(r.Left - pad, r.Top - pad, (r.Right - r.Left) + pad * 2, (r.Bottom - r.Top) + pad * 2);
    }

    private static void Save(string path, Rectangle area)
    {
        Rectangle screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        area.Intersect(screen);
        Bitmap bmp = new Bitmap(area.Width, area.Height);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(area.Left, area.Top, 0, 0, new Size(area.Width, area.Height));
        }
        bmp.Save(path, ImageFormat.Png);
        bmp.Dispose();
    }

    /// <summary>
    /// 截悬浮球窗口本身（不含桌面杂物）：抓窗口矩形，再贴着圆角胶囊的 Region 蒙版
    /// 贴到浅色底上，这样文档截图干净，也不会把用户桌面别的东西截进去。
    /// </summary>
    private static void SaveWidget(string path, RECT r, int margin)
    {
        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        if (w <= 2 || h <= 2) { Save(path, Inflate(r, margin)); return; }

        using (Bitmap shot = new Bitmap(w, h))
        {
            using (Graphics gs = Graphics.FromImage(shot))
            {
                gs.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h));
            }

            using (Bitmap canvas = new Bitmap(w + margin * 2, h + margin * 2))
            {
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    using (LinearGradientBrush bg = new LinearGradientBrush(
                        new Rectangle(0, 0, canvas.Width, canvas.Height),
                        Color.FromArgb(247, 248, 252), Color.FromArgb(230, 234, 243), 90f))
                    {
                        g.FillRectangle(bg, 0, 0, canvas.Width, canvas.Height);
                    }

                    // 阴影 + 胶囊蒙版。抓图最外 1px 是桌面，蒙版往里收 1px 去掉。
                    int iw = w - 2;
                    int ih = h - 2;
                    RectangleF body = new RectangleF(margin + 1, margin + 1, iw, ih);
                    using (GraphicsPath pill = RoundRect(body, ih / 2f))
                    using (SolidBrush shadow = new SolidBrush(Color.FromArgb(26, 30, 38, 62)))
                    {
                        using (GraphicsPath sp = RoundRect(new RectangleF(body.X, body.Y + 5f, body.Width, body.Height), ih / 2f))
                        {
                            g.FillPath(shadow, sp);
                        }
                        Region old = g.Clip;
                        g.SetClip(pill, CombineMode.Intersect);
                        g.DrawImage(shot, new Rectangle(margin, margin, w, h));
                        g.Clip = old;
                    }
                }
                canvas.Save(path, ImageFormat.Png);
            }
        }
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath();
        float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        if (d <= 0.1f)
        {
            p.AddRectangle(r);
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180f, 90f);
        p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
        p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
        p.CloseFigure();
        return p;
    }

    private static IntPtr FindWidget()
    {
        IntPtr found = IntPtr.Zero;
        Process[] ps = Process.GetProcessesByName("InputPolish");
        for (int i = 0; i < ps.Length && found == IntPtr.Zero; i++)
        {
            int pid = ps[i].Id;
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
                int ex = GetWindowLong(h, -20);
                int style = GetWindowLong(h, -16);
                bool tool = (ex & 0x80) != 0;            // WS_EX_TOOLWINDOW：悬浮球/浮标
                bool caption = (style & 0x00C00000) != 0;
                if (tool && !caption && hh >= 40 && hh <= 90 && w >= 40 && w <= 700)
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }
        return found;
    }

    private static void Tray(string outPng, string ico, int size)
    {
        using (Icon ic = new Icon(ico, size, size))
        using (Bitmap bmp = ic.ToBitmap())
        {
            bmp.Save(outPng, ImageFormat.Png);
            Console.WriteLine("tray " + size + "x" + size + " -> " + outPng);
        }
    }
}
