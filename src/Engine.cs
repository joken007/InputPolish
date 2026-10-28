using System;
using System.Collections.Generic;
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

        // ===== 划词翻译 / 网页翻译 / 风格切换 =====

        /// <summary>把动作切到 UI 线程执行（TrayApp 注入 _widget.BeginInvoke）。</summary>
        public Action<Action> UiDispatch;

        /// <summary>同步在 UI 线程执行（Control.Invoke），需要拿到返回值时用。</summary>
        public Func<Action, bool> UiInvoke;

        private void RunOnUi(Action action)
        {
            if (action == null) return;
            Action<Action> d = UiDispatch;
            if (d == null)
            {
                try { action(); }
                catch { }
                return;
            }
            try { d(action); }
            catch (Exception ex) { Log.Write("ui dispatch failed: " + ex.Message); }
        }

        private void RunOnUiSync(Action action)
        {
            if (action == null) return;
            Func<Action, bool> f = UiInvoke;
            if (f == null)
            {
                try { action(); }
                catch { }
                return;
            }
            try { f(action); }
            catch (Exception ex) { Log.Write("ui invoke failed: " + ex.Message); }
        }

        private bool Begin(string what)
        {
            bool alreadyBusy = false;
            double busyAge = 0;
            lock (_gate)
            {
                alreadyBusy = _busy;
                busyAge = (DateTime.UtcNow - _busySince).TotalSeconds;
                if (!_busy || busyAge >= 120)
                {
                    _busy = true;
                    _busySince = DateTime.UtcNow;
                }
            }
            if (alreadyBusy && busyAge < 120)
            {
                Notice("上一个任务还没结束，请稍等", true);
                Log.Write("begin rejected: busy age=" + busyAge.ToString("0.0") + "s what=" + what);
                return false;
            }
            Log.Write("begin " + what);
            return true;
        }

        private void End()
        {
            lock (_gate) { _busy = false; }
        }

        public void CycleStyle()
        {
            string next = Styles.Next(Cfg.polishStyle);
            SetStyle(next);
        }

        public void SetStyle(string id)
        {
            string style = Styles.Normalize(id);
            Cfg.polishStyle = style;
            Cfg.Save();
            string msg = "润色风格：" + Styles.Label(style);
            State(WidgetState.Success, Styles.Label(style));
            Notice(msg, false);
            Log.Write("style set to " + style);
        }

        public void TriggerTranslate()
        {
            if (!Cfg.enabled)
            {
                Notice("已暂停：点悬浮框的「启用」恢复", true);
                return;
            }
            if (!Begin("translate-selection")) return;
            _worker.Post(delegate { RunTranslateSelection(); });
        }

        private void RunTranslateSelection()
        {
            DateTime t0 = DateTime.UtcNow;
            try
            {
                State(WidgetState.Busy, "翻译中…");

                SelectionResult sel = SelectionReader.Read(Cfg);
                if (!sel.Ok)
                {
                    State(WidgetState.Error, "没读到选中文字");
                    Notice(sel.Error, true);
                    Log.Write("translate selection: " + sel.Error);
                    return;
                }

                string text = sel.Text == null ? "" : sel.Text.Trim();
                if (text.Length == 0)
                {
                    State(WidgetState.Error, "没读到选中文字");
                    Notice("没有选中任何文字", true);
                    return;
                }
                int cap = Math.Max(2000, Cfg.maxInputChars);
                if (text.Length > cap)
                {
                    text = text.Substring(0, cap);
                    Log.Write("selection truncated to " + cap);
                }

                Log.Write("selection read ok proc=" + sel.ProcessName + " type=" + sel.ControlTypeName +
                          " source=" + sel.Source + " len=" + text.Length);

                TransResult tr = Translate.Selection(Cfg, text);
                if (!tr.Ok)
                {
                    State(WidgetState.Error, "翻译失败");
                    Notice("翻译失败：" + tr.Error, true);
                    return;
                }

                RunOnUi(delegate { PopupCard.ShowResult(Cfg, tr); });

                int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                State(WidgetState.Success, "已翻译");
                Notice("已翻译 · " + ms + "ms", false);
            }
            catch (Exception ex)
            {
                State(WidgetState.Error, "翻译出错");
                Notice("翻译出错：" + ex.Message, true);
                Log.Write("translate selection error: " + ex);
            }
            finally
            {
                End();
            }
        }

        public void TriggerPageTranslate()
        {
            if (!Cfg.enabled)
            {
                Notice("已暂停：点悬浮框的「启用」恢复", true);
                return;
            }
            if (!Begin("translate-page")) return;
            _worker.Post(delegate { RunPageTranslate(); });
        }

        private void RunPageTranslate()
        {
            DateTime t0 = DateTime.UtcNow;
            ReadPane pane = null;
            try
            {
                State(WidgetState.Busy, "读取页面…");

                PageResult page = PageReader.Read(Cfg, Cfg.pageMaxChars);
                if (!page.Ok)
                {
                    State(WidgetState.Error, "没读到页面");
                    Notice(page.Error, true);
                    return;
                }

                List<string> paras = Translate.SplitParagraphs(page.Text);
                if (paras.Count == 0)
                {
                    State(WidgetState.Error, "页面没内容");
                    Notice("页面里没有可翻译的段落", true);
                    return;
                }
                bool truncated = false;
                if (paras.Count > Cfg.pageMaxParagraphs)
                {
                    paras = paras.GetRange(0, Cfg.pageMaxParagraphs);
                    truncated = true;
                }

                System.Text.StringBuilder sample = new System.Text.StringBuilder();
                for (int i = 0; i < paras.Count && sample.Length < 800; i++) sample.Append(paras[i]);
                string target = Translate.ResolveTarget(Cfg.translateTarget, sample.ToString());
                string targetLabel = Translate.TargetLabel(target);

                int total = paras.Count;
                ReadPane created = null;
                RunOnUiSync(delegate
                {
                    created = ReadPane.Present(page.Title, targetLabel, total);
                });
                // Present 在 UI 线程同步建窗，这里拿到的是同一个实例
                pane = created;

                State(WidgetState.Busy, "翻译 0/" + total);
                Log.Write("page translate start paras=" + total + " target=" + target + " chars=" + page.Chars);

                Dictionary<string, string> cache = new Dictionary<string, string>();
                int done = 0;

                for (int i = 0; i < total; )
                {
                    // 组批：每批累计不超过 ~1200 字，最多 12 段
                    int batchStart = i;
                    int batchChars = 0;
                    List<string> batchSrc = new List<string>();
                    List<int> batchIdx = new List<int>();

                    while (i < total && batchSrc.Count < 12)
                    {
                        string s = paras[i];
                        if (batchChars > 0 && batchChars + s.Length > 1200) break;
                        batchSrc.Add(s);
                        batchIdx.Add(i);
                        batchChars += s.Length;
                        i++;
                    }

                    // 已经是对照语言 / 重复段直接复用
                    List<string> need = new List<string>();
                    List<int> needPos = new List<int>();
                    for (int k = 0; k < batchSrc.Count; k++)
                    {
                        string s = batchSrc[k];
                        string cached;
                        if (cache.TryGetValue(s, out cached))
                        {
                            Publish(pane, batchIdx[k], s, cached, "", target);
                            done++;
                            continue;
                        }
                        if (Translate.NormalizeTarget(target) == Translate.Zh && Translate.LooksChinese(s))
                        {
                            cache[s] = s;
                            Publish(pane, batchIdx[k], s, s, "", target);
                            done++;
                            continue;
                        }
                        if (Translate.NormalizeTarget(target) == Translate.En && !Translate.LooksChinese(s))
                        {
                            cache[s] = s;
                            Publish(pane, batchIdx[k], s, s, "", target);
                            done++;
                            continue;
                        }
                        need.Add(s);
                        needPos.Add(k);
                    }

                    if (need.Count > 0)
                    {
                        string error;
                        List<string> dst = new List<string>();
                        bool any = Translate.Batch(Cfg, need, target, dst, out error);

                        for (int k = 0; k < need.Count; k++)
                        {
                            string s = need[k];
                            int pos = needPos[k];
                            int idx = batchIdx[pos];
                            string t = (k < dst.Count) ? dst[k] : "";
                            if (t.Length == 0)
                            {
                                Publish(pane, idx, s, "", any ? "无返回" : (error.Length > 0 ? error : "翻译失败"), target);
                            }
                            else
                            {
                                cache[s] = t;
                                Publish(pane, idx, s, t, "", target);
                            }
                            done++;
                        }
                    }

                    State(WidgetState.Busy, "翻译 " + done + "/" + total);
                    int d = done;
                    RunOnUi(delegate
                    {
                        if (pane != null && !pane.IsDisposed) pane.SetProgress(d, total);
                    });

                    if (pane == null || pane.IsDisposed)
                    {
                        Log.Write("readpane closed, stop page translate at " + done + "/" + total);
                        break;
                    }
                }

                int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                State(WidgetState.Success, "已翻译");
                string tail = truncated ? "（太长，只翻了前 " + total + " 段）" : "";
                Notice("网页翻译完成：" + done + " 段 · " + (ms / 1000) + "s" + tail, false);
                RunOnUi(delegate
                {
                    if (pane != null && !pane.IsDisposed) pane.SetStatus("完成 " + done + "/" + total + " 段");
                });
                Log.Write("page translate done paras=" + done + "/" + total + " ms=" + ms + " target=" + target);
            }
            catch (Exception ex)
            {
                State(WidgetState.Error, "网页翻译出错");
                Notice("网页翻译出错：" + ex.Message, true);
                Log.Write("page translate error: " + ex);
            }
            finally
            {
                End();
            }
        }

        private void Publish(ReadPane pane, int idx, string src, string dst, string error, string target)
        {
            BiPara item = new BiPara();
            item.Src = src;
            item.Dst = dst;
            item.Error = error;

            RunOnUi(delegate
            {
                if (pane == null || pane.IsDisposed) return;
                pane.AddItem(item);
            });
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
