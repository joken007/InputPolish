using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace InputPolish
{
    public class KeyHook : IDisposable
    {
        private Native.LowLevelKeyboardProc _proc;
        private IntPtr _hook = IntPtr.Zero;
        private readonly object _gate = new object();

        private int _spaceCount;
        private DateTime _lastSpace = DateTime.MinValue;

        private int _required = 3;
        private int _windowMs = 600;
        private bool _enabled = true;

        public event Action TripleSpace;

        public bool Installed
        {
            get { return _hook != IntPtr.Zero; }
        }

        public void Configure(int required, int windowMs, bool enabled)
        {
            lock (_gate)
            {
                _required = Math.Max(2, Math.Min(6, required));
                _windowMs = Math.Max(150, Math.Min(3000, windowMs));
                _enabled = enabled;
                _spaceCount = 0;
            }
        }

        private static bool _acceptInjected;

        // 仅测试用：设置环境变量 INPUTPOLISH_ACCEPT_INJECTED=1 后，注入按键也能触发
        private static bool AcceptInjected
        {
            get { return _acceptInjected; }
        }

        static KeyHook()
        {
            try
            {
                string v = Environment.GetEnvironmentVariable("INPUTPOLISH_ACCEPT_INJECTED");
                _acceptInjected = v == "1";
            }
            catch
            {
                _acceptInjected = false;
            }
        }

        public bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            try
            {
                _proc = HookProc;
                _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
                Log.Write("hook install ok=" + (_hook != IntPtr.Zero) + " err=" + Marshal.GetLastWin32Error());
                return _hook != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Log.Write("hook install exception: " + ex.Message);
                return false;
            }
        }

        public void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            try
            {
                Native.UnhookWindowsHookEx(_hook);
            }
            catch
            {
            }
            _hook = IntPtr.Zero;
            _proc = null;
            Log.Write("hook uninstalled");
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int msg = (int)wParam.ToInt64();
                    if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                    {
                        Native.KBDLLHOOKSTRUCT data = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                        bool injected = (data.flags & Native.LLKHF_INJECTED) != 0;
                        bool lowerIlInjected = (data.flags & Native.LLKHF_LOWER_IL_INJECTED) != 0;
                        if (AcceptInjected || (!injected && !lowerIlInjected))
                        {
                            HandleKeyDown((int)data.vkCode);
                        }
                    }
                }
                catch
                {
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private void HandleKeyDown(int vk)
        {
            bool fire = false;

            if (vk != Native.VK_SPACE)
            {
                lock (_gate)
                {
                    _spaceCount = 0;
                }
                return;
            }

            if (!_enabled) return;

            if (Native.IsKeyDown(Native.VK_CONTROL) || Native.IsKeyDown(Native.VK_MENU) ||
                Native.IsKeyDown(Native.VK_SHIFT) || Native.IsKeyDown(Native.VK_LWIN) ||
                Native.IsKeyDown(Native.VK_RWIN))
            {
                lock (_gate)
                {
                    _spaceCount = 0;
                }
                return;
            }

            lock (_gate)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastSpace).TotalMilliseconds > _windowMs) _spaceCount = 0;
                _lastSpace = now;
                _spaceCount++;
                if (_spaceCount >= _required)
                {
                    _spaceCount = 0;
                    fire = true;
                }
            }

            if (fire)
            {
                Action handler = TripleSpace;
                if (handler != null)
                {
                    ThreadPool.QueueUserWorkItem(delegate(object state)
                    {
                        try
                        {
                            ((Action)state)();
                        }
                        catch (Exception ex)
                        {
                            Log.Write("triple-space dispatch error: " + ex.Message);
                        }
                    }, handler);
                }
            }
        }

        public void Dispose()
        {
            Uninstall();
        }
    }
}
