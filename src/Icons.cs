using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace InputPolish
{
    /// <summary>
    /// 矢量图标渲染：把 IconsData 里的 24x24 path 字符串解析成 GraphicsPath（缓存），
    /// 再用 Graphics 变换缩放到任意矩形绘制。所有界面图标都从这里出，
    /// 不再拿「润 / 译 / ✎ / ＋」这类单字或符号当图标。
    /// 小尺寸（<=32px）自动换 IconsData.SmallVariant（brand -> brand_small 实心笔尖）。
    /// </summary>
    internal static class Icons
    {
        public const float Grid = 24f;
        public const float DefaultStroke = 2f;

        /// <summary>全部图标 id，供自检程序 / 图标表使用。</summary>
        public static readonly string[] All = new string[]
        {
            "brand", "brand_small", "translate", "summarize", "settings", "undo", "quit",
            "play", "pause", "check", "warning", "off", "spinner", "outline", "export",
            "note", "pen", "plus", "copy", "close", "chat", "search", "columns"
        };

        private sealed class Seg
        {
            public GraphicsPath Path;
            public bool Fill;
            public float Width;
        }

        private sealed class Shape
        {
            public List<Seg> Segs = new List<Seg>();
        }

        private static readonly Dictionary<string, Shape> _shapes = new Dictionary<string, Shape>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Bitmap> _bitmaps = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        private static readonly object _sync = new object();

        // ---------------------------------------------------------------- 数据

        /// <summary>按 id 取原始 path 数据（未知 id 返回 null）。</summary>
        public static string Data(string id)
        {
            if (id == null) return null;
            switch (id)
            {
                case "brand": return IconsData.Brand;
                case "brand_small": return IconsData.BrandSmall;
                case "translate": return IconsData.Translate;
                case "summarize": return IconsData.Summarize;
                case "settings": return IconsData.Settings;
                case "undo": return IconsData.Undo;
                case "quit": return IconsData.Quit;
                case "play": return IconsData.Play;
                case "pause": return IconsData.Pause;
                case "check": return IconsData.Check;
                case "warning": return IconsData.Warning;
                case "off": return IconsData.Off;
                case "spinner": return IconsData.Spinner;
                case "outline": return IconsData.Outline;
                case "export": return IconsData.Export;
                case "note": return IconsData.Note;
                case "pen": return IconsData.Pen;
                case "plus": return IconsData.Plus;
                case "copy": return IconsData.Copy;
                case "close": return IconsData.Close;
                case "chat": return IconsData.Chat;
                case "search": return IconsData.Search;
                case "columns": return IconsData.Columns;
                default: return null;
            }
        }

        public static bool Has(string id)
        {
            return Data(id) != null;
        }

        // ---------------------------------------------------------------- 绘制

        public static void Draw(Graphics g, string id, RectangleF rect, Color color)
        {
            Draw(g, id, rect, color, DefaultStroke, 0f);
        }

        /// <summary>
        /// 把图标画进 rect（等比缩放并居中）。rotation 为绕中心旋转的角度（度），spinner 用。
        /// strokeWidth 是 24 网格下的线宽，默认 2；段级 w1.4: 会按比例跟着缩放。
        /// </summary>
        public static void Draw(Graphics g, string id, RectangleF rect, Color color, float strokeWidth, float rotation)
        {
            if (g == null || rect.Width <= 0f || rect.Height <= 0f) return;
            string resolved = Resolve(id, Math.Min(rect.Width, rect.Height));
            if (resolved == null) return;
            DrawRaw(g, resolved, rect, color, strokeWidth, rotation);
        }

        private static void DrawRaw(Graphics g, string id, RectangleF rect, Color color, float strokeWidth, float rotation)
        {
            Shape shape = GetShape(id);
            if (shape == null || shape.Segs.Count == 0) return;

            float size = Math.Min(rect.Width, rect.Height);
            float scale = size / Grid;
            float strokeScale = strokeWidth / DefaultStroke;
            if (strokeScale <= 0f) strokeScale = 1f;

            GraphicsState st = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TranslateTransform(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
                if (rotation != 0f) g.RotateTransform(rotation);
                g.ScaleTransform(scale, scale);
                g.TranslateTransform(-Grid / 2f, -Grid / 2f);

                for (int i = 0; i < shape.Segs.Count; i++)
                {
                    Seg seg = shape.Segs[i];
                    if (seg.Fill)
                    {
                        using (SolidBrush b = new SolidBrush(color))
                        {
                            g.FillPath(b, seg.Path);
                        }
                    }
                    else
                    {
                        float w = seg.Width * strokeScale;
                        if (w < 0.35f) w = 0.35f;
                        using (Pen pen = new Pen(color, w))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            pen.LineJoin = LineJoin.Round;
                            g.DrawPath(pen, seg.Path);
                        }
                    }
                }
            }
            finally
            {
                g.Restore(st);
            }
        }

        // ---------------------------------------------------------------- 位图（托盘菜单 / ToolStrip / 自检）

        /// <summary>
        /// 生成 size x size 的透明位图。4 倍超采样后缩回来，16px 也有清晰边缘。
        /// 结果按 (id,size,color) 缓存，调用方不要 Dispose。
        /// </summary>
        public static Bitmap Bitmap(string id, int size, Color color)
        {
            if (id == null) return null;
            if (size < 1) size = 1;
            string key = id + "|" + size.ToString(CultureInfo.InvariantCulture) + "|" +
                         color.ToArgb().ToString(CultureInfo.InvariantCulture);
            lock (_sync)
            {
                Bitmap hit;
                if (_bitmaps.TryGetValue(key, out hit)) return hit;
            }

            string resolved = Resolve(id, size);
            if (resolved == null) return null;
            int ss = size <= 24 ? 4 : 3;
            Bitmap big = new Bitmap(size * ss, size * ss, PixelFormat.Format32bppArgb);
            Bitmap small = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics gb = Graphics.FromImage(big))
                {
                    gb.Clear(Color.Transparent);
                    DrawRaw(gb, resolved, new RectangleF(0f, 0f, big.Width, big.Height), color, DefaultStroke, 0f);
                }
                using (Graphics gs = Graphics.FromImage(small))
                {
                    gs.Clear(Color.Transparent);
                    gs.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gs.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    gs.DrawImage(big, new Rectangle(0, 0, size, size), 0, 0, big.Width, big.Height, GraphicsUnit.Pixel);
                }
            }
            finally
            {
                big.Dispose();
            }

            lock (_sync)
            {
                _bitmaps[key] = small;
            }
            return small;
        }

        // ---------------------------------------------------------------- 菜单图标

        /// <summary>菜单图标统一墨色（浅色菜单背景上）。</summary>
        public static readonly Color MenuInk = Color.FromArgb(56, 62, 80);

        /// <summary>16px 菜单图标（ToolStripMenuItem.Image 用），带缓存，别 Dispose。</summary>
        public static Bitmap MenuImage(string id)
        {
            return Bitmap(id, 16, MenuInk);
        }

        // ---------------------------------------------------------------- 应用 / 托盘图标

        /// <summary>
        /// 应用图标兜底：紫蓝渐变 squircle + 白色钢笔尖 + 右上高光星（与 assets/icon.ico 同款视觉）。
        /// 正常路径会优先用嵌入的 .ico；这里只是资源缺失时的矢量兜底。
        /// </summary>
        public static Bitmap AppTile(int size)
        {
            if (size < 8) size = 8;
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float pad = Math.Max(0.5f, size * 0.045f);
                RectangleF plate = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
                using (GraphicsPath p = RoundRect(plate, size * 0.235f))
                using (LinearGradientBrush br = new LinearGradientBrush(plate,
                    Color.FromArgb(146, 116, 255), Color.FromArgb(52, 120, 255), 55f))
                {
                    g.FillPath(br, p);
                }

                // 白色钢笔尖：略偏上，给小尺寸留出安全边
                float gl = plate.Width * 0.62f;
                RectangleF glyph = new RectangleF(plate.X + (plate.Width - gl) / 2f,
                    plate.Y + plate.Height * 0.13f, gl, gl);
                DrawRaw(g, "brand", glyph, Color.White, 2.6f, 0f);

                DrawSparkle(g, plate.Right - plate.Width * 0.235f, plate.Top + plate.Height * 0.235f,
                    plate.Width * 0.105f, Color.FromArgb(245, 255, 255, 255));
            }
            return bmp;
        }

        private static void DrawSparkle(Graphics g, float cx, float cy, float r, Color color)
        {
            using (GraphicsPath sp = new GraphicsPath())
            {
                float inner = r * 0.30f;
                for (int k = 0; k < 8; k++)
                {
                    double ang = -Math.PI / 2 + k * Math.PI / 4;
                    float rr = (k % 2 == 0) ? r : inner;
                    float px = cx + (float)(rr * Math.Cos(ang));
                    float py = cy + (float)(rr * Math.Sin(ang));
                    if (k == 0) sp.AddLine(px, py, px, py);
                    else sp.AddLine(sp.GetLastPoint(), new PointF(px, py));
                }
                sp.CloseFigure();
                using (SolidBrush sb = new SolidBrush(color))
                {
                    g.FillPath(sb, sp);
                }
            }
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
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

        // ---------------------------------------------------------------- 解析 / 缓存

        private static string Resolve(string id, float pixelSize)
        {
            if (id == null) return null;
            if (pixelSize <= 32f) return IconsData.SmallVariant(id);
            return id;
        }

        private static Shape GetShape(string id)
        {
            if (id == null) return null;
            lock (_sync)
            {
                Shape s;
                if (_shapes.TryGetValue(id, out s)) return s;
                s = Build(id);
                _shapes[id] = s;
                return s;
            }
        }

        private static Shape Build(string id)
        {
            string data = Data(id);
            if (data == null || data.Length == 0) return null;
            Shape shape = new Shape();
            string[] parts = data.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string seg = parts[i] == null ? "" : parts[i].Trim();
                if (seg.Length == 0) continue;

                bool fill = false;
                float width = DefaultStroke;
                // 前缀：可叠多个（f: / w1.5:）
                while (seg.Length > 0)
                {
                    if (seg.StartsWith("f:", StringComparison.Ordinal))
                    {
                        fill = true;
                        seg = seg.Substring(2);
                        continue;
                    }
                    if (seg[0] == 'w')
                    {
                        int colon = seg.IndexOf(':');
                        if (colon > 1)
                        {
                            float parsed;
                            if (float.TryParse(seg.Substring(1, colon - 1), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out parsed) && parsed > 0f)
                            {
                                width = parsed;
                            }
                            seg = seg.Substring(colon + 1);
                            continue;
                        }
                    }
                    break;
                }

                GraphicsPath path = new GraphicsPath(fill ? FillMode.Winding : FillMode.Alternate);
                ParsePath(seg, path);
                if (path.PointCount == 0)
                {
                    path.Dispose();
                    continue;
                }
                Seg item = new Seg();
                item.Path = path;
                item.Fill = fill;
                item.Width = width;
                shape.Segs.Add(item);
            }
            return shape;
        }

        /// <summary>
        /// 解析 M/L/H/V/C/Z（大小写都支持，相对坐标按当前点解释）。
        /// IconsData 只用这几条命令，所以不实现圆弧 / 二次曲线。
        /// </summary>
        private static void ParsePath(string s, GraphicsPath path)
        {
            if (s == null) return;
            int i = 0;
            int n = s.Length;
            char cmd = 'M';
            PointF cur = new PointF(0f, 0f);
            PointF start = new PointF(0f, 0f);
            bool open = false;

            while (i < n)
            {
                char c = s[i];
                if (c == ' ' || c == ',' || c == '\t' || c == '\r' || c == '\n')
                {
                    i++;
                    continue;
                }
                if (char.IsLetter(c))
                {
                    cmd = c;
                    i++;
                    if (cmd == 'Z' || cmd == 'z')
                    {
                        if (open) path.CloseFigure();
                        open = false;
                        cur = start;
                        cmd = 'M';
                    }
                    continue;
                }

                char u = char.ToUpperInvariant(cmd);
                bool rel = (cmd != u);

                float x;
                float y;
                if (!ReadNumber(s, ref i, out x)) break;
                if (u == 'H' || u == 'V')
                {
                    y = 0f;
                }
                else if (!ReadNumber(s, ref i, out y))
                {
                    break;
                }

                if (rel)
                {
                    if (u == 'H') x += cur.X;
                    else if (u == 'V') y += cur.Y;
                    else
                    {
                        x += cur.X;
                        y += cur.Y;
                    }
                }

                if (u == 'M')
                {
                    cur = new PointF(x, y);
                    start = cur;
                    path.StartFigure();
                    open = true;
                    cmd = rel ? 'l' : 'L';
                    continue;
                }

                if (u == 'L')
                {
                    if (!open)
                    {
                        path.StartFigure();
                        open = true;
                    }
                    path.AddLine(cur, new PointF(x, y));
                    cur = new PointF(x, y);
                    continue;
                }

                if (u == 'H')
                {
                    if (!open)
                    {
                        path.StartFigure();
                        open = true;
                    }
                    path.AddLine(cur, new PointF(x, cur.Y));
                    cur = new PointF(x, cur.Y);
                    continue;
                }

                if (u == 'V')
                {
                    if (!open)
                    {
                        path.StartFigure();
                        open = true;
                    }
                    path.AddLine(cur, new PointF(cur.X, y));
                    cur = new PointF(cur.X, y);
                    continue;
                }

                if (u == 'C')
                {
                    float x1 = x;
                    float y1 = y;
                    float x2;
                    float y2;
                    float x3;
                    float y3;
                    if (!ReadNumber(s, ref i, out x2)) break;
                    if (!ReadNumber(s, ref i, out y2)) break;
                    if (!ReadNumber(s, ref i, out x3)) break;
                    if (!ReadNumber(s, ref i, out y3)) break;
                    if (rel)
                    {
                        x1 += cur.X;
                        y1 += cur.Y;
                        x2 += cur.X;
                        y2 += cur.Y;
                        x3 += cur.X;
                        y3 += cur.Y;
                    }
                    if (!open)
                    {
                        path.StartFigure();
                        open = true;
                    }
                    path.AddBezier(cur, new PointF(x1, y1), new PointF(x2, y2), new PointF(x3, y3));
                    cur = new PointF(x3, y3);
                    continue;
                }

                // 未知命令：跳过这个数字继续，避免死循环
                i++;
            }
        }

        private static bool ReadNumber(string s, ref int i, out float value)
        {
            value = 0f;
            int n = s.Length;
            while (i < n)
            {
                char ws = s[i];
                if (ws == ' ' || ws == ',' || ws == '\t' || ws == '\r' || ws == '\n') i++;
                else break;
            }
            if (i >= n) return false;

            int start = i;
            if (s[i] == '+' || s[i] == '-') i++;
            bool dot = false;
            while (i < n)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    i++;
                    continue;
                }
                if (c == '.' && !dot)
                {
                    dot = true;
                    i++;
                    continue;
                }
                break;
            }
            if (i <= start) return false;
            string token = s.Substring(start, i - start);
            if (token == "+" || token == "-" || token == "." || token == "-." || token == "+.")
            {
                value = 0f;
                return true;
            }
            return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
