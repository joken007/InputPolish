using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InputPolish
{
    /// <summary>
    /// 划词翻译结果卡片：置顶、不抢焦点（WS_EX_NOACTIVATE），
    /// 所以弹出时原来的选区与光标都还在，用户可以直接继续操作。
    /// 卡片显示「译文」+「讲解」，可以一键复制译文。
    /// </summary>
    public class PopupCard : Form
    {
        private static PopupCard _current;

        private const int PadX = 16;
        private const int HeadH = 34;
        private const int MaxW = 520;
        private const int MinW = 260;

        private readonly string _translation;
        private readonly string _explanation;
        private readonly string _meta;
        private string _buttonState = "";
        private System.Windows.Forms.Timer _timer;
        private int _innerW;

        private PopupCard(string meta, string translation, string explanation)
        {
            _meta = meta;
            _translation = translation == null ? "" : translation.Trim();
            _explanation = explanation == null ? "" : explanation.Trim();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Font transFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel);
            Font expFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel);

            int w = MaxW;
            _innerW = w - PadX * 2;

            Size transSize = Measure(_translation, transFont, _innerW);
            Size expSize = _explanation.Length > 0 ? Measure(_explanation, expFont, _innerW) : new Size(0, 0);

            int h = HeadH + 6 + transSize.Height + 12 + PadX;
            if (_explanation.Length > 0) h += expSize.Height + 26;

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (h > wa.Height - 80)
            {
                h = wa.Height - 80;
                // 内容超长时缩小卡片宽度换取高度不溢出
                w = Math.Max(MinW, MaxW);
            }
            ClientSize = new Size(w, h);
            _innerW = w - PadX * 2;

            AddOverlay(transSize, expSize, w, h);
            PositionNearCursor(w, h);

            try { Region = new Region(Toast.RoundPath(new RectangleF(0, 0, w, h), 12f)); }
            catch { }

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 30000;
            _timer.Tick += delegate { _timer.Stop(); Close(); };
            _timer.Start();
            MouseEnter += delegate { _timer.Stop(); };
            MouseLeave += delegate { if (_timer != null) { _timer.Stop(); _timer.Start(); } };
        }

        private Size Measure(string text, Font f, int width)
        {
            if (text == null || text.Length == 0) return new Size(0, 18);
            Size s = TextRenderer.MeasureText(text, f, new Size(Math.Max(60, width), 4000),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
            return new Size(width, Math.Max(18, s.Height));
        }

        private void AddOverlay(Size transSize, Size expSize, int w, int h)
        {
            Label copy = new Label();
            copy.Text = "复制";
            copy.AutoSize = false;
            copy.Size = new Size(58, 20);
            copy.Location = new Point(w - 124, 8);
            copy.TextAlign = ContentAlignment.MiddleCenter;
            copy.Padding = new Padding(16, 0, 0, 0);      // 左侧留给 copy 矢量图标
            copy.ForeColor = Color.FromArgb(79, 124, 255);
            copy.BackColor = Color.FromArgb(238, 242, 255);
            copy.Cursor = Cursors.Hand;
            copy.Paint += delegate(object s, PaintEventArgs pe)
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Icons.Draw(pe.Graphics, "copy", new RectangleF(6f, (copy.Height - 12f) / 2f, 12f, 12f), copy.ForeColor);
            };
            copy.Click += delegate
            {
                try { ClipUtil.SetText(_translation); } catch { }
                _buttonState = "已复制";
                Invalidate();
            };
            copy.MouseEnter += delegate { copy.BackColor = Color.FromArgb(224, 231, 255); };
            copy.MouseLeave += delegate { copy.BackColor = Color.FromArgb(238, 242, 255); };
            Controls.Add(copy);

            Label close = new Label();
            close.Text = "";                              // 图标自绘，不用「✕」字符
            close.AutoSize = false;
            close.Size = new Size(22, 20);
            close.Location = new Point(w - 52, 8);
            close.TextAlign = ContentAlignment.MiddleCenter;
            close.ForeColor = Color.FromArgb(140, 148, 165);
            close.Cursor = Cursors.Hand;
            close.Paint += delegate(object s, PaintEventArgs pe)
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Icons.Draw(pe.Graphics, "close", new RectangleF(5f, (close.Height - 12f) / 2f, 12f, 12f), close.ForeColor);
            };
            close.Click += delegate { Close(); };
            close.MouseEnter += delegate { close.ForeColor = Color.FromArgb(60, 66, 80); };
            close.MouseLeave += delegate { close.ForeColor = Color.FromArgb(140, 148, 165); };
            Controls.Add(close);
        }

        private void PositionNearCursor(int w, int h)
        {
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Point p = Cursor.Position;
            int x = p.X + 14;
            int y = p.Y + 20;
            if (x + w > wa.Right - 8) x = p.X - w - 14;
            if (y + h > wa.Bottom - 8) y = p.Y - h - 14;
            if (x < wa.Left + 8) x = wa.Left + 8;
            if (y < wa.Top + 8) y = wa.Top + 8;
            Location = new Point(x, y);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = ClientSize.Width;
            int h = ClientSize.Height;

            using (SolidBrush bg = new SolidBrush(Color.White))
            {
                g.FillRectangle(bg, 0, 0, w, h);
            }

            // 左侧强调色条
            Rectangle bar = new Rectangle(0, 0, 4, h);
            using (LinearGradientBrush b = new LinearGradientBrush(
                new Rectangle(0, 0, 4, h), Color.FromArgb(124, 92, 255), Color.FromArgb(58, 132, 255), 90f))
            {
                g.FillRectangle(b, bar);
            }

            // 顶部标题
            using (Font titleFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(90, 98, 116)))
            {
                string head = _meta;
                if (_buttonState.Length > 0) head = head + "  ·  " + _buttonState;
                g.DrawString(head, titleFont, tb, PadX, 9);
            }

            int y = HeadH + 6;

            using (Font transFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(28, 32, 42)))
            {
                Rectangle r = new Rectangle(PadX, y, w - PadX * 2, h);
                TextRenderer.DrawText(g, _translation, transFont, r, Color.FromArgb(28, 32, 42),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
                Size ts = TextRenderer.MeasureText(_translation, transFont, new Size(Math.Max(60, w - PadX * 2), 4000),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
                y += Math.Max(18, ts.Height) + 10;
            }

            if (_explanation.Length > 0)
            {
                using (Pen p = new Pen(Color.FromArgb(232, 235, 242)))
                {
                    g.DrawLine(p, PadX, y, w - PadX, y);
                }
                y += 8;

                using (Font expFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    Rectangle r = new Rectangle(PadX, y, w - PadX * 2, h - y);
                    TextRenderer.DrawText(g, _explanation, expFont, r, Color.FromArgb(82, 90, 108),
                        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
                }
            }

            using (Pen border = new Pen(Color.FromArgb(224, 228, 238)))
            {
                g.DrawRectangle(border, 0, 0, w - 1, h - 1);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
            base.Dispose(disposing);
        }

        public static void ShowResult(Config cfg, TransResult r)
        {
            if (r == null) return;
            try
            {
                if (_current != null)
                {
                    try { _current.Close(); }
                    catch { }
                    _current = null;
                }
                if (!r.Ok)
                {
                    Toast.ShowNotice("翻译失败：" + r.Error, true, 3200);
                    return;
                }

                string meta = "划词翻译 → " + r.Target;
                PopupCard card = new PopupCard(meta, r.Translation, r.Explanation);
                _current = card;
                card.Show();
                Log.Write("popup shown target=" + r.Target + " trans=" + r.Translation.Length +
                          " explain=" + r.Explanation.Length + " hwnd=" + card.Handle.ToInt64() +
                          " rect=" + card.Left + "," + card.Top + "," + card.Width + "x" + card.Height);
            }
            catch (Exception ex)
            {
                Log.Write("popup failed: " + ex.Message);
            }
        }
    }
}
