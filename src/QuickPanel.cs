using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InputPolish
{
    /// <summary>
    /// Ctrl+` 弹出的快捷面板：一个可以改的默认提示词 +「总结本页」「翻译本页」两个动作。
    /// 这个窗口需要接收键盘输入，所以它会正常激活；失焦 / Esc 自动关闭。
    /// </summary>
    public class QuickPanel : Form
    {
        private static QuickPanel _current;

        private TextBox _prompt;
        private Label _hint;
        private bool _running;

        public event Action<string> SummaryRequested;
        public event Action TranslatePageRequested;

        public QuickPanel()
        {
            Text = "InputPolish 快捷面板";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(250, 251, 254);
            Size = new Size(430, 214);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;
            Padding = new Padding(1);

            Label title = new Label();
            title.Text = "网页总结 / 翻译";
            title.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(34, 40, 56);
            title.AutoSize = false;
            title.Bounds = new Rectangle(14, 10, 260, 22);
            Controls.Add(title);

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

            _prompt = new TextBox();
            _prompt.Multiline = true;
            _prompt.ScrollBars = ScrollBars.Vertical;
            _prompt.BorderStyle = BorderStyle.FixedSingle;
            _prompt.Bounds = new Rectangle(14, 38, Width - 30, 112);
            _prompt.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Controls.Add(_prompt);

            _hint = new Label();
            _hint.Text = "Ctrl+Enter 总结本页   ·   提示词可以先改一改再用";
            _hint.AutoSize = false;
            _hint.Bounds = new Rectangle(14, 156, Width - 30, 18);
            _hint.ForeColor = Color.FromArgb(130, 138, 158);
            Controls.Add(_hint);

            Button sum = MakeButton("总结本页", 14, 178, 116);
            sum.Click += delegate { RunSummary(); };
            Button tr = MakeButton("翻译本页", 140, 178, 116);
            tr.Click += delegate { RunTranslate(); };
            Button cancel = MakeButton("关闭", 322, 178, 88);
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
            };
            Deactivate += delegate { if (!_running) Hide(); };
        }

        private Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.Bounds = new Rectangle(x, y, w, 28);
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
        public static void ShowPanel(Config cfg, Action<string> onSummary, Action onTranslate)
        {
            if (_current == null || _current.IsDisposed) _current = new QuickPanel();
            QuickPanel p = _current;
            p._running = false;
            p._prompt.Text = cfg.summaryPrompt == null ? "" : cfg.summaryPrompt;
            p._hint.Text = "Ctrl+Enter 总结本页   ·   " + cfg.panelHotkey + " 打开本面板";
            p.SummaryRequested = onSummary;
            p.TranslatePageRequested = onTranslate;
            Log.Write("quick panel shown: summaryBound=" + (onSummary != null) + " translateBound=" + (onTranslate != null));

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
            Action<string> cb = SummaryRequested;
            if (cb == null) return;
            string prompt = _prompt.Text.Trim();
            if (prompt.Length == 0)
            {
                _hint.Text = "提示词不能为空";
                return;
            }
            _running = true;
            Hide();
            cb(prompt);
        }

        private void RunTranslate()
        {
            Action cb = TranslatePageRequested;
            if (cb == null) return;
            _running = true;
            Hide();
            cb();
        }
    }
}
