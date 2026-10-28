using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace InputPolish
{
    public static class SelfTest
    {
        private static StringBuilder _out = new StringBuilder();

        private static void Say(string line)
        {
            _out.AppendLine(line);
        }

        public static void Run(Config cfg)
        {
            try
            {
                Body(cfg);
            }
            catch (Exception ex)
            {
                Say("");
                Say("FATAL " + ex);
            }

            string text = _out.ToString();
            try
            {
                if (!Directory.Exists(Config.Dir)) Directory.CreateDirectory(Config.Dir);
                File.WriteAllText(Path.Combine(Config.Dir, "selftest.txt"), text, Encoding.UTF8);
            }
            catch
            {
            }
            Log.Write("selftest:\n" + text);
        }

        private static void Body(Config cfg)
        {
            Say("=== InputPolish selftest ===");
            Say("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Say("exe: " + System.Windows.Forms.Application.ExecutablePath);
            Say("config: " + Config.FilePath);
            Say("log: " + Log.LogPath);
            Say("os: " + Environment.OSVersion.VersionString + " x64=" + Environment.Is64BitProcess);
            Say("");

            Say("[config]");
            Say("  enabled=" + cfg.enabled + " model=" + cfg.model);
            Say("  baseUrl=" + cfg.baseUrl + " apiKey=" + (cfg.apiKey.Length > 0 ? "(set)" : "(empty)"));
            Say("  trigger=" + cfg.trigger.hotkey + " spaceCount=" + cfg.trigger.spaceCount +
                " windowMs=" + cfg.trigger.windowMs + " spaceTriple=" + cfg.trigger.spaceTriple);
            Say("  undo=" + cfg.trigger.undoHotkey + " maxTokens=" + cfg.maxTokens + " timeoutMs=" + cfg.timeoutMs);
            Say("  exclude=" + string.Join(",", cfg.excludeProcesses.ToArray()));
            Say("");

            Say("[keyboard hook]");
            KeyHook hook = new KeyHook();
            bool installed = hook.Install();
            Say("  install=" + installed + " err=" + Marshal.GetLastWin32Error());
            hook.Uninstall();
            Say("  uninstalled");
            Say("");

            Say("[hotkey parse]");
            int mods;
            int vk;
            Say("  " + cfg.trigger.hotkey + " -> " + HotkeyWindow.ParseHotkey(cfg.trigger.hotkey, out mods, out vk) +
                " mods=" + mods + " vk=" + vk);
            Say("  " + cfg.trigger.undoHotkey + " -> " + HotkeyWindow.ParseHotkey(cfg.trigger.undoHotkey, out mods, out vk) +
                " mods=" + mods + " vk=" + vk);
            Say("");

            Say("[clipboard]");
            string before = ClipUtil.GetText();
            bool clipOk = ClipUtil.SetText("InputPolish-clip-test-\u4e2d\u6587");
            string readback = ClipUtil.GetText();
            Say("  write=" + clipOk + " readback=[" + (readback == null ? "null" : readback) + "]");
            ClipUtil.SetText(before == null ? "" : before);
            Say("  restored=" + (before == null ? "(null)" : "(" + before.Length + " chars)"));
            Say("");

            Say("[backend]");
            DateTime t0 = DateTime.UtcNow;
            string error;
            int rawStatus;
            string rawErr;
            string raw = Polisher.HttpGet(Polisher.Endpoint(cfg.baseUrl, "/models"), cfg.apiKey, 8000, out rawStatus, out rawErr);
            Say("  raw GET status=" + rawStatus + " len=" + (raw == null ? -1 : raw.Length) + " err=" + rawErr);
            if (raw != null && raw.Length > 0) Say("  raw head: " + raw.Substring(0, Math.Min(160, raw.Length)));
            List<string> models = Polisher.ListModels(cfg, out error);
            int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            Say("  GET /models -> " + models.Count + " models in " + ms + "ms" + (error.Length > 0 ? " err=" + error : ""));
            if (models.Count > 0)
            {
                Say("  first: " + string.Join(", ", models.GetRange(0, Math.Min(6, models.Count)).ToArray()));
            }
            Say("");

            Say("[polish call]");
            PolishResult pr = Polisher.Polish(cfg, "\u8fd9\u4e2a\u65b9\u6848\u6709\u70b9\u95ee\u9898\u4e3b\u8981\u662f\u6210\u672c\u592a\u9ad8\u800c\u4e14\u4ea4\u4ed8\u5468\u671f\u957f");
            Say("  ok=" + pr.Ok + " ms=" + pr.ElapsedMs + " attempts=" + pr.Attempts + (pr.Ok ? "" : " err=" + pr.Error));
            if (pr.Raw != null && pr.Raw.Length > 0) Say("  raw: " + pr.Raw.Substring(0, Math.Min(700, pr.Raw.Length)));
            if (pr.Ok)
            {
                Say("  output:");
                foreach (string line in pr.Text.Replace("\r\n", "\n").Split('\n'))
                {
                    Say("    " + line);
                }
            }
            Say("");

            Say("[uia focus]");
            string focus = "";
            Thread th = new Thread(delegate()
            {
                try { focus = UiaIo.DescribeFocused(); }
                catch (Exception ex) { focus = "EX " + ex.Message; }
            });
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.MTA);
            th.Start();
            if (!th.Join(8000)) focus = "(timeout)";
            Say("  " + focus);
            Say("");

            Say("[autostart] " + (AutoStart.IsEnabled() ? "enabled" : "disabled"));
            Say("=== done ===");
        }
    }
}
