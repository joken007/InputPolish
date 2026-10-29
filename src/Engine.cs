using System;
using System.Collections.Generic;
using System.Threading;
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
        private string _pendingText;

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

        // ===== 网页内（浏览器扩展）通道 =====

        private static readonly string[] BrowserProcs =
        {
            "tabbit", "chrome", "msedge", "brave", "firefox", "vivaldi",
            "opera", "arc", "360se", "360chrome", "qqbrowser", "sogouexplorer", "iexplore"
        };

        private static string BrowserMatch(string procName)
        {
            if (procName == null || procName.Length == 0) return null;
            string l = procName.ToLowerInvariant();
            for (int i = 0; i < BrowserProcs.Length; i++)
            {
                if (l.IndexOf(BrowserProcs[i]) >= 0) return BrowserProcs[i];
            }
            return null;
        }

        /// <summary>前台窗口是不是浏览器（只有浏览器才值得把动作派给扩展）。</summary>
        public static bool ForegroundIsBrowser()
        {
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                if (h == IntPtr.Zero) return false;
                return BrowserMatch(Native.ProcessNameOf(h)) != null;
            }
            catch { return false; }
        }

        // 用户可能先点悬浮球 / Ctrl+` 面板按钮，这时前台已经是 InputPolish 自己了，
        // 所以记一下「刚刚是浏览器」这件事，操作类按钮在 60 秒内仍把命令派给扩展。
        private static long _lastBrowserTicks = 0;
        private static string _lastBrowserProc = "";

        /// <summary>由 TrayApp 的轮询定时器调用：记住最近一次前台是浏览器的时刻。</summary>
        public static void NoteForeground()
        {
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                if (h == IntPtr.Zero) return;
                string p = Native.ProcessNameOfCached(h);
                if (BrowserMatch(p) == null) return;
                lock (typeof(Engine))
                {
                    _lastBrowserTicks = DateTime.UtcNow.Ticks;
                    _lastBrowserProc = p;
                }
            }
            catch { }
        }

        /// <summary>「刚才在看浏览器」也算：面板 / 悬浮球的按钮点下去时前台已经不是浏览器了。</summary>
        public static bool BrowserInContext()
        {
            if (ForegroundIsBrowser()) return true;
            long t;
            lock (typeof(Engine)) { t = _lastBrowserTicks; }
            if (t == 0) return false;
            double age = (DateTime.UtcNow - new DateTime(t, DateTimeKind.Utc)).TotalSeconds;
            return age >= 0 && age < 60;
        }

        private static bool _extHintShown;

        /// <summary>
        /// 浏览器里但扩展没连上时，第一次走老路时提一句：装个扩展就能在网页里翻译。
        /// 只提示一次，避免每次翻译都弹。
        /// </summary>
        private void MaybeExtHint()
        {
            if (_extHintShown) return;
            if (!Cfg.inPage) return;
            if (!BrowserInContext()) return;
            if (HttpApi.HasClient) return;
            _extHintShown = true;
            Log.Write("ext hint shown: no browser extension connected");
            Notice("提示：装一下浏览器扩展就能在网页内翻译（设置 → 网页内翻译 → 打开扩展目录）", false);
        }

        /// <summary>扩展在线时把命令交给网页内执行；返回 true 表示已接管（调用方不要再走 UIA 老路）。</summary>
        private bool TryInPage(string command, string label, string notice)
        {
            if (!Cfg.inPage) return false;
            if (!HttpApi.Running) return false;
            if (!HttpApi.HasClient) return false;
            if (!BrowserInContext())
            {
                Log.Write("in-page skip: foreground not a browser command=" + command);
                return false;
            }
            if (!HttpApi.PostCommand(command)) return false;
            Log.Write("in-page dispatch command=" + command);
            State(WidgetState.Success, label);
            if (notice != null && notice.Length > 0) Notice(notice, false);
            return true;
        }

        public void TriggerTranslate()
        {
            if (!Cfg.enabled)
            {
                Notice("已暂停：点悬浮框的「启用」恢复", true);
                return;
            }
            if (TryInPage("selection", "网页内翻译", "")) return;
            _pendingText = null;
            if (!Begin("translate-selection")) return;
            _worker.Post(delegate { RunTranslateSelection(); });
        }

        /// <summary>划词浮标点进来的路径：文字已经在探测时读好了，直接用，不再读一次（避免选区丢失）。</summary>
        public void TriggerTranslateWith(string text)
        {
            if (!Cfg.enabled)
            {
                Notice("已暂停：点悬浮框的「启用」恢复", true);
                return;
            }
            if (text == null || text.Trim().Length == 0) return;
            _pendingText = text.Trim();
            if (!Begin("translate-selection")) return;
            _worker.Post(delegate { RunTranslateSelection(); });
        }

        private void RunTranslateSelection()
        {
            DateTime t0 = DateTime.UtcNow;
            try
            {
                State(WidgetState.Busy, "翻译中…");

                string preset = _pendingText;
                _pendingText = null;
                SelectionResult sel = null;
                string text;
                if (preset != null && preset.Trim().Length > 0)
                {
                    text = preset.Trim();
                    Log.Write("selection from bubble len=" + text.Length);
                }
                else
                {
                    sel = SelectionReader.Read(Cfg);
                    if (!sel.Ok)
                    {
                        State(WidgetState.Error, "没读到选中文字");
                        Notice(sel.Error, true);
                        Log.Write("translate selection: " + sel.Error);
                        return;
                    }
                    text = sel.Text == null ? "" : sel.Text.Trim();
                }
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

                if (sel != null)
                {
                    Log.Write("selection read ok proc=" + sel.ProcessName + " type=" + sel.ControlTypeName +
                              " source=" + sel.Source + " len=" + text.Length);
                }

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
            if (TryInPage("translate", "网页内翻译", "")) return;
            MaybeExtHint();
            if (!Begin("translate-page")) return;
            _worker.Post(delegate { RunPageTranslate(); });
        }

        /// <summary>让浏览器把已插入的译文撤掉（仅网页内通道）。</summary>
        public void TriggerPageRestore()
        {
            if (!Cfg.enabled)
            {
                Notice("已暂停：点悬浮框的「启用」恢复", true);
                return;
            }
            if (TryInPage("restore", "已还原", "已还原为原文")) return;
            Notice("网页内还原需要浏览器扩展在线（安装步骤见设置里的「网页内翻译」）", true);
        }

        /// <summary>总结当前网页（用配置里的默认提示词）。</summary>
        public void TriggerPageSummary()
        {
            TriggerPageSummaryWith(null);
        }

        /// <summary>总结当前网页，prompt 为空时用配置里的默认提示词。</summary>
        public void TriggerPageSummaryWith(string prompt)
        {
            if (!Cfg.enabled)
            {
                Notice("已暂停：点悬浮框的「启用」恢复", true);
                return;
            }
            string p = prompt == null ? "" : prompt.Trim();
            if (HttpApi.Running && HttpApi.HasClient && BrowserInContext() && Cfg.inPage)
            {
                HttpApi.SetPendingSummaryPrompt(p.Length == 0 ? Cfg.summaryPrompt : p);
                if (TryInPage("summary", "网页内总结", "正在总结本页…")) return;
            }
            MaybeExtHint();
            if (!Begin("summary")) return;
            _worker.Post(delegate { RunPageSummary(p); });
        }

        private void RunPageSummary(string prompt)
        {
            DateTime t0 = DateTime.UtcNow;
            try
            {
                State(WidgetState.Busy, "总结中…");
                Notice("正在读取并总结当前页面…", false);
                string use = prompt == null || prompt.Length == 0 ? Cfg.summaryPrompt : prompt;
                SummaryResult r = Summarizer.Run(Cfg, use);
                if (!r.Ok)
                {
                    State(WidgetState.Error, "总结失败");
                    Notice("总结失败：" + r.Error, true);
                    Log.Write("summary failed: " + r.Error);
                    return;
                }
                RunOnUi(delegate { SummaryPane.Present(Cfg, r, OnRerunSummary); });
                State(WidgetState.Success, "已总结");
                Notice("已总结 · " + r.Text.Length + " 字 · " + (r.ElapsedMs / 1000) + "s", false);
            }
            catch (Exception ex)
            {
                State(WidgetState.Error, "总结出错");
                Notice("总结出错：" + ex.Message, true);
                Log.Write("summary error: " + ex);
            }
            finally
            {
                End();
            }
        }

        private void OnRerunSummary(string prompt)
        {
            TriggerPageSummaryWith(prompt);
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
                object cacheGate = new object();
                object countGate = new object();
                int done = 0;
                int failed = 0;

                // 1) 预处理：已经是目标语言 / 重复段落直接复用，其余进入待译列表
                List<int> pending = new List<int>();
                for (int i = 0; i < total; i++)
                {
                    string s = paras[i];
                    string reuseText = null;
                    bool reuse = false;
                    lock (cacheGate)
                    {
                        string hit;
                        if (cache.TryGetValue(s, out hit)) { reuse = true; reuseText = hit; }
                    }
                    if (!reuse)
                    {
                        if (Translate.NormalizeTarget(target) == Translate.Zh && Translate.LooksChinese(s))
                        {
                            reuse = true;
                            reuseText = s;
                        }
                        else if (Translate.NormalizeTarget(target) == Translate.En && !Translate.LooksChinese(s))
                        {
                            reuse = true;
                            reuseText = s;
                        }
                    }
                    if (reuse)
                    {
                        lock (cacheGate) { cache[s] = reuseText; }
                        Publish(pane, i, s, reuseText, "", target);
                        done++;
                        continue;
                    }
                    pending.Add(i);
                }

                // 2) 组批：每批最多 8 段、累计约 700 字，保证单次请求不被 max_tokens 截断
                List<List<int>> batches = new List<List<int>>();
                List<int> cur = new List<int>();
                int curChars = 0;
                for (int k = 0; k < pending.Count; k++)
                {
                    int idx = pending[k];
                    int len = paras[idx].Length;
                    if (cur.Count > 0 && (cur.Count >= 8 || curChars + len > 700))
                    {
                        batches.Add(cur);
                        cur = new List<int>();
                        curChars = 0;
                    }
                    cur.Add(idx);
                    curChars += len;
                }
                if (cur.Count > 0) batches.Add(cur);

                // 3) 三个线程并发请求（原来串行跑 160 段要十几分钟，并发后约 2~3 分钟）
                const int workers = 4;
                Log.Write("page translate plan paras=" + total + " pending=" + pending.Count +
                          " batches=" + batches.Count + " workers=" + workers);

                int nextBatch = 0;
                object batchGate = new object();
                List<System.Threading.Thread> threads = new List<System.Threading.Thread>();
                for (int w = 0; w < workers; w++)
                {
                    System.Threading.Thread th = new System.Threading.Thread(delegate()
                    {
                        while (true)
                        {
                            int bi;
                            lock (batchGate)
                            {
                                if (nextBatch >= batches.Count) return;
                                bi = nextBatch;
                                nextBatch++;
                            }
                            if (pane == null || pane.IsDisposed)
                            {
                                lock (batchGate) { nextBatch = batches.Count; }
                                return;
                            }

                            List<int> grp = batches[bi];
                            List<string> need = new List<string>();
                            for (int k = 0; k < grp.Count; k++) need.Add(paras[grp[k]]);

                            string error = "";
                            List<string> dst = new List<string>();
                            bool any = false;
                            try
                            {
                                any = Translate.Batch(Cfg, need, target, dst, out error);
                            }
                            catch (Exception ex)
                            {
                                error = ex.Message;
                            }

                            int okCount = 0;
                            for (int k = 0; k < grp.Count; k++)
                            {
                                int idx = grp[k];
                                string s = paras[idx];
                                string t = (k < dst.Count) ? dst[k] : "";
                                if (t.Length == 0)
                                {
                                    string why = any ? "无返回" : (error != null && error.Length > 0 ? error : "翻译失败");
                                    Publish(pane, idx, s, "", why, target);
                                    lock (countGate) { failed++; done++; }
                                }
                                else
                                {
                                    lock (cacheGate) { cache[s] = t; }
                                    Publish(pane, idx, s, t, "", target);
                                    lock (countGate) { done++; }
                                    okCount++;
                                }
                            }

                            int d, f;
                            lock (countGate) { d = done; f = failed; }
                            State(WidgetState.Busy, "翻译 " + d + "/" + total);
                            RunOnUi(delegate
                            {
                                if (pane != null && !pane.IsDisposed) pane.SetProgress(d, total);
                            });
                            Log.Write("page batch " + (bi + 1) + "/" + batches.Count + " paras=" + grp.Count +
                                      " ok=" + okCount + " done=" + d + " failed=" + f);
                        }
                    });
                    th.IsBackground = true;
                    threads.Add(th);
                    th.Start();
                }
                for (int i = 0; i < threads.Count; i++) threads[i].Join();

                if (pane == null || pane.IsDisposed)
                {
                    Log.Write("readpane closed, stop page translate at " + done + "/" + total);
                }
                Log.Write("page translate pass1 done=" + done + "/" + total + " failed=" + failed);

                int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                State(WidgetState.Success, "已翻译");
                string tail = truncated ? "（太长，只翻了前 " + total + " 段）" : "";
                Notice("网页翻译完成：" + done + " 段 · " + (ms / 1000) + "s" + tail, false);
                RunOnUi(delegate
                {
                    if (pane != null && !pane.IsDisposed)
                    {
                        pane.SetStatus("完成 " + done + "/" + total + " 段");
                        pane.FlushRender();
                    }
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
                pane.SetItem(idx, item);
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
