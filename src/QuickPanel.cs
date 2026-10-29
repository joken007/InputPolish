using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InputPolish
{
    /// <summary>
    /// Ctrl+` 弹出的快捷面板：总结方案 chips + 自定义提示词 + 「问这一页」输入框 + 翻译/总结动作。
    /// 这个窗口需要接收键盘输入，所以它会正常激活；失焦 / Esc 自动关闭。
    /// </summary>
    public class QuickPanel : Form
    {
        private static QuickPanel _current;

        private TextBox _prompt;
        private TextBox _ask;
        private Label _hint;
        private FlowLayoutPanel _chips;
        private bool _running;
        private string _mode = "brief";
        private readonly List<Button> _chipButtons = new List<Button>();

        public event Action<string, string> SummaryRequested;   // (prompt, mode)
        public event Action<string> AskRequested;               // question
        public event Action TranslatePageRequested;

        public QuickPanel()
        {
            Text = "InputPolish 快捷面板";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(250, 251, 254);
            Size = new Size(478, 352);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;
            Padding = new Padding(1);

            Label title = new Label();
            title.Text = "网页工具";
            title.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(34, 40, 56);
            title.AutoSize = false;
            title.Bounds = new Rectangle(14, 10, 200, 22);
            Controls.Add(title);

            Label tip = new Label();
            tip.Text = "总结 · 问答 · 翻译";
            tip.AutoSize = false;
            tip.Bounds = new Rectangle(Width - 190, 12, 130, 18);
            tip.ForeColor = Color.FromArgb(150, 158, 176);
            tip.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(tip);

            Label close = new Label();
            close.Text = "×";
            close.AutoSize = false;
            close.TextAlign = ContentAlignment.MiddleCenter;
            close.Bounds = new Rectangle(Width - 40, 8, 26, 24);
            close.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular);
            close.ForeColor = Color.FromArgb(120, 128, 148);
            close.Cursor = Cursors.Hand;
            close.Click += delegate { Hide(); };
            close.MouseEnter += delegate { close.ForeColor = Color.FromArgb(60, 70, 90); };
            close.MouseLeave += delegate { close.ForeColor = Color.FromArgb(120, 128, 148); };
            Controls.Add(close);

            Label lMode = new Label();
            lMode.Text = "总结方案";
            lMode.AutoSize = false;
            lMode.Bounds = new Rectangle(14, 34, 120, 16);
            lMode.ForeColor = Color.FromArgb(130, 138, 158);
            Controls.Add(lMode);

            _chips = new FlowLayoutPanel();
            _chips.Bounds = new Rectangle(10, 52, Width - 20, 80);
            _chips.BackColor = Color.Transparent;
            _chips.WrapContents = true;
            _chips.AutoScroll = false;
            Controls.Add(_chips);
            BuildChips();

            Label lPrompt = new Label();
            lPrompt.Text = "自定义提示词（留空就用上面选中的方案）";
            lPrompt.AutoSize = false;
            lPrompt.Bounds = new Rectangle(14, 138, Width - 30, 16);
            lPrompt.ForeColor = Color.FromArgb(130, 138, 158);
            Controls.Add(lPrompt);

            _prompt = new TextBox();
            _prompt.Multiline = true;
            _prompt.ScrollBars = ScrollBars.Vertical;
            _prompt.BorderStyle = BorderStyle.FixedSingle;
            _prompt.Bounds = new Rectangle(14, 156, Width - 30, 62);
            _prompt.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Controls.Add(_prompt);

            Label lAsk = new Label();
            lAsk.Text = "问这一页";
            lAsk.AutoSize = false;
            lAsk.Bounds = new Rectangle(14, 226, 120, 16);
            lAsk.ForeColor = Color.FromArgb(130, 138, 158);
            Controls.Add(lAsk);

            _ask = new TextBox();
            _ask.BorderStyle = BorderStyle.FixedSingle;
            _ask.Bounds = new Rectangle(14, 244, Width - 124, 26);
            _ask.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            Controls.Add(_ask);

            Button askBtn = MakeButton("问", Width - 102, 244, 74, 26);
            askBtn.Click += delegate { RunAsk(); };

            _hint = new Label();
            _hint.Text = "Ctrl+Enter 总结   ·   Alt+Enter 提问   ·   Esc 关闭";
            _hint.AutoSize = false;
            _hint.Bounds = new Rectangle(14, 276, Width - 30, 18);
            _hint.ForeColor = Color.FromArgb(130, 138, 158);
            Controls.Add(_hint);

            Button sum = MakeButton("总结本页", 14, 300, 116, 28);
            sum.Click += delegate { RunSummary(); };
            Button ask2 = MakeButton("问这一页", 140, 300, 116, 28);
            ask2.Click += delegate { RunAsk(); };
            Button tr = MakeButton("翻译本页", 266, 300, 116, 28);
            tr.Click += delegate { RunTranslate(); };
            Button cancel = MakeButton("关闭", Width - 92, 300, 74, 28);
            cancel.Click += delegate { Hide(); };

            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Hide();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter && e.Control)
                {
                    RunSummary();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Enter && e.Alt)
                {
                    RunAsk();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            Deactivate += delegate { if (!_running) Hide(); };
        }

        /// <summary>生成九个方案 chip（顺序由 PageModes 决定，UI 和逻辑只有一份来源）。</summary>
        private void BuildChips()
        {
            PageMode[] all = PageModes.All();
            for (int i = 0; i < all.Length; i++)
            {
                PageMode m = all[i];
                Button b = new Button();
                b.Text = m.Label;
                b.Tag = m.Id;
                b.AutoSize = true;
                b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                b.Padding = new Padding(8, 3, 8, 3);
                b.Margin = new Padding(3, 3, 3, 3);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Color.FromArgb(210, 218, 236);
                b.BackColor = Color.White;
                b.ForeColor = Color.FromArgb(70, 78, 100);
                b.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
                b.Cursor = Cursors.Hand;
                b.Click += delegate(object s, EventArgs e)
                {
                    Button self = s as Button;
                    if (self == null) return;
                    SelectMode(Convert.ToString(self.Tag));
                };
                _chips.Controls.Add(b);
                _chipButtons.Add(b);
            }
        }

        private void SelectMode(string id)
        {
            _mode = id;
            for (int i = 0; i < _chipButtons.Count; i++)
            {
                Button b = _chipButtons[i];
                bool on = Convert.ToString(b.Tag) == id;
                b.BackColor = on ? Color.FromArgb(91, 109, 255) : Color.White;
                b.ForeColor = on ? Color.White : Color.FromArgb(70, 78, 100);
                b.FlatAppearance.BorderColor = on ? Color.FromArgb(91, 109, 255) : Color.FromArgb(210, 218, 236);
            }
            PageMode m = PageModes.Get(id);
            _hint.Text = m.Hint + "   ·   Ctrl+Enter 总结   ·   Alt+Enter 提问";
        }

        private Button MakeButton(string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.Bounds = new Rectangle(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(210, 218, 236);
            b.BackColor = Color.White;
            b.ForeColor = Color.FromArgb(40, 48, 68);
            b.Cursor = Cursors.Hand;
            Controls.Add(b);
            return b;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(Color.FromArgb(200, 210, 232)))
            {
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(64, 96, 220)))
            {
                e.Graphics.FillRectangle(b, 0, 0, Width, 3);
            }
        }

        /// <summary>把面板挪到鼠标附近并显示出来。</summary>
        public static void ShowPanel(Config cfg, Action<string, string> onSummary, Action<string> onAsk, Action onTranslate)
        {
            if (_current == null || _current.IsDisposed) _current = new QuickPanel();
            QuickPanel p = _current;
            p._running = false;
            p._prompt.Text = "";          // 留空 = 用选中的方案；想自定义再写
            p._ask.Text = "";
            PageMode def = PageModes.Get(cfg.summaryMode);
            p.SelectMode(def.Id);
            p._hint.Text = def.Hint + "   ·   Ctrl+Enter 总结   ·   Alt+Enter 提问";
            p.SummaryRequested = onSummary;
            p.AskRequested = onAsk;
            p.TranslatePageRequested = onTranslate;
            Log.Write("quick panel shown: summaryBound=" + (onSummary != null) + " askBound=" + (onAsk != null) +
                      " translateBound=" + (onTranslate != null) + " mode=" + def.Id);

            Point m = Cursor.Position;
            Rectangle scr = Screen.FromPoint(m).WorkingArea;
            int x = m.X - p.Width / 2;
            int y = m.Y + 20;
            if (x < scr.Left + 8) x = scr.Left + 8;
            if (x + p.Width > scr.Right - 8) x = scr.Right - p.Width - 8;
            if (y + p.Height > scr.Bottom - 8) y = m.Y - p.Height - 20;
            if (y < scr.Top + 8) y = scr.Top + 8;
            p.Location = new Point(x, y);

            if (!p.Visible) p.Show();
            p.WindowState = FormWindowState.Normal;
            p.BringToFront();
            p.Activate();
            p._prompt.Focus();
            p._prompt.SelectionStart = p._prompt.Text.Length;
        }

        private void RunSummary()
        {
            Action<string, string> cb = SummaryRequested;
            if (cb == null) return;
            string prompt = _prompt.Text.Trim();
            string mode = _mode == null || _mode.Length == 0 ? "brief" : _mode;
            _running = true;
            Hide();
            cb(prompt, mode);
        }

        private void RunAsk()
        {
            Action<string> cb = AskRequested;
            if (cb == null) return;
            string q = _ask.Text.Trim();
            if (q.Length == 0)
            {
                _hint.Text = "先写下要问的问题";
                _ask.Focus();
                return;
            }
            _running = true;
            Hide();
            cb(q);
        }

        private void RunTranslate()
        {
            Action cb = TranslatePageRequested;
            if (cb == null) return;
            _running = true;
            Hide();
            cb();
        }

        /// <summary>打开面板并把焦点放到「问这一页」输入框（托盘 / IPC 用）。</summary>
        public static void FocusAsk()
        {
            QuickPanel p = _current;
            if (p == null || p.IsDisposed) return;
            p._ask.Focus();
            p._ask.SelectionStart = p._ask.Text.Length;
        }
    }
}
