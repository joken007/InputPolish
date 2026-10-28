using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace InputPolish
{
    public class BiPara
    {
        public string Src = "";
        public string Dst = "";
        public string Error = "";
    }

    /// <summary>
    /// 双语阅读窗：左边没有、上下对照 —— 段落原文（灰）和译文（黑）交错排列，
    /// 可以切「只看原文 / 只看译文 / 对照」，可复制整篇译文。
    /// 翻译过程中逐段追加，不阻塞阅读。
    /// </summary>
    public class ReadPane : Form
    {
        private const int ModeBoth = 0;
        private const int ModeSrc = 1;
        private const int ModeDst = 2;

        private static ReadPane _current;

        private readonly List<BiPara> _items = new List<BiPara>();
        private RichTextBox _box;
        private Label _status;
        private Label _btnBoth;
        private Label _btnSrc;
        private Label _btnDst;
        private int _mode = ModeBoth;
        private float _fontSize = 11f;
        private string _pageTitle = "";
        private string _targetLabel = "";
        private bool _autoScroll = true;

        public ReadPane()
        {
            Text = "InputPolish 网页翻译";
            ClientSize = new Size(820, 640);
            MinimumSize = new Size(520, 320);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = true;
            KeyPreview = true;

            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 42;
            bar.BackColor = Color.FromArgb(248, 249, 252);
            bar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Color.FromArgb(228, 232, 242)))
                {
                    e.Graphics.DrawLine(p, 0, bar.Height - 1, bar.Width, bar.Height - 1);
                }
            };
            Controls.Add(bar);

            _btnBoth = MakeModeButton(bar, "对照", 10);
            _btnSrc = MakeModeButton(bar, "原文", 78);
            _btnDst = MakeModeButton(bar, "译文", 146);

            Label minus = MakeToolButton(bar, "A-", 220, 40);
            minus.Click += delegate { ChangeFont(-1f); };
            Label plus = MakeToolButton(bar, "A+", 264, 40);
            plus.Click += delegate { ChangeFont(1f); };

            Label copy = MakeToolButton(bar, "复制译文", 320, 76);
            copy.Click += delegate { CopyAll(); };

            _status = new Label();
            _status.AutoSize = false;
            _status.Dock = DockStyle.Right;
            _status.Width = 240;
            _status.TextAlign = ContentAlignment.MiddleRight;
            _status.Padding = new Padding(0, 0, 12, 0);
            _status.ForeColor = Color.FromArgb(110, 118, 136);
            bar.Controls.Add(_status);

            _box = new RichTextBox();
            _box.Dock = DockStyle.Fill;
            _box.ReadOnly = true;
            _box.BorderStyle = BorderStyle.None;
            _box.BackColor = Color.White;
            _box.WordWrap = true;
            _box.ScrollBars = RichTextBoxScrollBars.Vertical;
            _box.DetectUrls = false;
            _box.Font = new Font("Microsoft YaHei UI", _fontSize, FontStyle.Regular, GraphicsUnit.Point);
            _box.Padding = new Padding(16);
            _box.Margin = new Padding(0);
            Controls.Add(_box);
            _box.BringToFront();

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                if (e.Control && e.KeyCode == Keys.D1) SetMode(ModeBoth);
                if (e.Control && e.KeyCode == Keys.D2) SetMode(ModeSrc);
                if (e.Control && e.KeyCode == Keys.D3) SetMode(ModeDst);
            };

            SetMode(ModeBoth);
        }

        private Label MakeModeButton(Panel bar, string text, int x)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Size = new Size(62, 26);
            l.Location = new Point(x, 8);
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Cursor = Cursors.Hand;
            l.Tag = "mode";
            bar.Controls.Add(l);
            l.Click += delegate
            {
                if (text == "对照") SetMode(ModeBoth);
                else if (text == "原文") SetMode(ModeSrc);
                else SetMode(ModeDst);
            };
            return l;
        }

        private Label MakeToolButton(Panel bar, string text, int x, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Size = new Size(w, 26);
            l.Location = new Point(x, 8);
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Cursor = Cursors.Hand;
            l.ForeColor = Color.FromArgb(70, 78, 96);
            l.BackColor = Color.FromArgb(238, 241, 248);
            bar.Controls.Add(l);
            return l;
        }

        private void SetMode(int mode)
        {
            _mode = mode;
            StyleModeButton(_btnBoth, mode == ModeBoth);
            StyleModeButton(_btnSrc, mode == ModeSrc);
            StyleModeButton(_btnDst, mode == ModeDst);
            Render(false);
        }

        private void StyleModeButton(Label l, bool active)
        {
            if (active)
            {
                l.BackColor = Color.FromArgb(79, 124, 255);
                l.ForeColor = Color.White;
            }
            else
            {
                l.BackColor = Color.FromArgb(238, 241, 248);
                l.ForeColor = Color.FromArgb(70, 78, 96);
            }
        }

        private void ChangeFont(float delta)
        {
            _fontSize = Math.Max(8f, Math.Min(20f, _fontSize + delta));
            _box.Font = new Font("Microsoft YaHei UI", _fontSize, FontStyle.Regular, GraphicsUnit.Point);
            Render(false);
        }

        private void CopyAll()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Dst.Length == 0) continue;
                    if (sb.Length > 0) sb.Append("\r\n\r\n");
                    sb.Append(_items[i].Dst);
                }
                ClipUtil.SetText(sb.ToString());
                _status.Text = "译文已复制（" + _items.Count + " 段）";
            }
            catch (Exception ex)
            {
                _status.Text = "复制失败：" + ex.Message;
            }
        }

        public void BeginDocument(string pageTitle, string targetLabel, int expected)
        {
            _pageTitle = pageTitle;
            _targetLabel = targetLabel;
            _items.Clear();
            // 预置占位槽：整页翻译是多线程乱序回填的，必须先按段落数把位置留好，
            // 否则完成的顺序会打乱段落对应关系。
            for (int i = 0; i < expected; i++) _items.Add(new BiPara());
            Text = "InputPolish 网页翻译 → " + targetLabel + " · " + TrimTitle(pageTitle);
            SetStatus("准备翻译 0/" + expected + " 段…");
            Render(false);
        }

        private static string TrimTitle(string t)
        {
            if (t == null) return "";
            string s = t.Trim();
            if (s.Length > 60) s = s.Substring(0, 60) + "…";
            return s;
        }

        public void AddItem(BiPara item)
        {
            _items.Add(item);
            ScheduleRender();
        }

        /// <summary>按槽位写入（整页翻译多线程乱序回填用）。</summary>
        public void SetItem(int idx, BiPara item)
        {
            if (idx < 0) return;
            while (_items.Count <= idx) _items.Add(new BiPara());
            _items[idx] = item;
            ScheduleRender();
        }

        private bool _renderPending;
        private Timer _renderTimer;

        /// <summary>
        /// 渲染节流：整页翻译一次回填上百段，每段都全量重排会明显卡顿，
        /// 这里合并成最多每 300ms 刷一次。
        /// </summary>
        private void ScheduleRender()
        {
            if (_renderPending) return;
            _renderPending = true;
            if (_renderTimer == null)
            {
                _renderTimer = new Timer();
                _renderTimer.Interval = 300;
                _renderTimer.Tick += delegate
                {
                    _renderTimer.Stop();
                    _renderPending = false;
                    Render(true);
                };
            }
            _renderTimer.Start();
        }

        public void FlushRender()
        {
            if (_renderTimer != null) _renderTimer.Stop();
            _renderPending = false;
            Render(true);
        }

        public void AddItems(List<BiPara> items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++) _items.Add(items[i]);
            Render(true);
        }

        public void SetStatus(string text)
        {
            if (_status == null) return;
            _status.Text = text;
        }

        public void SetProgress(int done, int total)
        {
            SetStatus("翻译中 " + done + "/" + total + " 段…");
        }

        private void Render(bool keepBottom)
        {
            if (_box == null) return;

            bool atBottom = _autoScroll;
            if (keepBottom)
            {
                try
                {
                    atBottom = _box.TextLength == 0 ||
                        (_box.GetPositionFromCharIndex(_box.TextLength) .Y - _box.GetPositionFromCharIndex(0).Y)
                        <= _box.ClientSize.Height;
                }
                catch { atBottom = true; }
            }

            Color srcColor = Color.FromArgb(120, 128, 146);
            Color dstColor = Color.FromArgb(26, 30, 40);
            Color errColor = Color.FromArgb(200, 60, 60);

            _box.SuspendLayout();
            try
            {
                _box.Clear();

                for (int i = 0; i < _items.Count; i++)
                {
                    BiPara it = _items[i];

                    if (_mode == ModeSrc || _mode == ModeBoth)
                    {
                        Append(it.Src, srcColor, _fontSize - 1f, false);
                        if (_mode == ModeBoth) Append("\n", srcColor, _fontSize - 1f, false);
                    }

                    if (_mode == ModeDst || _mode == ModeBoth)
                    {
                        if (it.Dst.Length > 0)
                        {
                            Append(it.Dst, dstColor, _fontSize, false);
                        }
                        else if (it.Error.Length > 0)
                        {
                            Append("[翻译失败] " + it.Error, errColor, _fontSize, false);
                        }
                        else
                        {
                            Append("…", srcColor, _fontSize, false);
                        }
                    }

                    if (i < _items.Count - 1) Append("\n\n", dstColor, _fontSize, false);
                }
            }
            finally
            {
                _box.ResumeLayout();
            }

            if (atBottom)
            {
                _box.SelectionStart = _box.TextLength;
                _box.SelectionLength = 0;
                _box.ScrollToCaret();
            }
        }

        private void Append(string text, Color color, float size, bool bold)
        {
            if (text == null || text.Length == 0) return;
            int start = _box.TextLength;
            _box.AppendText(text);
            _box.Select(start, text.Length);
            _box.SelectionColor = color;
            _box.SelectionFont = new Font("Microsoft YaHei UI", size,
                bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            _box.Select(_box.TextLength, 0);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_current == this) _current = null;
            base.OnFormClosed(e);
        }

        /// <summary>新建或复用阅读窗（必须在 UI 线程调用）。</summary>
        public static ReadPane Present(string pageTitle, string targetLabel, int expected)
        {
            ReadPane pane = _current;
            if (pane == null || pane.IsDisposed)
            {
                pane = new ReadPane();
                _current = pane;
                pane.Show();
            }
            else
            {
                if (!pane.Visible) pane.Show();
                pane.Activate();
            }
            // 防御：窗口曾被最小化（rect 跑到 -32000）时，重新翻译会看不见内容
            if (pane.WindowState != FormWindowState.Normal) pane.WindowState = FormWindowState.Normal;
            pane.BringToFront();
            pane.BeginDocument(pageTitle, targetLabel, expected);
            Log.Write("readpane shown title=[" + Log.Preview(pageTitle) + "] paras=" + expected + " hwnd=" + pane.Handle.ToInt64());
            return pane;
        }

        public static ReadPane Current
        {
            get { return _current; }
        }
    }
}
