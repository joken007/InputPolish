using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace InputPolish
{
    public class PolishResult
    {
        public bool Ok;
        public string Error = "";
        public string Text = "";
        public int ElapsedMs;
        public int Attempts;
        public string Model = "";
        public string Raw = "";
    }

    public static class Polisher
    {
        public const string DefaultSystemPrompt =
            "你是专业的文本编辑器，只做一件事：把用户交给你的文本改写得更有逻辑、更有结构，同时严格保持原意。\n\n" +
            "必须遵守：\n" +
            "1. 不改变原意：不新增事实、数字、承诺或结论；不删减信息；不夸大也不弱化语气；不替用户下判断。\n" +
            "2. 保留原语言，不翻译；保留专有名词、品牌名、人名、代码、命令、变量名、路径、URL、邮箱、数字与单位、表情符号。\n" +
            "3. 结构优先：理顺语序与因果；一个意思一句话，过长的句子拆开；明显的并列、递进、因果、步骤关系用分点、编号或短标题呈现，层与层之间空行分隔。原文很短或是单句时，不要强行加小标题或编号。\n" +
            "4. 只输出改写后的正文本身：不要前言、说明、总结、点评；不要用『以下是优化后的文本』之类的话开头；不要询问用户是否满意；不要用代码块包裹。\n" +
            "5. 如果原文已经清晰通顺，只做最小的必要调整（标点、断句、错别字），不要为了看起来结构化而打散原意。\n" +
            "6. 输出语言的语种与输入一致。";

        public static string Endpoint(string baseUrl, string suffix)
        {
            string b = baseUrl == null ? "" : baseUrl.Trim();
            while (b.EndsWith("/")) b = b.Substring(0, b.Length - 1);
            if (b.Length == 0) b = "http://127.0.0.1:3425/v1";
            return b + suffix;
        }

        private static bool IsLoopback(string url)
        {
            try
            {
                Uri u = new Uri(url);
                string h = u.Host.ToLowerInvariant();
                return h == "localhost" || h == "127.0.0.1" || h == "::1" || h == "[::1]";
            }
            catch
            {
                return false;
            }
        }

        private static System.Collections.IList AsList(object value)
        {
            System.Collections.IList list = value as System.Collections.IList;
            return list;
        }

        private static Dictionary<string, object> AsMap(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static string HttpGet(string url, string apiKey, int timeoutMs, out int status, out string error)
        {
            status = 0;
            error = "";
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "InputPolish/1.0";
                if (IsLoopback(url)) req.Proxy = null;
                if (!string.IsNullOrEmpty(apiKey)) req.Headers["Authorization"] = "Bearer " + apiKey;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    status = (int)resp.StatusCode;
                    return ReadAll(resp);
                }
            }
            catch (WebException we)
            {
                HttpWebResponse r = we.Response as HttpWebResponse;
                if (r != null)
                {
                    status = (int)r.StatusCode;
                    string body = "";
                    try { body = ReadAll(r); }
                    catch { }
                    error = "HTTP " + status + ": " + Truncate(body, 300);
                    return body;
                }
                error = DescribeWebException(we);
                return null;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        public static string HttpPostJson(string url, string json, string apiKey, int timeoutMs, out int status, out string error)
        {
            status = 0;
            error = "";
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(json);

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Accept = "application/json";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "InputPolish/1.0";
                req.ContentLength = body.Length;
                if (IsLoopback(url)) req.Proxy = null;
                if (!string.IsNullOrEmpty(apiKey)) req.Headers["Authorization"] = "Bearer " + apiKey;

                using (Stream s = req.GetRequestStream())
                {
                    s.Write(body, 0, body.Length);
                }

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    status = (int)resp.StatusCode;
                    return ReadAll(resp);
                }
            }
            catch (WebException we)
            {
                HttpWebResponse r = we.Response as HttpWebResponse;
                if (r != null)
                {
                    status = (int)r.StatusCode;
                    string b = "";
                    try { b = ReadAll(r); }
                    catch { }
                    error = "HTTP " + status + ": " + Truncate(b, 300);
                    return b;
                }
                error = DescribeWebException(we);
                return null;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        private static string ReadAll(HttpWebResponse resp)
        {
            using (Stream s = resp.GetResponseStream())
            {
                if (s == null) return "";
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[8192];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        private static string DescribeWebException(WebException we)
        {
            switch (we.Status)
            {
                case WebExceptionStatus.Timeout:
                    return "请求超时（可调大超时时间，或换一个更快的模型）";
                case WebExceptionStatus.ConnectFailure:
                    return "无法连接后端服务（网关是否已启动？地址是否正确？）";
                case WebExceptionStatus.NameResolutionFailure:
                    return "无法解析后端主机名";
                case WebExceptionStatus.TrustFailure:
                    return "TLS 证书校验失败";
                case WebExceptionStatus.ProtocolError:
                    return "协议错误：" + we.Message;
                default:
                    return we.Status + ": " + we.Message;
            }
        }

        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

        public static List<string> ListModels(Config cfg, out string error)
        {
            List<string> result = new List<string>();
            error = "";
            int status;
            string json = HttpGet(Endpoint(cfg.baseUrl, "/models"), cfg.apiKey, Math.Max(5000, cfg.timeoutMs / 3), out status, out error);
            if (json == null)
            {
                if (error.Length == 0) error = "无法获取模型列表";
                return result;
            }
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                Dictionary<string, object> o = ser.Deserialize<Dictionary<string, object>>(json);
                object dataObj;
                if (o != null && o.TryGetValue("data", out dataObj))
                {
                    System.Collections.IList arr = AsList(dataObj);
                    if (arr != null)
                    {
                        foreach (object item in arr)
                        {
                            Dictionary<string, object> d = item as Dictionary<string, object>;
                            if (d == null) continue;
                            object idObj;
                            if (d.TryGetValue("id", out idObj) && idObj != null)
                            {
                                result.Add(Convert.ToString(idObj));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = "解析模型列表失败：" + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 通用 chat 调用（润色 / 翻译共享）。失败返回 null 并给出 error。
        /// content 为空且 finish=length 时自动把 max_tokens 翻倍重试一次（推理模型常见）。
        /// </summary>
        public static string Chat(Config cfg, string system, string user, int minMaxTokens, out string error, out string finish, out int attempts)
        {
            return ChatEx(cfg, system, user, minMaxTokens, 0, out error, out finish, out attempts);
        }

        /// <summary>同 Chat，但可指定超时（timeoutMs &lt;= 0 时用 cfg.timeoutMs）。长文总结/深度解读需要更长超时。</summary>
        public static string ChatEx(Config cfg, string system, string user, int minMaxTokens, int timeoutMs, out string error, out string finish, out int attempts)
        {
            error = "";
            finish = "";
            attempts = 0;

            int maxTokens = cfg.maxTokens;
            if (maxTokens < minMaxTokens) maxTokens = minMaxTokens;
            if (maxTokens < 256) maxTokens = 256;

            string url = Endpoint(cfg.baseUrl, "/chat/completions");
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 32 * 1024 * 1024;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                attempts = attempt + 1;

                Dictionary<string, object> body = new Dictionary<string, object>();
                body["model"] = cfg.model;
                body["temperature"] = cfg.temperature;
                body["max_tokens"] = maxTokens;
                body["stream"] = false;

                List<object> messages = new List<object>();
                Dictionary<string, object> m1 = new Dictionary<string, object>();
                m1["role"] = "system";
                m1["content"] = system;
                messages.Add(m1);
                Dictionary<string, object> m2 = new Dictionary<string, object>();
                m2["role"] = "user";
                m2["content"] = user;
                messages.Add(m2);
                body["messages"] = messages;

                string requestJson = ser.Serialize(body);

                int status;
                string httpError;
                int tmo = timeoutMs > 0 ? timeoutMs : cfg.timeoutMs;
            string responseJson = HttpPostJson(url, requestJson, cfg.apiKey, tmo, out status, out httpError);

                if (responseJson == null || responseJson.Trim().Length == 0)
                {
                    error = httpError.Length > 0 ? httpError : "后端返回空响应";
                    Log.Write("chat http failed status=" + status + " err=" + httpError);
                    return null;
                }

                string content = null;
                finish = "";
                try
                {
                    Dictionary<string, object> o = ser.Deserialize<Dictionary<string, object>>(responseJson);
                    object choicesObj;
                    if (o != null && o.TryGetValue("choices", out choicesObj))
                    {
                        System.Collections.IList choices = AsList(choicesObj);
                        if (choices != null && choices.Count > 0)
                        {
                            Dictionary<string, object> ch = AsMap(choices[0]);
                            if (ch != null)
                            {
                                object fObj;
                                if (ch.TryGetValue("finish_reason", out fObj) && fObj != null) finish = Convert.ToString(fObj);
                                object msgObj;
                                if (ch.TryGetValue("message", out msgObj))
                                {
                                    Dictionary<string, object> msg = AsMap(msgObj);
                                    if (msg != null)
                                    {
                                        object cObj;
                                        if (msg.TryGetValue("content", out cObj) && cObj != null) content = Convert.ToString(cObj);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = "解析响应失败：" + ex.Message + " / " + Truncate(responseJson, 200);
                    return null;
                }

                if (content == null) content = "";
                content = CleanOutput(content);

                if (content.Trim().Length == 0)
                {
                    Log.Write("chat empty content finish=" + finish + " maxTokens=" + maxTokens);
                    if (attempt == 0 && (finish == "length" || finish.Length == 0))
                    {
                        maxTokens = Math.Max(minMaxTokens, maxTokens * 2);
                        Log.Write("chat retry with maxTokens=" + maxTokens);
                        continue;
                    }
                    error = "模型返回了空内容（该模型可能是推理模型，思考占满了 token 预算）。请在设置里调大「最大 tokens」或换一个非推理模型。";
                    return null;
                }

                return content;
            }

            error = "模型返回了空内容";
            return null;
        }

        public static PolishResult Polish(Config cfg, string input)
        {
            PolishResult res = new PolishResult();
            DateTime start = DateTime.UtcNow;

            string system = Styles.SystemPrompt(cfg);

            string error;
            string finish;
            int attempts;
            string content = Chat(cfg, system, input, 1024, out error, out finish, out attempts);

            res.Attempts = attempts;
            res.ElapsedMs = (int)(DateTime.UtcNow - start).TotalMilliseconds;

            if (content == null)
            {
                res.Error = error;
                return res;
            }

            res.Ok = true;
            res.Text = content.Trim();
            res.Model = cfg.model;
            Log.Write("polish ok model=" + cfg.model + " style=" + Styles.Normalize(cfg.polishStyle) +
                      " in=" + input.Length + " out=" + res.Text.Length +
                      " ms=" + res.ElapsedMs + " attempts=" + res.Attempts);
            return res;
        }

        public static string CleanOutput(string s)
        {
            if (s == null) return "";
            string fence = new string((char)96, 3);
            string t = s.Trim();
            if (t.Length == 0) return "";

            if (t.StartsWith(fence))
            {
                int firstNl = t.IndexOf("\n");
                if (firstNl >= 0)
                {
                    string rest = t.Substring(firstNl + 1);
                    string trimmed = rest.TrimEnd();
                    if (trimmed.EndsWith(fence))
                    {
                        trimmed = trimmed.Substring(0, trimmed.Length - 3).TrimEnd();
                    }
                    t = trimmed.Trim();
                }
            }

            if (t.Length >= 2)
            {
                char a = t[0];
                char b = t[t.Length - 1];
                if ((a == '"' && b == '"') || (a == '\u201c' && b == '\u201d') || (a == '\u300c' && b == '\u300d'))
                {
                    string inner = t.Substring(1, t.Length - 2).Trim();
                    if (inner.IndexOf(a) < 0 && inner.Length > 0) t = inner;
                }
            }

            string[] metaPrefixes = new string[]
            {
                "以下是", "下面是", "优化后", "润色后", "改写后", "修改后", "整理后",
                "结构化后", "输出：", "输出:", "结果：", "结果:", "修改如下", "优化如下", "改写如下"
            };

            string[] lines = t.Replace("\r\n", "\n").Split('\n');
            if (lines.Length > 0)
            {
                string first = lines[0].Trim();
                bool isMeta = false;
                foreach (string p in metaPrefixes)
                {
                    if (first.StartsWith(p)) { isMeta = true; break; }
                }
                if (isMeta && (first.IndexOf('\uFF1A') >= 0 || first.IndexOf(':') >= 0))
                {
                    int colon = first.IndexOf('\uFF1A');
                    if (colon < 0) colon = first.IndexOf(':');
                    string after = first.Substring(colon + 1).Trim();
                    List<string> keep = new List<string>();
                    if (after.Length > 0) keep.Add(after);
                    for (int i = 1; i < lines.Length; i++) keep.Add(lines[i]);
                    string joined = string.Join("\n", keep.ToArray()).Trim();
                    if (joined.Length > 0) t = joined;
                }
            }

            string[] closingPrefixes = new string[]
            {
                "如需", "如果需要", "如果需要我", "希望这", "希望以上", "若需", "若有需要", "需要我", "以上内容"
            };
            for (int pass = 0; pass < 2; pass++)
            {
                string tt = t.TrimEnd();
                int lastNl = tt.LastIndexOf('\n');
                string lastLine = lastNl >= 0 ? tt.Substring(lastNl + 1).Trim() : tt.Trim();
                bool drop = false;
                foreach (string p in closingPrefixes)
                {
                    if (lastLine.StartsWith(p) && lastLine.Length < 60) { drop = true; break; }
                }
                if (drop && lastNl > 0)
                {
                    t = tt.Substring(0, lastNl).TrimEnd();
                    continue;
                }
                break;
            }

            t = t.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            t = t.Replace("\n", "\r\n");
            return t;
        }
    }
}
