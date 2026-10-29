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
        public const string Version = "0.8.0";
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
            if (prompt.Trim().Length == 0 && _pendingSummaryPrompt.Length > 0)
            {
                prompt = _pendingSummaryPrompt;
                _pendingSummaryPrompt = "";
                Log.Write("http summary uses pending prompt len=" + prompt.Length);
            }

            SummaryResult r;
            ModelGate.WaitOne();
            try
            {
                if (text.Trim().Length > 0)
                {
                    r = Summarizer.RunText(_cfg, text, title, prompt);
                }
                else
                {
                    r = Summarizer.Run(_cfg, prompt);
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
            if (!r.Ok) payload["error"] = r.Error;
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
            WriteJson(ctx, 200, payload);
            if (command != "idle")
            {
                Interlocked.Exchange(ref _lastDeliverTicks, DateTime.UtcNow.Ticks);
                Log.Write("http api poll delivered=" + command);
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
            }
            catch (Exception)
            {
            }
            finally
            {
                try { ctx.Response.Close(); }
                catch { }
            }
        }
    }
}
