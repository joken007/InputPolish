using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace InputPolish
{
    /// <summary>
    /// 一条提示词模板。Id 稳定（扩展、IPC、托盘、配置文件都用它），Label 给用户看。
    /// 内置的九个方案和用户自己加的提示词是同一种东西 —— 都能改、都能删。
    /// </summary>
    public class PromptItem
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Hint { get; set; }
        public string Prompt { get; set; }
        public int MaxTokens { get; set; }
        public bool Builtin { get; set; }

        public PromptItem()
        {
            Id = "";
            Label = "";
            Hint = "";
            Prompt = "";
            MaxTokens = 1800;
            Builtin = false;
        }

        public PromptItem(string id, string label, string hint, int maxTokens, string prompt, bool builtin)
        {
            Id = id;
            Label = label;
            Hint = hint;
            MaxTokens = maxTokens;
            Prompt = prompt;
            Builtin = builtin;
        }

        public PromptItem Copy()
        {
            return new PromptItem(Id, Label, Hint, MaxTokens, Prompt, Builtin);
        }
    }

    /// <summary>
    /// 提示词库：内置九种总结方案 + 用户自定义提示词，一起存在 %APPDATA%\InputPolish\prompts.json。
    /// 之所以不直接把方案写死在代码里：用户需要在面板上点「铅笔」改掉当前提示词，
    /// 也需要点「加号」存一份自己的常用提示词。
    /// </summary>
    public static class PromptLib
    {
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();
        private static readonly object Gate = new object();
        private static List<PromptItem> _items;

        public static string FilePath
        {
            get { return Path.Combine(Config.Dir, "prompts.json"); }
        }

        /// <summary>内置九种方案（顺序即默认顺序，第一个是默认选中项）。</summary>
        public static PromptItem[] Builtins()
        {
            PageMode[] all = PageModes.BuiltinTable();
            PromptItem[] list = new PromptItem[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                PageMode m = all[i];
                list[i] = new PromptItem(m.Id, m.Label, m.Hint, m.MaxTokens, m.Prompt, true);
            }
            return list;
        }

        /// <summary>这个 id 是不是九种内置方案之一（内置的不给删，只能改或恢复默认）。</summary>
        public static bool IsBuiltinId(string id)
        {
            if (id == null || id.Length == 0) return false;
            PageMode[] all = PageModes.BuiltinTable();
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].Id, id, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>读库（懒加载；文件不存在或损坏时用内置方案播种）。</summary>
        public static PromptItem[] All()
        {
            EnsureLoaded();
            lock (Gate)
            {
                PromptItem[] copy = new PromptItem[_items.Count];
                for (int i = 0; i < _items.Count; i++) copy[i] = _items[i].Copy();
                return copy;
            }
        }

        public static int Count()
        {
            EnsureLoaded();
            lock (Gate) { return _items.Count; }
        }

        /// <summary>按 id 取；空 / 未知 id 回到第一条（库空了就回内置第一条）。</summary>
        public static PromptItem Get(string id)
        {
            EnsureLoaded();
            lock (Gate)
            {
                if (id != null && id.Trim().Length > 0)
                {
                    string want = id.Trim();
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (string.Equals(_items[i].Id, want, StringComparison.OrdinalIgnoreCase)) return _items[i].Copy();
                    }
                }
                if (_items.Count > 0) return _items[0].Copy();
            }
            return Builtins()[0];
        }

        public static bool IsValid(string id)
        {
            EnsureLoaded();
            lock (Gate)
            {
                if (id == null) return false;
                string want = id.Trim();
                for (int i = 0; i < _items.Count; i++)
                {
                    if (string.Equals(_items[i].Id, want, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
        }

        /// <summary>加一条自定义提示词，返回新条目（失败返回 null 并写 error）。</summary>
        public static PromptItem Add(string label, string prompt, int maxTokens, out string error)
        {
            error = "";
            label = (label == null ? "" : label.Trim());
            prompt = (prompt == null ? "" : prompt.Trim());
            if (prompt.Length == 0) { error = "提示词内容不能为空"; return null; }
            if (label.Length == 0) label = AutoLabel(prompt);
            if (label.Length > 16) label = label.Substring(0, 16);
            if (maxTokens <= 0) maxTokens = 1800;
            if (maxTokens > 8000) maxTokens = 8000;

            EnsureLoaded();
            lock (Gate)
            {
                PromptItem item = new PromptItem(NewId(), label, HintOf(prompt, maxTokens), maxTokens, prompt, false);
                _items.Add(item);
                SaveLocked();
                return item.Copy();
            }
        }

        /// <summary>改一条（内置也能改：改完变成「用户版本」，仍能一键恢复默认）。</summary>
        public static bool Update(string id, string label, string prompt, int maxTokens, out string error)
        {
            error = "";
            prompt = (prompt == null ? "" : prompt.Trim());
            if (prompt.Length == 0) { error = "提示词内容不能为空"; return false; }
            label = (label == null ? "" : label.Trim());
            if (label.Length == 0) label = AutoLabel(prompt);
            if (label.Length > 16) label = label.Substring(0, 16);
            if (maxTokens <= 0) maxTokens = 1800;
            if (maxTokens > 8000) maxTokens = 8000;

            EnsureLoaded();
            lock (Gate)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (!string.Equals(_items[i].Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                    _items[i].Label = label;
                    _items[i].Prompt = prompt;
                    _items[i].MaxTokens = maxTokens;
                    _items[i].Hint = HintOf(prompt, maxTokens);
                    SaveLocked();
                    return true;
                }
            }
            error = "找不到这条提示词（可能已被删除）";
            return false;
        }

        /// <summary>删一条。库里至少要留一条，否则面板就空了。</summary>
        public static bool Remove(string id, out string error)
        {
            error = "";
            EnsureLoaded();
            lock (Gate)
            {
                if (_items.Count <= 1) { error = "至少保留一条提示词"; return false; }
                for (int i = 0; i < _items.Count; i++)
                {
                    if (!string.Equals(_items[i].Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                    // 内置方案不给删：删掉以后用户就找不回来了（只能从托盘「恢复内置提示词」补）。
                    // 想改回去还有「恢复默认」，所以这里直接挡住更省事。
                    if (IsBuiltinId(_items[i].Id)) { error = "内置提示词不能删除，可点「恢复默认」改回出厂内容"; return false; }
                    _items.RemoveAt(i);
                    SaveLocked();
                    return true;
                }
            }
            error = "找不到这条提示词（可能已被删除）";
            return false;
        }

        /// <summary>把缺的内置方案补回来（用户删掉 / 改坏的都能恢复），已存在的不动。</summary>
        public static int RestoreBuiltins()
        {
            EnsureLoaded();
            int added = 0;
            PromptItem[] seed = Builtins();
            lock (Gate)
            {
                for (int i = 0; i < seed.Length; i++)
                {
                    bool found = false;
                    for (int j = 0; j < _items.Count; j++)
                    {
                        if (string.Equals(_items[j].Id, seed[i].Id, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                    }
                    if (!found) { _items.Add(seed[i]); added++; }
                }
                if (added > 0 || NormalizeOrderLocked()) SaveLocked();
            }
            return added;
        }

        /// <summary>
        /// 内置项永远排在最前面、按出厂顺序（芯片顺序就是它），自定义项接在后面。
        /// 补回被删的内置项时如果不归一化，它会跑到列表末尾，用户第一眼就找不到默认方案。
        /// 返回是否改动了顺序。
        /// </summary>
        private static bool NormalizeOrderLocked()
        {
            PromptItem[] seed = Builtins();
            List<PromptItem> ordered = new List<PromptItem>();
            for (int i = 0; i < seed.Length; i++)
            {
                for (int j = 0; j < _items.Count; j++)
                {
                    if (string.Equals(_items[j].Id, seed[i].Id, StringComparison.OrdinalIgnoreCase)) { ordered.Add(_items[j]); break; }
                }
            }
            for (int j = 0; j < _items.Count; j++)
            {
                if (!IsBuiltinId(_items[j].Id)) ordered.Add(_items[j]);
            }
            if (ordered.Count != _items.Count) return false;
            bool same = true;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (!object.ReferenceEquals(ordered[i], _items[i])) { same = false; break; }
            }
            if (same) return false;
            _items = ordered;
            return true;
        }

        /// <summary>
        /// 按 max_tokens 估算的合理超时：20s 起步 + 每 token 40ms（推理模型出字慢）。
        /// 长方案动辄一分钟以上，用设置里 30s 的默认超时必然失败。
        /// </summary>
        public static int TimeoutFor(PromptItem item)
        {
            int tokens = item == null ? 1800 : item.MaxTokens;
            int t = 20000 + tokens * 40;
            if (t < 30000) t = 30000;
            if (t > 180000) t = 180000;
            return t;
        }

        public static string IdCsv()
        {
            PromptItem[] all = All();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < all.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(all[i].Id);
            }
            return sb.ToString();
        }

        /// <summary>给面板 / 扩展用的 id → 是否存在的快速判定，顺便回一条可读的错误。</summary>
        public static bool Require(string id, out PromptItem item, out string error)
        {
            item = null;
            error = "";
            if (id == null || id.Trim().Length == 0) { item = Get(""); return true; }
            if (!IsValid(id)) { error = "提示词不存在：" + id; return false; }
            item = Get(id);
            return true;
        }

        // ---------- 内部 ----------

        private static void EnsureLoaded()
        {
            if (_items != null) return;
            lock (Gate)
            {
                if (_items != null) return;
                List<PromptItem> loaded = LoadFromDisk();
                if (loaded == null || loaded.Count == 0)
                {
                    loaded = new List<PromptItem>();
                    PromptItem[] seed = Builtins();
                    for (int i = 0; i < seed.Length; i++) loaded.Add(seed[i]);
                    _items = loaded;
                    SaveLocked();
                    Log.Write("prompt lib seeded builtins=" + loaded.Count);
                }
                else
                {
                    _items = loaded;
                    Log.Write("prompt lib loaded count=" + loaded.Count + " file=" + FilePath);
                    // 老版本的 prompts.json 里内置项可能被补到了末尾，读盘时统一归一化一次。
                    if (NormalizeOrderLocked())
                    {
                        SaveLocked();
                        Log.Write("prompt lib order normalized builtins-first");
                    }
                }
            }
        }

        private static List<PromptItem> LoadFromDisk()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                if (json == null || json.Trim().Length == 0) return null;
                object raw = Ser.DeserializeObject(json);
                System.Collections.IList arr = raw as System.Collections.IList;
                if (arr == null) return null;
                List<PromptItem> list = new List<PromptItem>();
                for (int i = 0; i < arr.Count; i++)
                {
                    Dictionary<string, object> map = arr[i] as Dictionary<string, object>;
                    if (map == null) continue;
                    PromptItem it = new PromptItem();
                    it.Id = Str(map, "Id");
                    it.Label = Str(map, "Label");
                    it.Hint = Str(map, "Hint");
                    it.Prompt = Str(map, "Prompt");
                    it.MaxTokens = Int(map, "MaxTokens", 1800);
                    it.Builtin = Bool(map, "Builtin");
                    if (it.Id.Length == 0 || it.Prompt.Trim().Length == 0) continue;
                    if (it.Label.Length == 0) it.Label = AutoLabel(it.Prompt);
                    if (it.MaxTokens <= 0) it.MaxTokens = 1800;
                    list.Add(it);
                }
                return list;
            }
            catch (Exception ex)
            {
                Log.Write("prompt lib load failed: " + ex.Message);
                return null;
            }
        }

        private static void SaveLocked()
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                string json = Ser.Serialize(_items);
                File.WriteAllText(FilePath, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Write("prompt lib save failed: " + ex.Message);
            }
        }

        private static string Str(Dictionary<string, object> map, string key)
        {
            object v;
            if (map.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return "";
        }

        private static int Int(Dictionary<string, object> map, string key, int fallback)
        {
            object v;
            if (map.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToInt32(v); }
                catch { return fallback; }
            }
            return fallback;
        }

        private static bool Bool(Dictionary<string, object> map, string key)
        {
            object v;
            if (map.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToBoolean(v); }
                catch { return false; }
            }
            return false;
        }

        private static string NewId()
        {
            return "u" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        /// <summary>没填名称时，从提示词第一行里抠一个不像提示语的名字。</summary>
        private static string AutoLabel(string prompt)
        {
            string s = prompt.Replace("\r", " ").Replace("\n", " ").Trim();
            string[] trim = new string[] { "请", "帮我", "把下面这篇网页", "对下面这篇网页", "总结", "一下" };
            for (int i = 0; i < trim.Length; i++)
            {
                if (s.StartsWith(trim[i])) s = s.Substring(trim[i].Length).Trim();
            }
            if (s.Length > 8) s = s.Substring(0, 8);
            if (s.Trim().Length == 0) s = "自定义";
            return s;
        }

        private static string HintOf(string prompt, int maxTokens)
        {
            string s = prompt.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length > 28) s = s.Substring(0, 28) + "…";
            return s;
        }
    }
}
