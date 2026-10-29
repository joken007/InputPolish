using System;
using System.Collections.Generic;
using System.Text;

namespace InputPolish
{
    public class SummaryResult
    {
        public bool Ok;
        public string Error = "";
        public string Text = "";
        public string Title = "";
        public int Chars;
        public int Paragraphs;
        public int ElapsedMs;
        public int Calls;
        public string Prompt = "";
        public string Raw = "";
    }

    /// <summary>网页总结：抓当前页面正文 →（长文分段提炼）→ 按用户提示词生成最终总结。</summary>
    public static class Summarizer
    {
        public const string SystemPrompt =
            "你是一个严谨的阅读助理，负责把长网页压缩成高信息密度的中文总结。" +
            "只使用文中出现的事实，不要编造、不要添加文中没有的数据。" +
            "不要输出 Markdown 代码块围栏，不要输出「好的」「以下是总结」这类开场白。";

        private const int ChunkChars = 5000;
        private const int MaxChunks = 6;

        public static SummaryResult Run(Config cfg, string promptOverride)
        {
            SummaryResult r = new SummaryResult();
            DateTime t0 = DateTime.UtcNow;
            r.Prompt = string.IsNullOrEmpty(promptOverride) || promptOverride.Trim().Length == 0
                ? cfg.summaryPrompt
                : promptOverride.Trim();

            try
            {
                PageResult page = PageReader.Read(cfg, cfg.pageMaxChars);
                if (!page.Ok)
                {
                    r.Error = page.Error;
                    return r;
                }

                string text = page.Text == null ? "" : page.Text.Trim();
                r.Title = page.Title == null ? "" : page.Title;
                r.Chars = text.Length;
                if (text.Length < 40)
                {
                    r.Error = "这个页面几乎没有可读正文（" + text.Length + " 字）";
                    return r;
                }
                Log.Write("summary page ok title=[" + Log.Preview(r.Title) + "] chars=" + text.Length);

                string material = text;
                if (text.Length > ChunkChars)
                {
                    material = MapReduce(cfg, text, r);
                    if (material == null)
                    {
                        r.Error = "分段提炼失败：" + r.Error;
                        return r;
                    }
                }

                string user = r.Prompt + "\r\n\r\n===== 网页内容" +
                              (r.Title.Length > 0 ? "（标题：" + r.Title + "）" : "") +
                              " =====\r\n" + material;

                string error;
                string finish;
                int attempts;
                string content = Polisher.Chat(cfg, SystemPrompt, user, 2600, out error, out finish, out attempts);
                r.Calls++;
                if (content == null || content.Trim().Length == 0)
                {
                    r.Error = error == null || error.Length == 0 ? "模型没有返回内容（finish=" + finish + "）" : error;
                    return r;
                }

                r.Text = Polisher.CleanOutput(content);
                r.Raw = content;
                r.Ok = true;
                r.ElapsedMs = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                Log.Write("summary ok chars=" + r.Chars + " out=" + r.Text.Length +
                          " calls=" + r.Calls + " ms=" + r.ElapsedMs + " attempts=" + attempts);
                return r;
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                r.ElapsedMs = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                Log.Write("summary error: " + ex);
                return r;
            }
        }

        /// <summary>长文：先分块提炼要点，再把各块要点交给最终总结。返回合并后的材料，失败返回 null。</summary>
        private static string MapReduce(Config cfg, string text, SummaryResult r)
        {
            List<string> chunks = Split(text, ChunkChars, MaxChunks);
            Log.Write("summary map-reduce chunks=" + chunks.Count + " total=" + text.Length);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < chunks.Count; i++)
            {
                string user = "请提炼下面这段网页内容的要点（3-5 条），保留其中的数字、名称、时间与结论，不要评论。" +
                              "\r\n\r\n" + chunks[i];
                string error;
                string finish;
                int attempts;
                string part = Polisher.Chat(cfg, SystemPrompt, user, 1024, out error, out finish, out attempts);
                r.Calls++;
                if (part == null || part.Trim().Length == 0)
                {
                    r.Error = error == null || error.Length == 0 ? "第 " + (i + 1) + " 段提炼无返回（finish=" + finish + "）" : error;
                    return null;
                }
                sb.Append("【第 ").Append(i + 1).Append(" 段要点】\r\n");
                sb.Append(part.Trim());
                sb.Append("\r\n\r\n");
            }
            return sb.ToString();
        }

        private static List<string> Split(string text, int size, int max)
        {
            List<string> list = new List<string>();
            int pos = 0;
            while (pos < text.Length && list.Count < max)
            {
                int len = Math.Min(size, text.Length - pos);
                if (pos + len < text.Length)
                {
                    // 尽量在段落/句子边界切开
                    int cut = text.LastIndexOf("\n", pos + len - 1, len);
                    if (cut <= pos + size / 2) cut = text.LastIndexOf("。", pos + len - 1, len);
                    if (cut > pos + size / 2) len = cut - pos + 1;
                }
                list.Add(text.Substring(pos, len));
                pos += len;
            }
            return list;
        }
    }
}
