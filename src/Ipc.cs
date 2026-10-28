using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace InputPolish
{
    // 单实例命令通道：第二次启动 exe 时把 --polish / --undo / --settings 转交给已在运行的实例
    internal static class Ipc
    {
        public const string PipeName = "InputPolish_cmd_7F3A21";

        public static bool Send(string command)
        {
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(2500);
                    byte[] payload = Encoding.UTF8.GetBytes(command);
                    client.Write(payload, 0, payload.Length);
                    client.Flush();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void StartServer(Action<string> handler)
        {
            if (handler == null) return;
            Thread thread = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        using (NamedPipeServerStream server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1))
                        {
                            server.WaitForConnection();
                            byte[] buffer = new byte[64];
                            int read = server.Read(buffer, 0, buffer.Length);
                            if (read > 0)
                            {
                                string command = Encoding.UTF8.GetString(buffer, 0, read).Trim();
                                if (command.Length > 0) handler(command);
                            }
                        }
                    }
                    catch
                    {
                        Thread.Sleep(300);
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = "InputPolishIpc";
            thread.Start();
        }
    }
}
