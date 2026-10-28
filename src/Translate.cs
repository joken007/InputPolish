using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace InputPolish
{
    public class TransResult
    {
        public bool Ok;
        public string Error = "";
        public string Translation = "";
        public string Explanation = "";
        public string Target = "";
        public int ElapsedMs;
        public string Raw = "";
    }

    /// <summary>翻译相关的提示词、目标语言判定与批量协议。</summary>
    public static class Translate
    {
        public const string Auto = "auto";
        public const string Zh = "zh";
        public const string En = "en";

        public static string NormalizeTarget(string t)
        {
            if (t == null) return Auto;
            string s = t.Trim().ToLowerInvariant();
            if (s == Zh || s == "chinese" || s == "cn" || s == "中文") return Zh;
            if (s == En || s == "english" || s == "en-us" || s == "英文") return En;
            return Auto;
        }

        public static string TargetLabel(string target)
        {
            string t = NormalizeTarget(target);
            if (t == En) return "英文";
            if (t == Zh) return "简体中文";
            return "简体中文";
        }

        /// <summary>按字符统计猜原文语种：CJK 占比高就当它是中文，否则当外文处理。</summary>
        public static bool LooksChinese(string text)
        {
            if (text == null) return false;
            int cjk = 0;
            int latin = 0;
            int limit = Math.Min(text.Length, 800);
            for (int i = 0; i < limit; i++)
            {
                char c = text[i];
                if (c >= 0x4E00 && c <= 0x9FFF) cjk++;
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            }
            if (cjk == 0) return false;
            return cjk >= latin / 2;
        }

        /// <summary>auto 时按样本内容决定目标语言：中文→英文，其它→中文。</summary>
        public static string ResolveTarget(string cfgTarget, string sample)
        {
            string t = NormalizeTarget(cfgTarget);
            if (t == Zh || t == En) return t;
            return LooksChinese(sample) ? En : Zh;
        }

        private static string SelectionSystem(string targetLabel, bool explain)
        {
            string s = "你是专业翻译。把用户选中的文本翻译成" + targetLabel + "。\n\n" +
                "规则：\n" +
                "1. 忠实翻译：不增删信息，不改变原文立场与语气强度，不替用户补充内容。\n" +
                "2. 专有名词、品牌名、人名、代码、命令、变量名、URL、邮箱、数字与单位保留原样。\n" +
                "3. 直接输出译文本身：不要加『译文：』之类的标签，不要前言或总结，不要用代码块包裹。\n";
            if (explain)
            {
                s += "4. 译文结束后空一行，然后以「讲解：」开头给出 1-4 条简短讲解，每条以「- 」开头：" +
                     "说明关键词的译法取舍、语气与正式程度、以及原文里可能隐含的语境或文化含义。" +
                     "如果原文很直白、没有值得讲解的地方，就只输出译文，不要写「讲解：」。";
            }
            else
            {
                s += "4. 只输出译文，不要解释。";
            }
            return s;
        }

        /// <summary>划词翻译：返回译文 + 讲解。</summary>
        public static TransResult Selection(Config cfg, string text)
        {
            TransResult r = new TransResult();
            DateTime t0 = DateTime.UtcNow;

            string src = text == null ? "" : text.Trim();
            if (src.Length == 0)
            {
                r.Error = "没有选中任何文字";
                return r;
            }

            string target = ResolveTarget(cfg.translateTarget, src);
            r.Target = TargetLabel(target);

            string system = SelectionSystem(r.Target, cfg.translateExplain);
            string error;
            string finish;
            int attempts;
            string raw = Polisher.Chat(cfg, system, src, 1024, out error, out finish, out attempts);

            r.ElapsedMs = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            if (raw == null)
            {
                r.Error = error;
                Log.Write("translate selection failed: " + error);
                return r;
            }

            r.Raw = raw;
            string translation;
            string explanation;
            SplitExplanation(raw, out translation, out explanation);

            if (translation.Length == 0)
            {
                r.Error = "模型没有返回译文";
                return r;
            }

            r.Ok = true;
            r.Translation = translation;
            r.Explanation = explanation;
            Log.Write("translate selection ok target=" + r.Target + " in=" + src.Length + " out=" + translation.Length +
                      " explain=" + explanation.Length + " ms=" + r.ElapsedMs + " attempts=" + attempts);
            return r;
        }

        /// <summary>把「译文 + 讲解：」拆开。兼容 **讲解：** 与行首『讲解』等写法。</summary>
        public static void SplitExplanation(string raw, out string translation, out string explanation)
        {
            translation = raw == null ? "" : raw.Trim();
            explanation = "";

            if (translation.Length == 0) return;

            string[] markers = new string[] { "讲解：", "讲解:" };
            int cut = -1;
            for (int m = 0; m < markers.Length; m++)
            {
                string mk = markers[m];
                int i = translation.IndexOf(mk, StringComparison.Ordinal);
                while (i >= 0)
                {
                    // 只认「行首（允许前置 * 与空白）」的讲解标记，避免命中正文里的词
                    int lineStart = i;
                    while (lineStart > 0 && (translation[lineStart - 1] == ' ' || translation[lineStart - 1] == '*' ||
                                             translation[lineStart - 1] == '#' || translation[lineStart - 1] == '>'))
                    {
                        lineStart--;
                    }
                    if (lineStart == 0 || translation[lineStart - 1] == '\n')
                    {
                        cut = i;
                        break;
                    }
                    i = translation.IndexOf(mk, i + mk.Length, StringComparison.Ordinal);
                }
                if (cut >= 0) break;
            }

            if (cut < 0) return;

            int markerLen = 3; // 「讲解：」三个字符
            string head = translation.Substring(0, cut).TrimEnd();
            string tail = translation.Substring(cut + markerLen).Trim();

            head = head.TrimEnd('*', '#', '>', ' ');
            tail = tail.TrimStart('*', '#', '>', ' ');
            tail = tail.Replace("**", "");

            translation = head;
            explanation = tail;
        }

        /// <summary>纯翻译（无讲解），失败返回 null。</summary>
        public static string Simple(Config cfg, string text, string target, out string error)
        {
            error = "";
            string system = SelectionSystem(TargetLabel(target), false);
            string finish;
            int attempts;
            return Polisher.Chat(cfg, system, text, 512, out error, out finish, out attempts);
        }

        private static string BatchSystem(string targetLabel)
        {
            return "你是专业翻译引擎。用户给出用 [[n]] 标记的若干段落，请逐段翻译成" + targetLabel + "。\n\n" +
                "输出格式（必须严格遵守）：\n" +
                "- 使用与输入完全相同的标记，一段一个标记，顺序不变。\n" +
                "- 不要合并段落、不要跳过任何标记、不要写任何解释或说明。\n" +
                "- 纯数字、URL、代码片段、符号原样返回。\n" +
                "- 保留原文的分段结构：译文之间用换行分隔。";
        }

        private static readonly Regex MarkerRegex = new Regex(@"\[\[(\d+)\]\]", RegexOptions.Compiled);

        /// <summary>
        /// 批量翻译：段落按 [[n]] 标记一次请求发出去。
        /// 返回与 paras 等长的译文列表；解析不到的段落会退化为逐段翻译。
        /// </summary>
        public static bool Batch(Config cfg, List<string> paras, string target, List<string> dst, out string error)
        {
            error = "";
            dst.Clear();
            for (int i = 0; i < paras.Count; i++) dst.Add("");

            if (paras.Count == 0) return true;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < paras.Count; i++)
            {
                sb.Append("[[");
                sb.Append(i + 1);
                sb.Append("]] ");
                sb.Append(paras[i].Replace("[[", "[ ["));
                sb.Append("\n");
            }

            string finish;
            int attempts;
            string raw = Polisher.Chat(cfg, BatchSystem(TargetLabel(target)), sb.ToString(), 1024, out error, out finish, out attempts);

            if (raw != null)
            {
                MatchCollection ms = MarkerRegex.Matches(raw);
                if (ms.Count > 0)
                {
                    for (int k = 0; k < ms.Count; k++)
                    {
                        int id = 0;
                        try { id = Convert.ToInt32(ms[k].Groups[1].Value); }
                        catch { continue; }
                        if (id < 1 || id > paras.Count) continue;

                        int start = ms[k].Index + ms[k].Length;
                        int end = (k + 1 < ms.Count) ? ms[k + 1].Index : raw.Length;
                        if (end <= start) continue;

                        string body = raw.Substring(start, end - start).Trim();
                        if (body.Length > 0) dst[id - 1] = body;
                    }
                }
            }

            int missing = 0;
            for (int i = 0; i < dst.Count; i++) if (dst[i].Length == 0) missing++;
            if (missing == 0) return true;

            // 退化为逐段翻译；只有整批失败时才把错误往外抛。
            for (int i = 0; i < dst.Count; i++)
            {
                if (dst[i].Length > 0) continue;
                string perError;
                string one = Simple(cfg, paras[i], target, out perError);
                if (one != null && one.Trim().Length > 0) dst[i] = one.Trim();
                else if (error.Length == 0) error = perError;
            }

            for (int i = 0; i < dst.Count; i++) if (dst[i].Length > 0) return true;
            return false;
        }

        /// <summary>
        /// 把整页文本切成段落：按行切，丢掉空行与纯符号行，
        /// 并把「上一行没结束（没有句末标点）且本行很短」的碎片合并回上一段。
        /// </summary>
        public static List<string> SplitParagraphs(string pageText)
        {
            List<string> result = new List<string>();
            if (pageText == null) return result;

            string[] rawLines = pageText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder cur = new StringBuilder();

            for (int i = 0; i < rawLines.Length; i++)
            {
                string line = rawLines[i];
                if (line == null) continue;
                line = line.Trim().Trim('\u00a0');
                if (line.Length == 0)
                {
                    Flush(result, cur);
                    continue;
                }
                if (!HasContent(line)) continue;

                if (cur.Length == 0)
                {
                    cur.Append(line);
                }
                else if (ShouldMerge(cur.ToString(), line))
                {
                    cur.Append(' ');
                    cur.Append(line);
                }
                else
                {
                    Flush(result, cur);
                    cur.Append(line);
                }
            }
            Flush(result, cur);
            return result;
        }

        private static void Flush(List<string> result, StringBuilder cur)
        {
            if (cur.Length == 0) return;
            string s = cur.ToString().Trim();
            if (s.Length > 0) result.Add(s);
            cur.Length = 0;
        }

        private static bool HasContent(string line)
        {
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (char.IsLetterOrDigit(c)) return true;
                if (c >= 0x4E00 && c <= 0x9FFF) return true;
            }
            return false;
        }

        private static bool ShouldMerge(string head, string next)
        {
            if (next.Length > 30) return false;
            if (head.Length < 12) return false;
            char last = head[head.Length - 1];
            if (last == '.' || last == '!' || last == '?' || last == ':' || last == ';' ||
                last == '\u3002' || last == '\uff01' || last == '\uff1f' || last == '\uff1a' || last == '\uff1b' ||
                last == '\u201d' || last == '\u300d' || last == ')' || last == '\uff09') return false;
            return true;
        }
    }
}
