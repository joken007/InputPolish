using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InputPolish
{
    public class Toast : Form
    {
        private static Toast _current;
        private System.Windows.Forms.Timer _timer;
        private string _text = "";
        private Color _accent = Color.FromArgb(79, 124, 255);

        private Toast(string text, Color accent, int ms, Point anchor)
        {
            _text = text;
            _accent = accent;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(22, 24, 30);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            Font f = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Font = f;

            Size measured = MeasureText(text, f);
            int w = Math.Min(460, Math.Max(180, measured.Width + 34));
            int h = Math.Max(40, Math.Min(120, measured.Height + 18));
            ClientSize = new Size(w, h);

            Rectangle wa = Screen.FromPoint(anchor).WorkingArea;
            int x = anchor.X + 6;
            int y = anchor.Y + 18;
            if (x + w > wa.Right) x = wa.Right - w - 8;
            if (x < wa.Left) x = wa.Left + 8;
            if (y + h > wa.Bottom) y = anchor.Y - h - 12;
            if (y < wa.Top) y = wa.Top + 8;
            Location = new Point(x, y);

            Region = new Region(RoundPath(new RectangleF(0, 0, w, h), 10f));

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = Math.Max(700, ms);
            _timer.Tick += delegate { _timer.Stop(); Close(); };
            _timer.Start();
        }

        private static Size MeasureText(string text, Font f)
        {
            try
            {
                using (Bitmap bmp = new Bitmap(1, 1))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        SizeF sz = g.MeasureString(text, f, 400);
                        return new Size((int)Math.Ceiling(sz.Width), (int)Math.Ceiling(sz.Height));
                    }
                }
            }
            catch
            {
                return new Size(220, 24);
            }
        }

        public static GraphicsPath RoundPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            if (d <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
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

            using (GraphicsPath p = RoundPath(new RectangleF(0, 0, Width, Height), 10f))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, Width, Height),
                    Color.FromArgb(246, 26, 29, 38), Color.FromArgb(246, 38, 42, 56), 0f))
                {
                    g.FillPath(br, p);
                }
                using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255)))
                {
                    g.DrawPath(pen, p);
                }
            }

            using (SolidBrush accent = new SolidBrush(_accent))
            {
                g.FillRectangle(accent, 0, 0, 4, Height);
            }

            using (SolidBrush tb = new SolidBrush(Color.FromArgb(236, 240, 247)))
            {
                RectangleF textRect = new RectangleF(16, 6, Width - 26, Height - 12);
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(_text, Font, tb, textRect, sf);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (_current == this) _current = null;
        }

        public static Point CaretAnchor()
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                uint pid;
                uint tid = Native.GetWindowThreadProcessId(fg, out pid);
                Native.GUITHREADINFO gti = new Native.GUITHREADINFO();
                gti.cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO));
                if (Native.GetGUIThreadInfo(tid, ref gti) && gti.hwndCaret != IntPtr.Zero)
                {
                    Native.POINT p = new Native.POINT();
                    p.X = gti.rcCaret.Left;
                    p.Y = gti.rcCaret.Bottom;
                    if (gti.rcCaret.Bottom > gti.rcCaret.Top)
                    {
                        if (Native.ClientToScreen(gti.hwndCaret, ref p))
                        {
                            Point pt = new Point(p.X, p.Y);
                            // 有些程序（Chromium 等）报的 caret 坐标是垃圾值，落到屏幕外时退回鼠标位置
                            if (SystemInformation.VirtualScreen.Contains(pt)) return pt;
                        }
                    }
                }
            }
            catch
            {
            }
            return Cursor.Position;
        }

        public static void ShowNotice(string text, bool isError, int ms)
        {
            try
            {
                if (_current != null)
                {
                    try { _current.Close(); }
                    catch { }
                    _current = null;
                }
                Color accent = isError ? Color.FromArgb(239, 68, 68) : Color.FromArgb(34, 197, 94);
                Point anchor = CaretAnchor();
                Toast t = new Toast(text, accent, ms, anchor);
                _current = t;
                t.Show();
                Log.Write("toast show [" + text + "] anchor=" + anchor.X + "," + anchor.Y + " size=" + t.Width + "x" + t.Height + " hwnd=" + t.Handle.ToInt64() + " visible=" + t.Visible);
            }
            catch (Exception ex)
            {
                Log.Write("toast failed: " + ex.Message);
            }
        }
    }
}
