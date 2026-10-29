using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InputPolish
{
    /// <summary>
    /// 划词浮标：鼠标选完文字后出现在选区旁边的一个小胶囊，点一下就把选中的文字翻译出来。
    /// 关键约束：绝不抢焦点（WS_EX_NOACTIVATE），否则会把用户刚选中的选区弄没。
    /// </summary>
    public class SelectionBubble : Form
    {
        private const int BaseW = 108;
        private const int BaseH = 34;

        private readonly Timer _life;
        private int _lifeLeft;
        private bool _hover;

        public event Action TranslateClicked;
        public event Action CloseRequested;

        public SelectionBubble()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(32, 34, 44);
            Size = new Size(BaseW, BaseH);
            Cursor = Cursors.Hand;

            _life = new Timer();
            _life.Interval = 100;
            _life.Tick += OnLifeTick;

            Region = RoundRegion(Width, Height, 10);
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

        private static Region RoundRegion(int w, int h, int r)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(w - r * 2 - 1, 0, r * 2, r * 2, 270, 90);
            path.AddArc(w - r * 2 - 1, h - r * 2 - 1, r * 2, r * 2, 0, 90);
            path.AddArc(0, h - r * 2 - 1, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }

        /// <summary>在选区旁边弹出浮标。rect 为选区屏幕矩形（可能为 0），mx/my 为最后一击的鼠标位置。</summary>
        public void ShowNear(int selX, int selY, int selW, int selH, int mx, int my, int lifeMs)
        {
            int x;
            int y;
            if (selW > 0 && selH > 0)
            {
                x = selX + Math.Min(selW, 40);
                y = selY + selH + 10;
            }
            else
            {
                x = mx + 14;
                y = my + 16;
            }

            Rectangle scr = Screen.FromPoint(new Point(Math.Max(0, mx), Math.Max(0, my))).WorkingArea;
            if (x + BaseW > scr.Right - 8) x = scr.Right - BaseW - 8;
            if (y + BaseH > scr.Bottom - 8) y = (selH > 0 ? selY - BaseH - 8 : my - BaseH - 14);
            if (x < scr.Left + 8) x = scr.Left + 8;
            if (y < scr.Top + 8) y = scr.Top + 8;

            Location = new Point(x, y);
            _lifeLeft = Math.Max(1200, lifeMs);
            _hover = false;
            if (!Visible) Show();
            else
            {
                BringToFront();
                Invalidate();
            }
            _life.Start();
        }

        public void HideBubble()
        {
            try { _life.Stop(); } catch { }
            if (Visible) Hide();
        }

        private void OnLifeTick(object sender, EventArgs e)
        {
            if (_hover) return;
            _lifeLeft -= _life.Interval;
            if (_lifeLeft <= 0)
            {
                _life.Stop();
                Action handler = CloseRequested;
                if (handler != null) handler();
                HideBubble();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Log.Write("selection bubble clicked");
            HideBubble();
            Action handler = TranslateClicked;
            if (handler != null) handler();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color bg1 = _hover ? Color.FromArgb(72, 110, 235) : Color.FromArgb(52, 84, 210);
            Color bg2 = _hover ? Color.FromArgb(96, 74, 220) : Color.FromArgb(74, 58, 190);
            Rectangle full = new Rectangle(0, 0, Width, Height);
            using (LinearGradientBrush b = new LinearGradientBrush(full, bg1, bg2, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(b, full);
            }

            using (Font f = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold))
            using (SolidBrush br = new SolidBrush(Color.White))
            {
                StringFormat sf = new StringFormat();
                sf.LineAlignment = StringAlignment.Center;
                sf.Alignment = StringAlignment.Center;
                g.DrawString("译 划词翻译", f, br, new RectangleF(0, 0, Width, Height), sf);
                sf.Dispose();
            }
        }

        private void InitializeComponent()
        {
        }
    }
}
