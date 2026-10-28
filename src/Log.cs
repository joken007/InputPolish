using System;
using System.IO;
using System.Text;

namespace InputPolish
{
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static void Init(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "log.txt");
            }
            catch
            {
                _path = null;
            }
        }

        public static string LogPath
        {
            get { return _path; }
        }

        public static string Preview(string s)
        {
            if (s == null) return "(null)";
            string t = s.Replace("\r", "\\r").Replace("\n", "\\n");
            if (t.Length > 40) t = t.Substring(0, 40) + "…";
            return t + "|" + s.Length;
        }

        public static void Write(string message)
        {
            if (_path == null) return;
            lock (Gate)
            {
                try
                {
                    FileInfo fi = new FileInfo(_path);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string bak = _path + ".1";
                        if (File.Exists(bak)) File.Delete(bak);
                        File.Move(_path, bak);
                    }
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + "\r\n";
                    File.AppendAllText(_path, line, Encoding.UTF8);
                }
                catch
                {
                }
            }
        }
    }
}
