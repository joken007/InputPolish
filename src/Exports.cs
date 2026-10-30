using System;
using System.IO;
using System.Text;

namespace InputPolish
{
    /// <summary>把网页正文导出成 Markdown 文件，默认落在 %APPDATA%\InputPolish\exports\。</summary>
    public static class Exports
    {
        public static string Dir()
        {
            return Path.Combine(Config.Dir, "exports");
        }

        /// <summary>写一份导出文件，返回路径；失败返回 null 并给出 error。</summary>
        public static string Save(string title, string markdown, out string error)
        {
            error = "";
            try
            {
                string dir = Dir();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string slug = Notes.Slug(title);
                string file = Path.Combine(dir, stamp + (slug.Length > 0 ? "-" + slug : "") + ".md");

                File.WriteAllText(file, markdown == null ? "" : markdown, new UTF8Encoding(false));
                Log.Write("export saved file=" + file + " chars=" + (markdown == null ? 0 : markdown.Length));
                return file;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Write("export save failed: " + ex);
                return null;
            }
        }
    }
}
