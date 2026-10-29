using System;
using System.Drawing;
using System.Windows.Forms;

namespace InputPolish
{
    /// <summary>
    /// Ctrl+反引号 弹出的快捷面板（对齐 WebPilot 的用法：唤起 → 一句话指令 → 对当前网页执行）。
    /// 版式：顶部一条横向滚动的提示词芯片（每个芯片右边挂铅笔可改、末尾加号可新增），
    /// 中间是可选「补充要求」和自由指令框，底部三个动作。
    /// 这个窗口需要接收键盘输入，所以它会正常激活；失焦 / Esc 自动关闭。
    /// </summary>
    public class QuickPanel : Form
    {
        private static QuickPanel _current;

        private ChipStrip _chips;
        private TextBox _prompt;
        private TextBox _ask;
        private Label _lblPreview;
        private Label _lblStatus;

        private Config _cfg;
        private Action<string, string> _onSummary;
        private Action<string> _onAsk;
        private Action<string> _onRun;
        private Action _onTranslate;
        private bool _running;

        public QuickPanel()
        {
            Text = "快捷面板";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(540, 352);
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9f);
            KeyPreview = true;
            // 浏览器全屏时（F11 / 视频全屏）非置顶窗口会被整块盖住，面板就"消失"了，所以这里必须置顶。
            TopMost = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = new Label();
            title.Text = "网页工具";
            title.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(32, 35, 48);
            title.SetBounds(14, 10, 200, 26);
            Controls.Add(title);

            Label keys = new Label();
            keys.Text = "Ctrl+Enter 总结 · Alt+Enter 执行";
            keys.ForeColor = Color.FromArgb(150, 155, 172);
            keys.SetBounds(230, 16, 292, 20);
            keys.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(keys);

            _chips = new ChipStrip();
            _chips.SetBounds(10, 42, ClientSize.Width - 20, 36);
            _chips.SelectionChanged += delegate(int idx)
            {
                Log.Write("chip selected idx=" + idx + " id=" + (_chips.SelectedItem == null ? "" : _chips.SelectedItem.Id));
                RefreshPreview();
            };
            _chips.EditRequested += delegate(int idx)
            {
                Log.Write("chip pencil clicked idx=" + idx);
                EditItem(idx);
            };
            _chips.AddRequested += delegate
            {
                Log.Write("chip plus clicked");
                AddItem();
            };
            Controls.Add(_chips);

            _lblPreview = new Label();
            _lblPreview.ForeColor = Color.FromArgb(120, 126, 148);
            _lblPreview.SetBounds(18, 82, ClientSize.Width - 36, 30);
            _lblPreview.AutoEllipsis = true;
            Controls.Add(_lblPreview);

            Label lSup = new Label();
            lSup.Text = "补充要求（可选：填了就替代上面选中的提示词）";
            lSup.ForeColor = Color.FromArgb(96, 102, 122);
            lSup.SetBounds(18, 116, 420, 20);
            Controls.Add(lSup);

            _prompt = new TextBox();
            _prompt.Multiline = true;
            _prompt.ScrollBars = ScrollBars.Vertical;
            _prompt.SetBounds(18, 136, ClientSize.Width - 36, 52);
            Controls.Add(_prompt);

            Label lAsk = new Label();
            lAsk.Text = "对网页下指令（翻译成英文 / 提炼三条要点 / 改写成邮件…）也可以直接提问";
            lAsk.ForeColor = Color.FromArgb(96, 102, 122);
            lAsk.SetBounds(18, 194, 480, 20);
            Controls.Add(lAsk);

            _ask = new TextBox();
            _ask.SetBounds(18, 214, ClientSize.Width - 176, 26);
            _ask.KeyDown += OnAskKeyDown;
            Controls.Add(_ask);

            Button btnAsk = MakeButton("问", ClientSize.Width - 152, 214, 64);
            btnAsk.Click += delegate { DoAsk(); };
            Controls.Add(btnAsk);

            Button btnRun = MakeButton("执行", ClientSize.Width - 82, 214, 66);
            btnRun.Click += delegate { DoRun(); };
            Controls.Add(btnRun);

            Label hint = new Label();
            hint.Text = "「问」只依据本页回答；「执行」把它当任务（可改写/翻译）；补充要求只作用于总结。";
            hint.ForeColor = Color.FromArgb(150, 155, 172);
            hint.SetBounds(18, 248, ClientSize.Width - 36, 20);
            Controls.Add(hint);

            _lblStatus = new Label();
            _lblStatus.ForeColor = Color.FromArgb(150, 155, 172);
            _lblStatus.SetBounds(18, 268, ClientSize.Width - 36, 20);
            _lblStatus.Text = "";
            Controls.Add(_lblStatus);

            Button btnSummary = MakeButton("总结本页", 18, 300, 116);
            btnSummary.Click += delegate { DoSummary(); };
            Controls.Add(btnSummary);

            Button btnTranslate = MakeButton("翻译本页", 142, 300, 116);
            btnTranslate.Click += delegate { DoTranslate(); };
            Controls.Add(btnTranslate);

            Button btnClose = MakeButton("关闭", ClientSize.Width - 102, 300, 84);
            btnClose.Click += delegate { Hide(); };
            Controls.Add(btnClose);

            KeyDown += OnPanelKeyDown;
            Deactivate += delegate { _lblStatus.Text = ""; };
        }

        private Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 30);
            b.FlatStyle = FlatStyle.System;
            return b;
        }

        /// <summary>显示面板。onSummary(prompt, modeId) / onAsk(question) / onRun(instruction)。</summary>
        public static void ShowPanel(Config cfg, Action<string, string> onSummary, Action<string> onAsk,
            Action<string> onRun, Action onTranslate)
        {
            if (_current == null || _current.IsDisposed) _current = new QuickPanel();
            QuickPanel p = _current;
            p._cfg = cfg;
            p._onSummary = onSummary;
            p._onAsk = onAsk;
            p._onRun = onRun;
            p._onTranslate = onTranslate;
            p._running = false;
            p.ReloadChips(cfg == null ? "" : cfg.summaryMode);
            p._prompt.Text = "";
            // 指令框不用上次的内容预填：它与「补充要求」并排，预填会让用户写了补充要求后
            // 一点执行就悄悄跑起上一轮的旧指令。上次的指令改为灰色提示文字（cue banner）。
            p._ask.Text = "";
            p._lblStatus.Text = "";
            p.PositionPanel();
            p.SetInstructionCue(cfg == null ? "" : ("上次：" + cfg.lastInstruction));
            p.Show();
            p.Activate();
            p._ask.Focus();
            p._ask.SelectionStart = p._ask.Text.Length;
        }

        /// <summary>把上次的指令做成输入框里的灰色提示（EM_SETCUEBANNER，WinForms 4.8 没有 PlaceholderText）。</summary>
        public void SetInstructionCue(string text)
        {
            try { Native.SetCueBanner(_ask.Handle, text == null ? "" : text); }
            catch (Exception ex) { Log.Write("cue banner failed: " + ex.Message); }
        }

        private void PositionPanel()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int x = wa.Left + (wa.Width - Width) / 2;
            int y = wa.Top + (int)(wa.Height * 0.16);
            Location = new Point(x, y);
        }

        private void ReloadChips(string selectedId)
        {
            PromptItem[] items = PromptLib.All();
            _chips.SetItems(items, selectedId);
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            PromptItem it = _chips.SelectedItem;
            if (it == null)
            {
                _lblPreview.Text = "还没有提示词，点右上的加号新建一条";
                return;
            }
            string p = (it.Prompt == null ? "" : it.Prompt).Replace("\r", " ").Replace("\n", " ");
            while (p.IndexOf("  ") >= 0) p = p.Replace("  ", " ");
            if (p.Length > 110) p = p.Substring(0, 110) + "…";
            _lblPreview.Text = it.Label + "：" + p;
        }

        private string CurrentMode()
        {
            PromptItem it = _chips.SelectedItem;
            return it == null ? "" : it.Id;
        }

        private void EditItem(int idx)
        {
            PromptItem it = _chips.SelectedItem;
            PromptItem[] all = PromptLib.All();
            if (idx < 0 || idx >= all.Length) return;
            PromptItem target = all[idx];
            PromptItem saved;
            if (PromptEditForm.Show(this, target.Id, out saved))
            {
                ReloadChips(saved == null ? target.Id : saved.Id);
                _lblStatus.Text = "提示词已保存";
                RememberMode();
            }
        }

        private void AddItem()
        {
            PromptItem saved;
            if (PromptEditForm.Show(this, "", out saved))
            {
                ReloadChips(saved == null ? "" : saved.Id);
                _lblStatus.Text = "已新增提示词：" + (saved == null ? "" : saved.Label);
                RememberMode();
            }
        }

        private void RememberMode()
        {
            if (_cfg == null) return;
            string mode = CurrentMode();
            if (mode.Length > 0 && !string.Equals(_cfg.summaryMode, mode, StringComparison.Ordinal))
            {
                _cfg.summaryMode = mode;
                _cfg.Save();
                Log.Write("summary mode remembered: " + mode);
            }
        }

        private void OnAskKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                DoRun();
            }
        }

        private void OnPanelKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.Enter) { DoSummary(); e.Handled = true; return; }
            if (e.Alt && e.KeyCode == Keys.Enter) { DoRun(); e.Handled = true; return; }
        }

        private void DoSummary()
        {
            string prompt = _prompt.Text.Trim();
            string mode = prompt.Length > 0 ? "" : CurrentMode();
            RememberMode();
            Fire(delegate
            {
                if (_onSummary != null) _onSummary(prompt, mode);
            });
        }

        private void DoAsk()
        {
            string q = _ask.Text.Trim();
            if (q.Length == 0)
            {
                _lblStatus.Text = "先写下要问的问题";
                _ask.Focus();
                return;
            }
            Fire(delegate { if (_onAsk != null) _onAsk(q); });
        }

        private void DoRun()
        {
            string q = _ask.Text.Trim();
            // 用户可能把话写进了上面的「补充要求」就直接点执行 —— 别让他对着没反应的按钮发愣。
            if (q.Length == 0)
            {
                string alt = _prompt.Text.Trim();
                if (alt.Length > 0)
                {
                    q = alt;
                    _lblStatus.Text = "指令框是空的，改用上面「补充要求」当指令执行";
                }
            }
            if (q.Length == 0)
            {
                _lblStatus.Text = "先在「对网页下指令」框里写点什么（比如：提炼三条要点）";
                _ask.Focus();
                return;
            }
            if (_cfg != null)
            {
                _cfg.lastInstruction = q;
                _cfg.Save();
            }
            Fire(delegate { if (_onRun != null) _onRun(q); });
        }

        private void DoTranslate()
        {
            Fire(delegate { if (_onTranslate != null) _onTranslate(); });
        }

        private void Fire(Action act)
        {
            if (_running) return;
            _running = true;
            Hide();
            if (act != null) act();
        }

        /// <summary>打开面板并把焦点放到自由指令框（托盘 / IPC 用）。</summary>
        public static void FocusAsk()
        {
            QuickPanel p = _current;
            if (p == null || p.IsDisposed) return;
            p._ask.Focus();
            p._ask.SelectionStart = p._ask.Text.Length;
        }
    }
}
