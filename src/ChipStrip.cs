using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InputPolish
{
    /// <summary>
    /// 单行横向滚动的提示词芯片条。
    /// 为什么不用 FlowLayoutPanel：这里要三件它做不到的事——
    ///   1) 绝不换行（宽度不够就横向滚动，超长标签自动省略）；
    ///   2) 每个芯片右侧固定挂一个铅笔（点它编辑这条提示词）；
    ///   3) 末尾固定一个加号（点它新增自定义提示词）。
    /// 滚轮用 IMessageFilter 自己接（WinForms 的滚轮只发给有焦点的控件，这里不抢焦点）。
    /// </summary>
    public class ChipStrip : Control, IMessageFilter
    {
        private readonly List<PromptItem> _items = new List<PromptItem>();
        private readonly List<Rectangle> _bodies = new List<Rectangle>();
        private readonly List<Rectangle> _pens = new List<Rectangle>();
        private Rectangle _plus = Rectangle.Empty;   // 末尾的加号：钉在右侧，不随滚动跑掉
        private int _contentRight;
        private int _pinRight;      // 芯片可视区右边界（给钉住的加号留位）
        private int _selected;
        private int _scroll;
        private int _hover = -1;      // -1 无；>=0 第 i 个芯片；-2 加号
        private int _pressed = -1;
        private bool _dragging;
        private int _downX;
        private int _downScroll;
        private bool _filterOn;
        private float _dpi = 1f;

        /// <summary>选中项变化（-1 表示没有）。</summary>
        public event Action<int> SelectionChanged;
        /// <summary>点了某个芯片的铅笔。</summary>
        public event Action<int> EditRequested;
        /// <summary>点了末尾的加号。</summary>
        public event Action AddRequested;

        public ChipStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Height = 36;
            TabStop = false;
        }

        public int SelectedIndex { get { return _selected; } }
        public PromptItem SelectedItem
        {
            get
            {
                if (_selected < 0 || _selected >= _items.Count) return null;
                return _items[_selected];
            }
        }

        public void SetItems(PromptItem[] items, string selectedId)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            _selected = 0;
            if (selectedId != null && selectedId.Length > 0)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (string.Equals(_items[i].Id, selectedId, StringComparison.Ordinal)) { _selected = i; break; }
                }
            }
            if (_items.Count == 0) _selected = -1;
            _scroll = 0;
            MeasureChips();
            ScrollIntoView(_selected);
            Invalidate();
        }

        public void SelectIndex(int idx, bool notify)
        {
            if (idx < 0 || idx >= _items.Count || idx == _selected) return;
            _selected = idx;
            ScrollIntoView(idx);
            Invalidate();
            if (notify && SelectionChanged != null) SelectionChanged(idx);
        }

        /// <summary>把第 idx 个芯片滚进可视区。</summary>
        public void ScrollIntoView(int idx)
        {
            if (idx < 0 || idx >= _bodies.Count) return;
            Rectangle r = _bodies[idx];
            int view = _pinRight > 0 ? _pinRight : Width - S(4);
            if (r.Left - _scroll < 0) _scroll = r.Left;
            else if (r.Right - _scroll > view) _scroll = r.Right - view;
            ClampScroll();
        }

        public void ScrollBy(int dx)
        {
            _scroll += dx;
            ClampScroll();
            Invalidate();
        }

        private void ClampScroll()
        {
            int view = _pinRight > 0 ? _pinRight : Width - S(4);
            int max = Math.Max(0, _contentRight - view);
            if (_scroll > max) _scroll = max;
            if (_scroll < 0) _scroll = 0;
        }

        private int S(int v)
        {
            return (int)Math.Round(v * _dpi);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                using (Graphics g = CreateGraphics()) _dpi = g.DpiX / 96f;
            }
            catch { _dpi = 1f; }
            if (_dpi < 1f) _dpi = 1f;
            if (_dpi > 3f) _dpi = 3f;
            if (!_filterOn) { Application.AddMessageFilter(this); _filterOn = true; }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_filterOn) { Application.RemoveMessageFilter(this); _filterOn = false; }
            base.OnHandleDestroyed(e);
        }

        /// <summary>滚轮转发：光标在这个控件上就把滚轮横过来用。</summary>
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A) return false;             // WM_MOUSEWHEEL
            if (!IsHandleCreated || !Visible || IsDisposed) return false;
            try
            {
                if (!RectangleToScreen(ClientRectangle).Contains(Cursor.Position)) return false;
            }
            catch { return false; }
            int raw = (int)((long)m.WParam >> 16);
            short delta = (short)raw;
            ScrollBy(delta > 0 ? -S(52) : S(52));
            return true;
        }

        // ---------- 绘制 ----------

        /// <summary>算一遍所有芯片的矩形（不画）。滚动与命中都要用，所以和绘制分开。</summary>
        private void MeasureChips()
        {
            int h = S(30);
            int y = (Height - h) / 2;
            int x = S(2) - _scroll;

            _bodies.Clear();
            _pens.Clear();

            for (int i = 0; i < _items.Count; i++)
            {
                PromptItem it = _items[i];
                string label = it == null || it.Label == null ? "" : it.Label;
                Size ts = TextRenderer.MeasureText(label, Font, new Size(S(600), h), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int bodyW = Math.Min(ts.Width + S(22), S(132));
                if (bodyW < S(48)) bodyW = S(48);
                int penW = S(22);
                _bodies.Add(new Rectangle(x, y, bodyW, h));
                _pens.Add(new Rectangle(x + bodyW, y, penW, h));
                x += bodyW + penW + S(4);
            }

            // 加号永远钉在右边缘（不随横向滚动跑掉），芯片区在它左侧收住
            int plusW = S(26);
            _plus = new Rectangle(Width - plusW - S(2), y, plusW, h);
            _pinRight = _plus.Left - S(4);
            _contentRight = x + S(2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

            MeasureChips();

            // 芯片在钉住的加号左侧收住，别画到加号底下去
            Region old = g.Clip;
            g.SetClip(new Rectangle(0, 0, Math.Max(0, _plus.Left - S(2)), Height));
            for (int i = 0; i < _items.Count; i++)
            {
                PromptItem it = _items[i];
                string label = it == null || it.Label == null ? "" : it.Label;
                Rectangle body = _bodies[i];
                Rectangle pen = _pens[i];
                if (body.Right > 0 && body.Left < _plus.Left) DrawChip(g, i, body, pen, label);
            }
            g.Clip = old;

            if (_plus.Right > 0 && _plus.Left < Width) DrawPlus(g, _plus);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            MeasureChips();
            ScrollIntoView(_selected);
            Invalidate();
        }

        private void DrawChip(Graphics g, int idx, Rectangle body, Rectangle pen, string label)
        {
            bool on = idx == _selected;
            bool hot = idx == _hover;
            Color fill = on ? Color.FromArgb(91, 109, 255) : (hot ? Color.FromArgb(234, 237, 255) : Color.FromArgb(243, 244, 249));
            Color border = on ? Color.FromArgb(91, 109, 255) : Color.FromArgb(226, 228, 238);
            Color text = on ? Color.White : Color.FromArgb(52, 56, 72);
            Color ink = on ? Color.White : Color.FromArgb(120, 126, 148);

            GraphicsPath path = Round(body, S(9));
            using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
            using (Pen p = new Pen(border, 1f)) g.DrawPath(p, path);
            path.Dispose();

            Rectangle textRect = new Rectangle(body.Left + S(11), body.Top, body.Width - S(18), body.Height);
            TextRenderer.DrawText(g, label, Font, textRect, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (hot || on) DrawPencil(g, pen, ink);
            else DrawPencil(g, pen, Color.FromArgb(178, 182, 196));
        }

        private void DrawPlus(Graphics g, Rectangle r)
        {
            bool hot = _hover == -2;
            Rectangle box = new Rectangle(r.Left, r.Top, r.Width, r.Height);
            GraphicsPath path = Round(box, S(9));
            using (SolidBrush b = new SolidBrush(hot ? Color.FromArgb(234, 237, 255) : Color.FromArgb(248, 249, 252))) g.FillPath(b, path);
            using (Pen p = new Pen(hot ? Color.FromArgb(120, 132, 255) : Color.FromArgb(214, 217, 228), 1f))
            {
                p.DashStyle = DashStyle.Dash;
                g.DrawPath(p, path);
            }
            path.Dispose();
            Color ink = hot ? Color.FromArgb(91, 109, 255) : Color.FromArgb(130, 136, 158);
            int side = S(14);
            Icons.Draw(g, "plus",
                new RectangleF(box.Left + (box.Width - side) / 2f, box.Top + (box.Height - side) / 2f, side, side), ink);
        }

        /// <summary>
        /// 芯片右侧的小笔：直接用共享 pen 图标（24 网格矢量，和 exe 里其它图标同一套）。
        /// 以前是自绘的一支 45° 铅笔，形状和其它图标不是一个家族。
        /// </summary>
        private void DrawPencil(Graphics g, Rectangle box, Color c)
        {
            float side = Math.Min(box.Width, box.Height) * 0.64f;
            Icons.Draw(g, "pen",
                new RectangleF(box.Left + (box.Width - side) / 2f, box.Top + (box.Height - side) / 2f, side, side), c);
        }

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d < 2) d = 2;
            Rectangle a = new Rectangle(r.Left, r.Top, d, d);
            Rectangle b = new Rectangle(r.Right - d, r.Top, d, d);
            Rectangle c = new Rectangle(r.Left, r.Bottom - d, d, d);
            Rectangle e = new Rectangle(r.Right - d, r.Bottom - d, d, d);
            p.AddArc(a, 180, 90);
            p.AddArc(b, 270, 90);
            p.AddArc(e, 0, 90);
            p.AddArc(c, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------- 交互 ----------

        private int HitTest(Point pt, out bool onPen)
        {
            onPen = false;
            // 加号钉在右边缘、盖在芯片之上，必须优先命中；否则末尾芯片压在下面的铅笔会把加号吃掉。
            if (_plus.Contains(pt)) return -2;
            int limit = _pinRight > 0 ? _pinRight : Width;
            if (pt.X >= limit) return -1;
            for (int i = 0; i < _bodies.Count; i++)
            {
                Rectangle body = _bodies[i];
                if (body.Left >= limit) continue;                  // 已被裁掉，不可点
                Rectangle pen = _pens[i];
                if (pen.Right <= limit && pen.Contains(pt)) { onPen = true; return i; }
                if (body.Right <= limit && body.Contains(pt)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_pressed != -1 && (e.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                if (!_dragging && Math.Abs(e.X - _downX) > S(5)) _dragging = true;
                if (_dragging)
                {
                    _scroll = _downScroll - (e.X - _downX);
                    ClampScroll();
                    Invalidate();
                    return;
                }
            }
            bool onPen;
            int hit = HitTest(e.Location, out onPen);
            if (hit != _hover)
            {
                _hover = hit;
                Cursor = hit >= 0 || hit == -2 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            else
            {
                Cursor = hit >= 0 || hit == -2 ? Cursors.Hand : Cursors.Default;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            bool onPen;
            int hit = HitTest(e.Location, out onPen);
            _pressed = onPen ? -100 - hit : hit;
            _dragging = false;
            _downX = e.X;
            _downScroll = _scroll;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            bool wasDrag = _dragging;
            _dragging = false;
            int want = _pressed;
            _pressed = -1;
            if (wasDrag) return;

            bool onPen;
            int hit = HitTest(e.Location, out onPen);
            if (want == -2)
            {
                if (hit == -2 && AddRequested != null) AddRequested();
                return;
            }
            if (want <= -100)
            {
                int idx = -100 - want;
                if (hit == idx && onPen && EditRequested != null) EditRequested(idx);
                else if (hit == idx) SelectIndex(idx, true);
                return;
            }
            if (want >= 0)
            {
                if (hit == want)
                {
                    if (onPen && EditRequested != null) EditRequested(want);
                    else SelectIndex(want, true);
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) { SelectIndex(_selected - 1, true); e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { SelectIndex(_selected + 1, true); e.Handled = true; }
            else if (e.KeyCode == Keys.Apps && EditRequested != null && _selected >= 0) { EditRequested(_selected); e.Handled = true; }
        }
    }
}
