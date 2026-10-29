using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace InputPolish
{
    internal static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const uint LLMHF_INJECTED = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public int ptX;
            public int ptY;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;

            public POINT ToPoint()
            {
                POINT p = new POINT();
                p.X = ptX;
                p.Y = ptY;
                return p;
            }
        }

        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_HOTKEY = 0x0312;

        public const int VK_TAB = 0x09;
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;
        public const int VK_SPACE = 0x20;
        public const int VK_OEM_3 = 0xC0;   // 反引号 / 波浪号键
        public const int VK_A = 0x41;
        public const int VK_V = 0x56;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;

        public const uint LLKHF_INJECTED = 0x10;
        public const uint LLKHF_LOWER_IL_INJECTED = 0x02;

        public const int WM_GETTEXT = 0x000D;
        public const int EM_SETSEL = 0x00B1;
        public const int WM_SETTEXT = 0x000C;
        public const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        /// <summary>剪贴板序号：每次内容变化都会自增，用来判断「Ctrl+C 到底有没有复制到东西」。</summary>
        public static uint ClipboardSequence()
        {
            try { return GetClipboardSequenceNumber(); }
            catch { return 0; }
        }

        public const int CF_UNICODETEXT = 13;
        public const uint GMEM_MOVEABLE = 0x0002;

        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const int MOD_SHIFT = 0x0004;
        public const int MOD_WIN = 0x0008;
        public const int MOD_NOREPEAT = 0x4000;

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
        public static extern IntPtr SetWindowsHookExMouse(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        /// <summary>屏幕坐标处是否属于本进程的窗口（悬浮球 / 浮标 / 面板），用来避免自触发。</summary>
        public static bool IsOwnWindowAt(int x, int y)
        {
            try
            {
                POINT p = new POINT();
                p.X = x;
                p.Y = y;
                IntPtr h = WindowFromPoint(p);
                if (h == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public const uint GW_HWNDNEXT = 2;

        /// <summary>窗口是否属于本进程（自己的面板 / 提示框 / 阅读窗都要跳过）。</summary>
        public static bool IsOwnProcessWindow(IntPtr hWnd)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>窗口所属进程名，取不到返回空串。</summary>
        public static string ProcessNameOf(IntPtr hWnd)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == 0) return "";
                return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                return "";
            }
        }

        private static readonly System.Collections.Generic.Dictionary<int, string> ProcNameCache =
            new System.Collections.Generic.Dictionary<int, string>();
        private static readonly object ProcNameGate = new object();

        /// <summary>带缓存的进程名查询（给每秒一次的轮询用，避免反复 OpenProcess）。</summary>
        public static string ProcessNameOfCached(IntPtr hWnd)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == 0) return "";
                lock (ProcNameGate)
                {
                    string cached;
                    if (ProcNameCache.TryGetValue((int)pid, out cached)) return cached;
                }
                string name = ProcessNameOf(hWnd);
                if (name.Length > 0)
                {
                    lock (ProcNameGate)
                    {
                        if (ProcNameCache.Count > 400) ProcNameCache.Clear();
                        ProcNameCache[(int)pid] = name;
                    }
                }
                return name;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>按 Z 顺序从最上层往下枚举可见的顶层窗口。</summary>
        public static System.Collections.Generic.List<IntPtr> TopWindows()
        {
            System.Collections.Generic.List<IntPtr> list = new System.Collections.Generic.List<IntPtr>();
            try
            {
                IntPtr h = GetTopWindow(IntPtr.Zero);
                int guard = 0;
                while (h != IntPtr.Zero && guard++ < 600)
                {
                    list.Add(h);
                    h = GetWindow(h, GW_HWNDNEXT);
                }
            }
            catch
            {
            }
            return list;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeoutRead(IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeoutWrite(IntPtr hWnd, int msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessageInt(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder text, int maxCount);

        public static string ClassNameOf(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return "";
            try
            {
                StringBuilder sb = new StringBuilder(256);
                int n = GetClassNameW(hWnd, sb, sb.Capacity);
                return n > 0 ? sb.ToString() : "";
            }
            catch
            {
                return "";
            }
        }

        // 仅原生编辑控件（Edit / RichEdit / Scintilla）才允许整体 WM_SETTEXT，
        // 避免把整段文本发给普通窗口（那会改掉窗口标题）
        public static bool LooksLikeEditControl(IntPtr hWnd)
        {
            string up = ClassNameOf(hWnd).ToUpperInvariant();
            if (up.Length == 0) return false;
            if (up.IndexOf("RICHEDIT") >= 0 || up.IndexOf("SCINTILLA") >= 0) return true;
            // WinForms 的类名形如 WindowsForms10.EDIT.app.0.141b42a_r6_ad1
            if (up == "EDIT" || up.EndsWith("EDIT") || up.IndexOf(".EDIT.") >= 0 || up.IndexOf(".EDIT") >= 0) return true;
            return false;
        }

        public static string GetControlText(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(32768);
                IntPtr result;
                IntPtr ok = SendMessageTimeoutRead(hWnd, WM_GETTEXT, (IntPtr)sb.Capacity, sb, SMTO_ABORTIFHUNG, 400, out result);
                if (ok == IntPtr.Zero)
                {
                    Log.Write("WM_GETTEXT timeout hwnd=" + hWnd.ToInt64() + " lasterr=" + Marshal.GetLastWin32Error() + " result=" + result.ToInt64());
                    return null;
                }
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        public static bool SetControlText(IntPtr hWnd, string text)
        {
            long lr;
            return SetControlText(hWnd, text, out lr);
        }

        // WM_SETTEXT 的返回值本身就是成功标志：TRUE=已设置。回读失败时用它兜底。
        public static bool SetControlText(IntPtr hWnd, string text, out long lresult)
        {
            lresult = 0;
            if (hWnd == IntPtr.Zero) return false;
            try
            {
                IntPtr result;
                IntPtr ok = SendMessageTimeoutWrite(hWnd, WM_SETTEXT, IntPtr.Zero, text == null ? "" : text, SMTO_ABORTIFHUNG, 1200, out result);
                lresult = result.ToInt64();
                if (ok == IntPtr.Zero)
                {
                    Log.Write("WM_SETTEXT failed hwnd=" + hWnd.ToInt64() + " lasterr=" + Marshal.GetLastWin32Error() + " result=" + result.ToInt64());
                    return false;
                }
                int len = text == null ? 0 : text.Length;
                try
                {
                    SendMessageInt(hWnd, EM_SETSEL, (IntPtr)len, (IntPtr)len);
                }
                catch
                {
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string WindowTitle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return "";
            try
            {
                StringBuilder sb = new StringBuilder(512);
                GetWindowTextW(hWnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        [DllImport("user32.dll")]
        public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        public static IntPtr ForegroundFocusHwnd()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return IntPtr.Zero;
                uint pid;
                uint tid = GetWindowThreadProcessId(fg, out pid);
                GUITHREADINFO gti = new GUITHREADINFO();
                gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
                if (!GetGUIThreadInfo(tid, ref gti)) return IntPtr.Zero;
                if (gti.hwndFocus != IntPtr.Zero) return gti.hwndFocus;
                return gti.hwndActive;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        public const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        public const uint GA_ROOT = 2;

        // 跨线程把键盘焦点放到指定控件上（撤销需要重新定位输入框时使用）
        public static bool ForceControlFocus(IntPtr control)
        {
            if (control == IntPtr.Zero || !IsWindow(control)) return false;
            try
            {
                IntPtr root = GetAncestor(control, GA_ROOT);
                if (root != IntPtr.Zero && GetForegroundWindow() != root) ForceForeground(root);

                uint pid;
                uint targetThread = GetWindowThreadProcessId(control, out pid);
                uint myThread = GetCurrentThreadId();
                bool attached = false;
                if (targetThread != 0 && targetThread != myThread)
                {
                    attached = AttachThreadInput(myThread, targetThread, true);
                }
                IntPtr prev = SetFocus(control);
                if (attached) AttachThreadInput(myThread, targetThread, false);
                Thread.Sleep(120);
                Log.Write("ForceControlFocus control=" + control.ToInt64() + " prev=" + prev.ToInt64() + " now=" + ForegroundFocusHwnd().ToInt64());
                return ForegroundFocusHwnd() == control;
            }
            catch
            {
                return false;
            }
        }

        // 把目标窗口拉回前台（用于撤销时目标窗口还在但已被切走的情况）
        public static bool ForceForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return false;
            try
            {
                if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
                if (GetForegroundWindow() == hWnd) return true;

                uint pid;
                uint targetThread = GetWindowThreadProcessId(hWnd, out pid);
                uint myThread = GetCurrentThreadId();
                bool attached = false;
                if (targetThread != 0 && targetThread != myThread)
                {
                    attached = AttachThreadInput(myThread, targetThread, true);
                }

                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                ShowWindow(hWnd, SW_RESTORE);

                if (attached) AttachThreadInput(myThread, targetThread, false);
                Thread.Sleep(140);
                return GetForegroundWindow() == hWnd;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        public static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        public static bool IsKeyDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
    }
}
