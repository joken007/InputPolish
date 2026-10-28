using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace InputPolish
{
    public class SelectionResult
    {
        public bool Ok;
        public string Error = "";
        public string Text = "";
        public string Source = "";
        public string ProcessName = "";
        public string ControlTypeName = "";
    }

    /// <summary>
    /// 读取「当前焦点处被选中的文字」。
    /// 优先 UI Automation 的 TextPattern 选区（不碰剪贴板），失败才退到 Ctrl+C + 读剪贴板（并恢复原剪贴板）。
    /// </summary>
    public static class SelectionReader
    {
        public static SelectionResult Read(Config cfg)
        {
            SelectionResult r = new SelectionResult();

            AutomationElement el = null;
            try
            {
                el = AutomationElement.FocusedElement;
            }
            catch (Exception ex)
            {
                r.Error = "无法获取焦点控件：" + ex.Message;
                return r;
            }
            if (el == null)
            {
                r.Error = "当前没有焦点控件，请先选中要翻译的文字";
                return r;
            }

            try
            {
                r.ControlTypeName = el.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
                int pid = el.Current.ProcessId;
                try { r.ProcessName = Process.GetProcessById(pid).ProcessName; }
                catch { r.ProcessName = ""; }
            }
            catch (Exception ex)
            {
                r.Error = "无法读取焦点控件属性：" + ex.Message;
                return r;
            }

            if (cfg != null && cfg.excludeProcesses != null && r.ProcessName.Length > 0)
            {
                foreach (string p in cfg.excludeProcesses)
                {
                    if (p == null) continue;
                    string t = p.Trim();
                    if (t.Length == 0) continue;
                    if (string.Equals(t, r.ProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        r.Error = "该程序在排除名单中：" + r.ProcessName;
                        return r;
                    }
                }
            }

            bool isPassword = false;
            try { isPassword = el.Current.IsPassword; }
            catch { }
            if (isPassword)
            {
                r.Error = "检测到密码输入框，已拒绝读取";
                return r;
            }

            // 1) 焦点元素自身的 TextPattern 选区
            string sel = TryTextPatternSelection(el);
            if (sel != null && sel.Trim().Length > 0)
            {
                r.Ok = true;
                r.Text = sel;
                r.Source = "TextPattern";
                return r;
            }

            // 2) 往上找 Document（浏览器里焦点常在 Edit 上，而选区属于外层 Document）
            AutomationElement cur = el;
            for (int i = 0; i < 4 && cur != null; i++)
            {
                AutomationElement parent = null;
                try { parent = TreeWalker.ControlViewWalker.GetParent(cur); }
                catch { parent = null; }
                if (parent == null) break;
                cur = parent;
                string s2 = TryTextPatternSelection(cur);
                if (s2 != null && s2.Trim().Length > 0)
                {
                    r.Ok = true;
                    r.Text = s2;
                    r.Source = "TextPattern(parent)";
                    return r;
                }
            }

            // 3) 剪贴板兜底：Ctrl+C 过去，读完把原剪贴板还回去。
            //    关键：必须先记下剪贴板序号，只有「序号变了」才算真的复制到了选区，
            //    否则焦点处没有选区时会读到上一次留在剪贴板里的旧内容（会误译无关文字）。
            string backup = null;
            try { backup = ClipUtil.GetText(); }
            catch { backup = null; }

            uint seqBefore = Native.ClipboardSequence();
            Sender.Copy();

            uint seqAfter = seqBefore;
            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(60);
                seqAfter = Native.ClipboardSequence();
                if (seqAfter != seqBefore) break;
            }

            string copied = null;
            if (seqAfter != seqBefore)
            {
                try { copied = ClipUtil.GetText(); }
                catch { copied = null; }
            }

            if (backup != null && backup.Length > 0) ClipUtil.SetText(backup);

            if (copied != null && copied.Trim().Length > 0)
            {
                r.Ok = true;
                r.Text = copied;
                r.Source = "Clipboard";
                Log.Write("selection via clipboard len=" + copied.Length);
                return r;
            }
            Log.Write("selection via clipboard rejected: seq " + seqBefore + " -> " + seqAfter + " (nothing copied)");

            r.Error = "没有读到选中的文字，请先用鼠标选中一段文字再按热键";
            return r;
        }

        private static string TryTextPatternSelection(AutomationElement el)
        {
            if (el == null) return null;
            try
            {
                object pattern;
                if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) return null;
                TextPattern tp = pattern as TextPattern;
                if (tp == null) return null;

                TextPatternRange[] ranges = tp.GetSelection();
                if (ranges == null || ranges.Length == 0) return null;

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < ranges.Length; i++)
                {
                    string t = null;
                    try { t = ranges[i].GetText(-1); }
                    catch { t = null; }
                    if (t == null || t.Length == 0) continue;
                    if (sb.Length > 0) sb.Append("\n");
                    sb.Append(t);
                }
                if (sb.Length == 0) return null;
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
