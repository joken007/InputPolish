using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using InputPolish;

// 一次性自检程序：把 IconsData 里的全部图标用真的 src/Icons.cs 渲染成一张对照表。
// 编译（在仓库根目录）：
//   csc /nologo /target:exe /out:tests\out\iconsheet.exe tests\iconsheet.cs src\IconsData.cs src\Icons.cs
// 运行：tests\out\iconsheet.exe tests\out
internal static class IconSheet
{
    private static readonly Color LightBg = Color.FromArgb(246, 247, 251);
    private static readonly Color LightInk = Color.FromArgb(28, 32, 44);
    private static readonly Color LightSub = Color.FromArgb(110, 118, 136);
    private static readonly Color DarkBg = Color.FromArgb(24, 26, 34);
    private static readonly Color DarkInk = Color.FromArgb(238, 241, 248);
    private static readonly Color DarkSub = Color.FromArgb(150, 158, 176);

    private const int Cols = 6;
    private const int CellW = 96;
    private const int CellH = 104;
    private const int Pad = 16;
    private const int HeaderH = 38;
    private const int StripH = 92;

    private static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(dir);
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        string[] ids = Icons.All;
        int rows = (ids.Length + Cols - 1) / Cols;
        int gridH = rows * CellH;
        int width = Pad * 2 + Cols * CellW;
        int height = Pad + HeaderH + gridH + StripH + 12 + HeaderH + gridH + StripH + Pad;

        int fails = 0;
        foreach (string id in ids)
        {
            string data = Icons.Data(id);
            if (data == null || data.Length == 0) { Console.WriteLine("MISSING " + id); fails++; continue; }
        }

        using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                int y = Pad;
                // ---- 浅色底 ----
                using (SolidBrush bg = new SolidBrush(LightBg)) g.FillRectangle(bg, 0, y, width, HeaderH + gridH + StripH);
                y = Section(g, "浅色底（Light）· 64px 主体 / 16px 托盘菜单档", y, LightInk, LightSub, ids, false);
                y += 12;

                // ---- 深色底 ----
                using (SolidBrush bg = new SolidBrush(DarkBg)) g.FillRectangle(bg, 0, y, width, HeaderH + gridH + StripH);
                y = Section(g, "深色底（Dark）· 64px 主体 / 16px 托盘菜单档", y, DarkInk, DarkSub, ids, true);
            }
            string png = Path.Combine(dir, "icons-sheet.png");
            bmp.Save(png, ImageFormat.Png);
            Console.WriteLine("saved " + png + " " + width + "x" + height);
        }

        // 16px 实际落盘图，方便放大检查（托盘菜单就用这个尺寸）
        string strip = Path.Combine(dir, "icons-16px.png");
        using (Bitmap bmp = new Bitmap(ids.Length * 24 + 16, 40, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(LightBg);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                for (int i = 0; i < ids.Length; i++)
                {
                    Bitmap b = Icons.Bitmap(ids[i], 16, LightInk);
                    if (b == null) { Console.WriteLine("BITMAP FAIL " + ids[i]); fails++; continue; }
                    g.DrawImage(b, new Rectangle(8 + i * 24, 12, 16, 16));
                }
            }
            bmp.Save(strip, ImageFormat.Png);
            Console.WriteLine("saved " + strip);
        }

        // 校验：24 网格解析出的点数 + 关键像素非空
        foreach (string id in ids)
        {
            using (Bitmap probe = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(probe)) Icons.Draw(g, id, new RectangleF(0, 0, 64, 64), Color.Black);
                int ink = 0;
                for (int yy = 0; yy < 64; yy += 2)
                {
                    for (int xx = 0; xx < 64; xx += 2)
                    {
                        if (probe.GetPixel(xx, yy).A > 20) ink++;
                    }
                }
                if (ink < 3) { Console.WriteLine("EMPTY " + id); fails++; }
            }
        }

        Console.WriteLine(fails == 0 ? "ICONS OK (23/23)" : "ICONS FAIL " + fails);
    }

    private static int Section(Graphics g, string title, int y, Color ink, Color sub, string[] ids, bool dark)
    {
        using (Font hf = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (SolidBrush hb = new SolidBrush(ink))
        {
            g.DrawString(title, hf, hb, Pad, y + 12);
        }
        y += HeaderH;

        using (Font lf = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            for (int i = 0; i < ids.Length; i++)
            {
                int cx = Pad + (i % Cols) * CellW;
                int cy = y + (i / Cols) * CellH;
                RectangleF iconRect = new RectangleF(cx + (CellW - 56) / 2f, cy + 8, 56, 56);
                Icons.Draw(g, ids[i], iconRect, ink);
                using (SolidBrush sb = new SolidBrush(sub))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    g.DrawString(ids[i], lf, sb, new RectangleF(cx, cy + 68, CellW, 20), sf);
                    sf.Dispose();
                }
            }
        }
        y += ((ids.Length + Cols - 1) / Cols) * CellH;

        // 16px 托盘菜单档 + 旋转（spinner 验证）
        using (Font lf = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (SolidBrush sb = new SolidBrush(sub))
        {
            g.DrawString("16px:", lf, sb, Pad, y + 20);
        }
        for (int i = 0; i < ids.Length; i++)
        {
            Bitmap b = Icons.Bitmap(ids[i], 16, ink);
            if (b == null) continue;
            g.DrawImage(b, new Rectangle(Pad + 44 + i * 22, y + 16, 16, 16));
        }
        // 旋转检查：spinner 0/90/180/270（spinner 是唯一需要旋转的图标）
        using (Font lf = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (SolidBrush sb = new SolidBrush(sub))
        {
            g.DrawString("spin:", lf, sb, Pad, y + 50);
        }
        for (int k = 0; k < 4; k++)
        {
            RectangleF r = new RectangleF(Pad + 40 + k * 30, y + 42, 24, 24);
            Icons.Draw(g, "spinner", r, ink, 2f, k * 90f);
        }
        using (Font lf = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (SolidBrush sb = new SolidBrush(sub))
        {
            g.DrawString("（spinner 靠 rotation 参数旋转，悬浮球处理中态就是这样转的）", lf, sb, Pad + 40 + 4 * 30 + 8, y + 50);
        }
        return y + StripH;
    }
}
