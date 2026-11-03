using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InputPolish
{
    public enum WidgetState
    {
        Idle = 0,
        Busy = 1,
        Success = 2,
        Error = 3,
        Paused = 4
    }

    public class FloatWidget : Form
    {
        public event Action ToggleEnabledRequested;
        public event Action SettingsRequested;
        public event Action UndoRequested;
        public event Action PageTranslateRequested;
        public event Action PageSummaryRequested;
        public event Action ExitRequested;
        public event Action PositionChanged;

        private const float BaseH = 58f;
        private const float BaseCollapsedW = 58f;
        // 6 个「图标 + 中文短标签」按钮：64 + 6*64 + 5*6 + 右侧 8 边距
        private const float BaseExpandedW = 486f;

        private float _scale = 1f;
        private int _h;
        private int _collapsedW;
        private int _expandedW;

        private bool _expanded;
        private bool _pinned;
        private int _btnDown = -1;
        private int _awayTicks;
        private float _curW;
        private bool _animating;

        private bool _dragging;
        private bool _moved;
        private Point _dragStartCursor;
        private Point _dragStartLoc;
        private int _homeX;
        private int _homeY;

        private WidgetState _state = WidgetState.Idle;
        private string _status = "";
        private float _spin;
        private bool _enabled = true;
        private int _hoverIdx = -1;

        private Timer _tick;
        private ToolTip _tip;
        private ContextMenuStrip _menu;

        private Font _btnFont;
        private Font _hintFont;

        public FloatWidget()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(26, 28, 36);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            _menu = new ContextMenuStrip();
            _menu.ImageScalingSize = new Size(16, 16);
            ToolStripMenuItem miSettings = new ToolStripMenuItem("设置…", null, delegate { RaiseSettings(); });
            miSettings.Image = Icons.MenuImage("settings");
            _menu.Items.Add(miSettings);
            ToolStripMenuItem miUndo = new ToolStripMenuItem("撤销上一次润色", null, delegate { RaiseUndo(); });
            miUndo.Image = Icons.MenuImage("undo");
            _menu.Items.Add(miUndo);
            _menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem miDir = new ToolStripMenuItem("打开配置目录", null, delegate { OpenFolder(Config.Dir); });
            miDir.Image = Icons.MenuImage("outline");
            _menu.Items.Add(miDir);
            ToolStripMenuItem miLog = new ToolStripMenuItem("打开日志", null, delegate { OpenLog(); });
            miLog.Image = Icons.MenuImage("note");
            _menu.Items.Add(miLog);
            _menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem miExit = new ToolStripMenuItem("退出", null, delegate { RaiseExit(); });
            miExit.Image = Icons.MenuImage("quit");
            _menu.Items.Add(miExit);
            _menu.Closed += delegate { _menuOpen = false; };
            _menu.Opened += delegate { _menuOpen = true; };

            _tip = new ToolTip();
            _tip.SetToolTip(this, "三连空格 = AI 结构化润色\n单击展开操作 · 拖动可移动 · 右键更多");
            _tip.InitialDelay = 500;

            _tick = new Timer();
            _tick.Interval = 40;
            _tick.Tick += delegate { OnTick(); };
        }

        private bool _menuOpen;

        public void ApplyScale()
        {
            using (Graphics g = CreateGraphics())
            {
                _scale = g.DpiX / 96f;
            }
            if (_scale < 0.75f) _scale = 0.75f;
            if (_scale > 3f) _scale = 3f;

            _h = S(BaseH);
            _collapsedW = S(BaseCollapsedW);
            _expandedW = S(BaseExpandedW);

            _btnFont = new Font("Microsoft YaHei UI", 12.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
            _hintFont = new Font("Microsoft YaHei UI", 8f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);

            if (!_expanded)
            {
                ClientSize = new Size(_collapsedW, _h);
                _curW = _collapsedW;
            }
            UpdateRegion();
        }

        private int S(float v)
        {
            return (int)Math.Round(v * _scale);
        }

        // 四角星点缀，与程序图标呼应，暗示「AI 润色」
        private static void DrawSparkle(Graphics g, float cx, float cy, float r, int alpha)
        {
            using (System.Drawing.Drawing2D.GraphicsPath sp = new System.Drawing.Drawing2D.GraphicsPath())
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
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                {
                    g.FillPath(sb, sp);
                }
            }
        }

        public void SetHome(int x, int y)
        {
            _homeX = x;
            _homeY = y;
            if (!_expanded) Location = new Point(x, y);
        }

        public Point Home
        {
            get { return new Point(_homeX, _homeY); }
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled && _state == WidgetState.Idle) _state = WidgetState.Paused;
            if (enabled && _state == WidgetState.Paused) _state = WidgetState.Idle;
            Invalidate();
        }

        public void PostState(WidgetState state, string status)
        {
            if (!IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate { ApplyState(state, status); });
            }
            catch
            {
            }
        }

        private void ApplyState(WidgetState state, string status)
        {
            _state = state;
            _status = status == null ? "" : status;
            if (state == WidgetState.Busy)
            {
                _tick.Start();
            }
            Invalidate();
        }

        private void RaiseButton(int idx)
        {
            Log.Write("widget button " + idx + " clicked");
            if (idx == 0) RaiseToggle();
            else if (idx == 1) RaisePageTranslate();
            else if (idx == 2) RaisePageSummary();
            else if (idx == 3) RaiseSettings();
            else if (idx == 4) RaiseUndo();
            else RaiseExit();
        }

        private void RaisePageTranslate()
        {
            Action a = PageTranslateRequested;
            if (a != null) a();
        }

        private void RaisePageSummary()
        {
            Action a = PageSummaryRequested;
            if (a != null) a();
        }

        private void RaiseSettings()
        {
            Action a = SettingsRequested;
            if (a != null) a();
        }

        private void RaiseUndo()
        {
            Action a = UndoRequested;
            if (a != null) a();
        }

        private void RaiseExit()
        {
            Action a = ExitRequested;
            if (a != null) a();
        }

        private void RaiseToggle()
        {
            Action a = ToggleEnabledRequested;
            if (a != null) a();
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

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath p = Toast.RoundPath(new RectangleF(0, 0, Width, Height), Math.Min(Height / 2f, Width / 2f)))
            {
                Region old = Region;
                Region = new Region(p);
                if (old != null) old.Dispose();
            }
        }

        private Rectangle ButtonRect(int i)
        {
            int x0 = S(64);
            int bw = S(64);
            int gap = S(6);
            int bh = S(34);
            int y = (Height - bh) / 2;
            return new Rectangle(x0 + i * (bw + gap), y, bw, bh);
        }

        private string ButtonLabel(int i)
        {
            if (i == 0) return _enabled ? "暂停" : "启用";
            if (i == 1) return "译页";
            if (i == 2) return "总结";
            if (i == 3) return "设置";
            if (i == 4) return "撤销";
            return "退出";
        }

        // 展开胶囊的图标：状态类按钮跟着开关走，其余是固定语义
        private string ButtonIcon(int i)
        {
            if (i == 0) return _enabled ? "pause" : "play";
            if (i == 1) return "translate";
            if (i == 2) return "summarize";
            if (i == 3) return "settings";
            if (i == 4) return "undo";
            return "quit";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (GraphicsPath p = Toast.RoundPath(new RectangleF(0, 0, Width, Height), Math.Min(Height / 2f, Width / 2f)))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(2, Width), Math.Max(2, Height)),
                    Color.FromArgb(247, 24, 26, 34), Color.FromArgb(247, 36, 39, 52), 12f))
                {
                    g.FillPath(br, p);
                }
                using (Pen pen = new Pen(Color.FromArgb(64, 255, 255, 255)))
                {
                    g.DrawPath(pen, p);
                }
            }

            int cx = S(29);
            int cy = Height / 2;
            int r = S(22);

            Color c1;
            Color c2;
            switch (_state)
            {
                case WidgetState.Busy:
                    c1 = Color.FromArgb(251, 176, 59);
                    c2 = Color.FromArgb(245, 110, 30);
                    break;
                case WidgetState.Success:
                    c1 = Color.FromArgb(52, 211, 153);
                    c2 = Color.FromArgb(16, 160, 90);
                    break;
                case WidgetState.Error:
                    c1 = Color.FromArgb(248, 113, 113);
                    c2 = Color.FromArgb(220, 38, 38);
                    break;
                case WidgetState.Paused:
                    c1 = Color.FromArgb(120, 128, 144);
                    c2 = Color.FromArgb(70, 76, 90);
                    break;
                default:
                    c1 = Color.FromArgb(124, 92, 255);
                    c2 = Color.FromArgb(58, 132, 255);
                    break;
            }

            Rectangle circle = new Rectangle(cx - r, cy - r, r * 2, r * 2);
            using (LinearGradientBrush br = new LinearGradientBrush(circle, c1, c2, 45f))
            {
                g.FillEllipse(br, circle);
            }
            using (System.Drawing.Drawing2D.GraphicsPath clip = new System.Drawing.Drawing2D.GraphicsPath())
            {
                clip.AddEllipse(circle);
                Region saved = g.Clip;
                g.SetClip(clip, CombineMode.Intersect);
                // 径向渐变做出玻璃光泽：左上高光自然过渡到边缘，不会出现硬边椭圆
                RectangleF gloss = new RectangleF(circle.X + r * 0.02f, circle.Y - r * 0.10f, r * 1.96f, r * 1.96f);
                using (System.Drawing.Drawing2D.GraphicsPath gp = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    gp.AddEllipse(gloss);
                    using (PathGradientBrush pb = new PathGradientBrush(gp))
                    {
                        pb.CenterPoint = new PointF(circle.X + r * 0.62f, circle.Y + r * 0.34f);
                        pb.CenterColor = Color.FromArgb(96, 255, 255, 255);
                        pb.SurroundColors = new Color[] { Color.FromArgb(0, 255, 255, 255) };
                        pb.FocusScales = new PointF(0.18f, 0.18f);
                        g.FillPath(pb, gp);
                    }
                }
                using (System.Drawing.Drawing2D.GraphicsPath gp2 = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    RectangleF low = new RectangleF(circle.X - r * 0.10f, circle.Y + r * 0.20f, r * 2.20f, r * 2.00f);
                    gp2.AddEllipse(low);
                    using (PathGradientBrush pb2 = new PathGradientBrush(gp2))
                    {
                        pb2.CenterPoint = new PointF(circle.X + r * 0.55f, circle.Y + r * 1.05f);
                        pb2.CenterColor = Color.FromArgb(44, 12, 6, 44);
                        pb2.SurroundColors = new Color[] { Color.FromArgb(0, 12, 6, 44) };
                        pb2.FocusScales = new PointF(0.20f, 0.20f);
                        g.FillPath(pb2, gp2);
                    }
                }
                g.Clip = saved;
            }
            using (Pen pen = new Pen(Color.FromArgb(86, 255, 255, 255)))
            {
                g.DrawEllipse(pen, circle);
            }

            // 球体内容全部是矢量图标：待命=brand、处理中=spinner（旋转）、成功=check、
            // 出错=warning、暂停=off。状态靠形状区分，不再塞一个汉字进去。
            if (_state == WidgetState.Busy)
            {
                float spinSize = r * 2f * 0.62f;
                Icons.Draw(g, "spinner", new RectangleF(cx - spinSize / 2f, cy - spinSize / 2f, spinSize, spinSize),
                    Color.FromArgb(240, 255, 255, 255), 2f, _spin);
            }
            else
            {
                string iconId = "brand";
                float frac = 0.60f;
                if (_state == WidgetState.Success)
                {
                    iconId = "check";
                    frac = 0.50f;
                }
                else if (_state == WidgetState.Error)
                {
                    iconId = "warning";
                    frac = 0.62f;
                }
                else if (_state == WidgetState.Paused)
                {
                    iconId = "off";
                    frac = 0.62f;
                }

                float iconSize = r * 2f * frac;
                Icons.Draw(g, iconId,
                    new RectangleF(cx - iconSize / 2f, cy - iconSize / 2f, iconSize, iconSize), Color.White);

                if (_state == WidgetState.Idle)
                {
                    // 与图标一致的四角星点缀，暗示「AI 润色」
                    DrawSparkle(g, cx + r * 0.62f, cy - r * 0.66f, r * 0.22f, 165);
                }
            }

            if (_expanded || _curW > _collapsedW + 1)
            {
                for (int i = 0; i < 6; i++)
                {
                    Rectangle br2 = ButtonRect(i);
                    if (br2.Right > Width - S(2)) continue;
                    int fillAlpha = (i == _hoverIdx) ? 40 : 16;
                    using (GraphicsPath hp = Toast.RoundPath(new RectangleF(br2.X, br2.Y, br2.Width, br2.Height), S(9)))
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(fillAlpha, 255, 255, 255)))
                    {
                        g.FillPath(hb, hp);
                    }
                    // 图标 + 中文短标签（标签是文字，不是图标；图标统一来自 IconsData）
                    Color ink = (i == _hoverIdx) ? Color.White : Color.FromArgb(228, 234, 246);
                    float iconSize = S(14);
                    string label = ButtonLabel(i);
                    Size labelSize = TextRenderer.MeasureText(label, _btnFont, new Size(S(200), br2.Height),
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    float gap = S(4);
                    float groupW = iconSize + gap + labelSize.Width;
                    float gx = br2.Left + (br2.Width - groupW) / 2f;
                    if (gx < br2.Left + S(4)) gx = br2.Left + S(4);
                    Icons.Draw(g, ButtonIcon(i),
                        new RectangleF(gx, br2.Top + (br2.Height - iconSize) / 2f, iconSize, iconSize), ink);
                    Rectangle textRect = new Rectangle((int)Math.Round(gx + iconSize + gap), br2.Top,
                        Math.Max(S(8), labelSize.Width + S(3)), br2.Height);
                    TextRenderer.DrawText(g, label, _btnFont, textRect, ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
            }
        }

        private void OnTick()
        {
            bool needPaint = false;

            float target = _expanded ? _expandedW : _collapsedW;
            if (Math.Abs(_curW - target) > 0.5f)
            {
                float step = (_expandedW - _collapsedW) / 9f;
                if (step < 8f) step = 8f;
                if (_curW < target) _curW = Math.Min(target, _curW + step);
                else _curW = Math.Max(target, _curW - step);
                int newW = (int)Math.Round(_curW);
                if (newW != Width)
                {
                    Width = newW;
                    UpdateRegion();
                }
                needPaint = true;
                _animating = true;
            }
            else if (_animating)
            {
                _animating = false;
                needPaint = true;
            }

            bool inside = Bounds.Contains(Cursor.Position);

            if (inside)
            {
                _awayTicks = 0;
                if (!_expanded) Expand();
            }
            else
            {
                if (_expanded && !_pinned && !_menuOpen)
                {
                    _awayTicks++;
                    if (_awayTicks > 6) Collapse();
                }
            }

            if (_expanded)
            {
                int idx = HitButton(Cursor.Position);
                if (idx != _hoverIdx)
                {
                    _hoverIdx = idx;
                    needPaint = true;
                }
            }
            else if (_hoverIdx != -1)
            {
                _hoverIdx = -1;
                needPaint = true;
            }

            if (_state == WidgetState.Busy)
            {
                _spin += 26f;
                if (_spin > 360f) _spin -= 360f;
                needPaint = true;
            }

            if (needPaint) Invalidate();
        }

        private void Expand()
        {
            if (_expanded) return;
            _expanded = true;
            _hoverIdx = -1;

            Rectangle wa = Screen.FromPoint(new Point(_homeX, _homeY)).WorkingArea;
            int x = _homeX;
            if (x + _expandedW > wa.Right) x = wa.Right - _expandedW;
            if (x < wa.Left) x = wa.Left;
            Location = new Point(x, _homeY);
            _tick.Start();
        }

        private void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            _hoverIdx = -1;
            Location = new Point(_homeX, _homeY);
            _tick.Start();
        }

        private int HitButton(Point screenPt)
        {
            Point p = PointToClient(screenPt);
            for (int i = 0; i < 6; i++)
            {
                if (ButtonRect(i).Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                _menu.Show(this, new Point(e.X, e.Y));
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (_expanded && e.X > S(58))
            {
                _btnDown = HitButton(Cursor.Position);
                return;
            }

            if (e.X <= S(58))
            {
                _dragging = true;
                _moved = false;
                _dragStartCursor = Cursor.Position;
                _dragStartLoc = Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                Point now = Cursor.Position;
                int dx = now.X - _dragStartCursor.X;
                int dy = now.Y - _dragStartCursor.Y;
                if (!_moved && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)) _moved = true;
                if (_moved)
                {
                    Location = new Point(_dragStartLoc.X + dx, _dragStartLoc.Y + dy);
                }
                return;
            }
            if (_expanded)
            {
                int idx = HitButton(Cursor.Position);
                if (idx != _hoverIdx)
                {
                    _hoverIdx = idx;
                    Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }
            }
            else if (_hoverIdx != -1)
            {
                _hoverIdx = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_btnDown >= 0)
            {
                int idx = HitButton(Cursor.Position);
                int pressed = _btnDown;
                _btnDown = -1;
                if (idx == pressed) RaiseButton(idx);
                return;
            }

            if (!_dragging) return;
            _dragging = false;

            if (_moved)
            {
                Rectangle wa = Screen.FromPoint(Location).WorkingArea;
                int x = Location.X;
                int y = Location.Y;
                if (x < wa.Left - _collapsedW + 10) x = wa.Left - _collapsedW + 10;
                if (x > wa.Right - 10) x = wa.Right - 10;
                if (y < wa.Top) y = wa.Top;
                if (y > wa.Bottom - Height) y = wa.Bottom - Height;
                Location = new Point(x, y);
                _homeX = x;
                _homeY = y;
                Cursor = Cursors.Default;
                Action a = PositionChanged;
                if (a != null) a();
                return;
            }

            _pinned = !_pinned;
            if (_pinned) Expand();
            else Collapse();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && e.X <= S(58))
            {
                RaiseSettings();
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                Log.Write("open folder failed: " + ex.Message);
            }
        }

        private void OpenLog()
        {
            try
            {
                if (Log.LogPath != null && System.IO.File.Exists(Log.LogPath))
                {
                    System.Diagnostics.Process.Start("notepad.exe", "\"" + Log.LogPath + "\"");
                }
                else
                {
                    OpenFolder(Config.Dir);
                }
            }
            catch (Exception ex)
            {
                Log.Write("open log failed: " + ex.Message);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _tick.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_tick != null) _tick.Dispose();
                if (_btnFont != null) _btnFont.Dispose();
                if (_hintFont != null) _hintFont.Dispose();
                if (_menu != null) _menu.Dispose();
                if (_tip != null) _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
