using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace InputPolish
{
    public static class ClipUtil
    {
        public static string GetText()
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                if (Native.OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        IntPtr h = Native.GetClipboardData(Native.CF_UNICODETEXT);
                        if (h == IntPtr.Zero) return null;
                        IntPtr p = Native.GlobalLock(h);
                        if (p == IntPtr.Zero) return null;
                        try
                        {
                            return Marshal.PtrToStringUni(p);
                        }
                        finally
                        {
                            Native.GlobalUnlock(h);
                        }
                    }
                    finally
                    {
                        Native.CloseClipboard();
                    }
                }
                Thread.Sleep(30);
            }
            return null;
        }

        public static bool SetText(string text)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                if (Native.OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        Native.EmptyClipboard();
                        if (text == null) return true;
                        int bytes = (text.Length + 1) * 2;
                        IntPtr h = Native.GlobalAlloc(Native.GMEM_MOVEABLE, new UIntPtr((uint)bytes));
                        if (h == IntPtr.Zero) return false;
                        IntPtr p = Native.GlobalLock(h);
                        if (p == IntPtr.Zero)
                        {
                            Native.GlobalFree(h);
                            return false;
                        }
                        try
                        {
                            Marshal.Copy(text.ToCharArray(), 0, p, text.Length);
                            Marshal.WriteInt16(p, text.Length * 2, 0);
                        }
                        finally
                        {
                            Native.GlobalUnlock(h);
                        }
                        if (Native.SetClipboardData(Native.CF_UNICODETEXT, h) == IntPtr.Zero)
                        {
                            Native.GlobalFree(h);
                            return false;
                        }
                        return true;
                    }
                    finally
                    {
                        Native.CloseClipboard();
                    }
                }
                Thread.Sleep(30);
            }
            return false;
        }
    }
}
