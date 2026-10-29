using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace InputPolish
{
    public class SettingsForm : Form
    {
        private readonly Config _cfg;
        private Config _d;
        private bool _busy;

        private TextBox _txtBaseUrl;
        private TextBox _txtApiKey;
        private ComboBox _cmbModel;
        private NumericUpDown _numTemp;
        private NumericUpDown _numMaxTokens;
        private NumericUpDown _numTimeout;
        private NumericUpDown _numMaxChars;
        private NumericUpDown _numToast;
        private CheckBox _chkClipboard;

        private CheckBox _chkSpaceTriple;
        private NumericUpDown _numSpaceCount;
        private NumericUpDown _numWindow;
        private TextBox _txtHotkey;
        private TextBox _txtUndoHotkey;
        private CheckBox _chkAutoStart;
        private CheckBox _chkWidget;

        private CheckBox _chkBubble;
        private NumericUpDown _numBubbleMs;
        private TextBox _txtPanelHotkey;
        private TextBox _txtSummaryPrompt;

        private ComboBox _cmbStyle;
        private ComboBox _cmbTranslateTarget;
        private TextBox _txtTranslateHotkey;
        private TextBox _txtWebHotkey;
        private CheckBox _chkExplain;
        private NumericUpDown _numPageChars;
        private NumericUpDown _numPageParas;

        private CheckBox _chkInPage;
        private NumericUpDown _numApiPort;
        private TextBox _txtApiToken;
        private Label _lblExt;
        private System.Windows.Forms.Timer _extTimer;

        private TextBox _txtExclude;
        private TextBox _txtSystemPrompt;
        private Label _lblStatus;

        private Font _font;

        public SettingsForm(Config cfg)
        {
            _cfg = cfg;
            _d = cfg.Clone();

            _font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Text = "InputPolish 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            Font = _font;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(580, 1048);
            BackColor = Color.FromArgb(248, 249, 252);

            BuildBackend();
            BuildTrigger();
            BuildTranslate();
            BuildInPage();
            BuildExclude();
            BuildPrompt();
            BuildSummary();
            BuildButtons();
            Bind();
            StartExtStatus();

            _lblStatus.Text = "模型：" + ShortModel(_d.model);
        }

        private Label AddLabel(Control parent, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            l.ForeColor = Color.FromArgb(52, 58, 70);
            parent.Controls.Add(l);
            return l;
        }

        private TextBox AddText(Control parent, int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.Location = new Point(x, y);
            t.Width = w;
            t.Font = _font;
            parent.Controls.Add(t);
            return t;
        }

        private NumericUpDown AddNum(Control parent, int x, int y, int w, decimal min, decimal max, int decimals, decimal step)
        {
            NumericUpDown n = new NumericUpDown();
            n.Location = new Point(x, y);
            n.Width = w;
            n.Minimum = min;
            n.Maximum = max;
            n.DecimalPlaces = decimals;
            n.Increment = step;
            n.Font = _font;
            parent.Controls.Add(n);
            return n;
        }

        private CheckBox AddCheck(Control parent, string text, int x, int y, int w)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Location = new Point(x, y);
            c.Width = w;
            c.ForeColor = Color.FromArgb(52, 58, 70);
            parent.Controls.Add(c);
            return c;
        }

        private GroupBox AddGroup(string text, int x, int y, int w, int h)
        {
            GroupBox g = new GroupBox();
            g.Text = text;
            g.Location = new Point(x, y);
            g.Size = new Size(w, h);
            g.ForeColor = Color.FromArgb(70, 76, 92);
            Controls.Add(g);
            return g;
        }

        private void BuildBackend()
        {
            GroupBox g = AddGroup("AI 后端与请求参数", 12, 12, 556, 192);

            AddLabel(g, "接口地址", 14, 29);
            _txtBaseUrl = AddText(g, 100, 26, 442);

            AddLabel(g, "API Key", 14, 61);
            _txtApiKey = AddText(g, 100, 58, 442);
            _txtApiKey.UseSystemPasswordChar = true;

            AddLabel(g, "模型", 14, 93);
            _cmbModel = new ComboBox();
            _cmbModel.Location = new Point(100, 90);
            _cmbModel.Width = 300;
            _cmbModel.DropDownStyle = ComboBoxStyle.DropDown;
            _cmbModel.Font = _font;
            g.Controls.Add(_cmbModel);

            Button btnFetch = new Button();
            btnFetch.Text = "拉取模型列表";
            btnFetch.Location = new Point(408, 89);
            btnFetch.Size = new Size(134, 25);
            btnFetch.Click += delegate { FetchModels(); };
            g.Controls.Add(btnFetch);

            AddLabel(g, "温度", 14, 125);
            _numTemp = AddNum(g, 100, 122, 64, 0m, 2m, 2, 0.1m);

            AddLabel(g, "最大 tokens", 180, 125);
            _numMaxTokens = AddNum(g, 282, 122, 70, 256m, 64000m, 0, 100m);

            AddLabel(g, "超时 ms", 366, 125);
            _numTimeout = AddNum(g, 428, 122, 62, 3000m, 300000m, 0, 1000m);

            AddLabel(g, "字数上限", 14, 157);
            _numMaxChars = AddNum(g, 100, 154, 64, 200m, 200000m, 0, 1000m);

            AddLabel(g, "提示毫秒", 180, 157);
            _numToast = AddNum(g, 282, 154, 70, 800m, 15000m, 0, 200m);

            _chkClipboard = AddCheck(g, "剪贴板兜底", 366, 155, 160);
        }

        private void BuildTrigger()
        {
            GroupBox g = AddGroup("触发方式", 12, 214, 556, 118);

            _chkSpaceTriple = AddCheck(g, "连击空格触发润色", 14, 25, 145);
            AddLabel(g, "连击次数", 166, 27);
            _numSpaceCount = AddNum(g, 230, 24, 50, 2m, 6m, 0, 1m);
            AddLabel(g, "间隔 ms", 292, 27);
            _numWindow = AddNum(g, 356, 24, 56, 150m, 3000m, 0, 50m);

            AddLabel(g, "润色热键", 14, 59);
            _txtHotkey = AddText(g, 86, 56, 120);

            AddLabel(g, "撤销热键", 222, 59);
            _txtUndoHotkey = AddText(g, 292, 56, 120);

            _chkAutoStart = AddCheck(g, "开机自启", 14, 88, 100);
            _chkWidget = AddCheck(g, "显示桌面悬浮框", 140, 88, 150);
        }

        private void BuildTranslate()
        {
            GroupBox g = AddGroup("润色风格与翻译", 12, 340, 556, 186);

            AddLabel(g, "润色风格", 14, 29);
            _cmbStyle = new ComboBox();
            _cmbStyle.Location = new Point(86, 26);
            _cmbStyle.Width = 152;
            _cmbStyle.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbStyle.Font = _font;
            for (int i = 0; i < Styles.Ids.Length; i++)
            {
                _cmbStyle.Items.Add(Styles.Label(Styles.Ids[i]));
            }
            g.Controls.Add(_cmbStyle);

            AddLabel(g, "翻译目标", 258, 29);
            _cmbTranslateTarget = new ComboBox();
            _cmbTranslateTarget.Location = new Point(330, 26);
            _cmbTranslateTarget.Width = 110;
            _cmbTranslateTarget.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbTranslateTarget.Font = _font;
            _cmbTranslateTarget.Items.Add("自动识别");
            _cmbTranslateTarget.Items.Add("简体中文");
            _cmbTranslateTarget.Items.Add("英文");
            g.Controls.Add(_cmbTranslateTarget);

            AddLabel(g, "划词热键", 14, 61);
            _txtTranslateHotkey = AddText(g, 86, 58, 116);

            AddLabel(g, "网页热键", 258, 61);
            _txtWebHotkey = AddText(g, 330, 58, 110);

            _chkExplain = AddCheck(g, "翻译附带讲解", 14, 90, 160);

            AddLabel(g, "页面字数上限", 190, 92);
            _numPageChars = AddNum(g, 288, 89, 80, 2000m, 400000m, 0, 5000m);

            AddLabel(g, "段落上限", 386, 92);
            _numPageParas = AddNum(g, 460, 89, 68, 10m, 1000m, 0, 10m);

            _chkBubble = AddCheck(g, "划词浮标", 14, 122, 96);
            AddLabel(g, "浮标驻留 ms", 118, 124);
            _numBubbleMs = AddNum(g, 204, 121, 66, 1200m, 15000m, 0, 300m);

            AddLabel(g, "面板热键", 288, 124);
            _txtPanelHotkey = AddText(g, 356, 121, 96);

            Label hint = new Label();
            hint.Text = "浮标：鼠标选中文字后旁边冒出小胶囊，点一下即翻译。面板热键默认 Ctrl+反引号。";
            hint.AutoSize = true;
            hint.Location = new Point(14, 156);
            hint.ForeColor = Color.FromArgb(120, 126, 140);
            g.Controls.Add(hint);
        }

        private void BuildInPage()
        {
            GroupBox g = AddGroup("网页内翻译（浏览器扩展，译文直接显示在网页里）", 12, 534, 556, 96);

            _chkInPage = AddCheck(g, "启用网页内翻译", 14, 24, 150);

            AddLabel(g, "本机端口", 178, 26);
            _numApiPort = AddNum(g, 240, 23, 70, 1024m, 65535m, 0, 1m);

            AddLabel(g, "连接码", 322, 26);
            _txtApiToken = AddText(g, 372, 23, 168);
            _txtApiToken.ReadOnly = true;

            Button btnCopy = new Button();
            btnCopy.Text = "复制";
            btnCopy.Location = new Point(14, 56);
            btnCopy.Size = new Size(64, 26);
            btnCopy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(_d.apiToken);
                    _lblExt.Text = "连接码已复制到剪贴板";
                }
                catch (Exception ex)
                {
                    _lblExt.Text = "复制失败：" + ex.Message;
                }
            };
            g.Controls.Add(btnCopy);

            Button btnExt = new Button();
            btnExt.Text = "打开扩展目录";
            btnExt.Location = new Point(86, 56);
            btnExt.Size = new Size(104, 26);
            btnExt.Click += delegate { OpenExtFolder(); };
            g.Controls.Add(btnExt);

            _lblExt = new Label();
            _lblExt.AutoSize = false;
            _lblExt.Location = new Point(200, 60);
            _lblExt.Size = new Size(340, 18);
            _lblExt.ForeColor = Color.FromArgb(96, 104, 120);
            _lblExt.Text = "扩展状态：检查中…";
            g.Controls.Add(_lblExt);

            Label hint = new Label();
            hint.AutoSize = true;
            hint.Location = new Point(14, 82);
            hint.ForeColor = Color.FromArgb(120, 126, 140);
            hint.Text = "装扩展：打开扩展目录 → 浏览器「加载已解压的扩展程序」→ 扩展选项里粘贴连接码";
            g.Controls.Add(hint);
        }

        private void OpenExtFolder()
        {
            try
            {
                string dir = Config.ExtDir();
                if (dir == null || dir.Length == 0)
                {
                    MessageBox.Show(this, "没找到 ext 扩展目录，请确认安装包里的 ext 文件夹与 InputPolish.exe 在一起。",
                        "InputPolish", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                Log.Write("open ext folder: " + dir);
            }
            catch (Exception ex)
            {
                Log.Write("open ext folder failed: " + ex.Message);
            }
        }

        private void StartExtStatus()
        {
            UpdateExtStatus();
            _extTimer = new System.Windows.Forms.Timer();
            _extTimer.Interval = 1200;
            _extTimer.Tick += delegate { UpdateExtStatus(); };
            _extTimer.Start();
        }

        private void UpdateExtStatus()
        {
            if (_lblExt == null) return;
            int age = HttpApi.ClientAliveMs();
            if (!_d.inPage || !HttpApi.Running)
            {
                _lblExt.Text = "扩展状态：网页内翻译已关闭";
                _lblExt.ForeColor = Color.FromArgb(140, 146, 160);
            }
            else if (age < 0)
            {
                _lblExt.Text = "扩展状态：未连接（装好扩展后会自动连上）";
                _lblExt.ForeColor = Color.FromArgb(190, 120, 40);
            }
            else if (age < 40000)
            {
                _lblExt.Text = "扩展状态：已连接（" + (age / 1000) + " 秒前有心跳）";
                _lblExt.ForeColor = Color.FromArgb(40, 140, 80);
            }
            else
            {
                _lblExt.Text = "扩展状态：已断开（" + (age / 1000) + " 秒前有过心跳）";
                _lblExt.ForeColor = Color.FromArgb(190, 120, 40);
            }
            if (_txtApiToken != null && _txtApiToken.Text != _d.apiToken) _txtApiToken.Text = _d.apiToken;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_extTimer != null) { _extTimer.Stop(); _extTimer.Dispose(); _extTimer = null; }
            base.OnFormClosing(e);
        }

        private void BuildExclude()
        {
            GroupBox g = AddGroup("排除进程（每行一个，不用写 .exe）", 12, 638, 556, 104);
            _txtExclude = new TextBox();
            _txtExclude.Location = new Point(14, 22);
            _txtExclude.Size = new Size(528, 72);
            _txtExclude.Multiline = true;
            _txtExclude.ScrollBars = ScrollBars.Vertical;
            _txtExclude.Font = _font;
            g.Controls.Add(_txtExclude);
        }

        private void BuildPrompt()
        {
            GroupBox g = AddGroup("系统提示词（留空使用内置）", 12, 750, 556, 104);
            _txtSystemPrompt = new TextBox();
            _txtSystemPrompt.Location = new Point(14, 20);
            _txtSystemPrompt.Size = new Size(528, 76);
            _txtSystemPrompt.Multiline = true;
            _txtSystemPrompt.ScrollBars = ScrollBars.Vertical;
            _txtSystemPrompt.Font = _font;
            g.Controls.Add(_txtSystemPrompt);
        }

        private void BuildSummary()
        {
            GroupBox g = AddGroup("网页总结提示词（面板里选方案或临时填提示词时，以面板为准）", 12, 862, 556, 112);
            _txtSummaryPrompt = new TextBox();
            _txtSummaryPrompt.Location = new Point(14, 22);
            _txtSummaryPrompt.Size = new Size(528, 84);
            _txtSummaryPrompt.Multiline = true;
            _txtSummaryPrompt.ScrollBars = ScrollBars.Vertical;
            _txtSummaryPrompt.Font = _font;
            g.Controls.Add(_txtSummaryPrompt);
        }

        private void BuildButtons()
        {
            Button btnTest = new Button();
            btnTest.Text = "测试连接";
            btnTest.Location = new Point(12, 1006);
            btnTest.Size = new Size(96, 30);
            btnTest.Click += delegate { TestConnection(); };
            Controls.Add(btnTest);

            Button btnPolish = new Button();
            btnPolish.Text = "测试润色";
            btnPolish.Location = new Point(116, 1006);
            btnPolish.Size = new Size(96, 30);
            btnPolish.Click += delegate { TestPolish(); };
            Controls.Add(btnPolish);

            // 状态单独占一行：模型名（auto/deepseek-v4.1-flash）放按钮之间会被截断成「模型：」。
            _lblStatus = new Label();
            _lblStatus.Location = new Point(12, 980);
            _lblStatus.AutoSize = false;
            _lblStatus.Size = new Size(556, 18);
            _lblStatus.AutoEllipsis = true;
            _lblStatus.ForeColor = Color.FromArgb(96, 104, 120);
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_lblStatus);

            Button btnSave = new Button();
            btnSave.Text = "保存";
            btnSave.Location = new Point(368, 1006);
            btnSave.Size = new Size(88, 30);
            btnSave.Click += delegate { SaveAndClose(); };
            Controls.Add(btnSave);

            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(464, 1006);
            btnCancel.Size = new Size(88, 30);
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            AcceptButton = btnSave;
            CancelButton = btnCancel;
        }

        private void Bind()
        {
            _txtBaseUrl.Text = _d.baseUrl;
            _txtApiKey.Text = _d.apiKey;
            _cmbModel.Text = _d.model;
            _numTemp.Value = SafeDecimal((decimal)_d.temperature, _numTemp);
            _numMaxTokens.Value = SafeDecimal(_d.maxTokens, _numMaxTokens);
            _numTimeout.Value = SafeDecimal(_d.timeoutMs, _numTimeout);
            _numMaxChars.Value = SafeDecimal(_d.maxInputChars, _numMaxChars);
            _numToast.Value = SafeDecimal(_d.toastMs, _numToast);
            _chkClipboard.Checked = _d.clipboardFallback;

            _chkSpaceTriple.Checked = _d.trigger.spaceTriple;
            _numSpaceCount.Value = SafeDecimal(_d.trigger.spaceCount, _numSpaceCount);
            _numWindow.Value = SafeDecimal(_d.trigger.windowMs, _numWindow);
            _txtHotkey.Text = _d.trigger.hotkey;
            _txtUndoHotkey.Text = _d.trigger.undoHotkey;
            _chkAutoStart.Checked = _d.autoStart;
            _chkWidget.Checked = _d.widgetVisible;

            _cmbStyle.SelectedIndex = IndexOfId(Styles.Normalize(_d.polishStyle));
            _cmbTranslateTarget.SelectedIndex = TargetIndex(Translate.NormalizeTarget(_d.translateTarget));
            _txtTranslateHotkey.Text = _d.translateHotkey == null ? "" : _d.translateHotkey;
            _txtWebHotkey.Text = _d.webTranslateHotkey == null ? "" : _d.webTranslateHotkey;
            _chkExplain.Checked = _d.translateExplain;
            _numPageChars.Value = SafeDecimal(_d.pageMaxChars, _numPageChars);
            _numPageParas.Value = SafeDecimal(_d.pageMaxParagraphs, _numPageParas);
            _chkBubble.Checked = _d.selectionBubble;
            _numBubbleMs.Value = SafeDecimal(_d.selectionBubbleMs, _numBubbleMs);
            _chkInPage.Checked = _d.inPage;
            _numApiPort.Value = SafeDecimal(_d.apiPort, _numApiPort);
            _txtApiToken.Text = _d.apiToken;
            _txtPanelHotkey.Text = _d.panelHotkey == null ? "" : _d.panelHotkey;

            if (_d.excludeProcesses != null) _txtExclude.Text = string.Join(Environment.NewLine, _d.excludeProcesses.ToArray());
            _txtSystemPrompt.Text = _d.systemPromptOverride == null ? "" : _d.systemPromptOverride;
            _txtSummaryPrompt.Text = _d.summaryPrompt == null ? "" : _d.summaryPrompt;
        }

        /// <summary>状态栏很窄，模型名太长时只显示最后一段（auto/deepseek-v4.1-flash → deepseek-v4.1-flash）。</summary>
        private static string ShortModel(string model)
        {
            if (model == null) return "";
            int i = model.LastIndexOf('/');
            string s = i >= 0 && i < model.Length - 1 ? model.Substring(i + 1) : model;
            if (s.Length > 20) s = s.Substring(0, 19) + "…";
            return s;
        }

        private static int IndexOfId(string id)
        {
            for (int i = 0; i < Styles.Ids.Length; i++)
            {
                if (string.Equals(Styles.Ids[i], id, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        private static int TargetIndex(string target)
        {
            if (target == Translate.Zh) return 1;
            if (target == Translate.En) return 2;
            return 0;
        }

        private static string TargetOfIndex(int index)
        {
            if (index == 1) return Translate.Zh;
            if (index == 2) return Translate.En;
            return Translate.Auto;
        }

        private static decimal SafeDecimal(int value, NumericUpDown box)
        {
            return SafeDecimal((decimal)value, box);
        }

        private static decimal SafeDecimal(decimal value, NumericUpDown box)
        {
            if (value < box.Minimum) return box.Minimum;
            if (value > box.Maximum) return box.Maximum;
            return value;
        }

        private void Collect()
        {
            _d.baseUrl = _txtBaseUrl.Text.Trim();
            _d.apiKey = _txtApiKey.Text.Trim();
            _d.model = _cmbModel.Text.Trim();
            _d.temperature = (double)_numTemp.Value;
            _d.maxTokens = (int)_numMaxTokens.Value;
            _d.timeoutMs = (int)_numTimeout.Value;
            _d.maxInputChars = (int)_numMaxChars.Value;
            _d.toastMs = (int)_numToast.Value;
            _d.clipboardFallback = _chkClipboard.Checked;

            _d.trigger.spaceTriple = _chkSpaceTriple.Checked;
            _d.trigger.spaceCount = (int)_numSpaceCount.Value;
            _d.trigger.windowMs = (int)_numWindow.Value;
            _d.trigger.hotkey = _txtHotkey.Text.Trim();
            _d.trigger.undoHotkey = _txtUndoHotkey.Text.Trim();
            _d.autoStart = _chkAutoStart.Checked;
            _d.widgetVisible = _chkWidget.Checked;

            int si = _cmbStyle.SelectedIndex;
            _d.polishStyle = Styles.Ids[si < 0 ? 0 : si];
            _d.translateTarget = TargetOfIndex(_cmbTranslateTarget.SelectedIndex);
            _d.translateHotkey = _txtTranslateHotkey.Text.Trim();
            _d.webTranslateHotkey = _txtWebHotkey.Text.Trim();
            _d.translateExplain = _chkExplain.Checked;
            _d.pageMaxChars = (int)_numPageChars.Value;
            _d.pageMaxParagraphs = (int)_numPageParas.Value;
            _d.selectionBubble = _chkBubble.Checked;
            _d.selectionBubbleMs = (int)_numBubbleMs.Value;
            _d.panelHotkey = _txtPanelHotkey.Text.Trim();
            _d.inPage = _chkInPage.Checked;
            _d.apiPort = (int)_numApiPort.Value;

            _d.excludeProcesses = new System.Collections.Generic.List<string>();
            string[] lines = _txtExclude.Text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.Length > 0) _d.excludeProcesses.Add(t);
            }

            _d.systemPromptOverride = _txtSystemPrompt.Text;
            _d.summaryPrompt = _txtSummaryPrompt.Text.Trim();
        }

        private void SaveAndClose()
        {
            Collect();
            if (_d.baseUrl.Length == 0) _d.baseUrl = "http://127.0.0.1:3425/v1";
            if (_d.model.Length == 0)
            {
                MessageBox.Show(this, "请先选择或填写一个模型。", "InputPolish", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int mods;
            int vk;
            if (_d.trigger.hotkey.Length > 0 && !HotkeyWindow.ParseHotkey(_d.trigger.hotkey, out mods, out vk))
            {
                MessageBox.Show(this, "润色热键格式不对，示例：Ctrl+Alt+L", "InputPolish", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_d.trigger.undoHotkey.Length > 0 && !HotkeyWindow.ParseHotkey(_d.trigger.undoHotkey, out mods, out vk))
            {
                MessageBox.Show(this, "撤销热键格式不对，示例：Ctrl+Alt+Z", "InputPolish", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_d.translateHotkey.Length > 0 && !HotkeyWindow.ParseHotkey(_d.translateHotkey, out mods, out vk))
            {
                MessageBox.Show(this, "划词翻译热键格式不对，示例：Ctrl+Alt+T", "InputPolish", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_d.webTranslateHotkey.Length > 0 && !HotkeyWindow.ParseHotkey(_d.webTranslateHotkey, out mods, out vk))
            {
                MessageBox.Show(this, "网页翻译热键格式不对，示例：Ctrl+Alt+W", "InputPolish", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_d.panelHotkey.Length > 0 && !HotkeyWindow.ParseHotkey(_d.panelHotkey, out mods, out vk))
            {
                MessageBox.Show(this, "快捷面板热键格式不对，示例：Ctrl+`", "InputPolish", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            CopyInto(_cfg, _d);
            _cfg.Save();
            DialogResult = DialogResult.OK;
            Close();
        }

        private static void CopyInto(Config dst, Config src)
        {
            dst.baseUrl = src.baseUrl;
            dst.apiKey = src.apiKey;
            dst.model = src.model;
            dst.temperature = src.temperature;
            dst.maxTokens = src.maxTokens;
            dst.timeoutMs = src.timeoutMs;
            dst.maxInputChars = src.maxInputChars;
            dst.toastMs = src.toastMs;
            dst.clipboardFallback = src.clipboardFallback;
            dst.trigger = src.trigger.Clone();
            dst.autoStart = src.autoStart;
            dst.widgetVisible = src.widgetVisible;
            dst.excludeProcesses = new System.Collections.Generic.List<string>(src.excludeProcesses.ToArray());
            dst.systemPromptOverride = src.systemPromptOverride;
            dst.polishStyle = src.polishStyle;
            dst.translateTarget = src.translateTarget;
            dst.translateHotkey = src.translateHotkey;
            dst.webTranslateHotkey = src.webTranslateHotkey;
            dst.translateExplain = src.translateExplain;
            dst.pageMaxChars = src.pageMaxChars;
            dst.pageMaxParagraphs = src.pageMaxParagraphs;
            dst.selectionBubble = src.selectionBubble;
            dst.selectionBubbleMs = src.selectionBubbleMs;
            dst.panelHotkey = src.panelHotkey;
            dst.inPage = src.inPage;
            dst.apiPort = src.apiPort;
            dst.apiToken = src.apiToken;
            dst.summaryPrompt = src.summaryPrompt;
        }

        private void FetchModels()
        {
            if (_busy) return;
            Collect();
            Config tmp = new Config();
            CopyInto(tmp, _d);
            tmp.enabled = _d.enabled;

            _busy = true;
            _lblStatus.Text = "正在拉取…";

            Thread t = new Thread(delegate()
            {
                string error;
                System.Collections.Generic.List<string> models = Polisher.ListModels(tmp, out error);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        if (models.Count == 0)
                        {
                            _lblStatus.Text = "拉取失败";
                            MessageBox.Show(this, "拉取模型列表失败：\n" + (error.Length > 0 ? error : "未知错误"),
                                "InputPolish", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        string current = _cmbModel.Text;
                        _cmbModel.Items.Clear();
                        foreach (string m in models) _cmbModel.Items.Add(m);
                        if (current.Length > 0) _cmbModel.Text = current;
                        else _cmbModel.Text = models[0];
                        _lblStatus.Text = "共 " + models.Count + " 个模型";
                    });
                }
                catch
                {
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void TestConnection()
        {
            if (_busy) return;
            Collect();
            Config tmp = new Config();
            CopyInto(tmp, _d);

            _busy = true;
            _lblStatus.Text = "连接中…";

            Thread t = new Thread(delegate()
            {
                DateTime t0 = DateTime.UtcNow;
                string error;
                System.Collections.Generic.List<string> models = Polisher.ListModels(tmp, out error);
                int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        if (models.Count == 0)
                        {
                            _lblStatus.Text = "连接失败";
                            MessageBox.Show(this, "连接失败：" + (error.Length > 0 ? error : "未知错误"),
                                "InputPolish", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        else
                        {
                            _lblStatus.Text = "连通 · " + models.Count + " 个模型 · " + ms + "ms";
                            MessageBox.Show(this, "连接成功。\n\n耗时：" + ms + "ms\n模型数量：" + models.Count +
                                "\n\n前几个模型：\n" + string.Join("\n", models.GetRange(0, Math.Min(8, models.Count)).ToArray()),
                                "InputPolish", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    });
                }
                catch
                {
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void TestPolish()
        {
            if (_busy) return;
            Collect();
            Config tmp = new Config();
            CopyInto(tmp, _d);

            const string sample = "这个项目的进度有点慢主要是因为第三方接口一直没给我们文档所以我们前后端联调拖了快两周另外测试环境也不稳定经常挂掉";
            _busy = true;
            _lblStatus.Text = "润色中…";

            Thread t = new Thread(delegate()
            {
                PolishResult pr = Polisher.Polish(tmp, sample);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        if (!pr.Ok)
                        {
                            _lblStatus.Text = "润色失败";
                            MessageBox.Show(this, "测试润色失败：\n" + pr.Error, "InputPolish",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        _lblStatus.Text = "润色成功 · " + pr.ElapsedMs + "ms";
                        TextView.Show(this, "润色结果（" + pr.ElapsedMs + "ms · " + pr.Attempts + " 次请求）", pr.Text);
                    });
                }
                catch
                {
                }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    public class TextView : Form
    {
        public static void Show(IWin32Window owner, string title, string text)
        {
            TextView f = new TextView();
            f.Text = title;
            f.ClientSize = new Size(560, 420);
            f.StartPosition = FormStartPosition.CenterParent;
            f.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            f.TopMost = true;

            TextBox box = new TextBox();
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.ReadOnly = true;
            box.Dock = DockStyle.Fill;
            box.Text = text.Replace("\n", Environment.NewLine);
            box.Font = f.Font;
            f.Controls.Add(box);
            f.ShowDialog(owner);
        }
    }
}
