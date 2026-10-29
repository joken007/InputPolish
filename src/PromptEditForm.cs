using System;
using System.Drawing;
using System.Windows.Forms;

namespace InputPolish
{
    /// <summary>
    /// 编辑/新建一条提示词。
    /// 从快捷面板的铅笔（改当前这条）或加号（新建）打开；内置的九条也能改，
    /// 改坏了可以点「恢复默认」把出厂内容填回来。
    /// </summary>
    public class PromptEditForm : Form
    {
        private readonly string _id;
        private string _resultId;    // 新建成功后的真实 id（_id 此时还是空的）
        private bool _builtin;
        private bool _changed;

        private TextBox _name;
        private TextBox _body;
        private NumericUpDown _tokens;
        private Label _badge;
        private Label _status;
        private Button _btnSave;
        private Button _btnDelete;
        private Button _btnRestore;
        private Button _btnCancel;

        /// <summary>打开编辑框。id 为空表示新建；返回 true 表示提示词库有变化。</summary>
        public static bool Show(IWin32Window owner, string id, out PromptItem item)
        {
            item = null;
            PromptEditForm f = new PromptEditForm(id);
            DialogResult dr = owner == null ? f.ShowDialog() : f.ShowDialog(owner);
            if (f._changed) item = PromptLib.Get(f._resultId.Length > 0 ? f._resultId : f._id);
            return f._changed;
        }

        public PromptEditForm(string id)
        {
            _id = id == null ? "" : id.Trim();
            _resultId = "";
            bool isNew = _id.Length == 0;

            Text = isNew ? "新建提示词" : "编辑提示词";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;      // 面板本身置顶，编辑框不能被它盖住
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 452);
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9f);
            try { Icon = TrayIcon(); } catch { }

            Label title = new Label();
            title.Text = isNew ? "新建提示词" : "编辑提示词";
            title.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(32, 35, 48);
            title.SetBounds(16, 14, 300, 26);
            Controls.Add(title);

            _badge = new Label();
            _badge.Text = "";
            _badge.ForeColor = Color.FromArgb(120, 126, 148);
            _badge.SetBounds(206, 20, 338, 20);
            _badge.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_badge);

            Label ln = new Label();
            ln.Text = "名称（芯片上显示，太长会被省略）";
            ln.ForeColor = Color.FromArgb(96, 102, 122);
            ln.SetBounds(16, 50, 400, 20);
            Controls.Add(ln);

            _name = new TextBox();
            _name.SetBounds(16, 70, 528, 26);
            Controls.Add(_name);

            Label lb = new Label();
            lb.Text = "提示词（发给模型的系统指令，越具体越稳）";
            lb.ForeColor = Color.FromArgb(96, 102, 122);
            lb.SetBounds(16, 104, 400, 20);
            Controls.Add(lb);

            _body = new TextBox();
            _body.Multiline = true;
            _body.ScrollBars = ScrollBars.Vertical;
            _body.AcceptsReturn = true;
            _body.SetBounds(16, 124, 528, 226);
            _body.Font = new Font("Consolas", 9.5f);
            Controls.Add(_body);

            Label lt = new Label();
            lt.Text = "最大 tokens";
            lt.ForeColor = Color.FromArgb(96, 102, 122);
            lt.SetBounds(16, 362, 90, 20);
            Controls.Add(lt);

            _tokens = new NumericUpDown();
            _tokens.Minimum = 200;
            _tokens.Maximum = 8000;
            _tokens.Increment = 100;
            _tokens.Value = 1800;
            _tokens.SetBounds(110, 358, 84, 26);
            Controls.Add(_tokens);

            Label lhint = new Label();
            lhint.Text = "越大越不容易被截断，也更慢更贵";
            lhint.ForeColor = Color.FromArgb(150, 155, 172);
            lhint.SetBounds(204, 362, 344, 20);
            Controls.Add(lhint);

            ToolTip tip = new ToolTip();
            tip.SetToolTip(_tokens, "实际超时上限 ≈ 20 秒 + 最大 tokens × 40 毫秒（30~180 秒）");
            tip.SetToolTip(lhint, "实际超时上限 ≈ 20 秒 + 最大 tokens × 40 毫秒（30~180 秒）");

            _status = new Label();
            _status.ForeColor = Color.FromArgb(120, 126, 148);
            _status.SetBounds(16, 392, 528, 20);
            Controls.Add(_status);

            _btnSave = MakeButton("保存", 16, 412, 100);
            _btnSave.Click += delegate { DoSave(); };
            Controls.Add(_btnSave);

            _btnDelete = MakeButton("删除", 124, 412, 100);
            _btnDelete.Click += delegate { DoDelete(); };
            Controls.Add(_btnDelete);

            _btnRestore = MakeButton("恢复默认", 232, 412, 110);
            _btnRestore.Click += delegate { DoRestore(); };
            Controls.Add(_btnRestore);

            _btnCancel = MakeButton("取消", 444, 412, 100);
            _btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_btnCancel);
            CancelButton = _btnCancel;

            Load += delegate { LoadItem(isNew); };
        }

        private static Icon TrayIcon()
        {
            // 复用主程序的图标（没有就留空，不影响使用）
            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { return null; }
        }

        private Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 30);
            b.FlatStyle = FlatStyle.System;
            return b;
        }

        private void LoadItem(bool isNew)
        {
            if (isNew)
            {
                _badge.Text = "新建 · 会存进你自己的提示词库";
                _btnDelete.Enabled = false;
                _btnRestore.Enabled = false;
                _name.Text = "";
                _body.Text = "";
                _tokens.Value = 1800;
                _name.Focus();
                return;
            }
            PromptItem it = PromptLib.Get(_id);
            _builtin = it.Builtin;
            _badge.Text = _builtin ? "内置提示词（可以改，也能恢复默认）" : "自定义提示词";
            _name.Text = it.Label;
            _body.Text = it.Prompt;
            int t = it.MaxTokens;
            if (t < (int)_tokens.Minimum) t = (int)_tokens.Minimum;
            if (t > (int)_tokens.Maximum) t = (int)_tokens.Maximum;
            _tokens.Value = t;
            // 内置方案不给删（删了用户就找不回来），只留「恢复默认」。
            _btnDelete.Visible = !_builtin && PromptLib.Count() > 1;
            _btnRestore.Visible = _builtin;
            _body.Focus();
        }

        private void DoSave()
        {
            string label = _name.Text.Trim();
            string prompt = _body.Text.Trim();
            int tokens = (int)_tokens.Value;
            if (prompt.Length < 4)
            {
                _status.Text = "提示词太短了，至少写一句话";
                _status.ForeColor = Color.FromArgb(198, 60, 60);
                return;
            }
            string error;
            if (_id.Length == 0)
            {
                PromptItem added = PromptLib.Add(label, prompt, tokens, out error);
                if (added == null) { Fail(error); return; }
                _resultId = added.Id;
                _changed = true;
                Log.Write("prompt created id=" + added.Id + " label=" + added.Label);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            if (!PromptLib.Update(_id, label, prompt, tokens, out error)) { Fail(error); return; }
            _changed = true;
            Log.Write("prompt updated id=" + _id + " label=" + label);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void DoDelete()
        {
            if (_builtin)
            {
                _status.ForeColor = Color.FromArgb(198, 60, 60);
                _status.Text = "内置提示词不能删除，可点「恢复默认」改回出厂内容";
                return;
            }
            string error;
            if (!PromptLib.Remove(_id, out error)) { Fail(error); return; }
            _changed = true;
            Log.Write("prompt deleted id=" + _id);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void DoRestore()
        {
            PageMode[] table = PageModes.BuiltinTable();
            for (int i = 0; i < table.Length; i++)
            {
                if (!string.Equals(table[i].Id, _id, StringComparison.Ordinal)) continue;
                _name.Text = table[i].Label;
                _body.Text = table[i].Prompt;
                int t = table[i].MaxTokens;
                if (t < (int)_tokens.Minimum) t = (int)_tokens.Minimum;
                if (t > (int)_tokens.Maximum) t = (int)_tokens.Maximum;
                _tokens.Value = t;
                _status.Text = "已填入出厂内容，点「保存」生效";
                _status.ForeColor = Color.FromArgb(60, 130, 90);
                return;
            }
            _status.Text = "这条不是内置提示词，没有默认内容可恢复";
        }

        private void Fail(string error)
        {
            _status.Text = error == null || error.Length == 0 ? "操作失败" : error;
            _status.ForeColor = Color.FromArgb(198, 60, 60);
        }
    }
}
