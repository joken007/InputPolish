using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;

internal static class IpcSend
{
    [STAThread]
    private static int Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0] : "settings";
        try
        {
            using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", "InputPolish_cmd_7F3A21", PipeDirection.Out))
            {
                pipe.Connect(2500);
                byte[] payload = Encoding.UTF8.GetBytes(cmd);
                pipe.Write(payload, 0, payload.Length);
                pipe.Flush();
            }
            Console.WriteLine("sent=" + cmd);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("fail: " + ex.Message);
            return 1;
        }
    }
}
