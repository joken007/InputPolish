using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace InputPolish
{
    /// <summary>
    /// 本机回环 HTTP 接口：浏览器扩展用它在网页里做双语对照与划词翻译。
    /// 只监听 127.0.0.1，所有请求都要带上设置里生成的连接码。
    /// </summary>
    public static class HttpApi
    {
        public const string Version = "0.12.2";
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();
        private static readonly Semaphore ModelGate = new Semaphore(4, 4);

        private static HttpListener _listener;
        private static Thread _thread;
        private static volatile bool _running;
        private static Config _cfg;
        private static int _port;

        // 桌面端（悬浮球 / 快捷键）下发给浏览器扩展的命令队列
        private static readonly object QueueGate = new object();
        private static readonly Queue<string> Commands = new Queue<string>();
        private static readonly AutoResetEvent CommandSignal = new AutoResetEvent(false);
        private static long _lastPollTicks = 0;
        private static long _lastDeliverTicks = 0;

        /// <summary>桌面端面板里改过的总结提示词：扩展发来 /summary 时若没带 prompt 就用它。</summary>
        private static volatile string _pendingSummaryPrompt = "";

        public static void SetPendingSummaryPrompt(string prompt)
        {
            _pendingSummaryPrompt = prompt == null ? "" : prompt.Trim();
        }

        public static bool Running { get { return _running; } }
        public static int Port { get { return _port; } }

        public static string NewToken()
        {
            return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N").Substring(8);
        }

        public static bool Start(Config cfg)
        {
            Stop();
            if (cfg == null) return false;
            _cfg = cfg;
            _port = cfg.apiPort;
            if (_port < 1024 || _port > 65000) _port = 8790;
            try
            {
                HttpListener l = new HttpListener();
                l.Prefixes.Add("http://127.0.0.1:" + _port + "/");
                l.Start();
                _listener = l;
            }
            catch (Exception ex)
            {
                Log.Write("http api start failed port=" + _port + ": " + ex.Message);
                _listener = null;
                return false;
            }

            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "InputPolish-HTTP";
            _thread.Start();
            Log.Write("http api listening http://127.0.0.1:" + _port + "/ tokenLen=" +
                      (cfg.apiToken == null ? 0 : cfg.apiToken.Length));
            Config.WritePairFile(cfg);
            return true;
        }

        public static void Stop()
        {
            _running = false;
            try
            {
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener.Close();
                }
            }
            catch
            {
            }
            _listener = null;
            Log.Write("http api stopped");
        }

        private static void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx = null;
                try
                {
                    if (_listener == null) break;
                    ctx = _listener.GetContext();
                }
                catch (Exception)
                {
                    if (!_running) break;
                    Thread.Sleep(120);
                    continue;
                }
                HttpListenerContext captured = ctx;
                ThreadPool.QueueUserWorkItem(delegate { Handle(captured); });
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                string method = ctx.Request.HttpMethod.ToUpperInvariant();
                string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();

                if (method == "OPTIONS")
                {
                    WriteJson(ctx, 200, Ok());
                    return;
                }

                // 自动配对要在鉴权之前：扩展拿连接码这件事本身就是配对的入口。
                if (path == "/pair")
                {
                    HandlePair(ctx);
                    return;
                }

                string token = ctx.Request.Headers["X-IP-Token"];
                if (token == null || token.Length == 0) token = ctx.Request.QueryString["token"];
                if (!TokenOk(token))
                {
                    Log.Write("http api reject token path=" + path);
                    WriteJson(ctx, 401, Error("连接码不对：请把 InputPolish 设置里的连接码填到扩展选项里"));
                    return;
                }

                if (path == "/poll")
                {
                    HandlePoll(ctx);
                    return;
                }

                if (path == "/modes")
                {
                    WriteModes(ctx);
                    return;
                }

                if (path == "/prompts" && ctx.Request.HttpMethod == "GET")
                {
                    HandlePrompts(ctx, "");
                    return;
                }

                if (path == "/health" || path == "/ping")
                {
                    Dictionary<string, object> h = Ok();
                    h["name"] = "InputPolish";
                    h["version"] = Version;
                    h["model"] = _cfg.model;
                    h["port"] = _port;
                    h["inPage"] = _cfg.inPage;
                    WriteJson(ctx, 200, h);
                    return;
                }

                if (method != "POST")
                {
                    WriteJson(ctx, 405, Error("只支持 POST"));
                    return;
                }

                string body = ReadBody(ctx);
                if (body == null)
                {
                    WriteJson(ctx, 413, Error("请求体太大"));
                    return;
                }

                if (path == "/translate") { HandleTranslate(ctx, body); return; }
                if (path == "/command") { HandleCommand(ctx, body); return; }
                if (path == "/selection") { HandleSelection(ctx, body); return; }
                if (path == "/summary") { HandleSummary(ctx, body); return; }
                if (path == "/ask") { HandleAsk(ctx, body); return; }
                if (path == "/run") { HandleRun(ctx, body); return; }
                if (path == "/note") { HandleNote(ctx, body); return; }
                if (path == "/export") { HandleExport(ctx, body); return; }
                if (path == "/prompts") { HandlePrompts(ctx, body); return; }

                WriteJson(ctx, 404, Error("没有这个接口：" + path));
            }
            catch (Exception ex)
            {
                Log.Write("http api error: " + ex.Message);
                try { WriteJson(ctx, 500, Error(ex.Message)); }
                catch { }
            }
        }

        private static bool TokenOk(string token)
        {
            if (_cfg == null || _cfg.apiToken == null || _cfg.apiToken.Length == 0) return false;
            if (token == null) return false;
            if (token.Length != _cfg.apiToken.Length) return false;
            int diff = 0;
            for (int i = 0; i < token.Length; i++) diff |= token[i] ^ _cfg.apiToken[i];
            return diff == 0;
        }

        private static string ReadBody(HttpListenerContext ctx)
        {
            if (!ctx.Request.HasEntityBody) return "";
            if (ctx.Request.ContentLength64 > 4L * 1024 * 1024) return null;
            using (StreamReader sr = new StreamReader(ctx.Request.InputStream, new UTF8Encoding(false)))
            {
                return sr.ReadToEnd();
            }
        }

        private static Dictionary<string, object> Parse(string body)
        {
            if (body == null || body.Trim().Length == 0) return new Dictionary<string, object>();
            try
            {
                Dictionary<string, object> o = Ser.Deserialize<Dictionary<string, object>>(body);
                return o == null ? new Dictionary<string, object>() : o;
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static List<string> ReadTexts(Dictionary<string, object> o)
        {
            List<string> list = new List<string>();
            object v;
            if (!o.TryGetValue("texts", out v) || v == null) return list;
            System.Collections.IList arr = v as System.Collections.IList;
            if (arr == null)
            {
                list.Add(Convert.ToString(v));
                return list;
            }
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] == null) list.Add("");
                else list.Add(Convert.ToString(arr[i]));
            }
            return list;
        }

        private static string Str(Dictionary<string, object> o, string key, string def)
        {
            object v;
            if (o.TryGetValue(key, out v) && v != null)
            {
                string s = Convert.ToString(v);
                return s == null ? def : s;
            }
            return def;
        }

        private static bool Bool(Dictionary<string, object> o, string key, bool def)
        {
            object v;
            if (o.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToBoolean(v); }
                catch { return def; }
            }
            return def;
        }

        private static void HandleTranslate(HttpListenerContext ctx, string body)
        {
            Dictionary<string, object> o = Parse(body);
            List<string> texts = ReadTexts(o);
            if (texts.Count == 0)
            {
                WriteJson(ctx, 200, Error("texts 为空"));
                return;
            }
            if (texts.Count > 16) texts = texts.GetRange(0, 16);

            string target = Str(o, "target", "auto");
            string sample = "";
            for (int i = 0; i < texts.Count; i++)
            {
                if (texts[i] != null && texts[i].Trim().Length > 0) { sample = texts[i]; break; }
            }

            Config c = _cfg.Clone();
            c.translateTarget = Translate.NormalizeTarget(target);
            string resolved = Translate.ResolveTarget(c.translateTarget, sample);

            List<string> dst = new List<string>();
            string error = "";
            bool ok = false;
            ModelGate.WaitOne();
            DateTime t0 = DateTime.UtcNow;
            try
            {
                ok = Translate.Batch(c, texts, resolved, dst, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                ModelGate.Release();
            }

            int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            List<object> results = new List<object>();
            int done = 0;
            for (int i = 0; i < texts.Count; i++)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["src"] = texts[i];
                item["text"] = i < dst.Count ? dst[i] : "";
                if (item["text"] != null && Convert.ToString(item["text"]).Length > 0) done++;
                results.Add(item);
            }

            Dictionary<string, object> payload = Ok();
            payload["results"] = results;
            payload["target"] = Translate.TargetLabel(resolved);
            payload["ms"] = ms;
            payload["count"] = texts.Count;
            payload["done"] = done;
            if (!ok && done == 0) payload["error"] = error == null || error.Length == 0 ? "翻译失败" : error;
            payload["ok"] = done > 0;

            Log.Write("http translate count=" + texts.Count + " done=" + done + " target=" + resolved + " ms=" + ms +
                      (ok ? "" : " error=" + error));
            WriteJson(ctx, 200, payload);
        }

        private static void HandleSelection(HttpListenerContext ctx, string body)
        {
            Dictionary<string, object> o = Parse(body);
            string text = Str(o, "text", "");
            if (text.Trim().Length == 0)
            {
                WriteJson(ctx, 200, Error("text 为空"));
                return;
            }
            if (text.Length > 4000) text = text.Substring(0, 4000);

            Config c = _cfg.Clone();
            c.translateTarget = Translate.NormalizeTarget(Str(o, "target", "auto"));
            c.translateExplain = Bool(o, "explain", c.translateExplain);

            TransResult r;
            ModelGate.WaitOne();
            try
            {
                r = Translate.Selection(c, text);
            }
            finally
            {
                ModelGate.Release();
            }

            Dictionary<string, object> payload = Ok();
            payload["ok"] = r.Ok;
            payload["translation"] = r.Translation;
            payload["explain"] = r.Explanation;
            payload["target"] = r.Target;
            payload["ms"] = r.ElapsedMs;
            if (!r.Ok) payload["error"] = r.Error;
            WriteJson(ctx, 200, payload);
        }

        private static void HandleSummary(HttpListenerContext ctx, string body)
        {
            Dictionary<string, object> o = Parse(body);
            string text = Str(o, "text", "");
            string title = Str(o, "title", "");
            string prompt = Str(o, "prompt", "");
            string mode = Str(o, "mode", "");
            if (prompt.Trim().Length == 0 && _pendingSummaryPrompt.Length > 0)
            {
                prompt = _pendingSummaryPrompt;
                _pendingSummaryPrompt = "";
                Log.Write("http summary uses pending prompt len=" + prompt.Length);
            }
            if (prompt.Trim().Length > 0) mode = "";   // 自定义提示词优先，方案让位

            SummaryResult r;
            ModelGate.WaitOne();
            try
            {
                if (text.Trim().Length > 0)
                {
                    r = Summarizer.RunTextMode(_cfg, text, title, mode, prompt);
                }
                else
                {
                    r = Summarizer.Run(_cfg, mode, prompt);
                }
            }
            finally
            {
                ModelGate.Release();
            }

            Dictionary<string, object> payload = Ok();
            payload["ok"] = r.Ok;
            payload["text"] = r.Text;
            payload["title"] = r.Title;
            payload["chars"] = r.Chars;
            payload["ms"] = r.ElapsedMs;
            payload["calls"] = r.Calls;
            payload["mode"] = r.Mode;
            payload["modeLabel"] = r.ModeLabel;
            if (!r.Ok) payload["error"] = r.Error;
            WriteJson(ctx, 200, payload);
        }

        /// <summary>GET /modes：把提示词库交给扩展，避免两边各写一份导致漂移。</summary>
        private static void WriteModes(HttpListenerContext ctx)
        {
            Dictionary<string, object> payload = Ok();
            payload["modes"] = ModesJson();
            WriteJson(ctx, 200, payload);
        }

        /// <summary>提示词库的线上形态：只给 id/label/hint/内置标记，正文另走 /prompts。</summary>
        private static List<object> ModesJson()
        {
            PromptItem[] all = PromptLib.All();
            List<object> arr = new List<object>();
            for (int i = 0; i < all.Length; i++)
            {
                Dictionary<string, object> m = new Dictionary<string, object>();
                m["id"] = all[i].Id;
                m["label"] = all[i].Label;
                m["hint"] = all[i].Hint;
                m["builtin"] = all[i].Builtin;
                m["maxTokens"] = all[i].MaxTokens;
                arr.Add(m);
            }
            return arr;
        }

        /// <summary>
        /// /prompts：提示词库读写。
        ///   GET  /prompts            所有条目（含提示词正文）
        ///   GET  /prompts?id=x       单条
        ///   POST /prompts {action:save, id, label, prompt, maxTokens}  带 id 改、不带 id 加
        ///   POST /prompts {action:delete, id}
        ///   POST /prompts {action:restore}
        /// </summary>
        private static void HandlePrompts(HttpListenerContext ctx, string body)
        {
            Dictionary<string, object> payload = Ok();

            if (ctx.Request.HttpMethod == "GET")
            {
                string id = ctx.Request.QueryString["id"];
                // default=1：取内置的出厂内容（内置项被改过之后，编辑框要能「恢复默认」）
                if (id != null && id.Trim().Length > 0 && ctx.Request.QueryString["default"] == "1")
                {
                    PageMode[] table = PageModes.BuiltinTable();
                    for (int i = 0; i < table.Length; i++)
                    {
                        if (!string.Equals(table[i].Id, id.Trim(), StringComparison.Ordinal)) continue;
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["id"] = table[i].Id;
                        d["label"] = table[i].Label;
                        d["hint"] = table[i].Hint;
                        d["prompt"] = table[i].Prompt;
                        d["maxTokens"] = table[i].MaxTokens;
                        d["builtin"] = true;
                        payload["item"] = d;
                        WriteJson(ctx, 200, payload);
                        return;
                    }
                    WriteJson(ctx, 404, Error("这条不是内置提示词，没有默认内容"));
                    return;
                }
                if (id != null && id.Trim().Length > 0)
                {
                    if (!PromptLib.IsValid(id)) { WriteJson(ctx, 404, Error("提示词不存在")); return; }
                    payload["item"] = PromptJson(PromptLib.Get(id));
                }
                else
                {
                    List<object> arr = new List<object>();
                    PromptItem[] all = PromptLib.All();
                    for (int i = 0; i < all.Length; i++) arr.Add(PromptJson(all[i]));
                    payload["items"] = arr;
                }
                WriteJson(ctx, 200, payload);
                return;
            }

            Dictionary<string, object> o = Parse(body);
            string action = Str(o, "action", "save").Trim().ToLowerInvariant();
            string error;

            if (action == "delete")
            {
                string delId = Str(o, "id", "");
                if (!PromptLib.Remove(delId, out error)) { WriteJson(ctx, 400, Error(error)); return; }
                Log.Write("http prompts delete id=" + delId);
                payload["modes"] = ModesJson();
                WriteJson(ctx, 200, payload);
                return;
            }

            if (action == "restore")
            {
                int added = PromptLib.RestoreBuiltins();
                Log.Write("http prompts restore added=" + added);
                payload["added"] = added;
                payload["modes"] = ModesJson();
                WriteJson(ctx, 200, payload);
                return;
            }

            string label = Str(o, "label", "");
            string prompt = Str(o, "prompt", "");
            int maxTokens = IntOf(o, "maxTokens", 1800);
            string wantedId = Str(o, "id", "").Trim();

            if (wantedId.Length > 0)
            {
                if (!PromptLib.Update(wantedId, label, prompt, maxTokens, out error)) { WriteJson(ctx, 400, Error(error)); return; }
                Log.Write("http prompts update id=" + wantedId + " label=" + label);
                payload["item"] = PromptJson(PromptLib.Get(wantedId));
            }
            else
            {
                PromptItem added = PromptLib.Add(label, prompt, maxTokens, out error);
                if (added == null) { WriteJson(ctx, 400, Error(error)); return; }
                Log.Write("http prompts add id=" + added.Id + " label=" + added.Label);
                payload["item"] = PromptJson(added);
            }
            payload["modes"] = ModesJson();
            WriteJson(ctx, 200, payload);
        }

        private static object PromptJson(PromptItem it)
        {
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["id"] = it.Id;
            m["label"] = it.Label;
            m["hint"] = it.Hint;
            m["prompt"] = it.Prompt;
            m["maxTokens"] = it.MaxTokens;
            m["builtin"] = it.Builtin;
            return m;
        }

        /// <summary>读整数参数（扩展发来的数字可能被解析成 int / double / string）。</summary>
        private static int IntOf(Dictionary<string, object> o, string key, int def)
        {
            object v;
            if (o.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToInt32(v); }
                catch { return def; }
            }
            return def;
        }

        /// <summary>POST /ask {text,title,question}：基于网页内容回答问题。</summary>
        private static void HandleAsk(HttpListenerContext ctx, string body)
        {
            Dictionary<string, object> o = Parse(body);
            string text = Str(o, "text", "");
            string title = Str(o, "title", "");
            string question = Str(o, "question", "");

            AskResult r;
            ModelGate.WaitOne();
            try
            {
                if (text.Trim().Length > 0)
                {
                    r = PageAsker.Ask(_cfg, text, title, question);
                }
                else
                {
                    r = PageAsker.AskPage(_cfg, question);
                }
            }
            finally
            {
                ModelGate.Release();
            }

            Dictionary<string, object> payload = Ok();
            payload["ok"] = r.Ok;
            payload["answer"] = r.Answer;
            payload["question"] = r.Question;
            payload["chars"] = r.Chars;
            payload["ms"] = r.ElapsedMs;
            if (!r.Ok) payload["error"] = r.Error;
            WriteJson(ctx, 200, payload);
        }

        /// <summary>
        /// POST /run {text,title,instruction}：WebPilot 式自由指令——把用户写的一句话当任务，网页正文当素材。
        /// 和 /ask 的区别：/ask 只依据原文回答，/run 允许按指令改写、翻译、重排、提炼成任何格式。
        /// </summary>
        private static void HandleRun(HttpListenerContext ctx, string body)
        {
            Dictionary<string, object> o = Parse(body);
            string text = Str(o, "text", "");
            string title = Str(o, "title", "");
            string instruction = Str(o, "instruction", "");
            string control = Str(o, "prompt", "");
            if (control.Trim().Length > 0) instruction = control;

            AskResult r;
            ModelGate.WaitOne();
            try
            {
                if (text.Trim().Length > 0)
                {
                    r = PageAsker.Instruct(_cfg, text, title, instruction);
                }
                else
                {
                    PageResult page = PageReader.Read(_cfg, _cfg.pageMaxChars);
                    if (!page.Ok) { r = new AskResult(); r.Error = page.Error; }
                    else r = PageAsker.Instruct(_cfg, page.Text, page.Title, instruction);
                }
            }
            finally
            {
                ModelGate.Release();
            }

            Dictionary<string, object> payload = Ok();
            payload["ok"] = r.Ok;
            payload["answer"] = r.Answer;
            payload["instruction"] = r.Question;
            payload["chars"] = r.Chars;
            payload["ms"] = r.ElapsedMs;
            if (!r.Ok) payload["error"] = r.Error;
            WriteJson(ctx, 200, payload);
        }

        /// <summary>POST /note {title,body,kind,url}：把总结/问答存成本地 Markdown。</summary>
        private static void HandleNote(HttpListenerContext ctx, string body)
        {
            Dictionary<string, object> o = Parse(body);
            string title = Str(o, "title", "");
            string text = Str(o, "body", "");
            string kind = Str(o, "kind", "");
            string url = Str(o, "url", "");
            if (text.Trim().Length == 0)
            {
                WriteJson(ctx, 400, Error("内容是空的，没什么可存的"));
                return;
            }
            string error;
            string file = Notes.Save(title, text, kind, url, out error);
            Dictionary<string, object> payload = Ok();
            payload["ok"] = file != null;
            payload["path"] = file == null ? "" : file;
            payload["dir"] = Notes.Dir();
            if (file == null) payload["error"] = error;
            WriteJson(ctx, 200, payload);
        }


        /// <summary>POST /export {title,markdown,url}：把整页正文导出成本地 Markdown 文件。</summary>
        private static void HandleExport(HttpListenerContext ctx, string body)
        {
            Dictionary<string, object> o = Parse(body);
            string title = Str(o, "title", "");
            string md = Str(o, "markdown", "");
            string url = Str(o, "url", "");
            if (md == null || md.Trim().Length == 0)
            {
                WriteJson(ctx, 400, Error("没读到可导出的正文"));
                return;
            }
            string error;
            string file = Exports.Save(title, md, out error);
            Dictionary<string, object> payload = Ok();
            payload["ok"] = file != null;
            payload["path"] = file == null ? "" : file;
            payload["name"] = file == null ? "" : Path.GetFileName(file);
            payload["dir"] = Exports.Dir();
            payload["chars"] = md.Length;
            payload["url"] = url;
            if (file == null) payload["error"] = error;
            WriteJson(ctx, 200, payload);
        }

        // ---------------- 桌面端 → 浏览器扩展 的命令通道 ----------------

        /// <summary>桌面端请求浏览器扩展执行命令：translate / restore / selection / summary。</summary>
        public static bool PostCommand(string command)
        {
            if (!_running) return false;
            if (command == null || command.Trim().Length == 0) return false;
            lock (QueueGate)
            {
                while (Commands.Count > 8) Commands.Dequeue();
                Commands.Enqueue(command.Trim());
            }
            CommandSignal.Set();
            Log.Write("http api queue command=" + command + " clientAlive=" + ClientAliveMs());
            return true;
        }

        /// <summary>扩展最近是否在轮询（秒；从未连过返回 -1）。</summary>
        public static int ClientAliveMs()
        {
            long t = Interlocked.Read(ref _lastPollTicks);
            if (t == 0) return -1;
            return (int)(DateTime.UtcNow.Ticks - t) / 10000;
        }

        public static bool HasClient
        {
            get
            {
                int age = ClientAliveMs();
                if (age < 0 || age >= 40000) return false;
                // 刚下发过命令却迟迟没有下一次轮询 —— 浏览器已经被关掉了。
                // 健康的客户端拿到响应后 120ms 内就会再发一次，所以 6 秒足够判定。
                long deliver = Interlocked.Read(ref _lastDeliverTicks);
                long poll = Interlocked.Read(ref _lastPollTicks);
                if (deliver > poll)
                {
                    long since = (DateTime.UtcNow.Ticks - deliver) / 10000;
                    if (since > 6000) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 浏览器扩展的自动配对：只要请求带 chrome-extension:// 的 Origin 就把连接码给它。
        /// 网页永远伪造不出这个 Origin，所以不会把连接码泄露给任意站点。
        /// </summary>
        private static void HandlePair(HttpListenerContext ctx)
        {
            string origin = ctx.Request.Headers["Origin"];
            if (origin == null) origin = "";
            bool fromExt = origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("extension://", StringComparison.OrdinalIgnoreCase);
            if (!fromExt)
            {
                Log.Write("http api pair reject origin=[" + origin + "]");
                WriteJson(ctx, 403, Error("只有浏览器扩展可以自动配对，请在扩展选项里手动填连接码"));
                return;
            }
            Log.Write("http api pair ok origin=" + origin);
            Dictionary<string, object> payload = Ok();
            payload["token"] = _cfg.apiToken;
            payload["port"] = _port;
            payload["name"] = "InputPolish";
            payload["version"] = Version;
            WriteJson(ctx, 200, payload);
        }

        private static void HandlePoll(HttpListenerContext ctx)
        {
            Interlocked.Exchange(ref _lastPollTicks, DateTime.UtcNow.Ticks);
            string command = "idle";
            DateTime deadline = DateTime.UtcNow.AddSeconds(24);
            while (DateTime.UtcNow < deadline)
            {
                lock (QueueGate)
                {
                    if (Commands.Count > 0)
                    {
                        command = Commands.Dequeue();
                        break;
                    }
                }
                int remainMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remainMs <= 0) break;
                CommandSignal.WaitOne(Math.Min(remainMs, 2000));
            }

            Dictionary<string, object> payload = Ok();
            payload["command"] = command;
            bool sent = TryWriteJson(ctx, 200, payload);
            if (command != "idle")
            {
                if (sent)
                {
                    Interlocked.Exchange(ref _lastDeliverTicks, DateTime.UtcNow.Ticks);
                    Log.Write("http api poll delivered=" + command);
                }
                else
                {
                    lock (QueueGate)
                    {
                        if (Commands.Count < 8) Commands.Enqueue(command);
                    }
                    Log.Write("http api poll lost=" + command + " client gone, re-queued");
                }
            }
        }

        private static void HandleCommand(HttpListenerContext ctx, string body)
        {
            Dictionary<string, object> o = Parse(body);
            string command = Str(o, "command", "");
            if (command.Length == 0)
            {
                WriteJson(ctx, 200, Error("command 为空"));
                return;
            }
            bool queued = PostCommand(command);
            Dictionary<string, object> payload = Ok();
            payload["queued"] = queued;
            payload["client"] = HasClient;
            WriteJson(ctx, 200, payload);
        }

        private static Dictionary<string, object> Ok()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["ok"] = true;
            return d;
        }

        private static Dictionary<string, object> Error(string message)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["ok"] = false;
            d["error"] = message == null ? "" : message;
            return d;
        }

        private static void WriteJson(HttpListenerContext ctx, int status, Dictionary<string, object> payload)
        {
            TryWriteJson(ctx, status, payload);
        }

        /// <summary>
        /// 和 WriteJson 一样，但会告诉你这次响应到底有没有送出去。
        /// 命令通道必须知道这一点：浏览器扩展的 service worker 随时会被浏览器回收，
        /// 写失败时那条命令根本没到网页，得放回队列重投，否则用户在网页里点「总结」会静默没反应。
        /// </summary>
        private static bool TryWriteJson(HttpListenerContext ctx, int status, Dictionary<string, object> payload)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(Ser.Serialize(payload));
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                ctx.Response.Headers["Access-Control-Allow-Headers"] = "content-type, x-ip-token";
                ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                ctx.Response.Headers["Cache-Control"] = "no-store";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Flush();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                try { ctx.Response.Close(); }
                catch { }
            }
        }
    }
}
