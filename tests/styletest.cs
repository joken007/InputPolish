using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace InputPolish
{
    internal static class StyleTest
    {
        private static StringBuilder _o = new StringBuilder();

        private static void Say(string s)
        {
            _o.AppendLine(s);
        }

        [STAThread]
        private static void Main(string[] args)
        {
            try { Body(args); }
            catch (Exception ex) { Say("EXCEPTION: " + ex.ToString()); }
            string p = Path.Combine(@"C:\Users\Administrator\AppData\Local\InputPolishTest", "styletest.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, _o.ToString(), Encoding.UTF8);
        }

        private static void Body(string[] args)
        {
            Config cfg = Config.Defaults();
            Log.Init(Config.Dir);
            cfg.baseUrl = "http://127.0.0.1:3425/v1";
            cfg.model = "auto/deepseek-v4.1-flash";
            cfg.timeoutMs = 60000;

            string draft = "这个方案有点问题主要是成本太高而且交付周期长";
            string[] ids = new string[] { Styles.Default, Styles.Shorter, Styles.Formal, Styles.English };
            foreach (string id in ids)
            {
                cfg.polishStyle = id;
                string label = Styles.Label(id);
                long t0 = DateTime.UtcNow.Ticks;
                PolishResult r = Polisher.Polish(cfg, draft);
                int ms = (int)((DateTime.UtcNow.Ticks - t0) / 10000);
                Say("=== style=" + id + " (" + label + ") ok=" + r.Ok + " ms=" + ms);
                if (!r.Ok) { Say("    error: " + r.Error); continue; }
                Say("    out: " + r.Text.Replace("\r", "").Replace("\n", " / "));
            }

            cfg.translateTarget = Translate.Auto;
            TransResult t1 = Translate.Selection(cfg, "The deployment pipeline needs two more days because the database migration is risky.");
            Say("=== translate en->zh auto ok=" + t1.Ok + " target=" + t1.Target + " ms=" + t1.ElapsedMs);
            if (t1.Ok)
            {
                Say("    translation: " + t1.Translation.Replace("\n", " / "));
                Say("    explanation: " + (t1.Explanation == null ? "" : t1.Explanation.Replace("\n", " / ")));
            }
            else Say("    error: " + t1.Error);

            cfg.translateTarget = Translate.Auto;
            TransResult t2 = Translate.Selection(cfg, "这个接口的响应时间不稳定，因为缓存命中率太低。");
            Say("=== translate zh->en auto ok=" + t2.Ok + " target=" + t2.Target + " ms=" + t2.ElapsedMs);
            if (t2.Ok)
            {
                Say("    translation: " + t2.Translation.Replace("\n", " / "));
                Say("    explanation: " + (t2.Explanation == null ? "" : t2.Explanation.Replace("\n", " / ")));
            }
            else Say("    error: " + t2.Error);

            System.Collections.Generic.List<string> paras = new System.Collections.Generic.List<string>();
            paras.Add("输入框优化插件需要在任何软件里可用。");
            paras.Add("它通过全局键盘钩子识别三连空格。");
            paras.Add("识别成功后用 UI Automation 读取文本并调用模型。");
            string err;
            System.Collections.Generic.List<string> dst = new System.Collections.Generic.List<string>();
            bool bok = Translate.Batch(cfg, paras, Translate.En, dst, out err);
            Say("=== batch ok=" + bok + " count=" + dst.Count + " err=" + err);
            for (int i = 0; i < dst.Count; i++) Say("    [" + i + "] " + dst[i]);
        }
    }
}
