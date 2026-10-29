using System;
using System.IO;
using System.Text;

namespace InputPolish
{
    /// <summary>把总结 / 问答结果存成本地 Markdown 笔记，方便以后翻。</summary>
    public static class Notes
    {
        public static string Dir()
        {
            return Path.Combine(Config.Dir, "notes");
        }

        /// <summary>保存一条笔记，返回文件路径；失败返回 null 并给出 error。</summary>
        public static string Save(string title, string body, string kind, string url, out string error)
        {
            error = "";
            try
            {
                string dir = Dir();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string slug = Slug(title);
                string file = Path.Combine(dir, stamp + (slug.Length > 0 ? "-" + slug : "") + ".md");

                StringBuilder sb = new StringBuilder();
                if (title != null && title.Trim().Length > 0)
                {
                    sb.Append("# ").Append(title.Trim()).Append("\r\n\r\n");
                }
                sb.Append("> 类型：").Append(kind == null || kind.Length == 0 ? "网页总结" : kind).Append("\r\n");
                if (url != null && url.Trim().Length > 0)
                {
                    sb.Append("> 来源：").Append(url.Trim()).Append("\r\n");
                }
                sb.Append("> 生成：").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("  ·  InputPolish\r\n\r\n");
                sb.Append("---\r\n\r\n");
                sb.Append((body == null ? "" : body.Trim())).Append("\r\n");

                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
                Log.Write("note saved file=" + file + " chars=" + (body == null ? 0 : body.Length));
                return file;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Write("note save failed: " + ex);
                return null;
            }
        }

        /// <summary>标题转成安全的文件名片段（去掉非法字符，限长 40）。</summary>
        private static string Slug(string title)
        {
            if (title == null) return "";
            StringBuilder sb = new StringBuilder();
            char[] bad = new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\r', '\n', '\t' };
            for (int i = 0; i < title.Length && sb.Length < 40; i++)
            {
                char c = title[i];
                bool skip = false;
                for (int b = 0; b < bad.Length; b++)
                {
                    if (c == bad[b]) { skip = true; break; }
                }
                if (!skip && c != ' ') sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
