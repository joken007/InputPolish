using System;
using System.Windows.Automation;

namespace InputPolish
{
    public class UndoRecord
    {
        public AutomationElement Element;
        public string Original = "";
        public string Polished = "";
        public IoLayer Layer = IoLayer.None;
        public DateTime At = DateTime.Now;
        public IntPtr TopWindow = IntPtr.Zero;
        public string WindowTitle = "";
        public int ElementHandle;
    }

    public class Engine
    {
        public Config Cfg;
        public Action<WidgetState, string> OnState;
        public Action<string, bool> OnNotice;

        private readonly Worker _worker = new Worker("InputPolish-Worker");
        private readonly object _gate = new object();
        private bool _busy;
        private DateTime _busySince = DateTime.MinValue;
        private UndoRecord _undo;

        public Engine(Config cfg)
        {
            Cfg = cfg;
        }

        public bool HasUndo
        {
            get
            {
                lock (_gate) { return _undo != null; }
            }
        }

        private void State(WidgetState s, string text)
        {
            Action<WidgetState, string> a = OnState;
            if (a != null)
            {
                try { a(s, text); }
                catch { }
            }
        }

        private void Notice(string text, bool isError)
        {
            Action<string, bool> a = OnNotice;
            if (a != null)
            {
                try { a(text, isError); }
                catch { }
            }
        }

        public void Trigger()
        {
            if (!Cfg.enabled)
            {
                Notice("已暂停：点悬浮框的「启用」恢复", true);
                return;
            }
            bool alreadyBusy = false;
            double busyAge = 0;
            lock (_gate)
            {
                alreadyBusy = _busy;
                busyAge = (DateTime.UtcNow - _busySince).TotalSeconds;
                if (_busy && busyAge < 120)
                {
                    Log.Write("trigger skipped: busy age=" + busyAge.ToString("0.0") + "s");
                }
                else
                {
                    _busy = true;
                    _busySince = DateTime.UtcNow;
                }
            }
            if (alreadyBusy && busyAge < 120)
            {
                Notice("上一次润色还没结束，请稍等", true);
                return;
            }
            Log.Write("trigger accepted");
            _worker.Post(delegate { RunPolish(); });
        }

        private void Fail(string message)
        {
            Log.Write("fail: " + message);
            State(WidgetState.Error, message);
            Notice(message, true);
        }

        private void RunPolish()
        {
            DateTime t0 = DateTime.UtcNow;
            try
            {
                Log.Write("pipeline start");
                State(WidgetState.Busy, "润色中…");

                ReadResult rr = UiaIo.Read(Cfg);
                if (!rr.Ok)
                {
                    Fail(rr.Error);
                    return;
                }

                string input = rr.Text == null ? "" : rr.Text;
                input = input.TrimEnd(' ', '\t', '\r', '\n');
                if (input.Trim().Length == 0)
                {
                    Fail("没有可润色的内容");
                    return;
                }
                if (input.Length > Cfg.maxInputChars)
                {
                    Fail("内容过长（" + input.Length + " 字，上限 " + Cfg.maxInputChars + "），已中止。可先在设置里提高字数上限，或只选中要润色的片段");
                    return;
                }

                Log.Write("read ok proc=" + rr.ProcessName + " type=" + rr.ControlTypeName + " layer=" + rr.Layer + " len=" + input.Length);

                PolishResult pr = Polisher.Polish(Cfg, input);
                if (!pr.Ok)
                {
                    Fail(pr.Error);
                    return;
                }

                if (UiaIo.Normalize(pr.Text) == UiaIo.Normalize(input))
                {
                    State(WidgetState.Success, "已很清晰");
                    Notice("原文已经足够清晰，未做改动", false);
                    return;
                }

                WriteResult wr = UiaIo.Write(Cfg, rr.Element, rr.Layer, pr.Text, false);
                if (!wr.Changed)
                {
                    Fail("无法写入该输入框：" + wr.Error + "（润色结果已保存到日志目录）");
                    SaveFallback(pr.Text);
                    return;
                }

                UndoRecord rec = new UndoRecord();
                rec.TopWindow = Native.GetForegroundWindow();
                rec.WindowTitle = Native.WindowTitle(rec.TopWindow);
                rec.Element = rr.Element;
                try { rec.ElementHandle = rr.Element.Current.NativeWindowHandle; }
                catch { rec.ElementHandle = 0; }
                rec.Original = input;
                rec.Polished = pr.Text;
                rec.Layer = rr.Layer;
                rec.At = DateTime.Now;
                lock (_gate) { _undo = rec; }

                int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                State(WidgetState.Success, "已优化");
                if (wr.Error.Length > 0)
                {
                    Notice("已写入但回读未确认：" + wr.Error, true);
                }
                else
                {
                    Notice("已优化 · " + ms + "ms · Ctrl+Alt+Z 撤销", false);
                }
                Log.Write("write ok layer=" + wr.LayerName + " verified=" + wr.Verified + " total=" + ms + "ms");
            }
            catch (Exception ex)
            {
                Fail("内部错误：" + ex.Message);
                Log.Write("pipeline error: " + ex);
            }
            finally
            {
                lock (_gate) { _busy = false; }
            }
        }

        private void SaveFallback(string text)
        {
            try
            {
                string dir = Config.Dir;
                string path = System.IO.Path.Combine(dir, "last-result.txt");
                System.IO.File.WriteAllText(path, text, System.Text.Encoding.UTF8);
                Log.Write("saved fallback result to " + path);
            }
            catch
            {
            }
        }

        public void RequestUndo()
        {
            _worker.Post(delegate { DoUndo(); });
        }

        private void DoUndo()
        {
            UndoRecord u;
            lock (_gate) { u = _undo; }
            if (u == null)
            {
                Notice("没有可撤销的记录", true);
                return;
            }
            if ((DateTime.Now - u.At).TotalMinutes > 10)
            {
                lock (_gate) { _undo = null; }
                Notice("撤销窗口已过期（超过 10 分钟）", true);
                return;
            }

            if (u.TopWindow != IntPtr.Zero && !Native.IsWindow(u.TopWindow))
            {
                lock (_gate) { _undo = null; }
                Notice("撤销失败：原来的窗口「" + u.WindowTitle + "」已经关闭", true);
                Log.Write("undo failed: target window gone");
                return;
            }

            if (u.TopWindow != IntPtr.Zero && Native.GetForegroundWindow() != u.TopWindow)
            {
                Log.Write("undo restoring foreground to [" + u.WindowTitle + "] hwnd=" + u.TopWindow.ToInt64());
                if (!Native.ForceForeground(u.TopWindow))
                {
                    Notice("撤销失败：请先点回「" + u.WindowTitle + "」的输入框，再按一次撤销", true);
                    Log.Write("undo failed: cannot restore foreground");
                    return;
                }
            }

            WriteResult wr = UiaIo.Write(Cfg, u.Element, u.Layer, u.Original, true, u.ElementHandle);
            if (wr.Changed)
            {
                lock (_gate) { _undo = null; }
                State(WidgetState.Idle, "");
                Notice("已撤销，恢复原文", false);
                Log.Write("undo ok");
            }
            else
            {
                Notice("撤销失败：" + wr.Error, true);
                Log.Write("undo failed: " + wr.Error);
            }
        }

        public void Shutdown()
        {
            _worker.Dispose();
        }
    }
}
