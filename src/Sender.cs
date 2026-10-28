using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace InputPolish
{
    public static class Sender
    {
        private static Native.INPUT MakeKey(int vk, bool up)
        {
            Native.INPUT input = new Native.INPUT();
            input.type = Native.INPUT_KEYBOARD;
            input.u.ki.wVk = (ushort)vk;
            input.u.ki.wScan = 0;
            input.u.ki.dwFlags = up ? Native.KEYEVENTF_KEYUP : 0;
            input.u.ki.time = 0;
            input.u.ki.dwExtraInfo = IntPtr.Zero;
            return input;
        }

        public static void SendCtrl(int vk)
        {
            Native.INPUT[] inputs = new Native.INPUT[4];
            inputs[0] = MakeKey(Native.VK_CONTROL, false);
            inputs[1] = MakeKey(vk, false);
            inputs[2] = MakeKey(vk, true);
            inputs[3] = MakeKey(Native.VK_CONTROL, true);
            Native.SendInput(4, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public static void SendKey(int vk)
        {
            Native.INPUT[] inputs = new Native.INPUT[2];
            inputs[0] = MakeKey(vk, false);
            inputs[1] = MakeKey(vk, true);
            Native.SendInput(2, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public static void SelectAll()
        {
            SendCtrl(Native.VK_A);
            Thread.Sleep(70);
        }

        public static void Copy()
        {
            SendCtrl(0x43);
            Thread.Sleep(90);
        }

        public static void Paste()
        {
            SendCtrl(Native.VK_V);
        }

        public static void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                Native.INPUT[] inputs = new Native.INPUT[2];
                inputs[0] = MakeUnicode(ch, false);
                inputs[1] = MakeUnicode(ch, true);
                Native.SendInput(2, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
            }
        }

        private static Native.INPUT MakeUnicode(char ch, bool up)
        {
            Native.INPUT input = new Native.INPUT();
            input.type = Native.INPUT_KEYBOARD;
            input.u.ki.wVk = 0;
            input.u.ki.wScan = ch;
            input.u.ki.dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0);
            input.u.ki.time = 0;
            input.u.ki.dwExtraInfo = IntPtr.Zero;
            return input;
        }
    }
}
