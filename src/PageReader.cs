using System;
using System.Diagnostics;
using System.Windows.Automation;

namespace InputPolish
{
    public class PageResult
    {
        public bool Ok;
        public string Error = "";
        public string Text = "";
        public string Title = "";
        public string ProcessName = "";
        public string Source = "";
        public int Documents;
        public int Chars;
        public int ElapsedMs;
    }

    /// <summary>用 UI Automation 把当前前台窗口的正文读出来（网页 / PDF / 文档阅读器都可以）。</summary>
    public static class PageReader
    {
        public static PageResult Read(Config cfg, int maxChars)
        {
            PageResult r = new PageResult();
            DateTime t0 = DateTime.UtcNow;

            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                r.Error = "没有前台窗口";
                return r;
            }
            r.Title = Native.WindowTitle(hwnd);

            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                r.ProcessName = Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                r.ProcessName = "";
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

            AutomationElement root = null;
            try
            {
                root = AutomationElement.FromHandle(hwnd);
            }
            catch (Exception ex)
            {
                r.Error = "无法连接该窗口的 UI Automation：" + ex.Message;
                return r;
            }
            if (root == null)
            {
                r.Error = "无法连接该窗口的 UI Automation";
                return r;
            }

            string best = null;
            string bestSource = "";

            // 1) 窗口里所有支持 TextPattern 的 Document，取文字最多的那个
            try
            {
                // 注意：不要用 AndCondition(ControlType.Document, IsTextPatternAvailable)，
                // Chromium 对组合条件会返回 0 个结果（单独查 ControlType.Document 却是 1 个）。
                // 这里先按 ControlType 找，再逐个确认 TextPattern。
                Condition cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);

                AutomationElementCollection docs = root.FindAll(TreeScope.Descendants, cond);
                r.Documents = docs.Count;
                for (int i = 0; i < docs.Count; i++)
                {
                    object pat;
                    if (!docs[i].TryGetCurrentPattern(TextPattern.Pattern, out pat)) continue;
                    string s = TextOf(docs[i], maxChars);
                    if (s == null) continue;
                    if (best == null || s.Length > best.Length)
                    {
                        best = s;
                        bestSource = "Document";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("page find documents failed: " + ex.Message);
            }

            // 2) 退一步：当前焦点元素及其祖先的 TextPattern
            if (best == null || best.Length < 40)
            {
                AutomationElement el = null;
                try { el = AutomationElement.FocusedElement; }
                catch { el = null; }
                for (int i = 0; i < 4 && el != null; i++)
                {
                    string s = TextOf(el, maxChars);
                    if (s != null && (best == null || s.Length > best.Length))
                    {
                        best = s;
                        bestSource = "Focused";
                    }
                    try { el = TreeWalker.ControlViewWalker.GetParent(el); }
                    catch { el = null; }
                }
            }

            // 3) 最后兜底：原生控件文本（记事本之类）
            if (best == null || best.Length < 40)
            {
                IntPtr focus = Native.ForegroundFocusHwnd();
                if (focus != IntPtr.Zero)
                {
                    string s = Native.GetControlText(focus);
                    if (s != null && (best == null || s.Length > best.Length))
                    {
                        best = s;
                        bestSource = "Win32";
                    }
                }
            }

            r.ElapsedMs = (int)(DateTime.UtcNow - t0).TotalMilliseconds;

            if (best == null || best.Trim().Length == 0)
            {
                r.Error = "没读到页面正文（" + r.ProcessName + "）。请把焦点点进正文区域后再试";
                Log.Write("page read empty proc=" + r.ProcessName + " docs=" + r.Documents + " ms=" + r.ElapsedMs);
                return r;
            }

            if (best.Length > maxChars) best = best.Substring(0, maxChars);

            r.Ok = true;
            r.Text = best;
            r.Source = bestSource;
            r.Chars = best.Length;
            Log.Write("page read ok proc=" + r.ProcessName + " source=" + bestSource + " docs=" + r.Documents +
                      " chars=" + r.Chars + " ms=" + r.ElapsedMs + " title=[" + Log.Preview(r.Title) + "]");
            return r;
        }

        private static string TextOf(AutomationElement el, int maxChars)
        {
            if (el == null) return null;
            try
            {
                object pattern;
                if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) return null;
                TextPattern tp = pattern as TextPattern;
                if (tp == null) return null;

                string s = tp.DocumentRange.GetText(maxChars + 200);
                if (s == null) return null;
                s = s.Trim();
                if (s.Length == 0) return null;
                return s;
            }
            catch
            {
                return null;
            }
        }
    }
}
