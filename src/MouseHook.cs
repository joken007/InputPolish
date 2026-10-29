using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace InputPolish
{
    /// <summary>
    /// WH_MOUSE_LL 全局鼠标钩子：只关心「左键抬起」，用来判断用户是不是刚用鼠标划完一段文字。
    /// 回调里绝不做 UIA 之类的重活，只把坐标丢给线程池。
    /// </summary>
    public class MouseHook : IDisposable
    {
        private Native.LowLevelMouseProc _proc;
        private IntPtr _hook = IntPtr.Zero;

        /// <summary>左键抬起（屏幕坐标）。已过滤注入事件（测试模式除外）。</summary>
        public event Action<int, int> LeftUp;

        private static bool _acceptInjected;

        static MouseHook()
        {
            try
            {
                _acceptInjected = Environment.GetEnvironmentVariable("INPUTPOLISH_ACCEPT_INJECTED") == "1";
            }
            catch
            {
                _acceptInjected = false;
            }
        }

        public bool Installed
        {
            get { return _hook != IntPtr.Zero; }
        }

        public bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            try
            {
                _proc = HookProc;
                _hook = Native.SetWindowsHookExMouse(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
                Log.Write("mouse hook install ok=" + (_hook != IntPtr.Zero) + " err=" + Marshal.GetLastWin32Error());
                return _hook != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Log.Write("mouse hook install exception: " + ex.Message);
                return false;
            }
        }

        public void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            try { Native.UnhookWindowsHookEx(_hook); }
            catch { }
            _hook = IntPtr.Zero;
            _proc = null;
            Log.Write("mouse hook uninstalled");
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int msg = (int)wParam.ToInt64();
                    if (msg == Native.WM_LBUTTONUP)
                    {
                        Native.MSLLHOOKSTRUCT data = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                        bool injected = (data.flags & Native.LLMHF_INJECTED) != 0;
                        if (_acceptInjected || !injected)
                        {
                            Action<int, int> handler = LeftUp;
                            if (handler != null)
                            {
                                int x = data.ptX;
                                int y = data.ptY;
                                ThreadPool.QueueUserWorkItem(delegate(object state)
                                {
                                    try
                                    {
                                        object[] arr = (object[])state;
                                        ((Action<int, int>)arr[0])((int)arr[1], (int)arr[2]);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Write("mouse leftup dispatch error: " + ex.Message);
                                    }
                                }, new object[] { handler, x, y });
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Uninstall();
        }
    }
}
