using System;
using System.Drawing;
using System.Threading;

namespace InputPolish
{
    /// <summary>
    /// 监听全局鼠标：左键抬起后稍等一下（等选区稳定），探测焦点处有没有选中文字；
    /// 有就弹一个小浮标，点浮标即划词翻译。
    /// </summary>
    public class SelectionWatcher : IDisposable
    {
        private readonly Config _cfg;
        private readonly Func<Action, bool> _uiInvoke;
        private readonly MouseHook _mouse;
        private SelectionBubble _bubble;

        private int _clickTick;
        private int _clickX;
        private int _clickY;
        private string _lastText = "";
        private int _suppressUntil;

        public SelectionWatcher(Config cfg, Func<Action, bool> uiInvoke)
        {
            _cfg = cfg;
            _uiInvoke = uiInvoke;
            _mouse = new MouseHook();
            _mouse.LeftUp += OnLeftUp;
        }

        public bool Installed
        {
            get { return _mouse.Installed; }
        }

        public void Start()
        {
            if (!_cfg.selectionBubble) return;
            if (!_mouse.Install()) return;
            RunOnUi(CreateBubble);
        }

        public void Stop()
        {
            try { _mouse.Uninstall(); } catch { }
            RunOnUi(HideBubbleNow);
        }

        public void Dispose()
        {
            Stop();
        }

        private void CreateBubble()
        {
            if (_bubble != null) return;
            _bubble = new SelectionBubble();
            _bubble.TranslateClicked += OnBubbleClicked;
            _bubble.CloseRequested += OnBubbleClosed;
            Log.Write("selection bubble created");
        }

        private void OnBubbleClicked()
        {
            // 点浮标本身也算一次左键抬起：屏蔽掉这次抬起，避免刚点完又探测一次、浮标立刻重生
            _suppressUntil = Environment.TickCount + 1500;
            string text = _lastText;
            if (text == null || text.Trim().Length == 0) return;
            Log.Write("bubble translate requested len=" + text.Length);
            Action<string> handler = TranslateRequested;
            if (handler != null) handler(text);
        }

        private void OnBubbleClosed()
        {
        }

        /// <summary>点浮标要翻译的文字（已经探测好的，不用再读一次，避免选区丢失）。</summary>
        public event Action<string> TranslateRequested;

        private void HideBubbleNow()
        {
            if (_bubble != null) _bubble.HideBubble();
        }

        private void OnLeftUp(int x, int y)
        {
            if (unchecked(Environment.TickCount - _suppressUntil) < 0) return;
            Log.Write("watcher leftup " + x + "," + y + " enabled=" + _cfg.selectionBubble);
            if (!_cfg.selectionBubble) return;

            // 点在自己的浮标/悬浮球/面板上：交给对应的窗口自己处理，不重复探测
            if (IsOwnPoint(x, y)) return;

            Interlocked.Exchange(ref _clickTick, Environment.TickCount);
            _clickX = x;
            _clickY = y;
            int stamp = Environment.TickCount;

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    Thread.Sleep(300);
                    if (Environment.TickCount - _clickTick < 260) return; // 又点了一下，等最新的那次
                    if (!_cfg.selectionBubble) return;

                    int sx = _clickX;
                    int sy = _clickY;
                    SelectionHit hit = SelectionProbe.Probe(sx, sy);
                    if (!hit.Ok || hit.Text == null || hit.Text.Trim().Length == 0)
                    {
                        Log.Write("watcher probe miss: " + hit.Error + " proc=" + hit.ProcessName
                            + " type=" + hit.ControlTypeName + " rect=" + hit.X + "," + hit.Y + "," + hit.W + "x" + hit.H);
                        _lastText = "";
                        RunOnUi(HideBubbleNow);
                        return;
                    }

                    string text = hit.Text.Trim();
                    if (text.Length > 6000) text = text.Substring(0, 6000);
                    _lastText = text;
                    Log.Write("selection probe ok proc=" + hit.ProcessName + " len=" + text.Length
                        + " rect=" + hit.X + "," + hit.Y + "," + hit.W + "x" + hit.H);

                    int hx = hit.X, hy = hit.Y, hw = hit.W, hh = hit.H;
                    RunOnUi(delegate
                    {
                        CreateBubble();
                        if (_bubble != null) _bubble.ShowNear(hx, hy, hw, hh, sx, sy, _cfg.selectionBubbleMs);
                    });
                }
                catch (Exception ex)
                {
                    Log.Write("selection watcher error: " + ex.Message);
                }
            });
        }

        private bool IsOwnPoint(int x, int y)
        {
            try
            {
                if (_bubble != null && _bubble.Visible)
                {
                    Rectangle r = _bubble.Bounds;
                    r.Inflate(2, 2);
                    if (r.Contains(x, y)) return true;
                }
                if (Native.IsOwnWindowAt(x, y)) return true;
            }
            catch
            {
            }
            return false;
        }

        private void RunOnUi(Action a)
        {
            try
            {
                if (_uiInvoke != null)
                {
                    if (_uiInvoke(a)) return;
                }
            }
            catch
            {
            }
            try { a(); }
            catch { }
        }
    }
}
