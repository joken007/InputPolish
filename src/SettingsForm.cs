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
            ClientSize = new Size(580, 654);
            BackColor = Color.FromArgb(248, 249, 252);

            BuildBackend();
            BuildTrigger();
            BuildExclude();
            BuildPrompt();
            BuildButtons();
            Bind();

            _lblStatus.Text = "模型：" + _d.model;
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

        private void BuildExclude()
        {
            GroupBox g = AddGroup("排除进程（每行一个，不用写 .exe）", 12, 340, 556, 118);
            _txtExclude = new TextBox();
            _txtExclude.Location = new Point(14, 22);
            _txtExclude.Size = new Size(528, 86);
            _txtExclude.Multiline = true;
            _txtExclude.ScrollBars = ScrollBars.Vertical;
            _txtExclude.Font = _font;
            g.Controls.Add(_txtExclude);
        }

        private void BuildPrompt()
        {
            GroupBox g = AddGroup("系统提示词（留空使用内置）", 12, 466, 556, 134);
            _txtSystemPrompt = new TextBox();
            _txtSystemPrompt.Location = new Point(14, 20);
            _txtSystemPrompt.Size = new Size(528, 104);
            _txtSystemPrompt.Multiline = true;
            _txtSystemPrompt.ScrollBars = ScrollBars.Vertical;
            _txtSystemPrompt.Font = _font;
            g.Controls.Add(_txtSystemPrompt);
        }

        private void BuildButtons()
        {
            Button btnTest = new Button();
            btnTest.Text = "测试连接";
            btnTest.Location = new Point(12, 610);
            btnTest.Size = new Size(96, 30);
            btnTest.Click += delegate { TestConnection(); };
            Controls.Add(btnTest);

            Button btnPolish = new Button();
            btnPolish.Text = "测试润色";
            btnPolish.Location = new Point(116, 610);
            btnPolish.Size = new Size(96, 30);
            btnPolish.Click += delegate { TestPolish(); };
            Controls.Add(btnPolish);

            _lblStatus = new Label();
            _lblStatus.Location = new Point(222, 617);
            _lblStatus.AutoSize = false;
            _lblStatus.Size = new Size(136, 20);
            _lblStatus.ForeColor = Color.FromArgb(96, 104, 120);
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_lblStatus);

            Button btnSave = new Button();
            btnSave.Text = "保存";
            btnSave.Location = new Point(368, 610);
            btnSave.Size = new Size(88, 30);
            btnSave.Click += delegate { SaveAndClose(); };
            Controls.Add(btnSave);

            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(464, 610);
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

            if (_d.excludeProcesses != null) _txtExclude.Text = string.Join(Environment.NewLine, _d.excludeProcesses.ToArray());
            _txtSystemPrompt.Text = _d.systemPromptOverride == null ? "" : _d.systemPromptOverride;
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

            _d.excludeProcesses = new System.Collections.Generic.List<string>();
            string[] lines = _txtExclude.Text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.Length > 0) _d.excludeProcesses.Add(t);
            }

            _d.systemPromptOverride = _txtSystemPrompt.Text;
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
