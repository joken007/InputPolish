using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace InputPolish
{
    public class TriggerConfig
    {
        public bool spaceTriple = true;
        public int spaceCount = 3;
        public int windowMs = 600;
        public string hotkey = "Ctrl+Alt+L";
        public string undoHotkey = "Ctrl+Alt+Z";

        public TriggerConfig Clone()
        {
            TriggerConfig c = new TriggerConfig();
            c.spaceTriple = spaceTriple;
            c.spaceCount = spaceCount;
            c.windowMs = windowMs;
            c.hotkey = hotkey;
            c.undoHotkey = undoHotkey;
            return c;
        }
    }

    public class Config
    {
        public int version = 2;
        public bool enabled = true;
        public string baseUrl = "http://127.0.0.1:3425/v1";
        public string apiKey = "";
        public string model = "auto/deepseek-v4.1-flash";
        public double temperature = 0.2;
        public int maxTokens = 2000;
        public int timeoutMs = 30000;
        public int maxInputChars = 2000;
        public TriggerConfig trigger = new TriggerConfig();
        public bool clipboardFallback = true;
        public List<string> excludeProcesses = new List<string>();
        public bool autoStart = true;
        public int toastMs = 2500;
        public string systemPromptOverride = "";
        public int widgetX = -1;
        public int widgetY = -1;
        public bool widgetVisible = true;

        public static string Dir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InputPolish");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(Dir, "config.json"); }
        }

        public static Config Defaults()
        {
            Config c = new Config();
            c.excludeProcesses.Add("WindowsTerminal");
            c.excludeProcesses.Add("conhost");
            c.excludeProcesses.Add("KeePass");
            c.excludeProcesses.Add("KeePassXC");
            c.excludeProcesses.Add("1Password");
            c.excludeProcesses.Add("Bitwarden");
            return c;
        }

        public Config Clone()
        {
            Config c = new Config();
            c.version = version;
            c.enabled = enabled;
            c.baseUrl = baseUrl;
            c.apiKey = apiKey;
            c.model = model;
            c.temperature = temperature;
            c.maxTokens = maxTokens;
            c.timeoutMs = timeoutMs;
            c.maxInputChars = maxInputChars;
            c.trigger = trigger == null ? new TriggerConfig() : trigger.Clone();
            c.clipboardFallback = clipboardFallback;
            c.excludeProcesses = new List<string>(excludeProcesses == null ? new string[0] : excludeProcesses.ToArray());
            c.autoStart = autoStart;
            c.toastMs = toastMs;
            c.systemPromptOverride = systemPromptOverride;
            c.widgetX = widgetX;
            c.widgetY = widgetY;
            c.widgetVisible = widgetVisible;
            return c;
        }

        public static Config Load()
        {
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
            }
            catch
            {
            }

            if (!File.Exists(FilePath))
            {
                Config d = Defaults();
                d.Save();
                return d;
            }

            try
            {
                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                Dictionary<string, object> o = ser.Deserialize<Dictionary<string, object>>(json);
                Config c = Defaults();
                if (o == null) return c;

                c.version = GetInt(o, "version", c.version);
                c.enabled = GetBool(o, "enabled", c.enabled);
                c.baseUrl = GetString(o, "baseUrl", c.baseUrl);
                c.apiKey = GetString(o, "apiKey", c.apiKey);
                c.model = GetString(o, "model", c.model);
                c.temperature = GetDouble(o, "temperature", c.temperature);
                c.maxTokens = GetInt(o, "maxTokens", c.maxTokens);
                c.timeoutMs = GetInt(o, "timeoutMs", c.timeoutMs);
                c.maxInputChars = GetInt(o, "maxInputChars", c.maxInputChars);
                if (c.version < 2 && c.maxInputChars > 2000) c.maxInputChars = 2000;
                c.clipboardFallback = GetBool(o, "clipboardFallback", c.clipboardFallback);
                c.autoStart = GetBool(o, "autoStart", c.autoStart);
                c.toastMs = GetInt(o, "toastMs", c.toastMs);
                c.systemPromptOverride = GetString(o, "systemPromptOverride", c.systemPromptOverride);
                c.widgetX = GetInt(o, "widgetX", c.widgetX);
                c.widgetY = GetInt(o, "widgetY", c.widgetY);
                c.widgetVisible = GetBool(o, "widgetVisible", c.widgetVisible);

                object tObj;
                if (o.TryGetValue("trigger", out tObj))
                {
                    Dictionary<string, object> t = tObj as Dictionary<string, object>;
                    if (t != null)
                    {
                        c.trigger.spaceTriple = GetBool(t, "spaceTriple", c.trigger.spaceTriple);
                        c.trigger.spaceCount = GetInt(t, "spaceCount", c.trigger.spaceCount);
                        c.trigger.windowMs = GetInt(t, "windowMs", c.trigger.windowMs);
                        c.trigger.hotkey = GetString(t, "hotkey", c.trigger.hotkey);
                        c.trigger.undoHotkey = GetString(t, "undoHotkey", c.trigger.undoHotkey);
                    }
                }

                object exObj;
                if (o.TryGetValue("excludeProcesses", out exObj))
                {
                    object[] arr = exObj as object[];
                    if (arr != null)
                    {
                        c.excludeProcesses = new List<string>();
                        foreach (object item in arr)
                        {
                            if (item != null) c.excludeProcesses.Add(Convert.ToString(item));
                        }
                    }
                }

                if (c.trigger.spaceCount < 2) c.trigger.spaceCount = 2;
                if (c.trigger.spaceCount > 6) c.trigger.spaceCount = 6;
                if (c.trigger.windowMs < 150) c.trigger.windowMs = 150;
                if (c.trigger.windowMs > 3000) c.trigger.windowMs = 3000;
                if (c.maxTokens < 256) c.maxTokens = 256;
                if (c.timeoutMs < 3000) c.timeoutMs = 3000;
                if (c.maxInputChars < 200) c.maxInputChars = 200;
                if (c.toastMs < 800) c.toastMs = 800;
                if (c.version < 2)
                {
                    c.version = 2;
                    Log.Write("config migrated to v2: maxInputChars=" + c.maxInputChars);
                    c.Save();
                }
                return c;
            }
            catch (Exception ex)
            {
                Log.Write("config load failed: " + ex.Message);
                return Defaults();
            }
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["version"] = version;
                o["enabled"] = enabled;
                o["baseUrl"] = baseUrl;
                o["apiKey"] = apiKey;
                o["model"] = model;
                o["temperature"] = temperature;
                o["maxTokens"] = maxTokens;
                o["timeoutMs"] = timeoutMs;
                o["maxInputChars"] = maxInputChars;
                o["clipboardFallback"] = clipboardFallback;
                o["autoStart"] = autoStart;
                o["toastMs"] = toastMs;
                o["systemPromptOverride"] = systemPromptOverride;
                o["widgetX"] = widgetX;
                o["widgetY"] = widgetY;
                o["widgetVisible"] = widgetVisible;

                Dictionary<string, object> t = new Dictionary<string, object>();
                t["spaceTriple"] = trigger == null ? true : trigger.spaceTriple;
                t["spaceCount"] = trigger == null ? 3 : trigger.spaceCount;
                t["windowMs"] = trigger == null ? 600 : trigger.windowMs;
                t["hotkey"] = trigger == null ? "Ctrl+Alt+L" : trigger.hotkey;
                t["undoHotkey"] = trigger == null ? "Ctrl+Alt+Z" : trigger.undoHotkey;
                o["trigger"] = t;

                o["excludeProcesses"] = excludeProcesses == null ? new string[0] : excludeProcesses.ToArray();

                JavaScriptSerializer ser = new JavaScriptSerializer();
                string json = ser.Serialize(o);
                File.WriteAllText(FilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Write("config save failed: " + ex.Message);
            }
        }

        private static string GetString(Dictionary<string, object> d, string key, string def)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return def;
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool def)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToBoolean(v); }
                catch { return def; }
            }
            return def;
        }

        private static int GetInt(Dictionary<string, object> d, string key, int def)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToInt32(v); }
                catch { return def; }
            }
            return def;
        }

        private static double GetDouble(Dictionary<string, object> d, string key, double def)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToDouble(v); }
                catch { return def; }
            }
            return def;
        }
    }
}
