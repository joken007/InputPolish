using System;
using System.Threading;
using System.Windows.Forms;

namespace InputPolish
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Config cfg = Config.Load();
            Log.Init(Config.Dir);

            if (HasArg(args, "--selftest"))
            {
                SelfTest.Run(cfg);
                return;
            }

            // 第二实例把命令转交给已在运行的实例（润色 / 撤销 / 打开设置）
            if (SendToRunningInstance(args)) return;

            bool created;
            using (Mutex mutex = new Mutex(true, "InputPolish_SingleInstance_7F3A21", out created))
            {
                if (!created)
                {
                    MessageBox.Show("InputPolish 已经在运行。\n\n请在屏幕右下角的悬浮框或系统托盘里操作。",
                        "InputPolish", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Log.Write("=== InputPolish start === acceptInjected=" +
                          (Environment.GetEnvironmentVariable("INPUTPOLISH_ACCEPT_INJECTED") == "1") +
                          " (env INPUTPOLISH_ACCEPT_INJECTED)");

                try
                {
                    Application.Run(new TrayApp(cfg));
                }
                catch (Exception ex)
                {
                    Log.Write("fatal: " + ex);
                    MessageBox.Show("InputPolish 启动失败：" + ex.Message, "InputPolish");
                }

                Log.Write("=== InputPolish exit ===");
            }
        }

        private static bool SendToRunningInstance(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            string command = null;
            foreach (string a in args)
            {
                string s = a == null ? "" : a.Trim().ToLowerInvariant();
                if (s == "--polish") { command = "polish"; break; }
                if (s == "--undo") { command = "undo"; break; }
                if (s == "--settings") { command = "settings"; break; }
            }
            if (command == null) return false;

            try
            {
                return Ipc.Send(command);
            }
            catch (Exception ex)
            {
                Log.Write("ipc send failed: " + ex.Message);
                return false;
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string a in args)
            {
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
