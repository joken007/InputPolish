using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace InputPolish
{
    /// <summary>
    /// 网页总结窗：顶部是提示词（可改），中间是总结结果。
    /// 「重新总结」会把当前提示词交回引擎再跑一次，所以同一套面板既能总结也能改需求重来。
    /// </summary>
    public class SummaryPane : Form
    {
        private static SummaryPane _current;

        private RichTextBox _result;
        private TextBox _prompt;
        private Label _status;
        private Panel _promptHost;
        private bool _promptOpen = true;
        private Action<string> _rerun;
        private string _pageTitle = "";

        public SummaryPane()
        {
            Text = "InputPolish 网页总结";
            ClientSize = new Size(860, 660);
            MinimumSize = new Size(560, 380);
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

            Label toggle = MakeButton(bar, "提示词", 10, 64);
            toggle.Click += delegate { TogglePrompt(); };

            Label rerun = MakeButton(bar, "重新总结", 82, 84);
            rerun.Click += delegate { Rerun(); };

            Label minus = MakeButton(bar, "A-", 178, 40);
            minus.Click += delegate { ChangeFont(-1f); };
            Label plus = MakeButton(bar, "A+", 222, 40);
            plus.Click += delegate { ChangeFont(1f); };

            Label copy = MakeButton(bar, "复制", 274, 56);
            copy.Click += delegate { CopyAll(); };
            Label save = MakeButton(bar, "保存", 334, 56);
            save.Click += delegate { SaveFile(); };

            _promptHost = new Panel();
            _promptHost.Dock = DockStyle.Top;
            _promptHost.Height = 128;
            _promptHost.BackColor = Color.FromArgb(252, 252, 255);
            _promptHost.Padding = new Padding(10, 8, 10, 6);
            Controls.Add(_promptHost);

            _prompt = new TextBox();
            _prompt.Multiline = true;
            _prompt.ScrollBars = ScrollBars.Vertical;
            _prompt.Dock = DockStyle.Fill;
            _prompt.BorderStyle = BorderStyle.FixedSingle;
            _prompt.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _promptHost.Controls.Add(_prompt);

            _result = new RichTextBox();
            _result.Dock = DockStyle.Fill;
            _result.ReadOnly = true;
            _result.BorderStyle = BorderStyle.None;
            _result.BackColor = Color.White;
            _result.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
            _result.WordWrap = true;
            _result.DetectUrls = true;
            _result.HideSelection = false;
            _result.Padding = new Padding(14);
            Controls.Add(_result);

            _status = new Label();
            _status.Dock = DockStyle.Bottom;
            _status.Height = 26;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(12, 0, 0, 0);
            _status.BackColor = Color.FromArgb(248, 249, 252);
            _status.ForeColor = Color.FromArgb(90, 96, 112);
            Controls.Add(_status);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.Control && e.KeyCode == Keys.C && !_result.Focused) CopyAll();
            };
        }

        private Label MakeButton(Panel host, string text, int x, int w)
        {
            Label b = new Label();
            b.Text = text;
            b.AutoSize = false;
            b.TextAlign = ContentAlignment.MiddleCenter;
            b.Bounds = new Rectangle(x, 8, w, 26);
            b.Cursor = Cursors.Hand;
            b.ForeColor = Color.FromArgb(52, 62, 84);
            b.BackColor = Color.FromArgb(236, 240, 250);
            b.MouseEnter += delegate { b.BackColor = Color.FromArgb(222, 230, 248); };
            b.MouseLeave += delegate { b.BackColor = Color.FromArgb(236, 240, 250); };
            host.Controls.Add(b);
            return b;
        }

        public static void Present(Config cfg, SummaryResult r, Action<string> rerun)
        {
            if (_current == null || _current.IsDisposed)
            {
                _current = new SummaryPane();
            }
            SummaryPane p = _current;
            p._rerun = rerun;
            p._pageTitle = r.Title == null ? "" : r.Title;
            p._prompt.Text = r.Prompt == null ? "" : r.Prompt;
            p.SetText(r.Text == null ? "" : r.Text);
            string head = r.Title != null && r.Title.Length > 0 ? r.Title : "当前页面";
            p.Text = "InputPolish 网页总结 · " + Shorten(head, 40);
            p.SetStatus("共 " + r.Chars + " 字 · 用时 " + (r.ElapsedMs / 1000.0).ToString("0.0") + "s · " + r.Calls + " 次模型调用");
            if (!p.Visible)
            {
                p.WindowState = FormWindowState.Normal;
                p.Show();
            }
            if (p.WindowState == FormWindowState.Minimized) p.WindowState = FormWindowState.Normal;
            p.BringToFront();
            p.Activate();
        }

        public void SetText(string text)
        {
            _result.Clear();
            _result.SelectionFont = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            _result.SelectionColor = Color.FromArgb(30, 34, 46);
            string[] lines = (text == null ? "" : text).Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                bool bullet = line.TrimStart().StartsWith("-") || line.TrimStart().StartsWith("•") ||
                              (line.Trim().Length > 1 && char.IsDigit(line.Trim()[0]) && line.Trim().IndexOf('.') > 0 && line.Trim().IndexOf('.') <= 3);
                _result.SelectionFont = new Font("Microsoft YaHei UI", 11F, bullet ? FontStyle.Regular : FontStyle.Regular);
                _result.SelectionColor = bullet ? Color.FromArgb(34, 40, 56) : Color.FromArgb(34, 40, 56);
                _result.AppendText(line);
                if (i < lines.Length - 1) _result.AppendText("\n");
            }
            _result.SelectionStart = 0;
            _result.ScrollToCaret();
        }

        public void SetStatus(string text)
        {
            _status.Text = text;
        }

        private void TogglePrompt()
        {
            _promptOpen = !_promptOpen;
            _promptHost.Visible = _promptOpen;
        }

        private void Rerun()
        {
            Action<string> cb = _rerun;
            if (cb == null) return;
            string prompt = _prompt.Text.Trim();
            if (prompt.Length == 0)
            {
                SetStatus("提示词不能为空");
                return;
            }
            SetText("");
            SetStatus("正在重新总结…（" + _pageTitle.Length + " 字标题页）");
            cb(prompt);
        }

        private void CopyAll()
        {
            string text = _result.Text;
            if (text == null || text.Length == 0) return;
            try
            {
                ClipUtil.SetText(text);
                SetStatus("已复制总结（" + text.Length + " 字）");
            }
            catch (Exception ex)
            {
                SetStatus("复制失败：" + ex.Message);
            }
        }

        private void SaveFile()
        {
            try
            {
                SaveFileDialog d = new SaveFileDialog();
                d.Filter = "文本文件|*.txt|所有文件|*.*";
                d.FileName = "summary-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                StringBuilder sb = new StringBuilder();
                if (_pageTitle.Length > 0) sb.Append(_pageTitle).Append("\r\n\r\n");
                sb.Append(_result.Text);
                System.IO.File.WriteAllText(d.FileName, sb.ToString(), Encoding.UTF8);
                SetStatus("已保存到 " + d.FileName);
            }
            catch (Exception ex)
            {
                SetStatus("保存失败：" + ex.Message);
            }
        }

        private void ChangeFont(float delta)
        {
            float size = _result.Font.Size + delta;
            if (size < 9f) size = 9f;
            if (size > 22f) size = 22f;
            _result.Font = new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        private static string Shorten(string s, int max)
        {
            if (s == null) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }
    }
}
