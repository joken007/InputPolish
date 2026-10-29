using System;
using System.Collections.Generic;
using System.Text;

namespace InputPolish
{
    public class AskResult
    {
        public bool Ok;
        public string Error = "";
        public string Answer = "";
        public string Question = "";
        public string Title = "";
        public int Chars;
        public int ElapsedMs;
        public int Calls;
        public string Raw = "";
    }

    /// <summary>
    /// 网页问答：拿当前页面正文回答一个问题。
    /// 长页面不是无脑截断，而是按问题里的关键词挑最相关的几段送进模型（省 token 也更准）。
    /// </summary>
    public static class PageAsker
    {
        public const string SystemPrompt =
            "你是一个严谨的阅读助理，只依据用户提供的网页内容回答问题。" +
            "文中没有的信息必须明确说「文中没有提到」，绝不能用你自己的先验知识补足。" +
            "不要输出 Markdown 代码块围栏，不要写「好的」「根据您提供的网页」这类开场白。";

        private const int ChunkChars = 1400;
        private const int BudgetChars = 9000;
        private const int MaxTokens = 1600;

        /// <summary>抓当前前台网页（UIA）再问答。</summary>
        public static AskResult AskPage(Config cfg, string question)
        {
            AskResult r = new AskResult();
            r.Question = question == null ? "" : question.Trim();
            PageResult page;
            try
            {
                page = PageReader.Read(cfg, cfg.pageMaxChars);
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                Log.Write("ask page error: " + ex);
                return r;
            }
            if (!page.Ok)
            {
                r.Error = page.Error;
                return r;
            }
            return Ask(cfg, page.Text, page.Title, question);
        }

        /// <summary>对给定网页正文提问（浏览器扩展 / 本机接口走这里）。</summary>
        public static AskResult Ask(Config cfg, string text, string title, string question)
        {
            DateTime t0 = DateTime.UtcNow;
            AskResult r = new AskResult();
            r.Question = question == null ? "" : question.Trim();
            r.Title = title == null ? "" : title;
            string body = text == null ? "" : text.Trim();
            r.Chars = body.Length;
            if (r.Question.Length == 0)
            {
                r.Error = "请先写下要问的问题";
                return r;
            }
            if (r.Chars < 40)
            {
                r.Error = "这个页面几乎没有可读正文（" + r.Chars + " 字）";
                return r;
            }

            try
            {
                string material = PickRelevant(body, r.Question, BudgetChars);
                string user = "问题：" + r.Question + "\r\n\r\n" +
                              "回答要求：先用 1-3 句给出直接答案，再用「依据：」开头列 2-4 条原文关键句。" +
                              "文中没有的内容要直接说「文中没有提到」。\r\n\r\n" +
                              "===== 网页内容" + (r.Title.Length > 0 ? "（标题：" + r.Title + "）" : "") + " =====\r\n" +
                              material;

                string error;
                string finish;
                int attempts;
                int tmo = cfg.timeoutMs;
            if (tmo < 60000) tmo = 60000;
            string content = Polisher.ChatEx(cfg, SystemPrompt, user, MaxTokens, tmo, out error, out finish, out attempts);
                r.Calls++;
                if (content == null || content.Trim().Length == 0)
                {
                    r.Error = error == null || error.Length == 0 ? "模型没有返回内容（finish=" + finish + "）" : error;
                    return r;
                }
                r.Answer = Polisher.CleanOutput(content);
                r.Raw = content;
                r.Ok = true;
                r.ElapsedMs = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                Log.Write("ask ok chars=" + r.Chars + " q=" + Log.Preview(r.Question) +
                          " out=" + r.Answer.Length + " ms=" + r.ElapsedMs + " attempts=" + attempts);
                return r;
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                r.ElapsedMs = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                Log.Write("ask error: " + ex);
                return r;
            }
        }

        /// <summary>按问题关键词给各段打分，挑最相关的段落凑到 budget 字（顺带保留开头一小段当背景）。</summary>
        private static string PickRelevant(string text, string question, int budget)
        {
            if (text.Length <= budget) return text;

            List<string> chunks = Split(text, ChunkChars);
            string[] keys = Keywords(question);
            if (keys.Length == 0)
            {
                return text.Substring(0, budget);
            }

            List<int> order = new List<int>();
            List<int> scores = new List<int>();
            for (int i = 0; i < chunks.Count; i++)
            {
                int sc = 0;
                string low = chunks[i].ToLowerInvariant();
                for (int k = 0; k < keys.Length; k++)
                {
                    if (keys[k].Length == 0) continue;
                    int at = 0;
                    while (true)
                    {
                        int hit = low.IndexOf(keys[k], at, StringComparison.Ordinal);
                        if (hit < 0) break;
                        sc += keys[k].Length >= 2 ? 3 : 1;
                        at = hit + keys[k].Length;
                        if (sc > 400) break;
                    }
                }
                order.Add(i);
                scores.Add(sc);
            }

            // 简单选择排序：分数高的先拿，同分保持原文顺序
            bool[] taken = new bool[chunks.Count];
            StringBuilder sb = new StringBuilder();
            List<int> picked = new List<int>();

            // 背景段：第一段（通常是导语）先放进去
            if (chunks.Count > 0)
            {
                taken[0] = true;
                picked.Add(0);
                sb.Append(chunks[0]).Append("\r\n\r\n");
            }

            while (sb.Length < budget)
            {
                int best = -1;
                int bestScore = -1;
                for (int i = 0; i < order.Count; i++)
                {
                    int idx = order[i];
                    if (taken[idx]) continue;
                    if (scores[i] > bestScore)
                    {
                        bestScore = scores[i];
                        best = idx;
                    }
                }
                if (best < 0) break;
                if (bestScore <= 0 && picked.Count > 3) break;
                taken[best] = true;
                picked.Add(best);
                sb.Append(chunks[best]).Append("\r\n\r\n");
            }

            picked.Sort();
            Log.Write("ask pick chunks=" + chunks.Count + " picked=" + picked.Count + " chars=" + sb.Length);
            return sb.ToString().Trim();
        }

        /// <summary>把问题切成检索关键词：英文按词、中文按二元组。</summary>
        private static string[] Keywords(string question)
        {
            List<string> list = new List<string>();
            string q = (question == null ? "" : question).ToLowerInvariant();

            StringBuilder word = new StringBuilder();
            for (int i = 0; i < q.Length; i++)
            {
                char c = q[i];
                bool ascii = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (ascii)
                {
                    word.Append(c);
                }
                else
                {
                    if (word.Length >= 2) list.Add(word.ToString());
                    word.Length = 0;
                }
            }
            if (word.Length >= 2) list.Add(word.ToString());

            // 中文二元组（去掉标点和空白后的连续汉字串）
            StringBuilder run = new StringBuilder();
            for (int i = 0; i < q.Length; i++)
            {
                char c = q[i];
                bool cjk = c >= 0x4E00 && c <= 0x9FFF;
                if (cjk)
                {
                    run.Append(c);
                }
                else
                {
                    AddBigrams(list, run.ToString());
                    run.Length = 0;
                }
            }
            AddBigrams(list, run.ToString());

            // 常见虚词二元组没有区分度，去掉
            string[] stop = new string[] { "什么", "怎么", "为什么", "这个", "那个", "可以", "一下", "以及", "还有", "是否", "哪些", "如何" };
            List<string> filtered = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                string k = list[i];
                bool skip = false;
                for (int s = 0; s < stop.Length; s++)
                {
                    if (k == stop[s]) { skip = true; break; }
                }
                if (!skip && k.Length >= 2) filtered.Add(k);
            }
            return filtered.ToArray();
        }

        private static void AddBigrams(List<string> list, string run)
        {
            if (run == null) return;
            for (int i = 0; i + 1 < run.Length; i++)
            {
                list.Add(run.Substring(i, 2));
            }
            if (run.Length == 1) list.Add(run);
        }

        private static List<string> Split(string text, int size)
        {
            List<string> list = new List<string>();
            int pos = 0;
            while (pos < text.Length)
            {
                int len = Math.Min(size, text.Length - pos);
                if (pos + len < text.Length)
                {
                    int cut = text.LastIndexOf("\n", pos + len - 1, len);
                    if (cut > pos + size / 2) len = cut - pos + 1;
                }
                list.Add(text.Substring(pos, len));
                pos += len;
            }
            return list;
        }
    }
}
