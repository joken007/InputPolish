using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InputPolish
{
    public class TrayApp : ApplicationContext
    {
        private const int HOTKEY_POLISH = 0x1001;
        private const int HOTKEY_UNDO = 0x1002;
        private const int HOTKEY_TRANSLATE = 0x1003;
        private const int HOTKEY_WEB = 0x1004;
        private const int HOTKEY_PANEL = 0x1005;

        private readonly Config _cfg;
        private FloatWidget _widget;
        private KeyHook _hook;
        private Engine _engine;
        private HotkeyWindow _hotkeys;
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private Timer _stateReset;
        private SelectionWatcher _watcher;

        private ToolStripMenuItem _miEnabled;
        private ToolStripMenuItem _miWidget;
        private ToolStripMenuItem _miAutoStart;
        private ToolStripMenuItem[] _miStyle = new ToolStripMenuItem[4];

        private static Bitmap _iconBitmap;

        public TrayApp(Config cfg)
        {
            _cfg = cfg;

            _engine = new Engine(cfg);
            _engine.OnState = OnEngineState;
            _engine.OnNotice = OnEngineNotice;
            _engine.UiDispatch = delegate(Action a) { Ui(a); };
            _engine.UiInvoke = delegate(Action a) { return InvokeOnUi(a); };

            Ipc.StartServer(OnIpcCommand);
            CreateWidget();
            CreateTray();
            CreateHotkeys();
            InstallHook();
            CreateWatcher();

            AutoStart.Apply(cfg.autoStart);
            if (_miAutoStart != null) _miAutoStart.Checked = AutoStart.IsEnabled();
            Log.Write("autostart=" + cfg.autoStart + " registry=" + AutoStart.IsEnabled());

            Log.Write("ready: model=" + cfg.model + " base=" + cfg.baseUrl + " enabled=" + cfg.enabled +
                      " hotkey=" + cfg.trigger.hotkey);
        }

        private void OnIpcCommand(string command)
        {
            try
            {
                _widget.BeginInvoke(new Action(delegate()
                {
                    if (string.Equals(command, "polish", StringComparison.OrdinalIgnoreCase)) _engine.Trigger();
                    else if (string.Equals(command, "undo", StringComparison.OrdinalIgnoreCase)) _engine.RequestUndo();
                    else if (string.Equals(command, "settings", StringComparison.OrdinalIgnoreCase)) OpenSettings();
                    else if (string.Equals(command, "menu", StringComparison.OrdinalIgnoreCase)) ShowMenu();
                    else if (string.Equals(command, "translate", StringComparison.OrdinalIgnoreCase)) _engine.TriggerTranslate();
                    else if (string.Equals(command, "page", StringComparison.OrdinalIgnoreCase)) _engine.TriggerPageTranslate();
                    else if (string.Equals(command, "summary", StringComparison.OrdinalIgnoreCase)) _engine.TriggerPageSummary();
                    else if (string.Equals(command, "panel", StringComparison.OrdinalIgnoreCase)) ShowQuickPanel();
                    else if (command != null && command.StartsWith("summary=", StringComparison.OrdinalIgnoreCase))
                    {
                        _engine.TriggerPageSummaryWith(command.Substring(8).Trim());
                    }
                    else if (command != null && command.StartsWith("ttext=", StringComparison.OrdinalIgnoreCase))
                    {
                        _engine.TriggerTranslateWith(command.Substring(6));
                    }
                    else if (string.Equals(command, "style", StringComparison.OrdinalIgnoreCase)) _engine.CycleStyle();
                    else if (command != null && command.StartsWith("style=", StringComparison.OrdinalIgnoreCase))
                    {
                        _engine.SetStyle(command.Substring(6).Trim());
                        UpdateStyleMenu();
                    }
                    else Log.Write("ipc command unknown: " + command);
                }));
            }
            catch (Exception ex)
            {
                Log.Write("ipc command failed: " + ex.Message);
            }
        }

        private void CreateWidget()
        {
            _widget = new FloatWidget();
            _widget.ApplyScale();

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int wx = _cfg.widgetX;
            int wy = _cfg.widgetY;
            bool ok = true;
            if (wx < wa.Left - 100 || wx > wa.Right || wy < wa.Top - 100 || wy > wa.Bottom) ok = false;
            if (wx < 0 || wy < 0) ok = false;
            if (!ok)
            {
                wx = wa.Right - 380;
                wy = wa.Bottom - 160;
            }
            _widget.SetHome(wx, wy);
            _widget.SetEnabled(_cfg.enabled);

            _widget.ToggleEnabledRequested += ToggleEnabled;
            _widget.SettingsRequested += OpenSettings;
            _widget.UndoRequested += delegate { _engine.RequestUndo(); };
            _widget.PageTranslateRequested += delegate { _engine.TriggerPageTranslate(); };
            _widget.PageSummaryRequested += delegate { _engine.TriggerPageSummary(); };
            _widget.ExitRequested += ExitApp;
            _widget.PositionChanged += delegate
            {
                _cfg.widgetX = _widget.Home.X;
                _cfg.widgetY = _widget.Home.Y;
                _cfg.Save();
            };

            if (_cfg.widgetVisible)
            {
                _widget.Show();
            }
        }

        private static Icon MakeIcon()
        {
            try
            {
                // 优先使用嵌入资源里的多尺寸 .ico（与 exe 图标、悬浮球视觉一致）
                System.IO.Stream s = typeof(TrayApp).Assembly.GetManifestResourceStream("InputPolish.app.ico");
                if (s != null)
                {
                    using (s)
                    {
                        Icon ic = new Icon(s, SystemInformation.SmallIconSize);
                        if (ic != null) return ic;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("embedded icon load failed: " + ex.Message);
            }

            _iconBitmap = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(_iconBitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                    Color.FromArgb(124, 92, 255), Color.FromArgb(58, 132, 255), 45f))
                {
                    g.FillEllipse(br, 1, 1, 30, 30);
                }
                using (Font f = new Font("Microsoft YaHei", 17f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush b = new SolidBrush(Color.White))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("润", f, b, new RectangleF(0, 0, 32, 32), sf);
                }
            }
            IntPtr h = _iconBitmap.GetHicon();
            return Icon.FromHandle(h);
        }

        private void CreateTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = "InputPolish · 三连空格 AI 润色";
            _tray.Visible = true;

            ContextMenuStrip m = new ContextMenuStrip();
            m.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            _miEnabled = new ToolStripMenuItem("启用（三连空格润色）");
            _miEnabled.CheckOnClick = false;
            _miEnabled.Checked = _cfg.enabled;
            _miEnabled.Click += delegate { ToggleEnabled(); };
            m.Items.Add(_miEnabled);

            _miWidget = new ToolStripMenuItem("显示桌面悬浮框");
            _miWidget.Checked = _cfg.widgetVisible;
            _miWidget.Click += delegate { ToggleWidget(); };
            m.Items.Add(_miWidget);

            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miNow = new ToolStripMenuItem("立即润色当前输入框");
            miNow.Click += delegate { _engine.Trigger(); };
            m.Items.Add(miNow);

            ToolStripMenuItem miUndo = new ToolStripMenuItem("撤销上一次润色");
            miUndo.Click += delegate { _engine.RequestUndo(); };
            m.Items.Add(miUndo);

            ToolStripMenuItem miStyle = new ToolStripMenuItem("润色风格");
            for (int i = 0; i < Styles.Ids.Length; i++)
            {
                string id = Styles.Ids[i];
                _miStyle[i] = new ToolStripMenuItem(Styles.Label(id));
                _miStyle[i].Tag = id;
                _miStyle[i].Click += delegate(object sender, EventArgs e)
                {
                    ToolStripMenuItem it = sender as ToolStripMenuItem;
                    if (it == null) return;
                    _engine.SetStyle((string)it.Tag);
                    UpdateStyleMenu();
                };
                miStyle.DropDownItems.Add(_miStyle[i]);
            }
            m.Items.Add(miStyle);
            UpdateStyleMenu();

            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miTranslate = new ToolStripMenuItem("翻译选中文字");
            miTranslate.Click += delegate { _engine.TriggerTranslate(); };
            m.Items.Add(miTranslate);

            ToolStripMenuItem miPage = new ToolStripMenuItem("翻译整个网页");
            miPage.Click += delegate { _engine.TriggerPageTranslate(); };
            m.Items.Add(miPage);

            ToolStripMenuItem miSummary = new ToolStripMenuItem("总结当前网页");
            miSummary.Click += delegate { _engine.TriggerPageSummary(); };
            m.Items.Add(miSummary);

            ToolStripMenuItem miPanel = new ToolStripMenuItem("快捷面板（提示词 · 总结 · 翻译）");
            miPanel.Click += delegate { ShowQuickPanel(); };
            m.Items.Add(miPanel);

            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miSettings = new ToolStripMenuItem("设置…");
            miSettings.Click += delegate { OpenSettings(); };
            m.Items.Add(miSettings);

            ToolStripMenuItem miLog = new ToolStripMenuItem("打开日志");
            miLog.Click += delegate { OpenPath(Log.LogPath); };
            m.Items.Add(miLog);

            ToolStripMenuItem miDir = new ToolStripMenuItem("打开配置目录");
            miDir.Click += delegate { OpenPath(Config.Dir); };
            m.Items.Add(miDir);

            m.Items.Add(new ToolStripSeparator());

            _miAutoStart = new ToolStripMenuItem("开机自启");
            _miAutoStart.Checked = AutoStart.IsEnabled();
            _miAutoStart.Click += delegate
            {
                bool target = !AutoStart.IsEnabled();
                AutoStart.Apply(target);
                _miAutoStart.Checked = AutoStart.IsEnabled();
                _cfg.autoStart = _miAutoStart.Checked;
                _cfg.Save();
                Toast.ShowNotice(target ? "已设置开机自启" : "已取消开机自启", false, 1400);
            };
            m.Items.Add(_miAutoStart);

            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { ExitApp(); };
            m.Items.Add(miExit);

            _menu = m;
            _tray.ContextMenuStrip = m;
            _tray.DoubleClick += delegate { OpenSettings(); };
        }

        /// <summary>把菜单里「润色风格」的勾选状态与当前 config 同步。</summary>
        private void UpdateStyleMenu()
        {
            if (_miStyle == null) return;
            string cur = Styles.Normalize(_cfg.polishStyle);
            for (int i = 0; i < _miStyle.Length; i++)
            {
                if (_miStyle[i] == null) continue;
                _miStyle[i].Checked = string.Equals((string)_miStyle[i].Tag, cur, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void ShowMenu()
        {
            if (_menu == null) return;
            Point p = new Point(_widget.Home.X + 10, _widget.Home.Y + 10);
            Log.Write("menu shown at " + p.X + "," + p.Y + " items=" + _menu.Items.Count);
            _menu.Show(p);
        }

        private void CreateHotkeys()
        {
            _hotkeys = new HotkeyWindow();
            _hotkeys.Hotkey += OnHotkey;
            RegisterHotkeys();
        }

        private void RegisterHotkeys()
        {
            UnregisterHotkeys();
            int mods;
            int vk;

            if (_cfg.trigger.hotkey != null && _cfg.trigger.hotkey.Length > 0)
            {
                if (HotkeyWindow.ParseHotkey(_cfg.trigger.hotkey, out mods, out vk))
                {
                    bool ok = Native.RegisterHotKey(_hotkeys.Handle, HOTKEY_POLISH, mods | Native.MOD_NOREPEAT, vk);
                    Log.Write("register hotkey " + _cfg.trigger.hotkey + " -> " + ok);
                    if (!ok)
                    {
                        Toast.ShowNotice("润色热键注册失败（可能被其它软件占用）：" + _cfg.trigger.hotkey, true, 3200);
                    }
                }
                else
                {
                    Log.Write("hotkey parse failed: " + _cfg.trigger.hotkey);
                }
            }

            if (_cfg.trigger.undoHotkey != null && _cfg.trigger.undoHotkey.Length > 0)
            {
                if (HotkeyWindow.ParseHotkey(_cfg.trigger.undoHotkey, out mods, out vk))
                {
                    bool ok = Native.RegisterHotKey(_hotkeys.Handle, HOTKEY_UNDO, mods | Native.MOD_NOREPEAT, vk);
                    Log.Write("register undo hotkey " + _cfg.trigger.undoHotkey + " -> " + ok);
                    if (!ok)
                    {
                        Toast.ShowNotice("撤销热键注册失败：" + _cfg.trigger.undoHotkey, true, 3200);
                    }
                }
            }

            if (_cfg.translateHotkey != null && _cfg.translateHotkey.Length > 0)
            {
                if (HotkeyWindow.ParseHotkey(_cfg.translateHotkey, out mods, out vk))
                {
                    bool ok = Native.RegisterHotKey(_hotkeys.Handle, HOTKEY_TRANSLATE, mods | Native.MOD_NOREPEAT, vk);
                    Log.Write("register translate hotkey " + _cfg.translateHotkey + " -> " + ok);
                    if (!ok) Toast.ShowNotice("划词翻译热键注册失败（可能被占用）：" + _cfg.translateHotkey, true, 3200);
                }
                else
                {
                    Log.Write("translate hotkey parse failed: " + _cfg.translateHotkey);
                }
            }

            if (_cfg.webTranslateHotkey != null && _cfg.webTranslateHotkey.Length > 0)
            {
                if (HotkeyWindow.ParseHotkey(_cfg.webTranslateHotkey, out mods, out vk))
                {
                    bool ok = Native.RegisterHotKey(_hotkeys.Handle, HOTKEY_WEB, mods | Native.MOD_NOREPEAT, vk);
                    Log.Write("register page hotkey " + _cfg.webTranslateHotkey + " -> " + ok);
                    if (!ok) Toast.ShowNotice("网页翻译热键注册失败（可能被占用）：" + _cfg.webTranslateHotkey, true, 3200);
                }
                else
                {
                    Log.Write("page hotkey parse failed: " + _cfg.webTranslateHotkey);
                }
            }

            if (_cfg.panelHotkey != null && _cfg.panelHotkey.Length > 0)
            {
                if (HotkeyWindow.ParseHotkey(_cfg.panelHotkey, out mods, out vk))
                {
                    bool ok = Native.RegisterHotKey(_hotkeys.Handle, HOTKEY_PANEL, mods | Native.MOD_NOREPEAT, vk);
                    Log.Write("register panel hotkey " + _cfg.panelHotkey + " -> " + ok);
                    if (!ok) Toast.ShowNotice("快捷面板热键注册失败（可能被占用）：" + _cfg.panelHotkey, true, 3200);
                }
                else
                {
                    Log.Write("panel hotkey parse failed: " + _cfg.panelHotkey);
                }
            }
        }

        private void UnregisterHotkeys()
        {
            try
            {
                Native.UnregisterHotKey(_hotkeys.Handle, HOTKEY_POLISH);
                Native.UnregisterHotKey(_hotkeys.Handle, HOTKEY_UNDO);
                Native.UnregisterHotKey(_hotkeys.Handle, HOTKEY_TRANSLATE);
                Native.UnregisterHotKey(_hotkeys.Handle, HOTKEY_WEB);
                Native.UnregisterHotKey(_hotkeys.Handle, HOTKEY_PANEL);
            }
            catch
            {
            }
        }

        private void OnHotkey(int id)
        {
            if (id == HOTKEY_POLISH) _engine.Trigger();
            else if (id == HOTKEY_UNDO) _engine.RequestUndo();
            else if (id == HOTKEY_TRANSLATE) _engine.TriggerTranslate();
            else if (id == HOTKEY_WEB) _engine.TriggerPageTranslate();
            else if (id == HOTKEY_PANEL) ShowQuickPanel();
        }

        /// <summary>Ctrl+` 面板：默认提示词 + 总结本页 / 翻译本页。</summary>
        private void ShowQuickPanel()
        {
            Log.Write("quick panel requested");
            QuickPanel.ShowPanel(_cfg,
                delegate(string prompt) { _engine.TriggerPageSummaryWith(prompt); },
                delegate { _engine.TriggerPageTranslate(); });
        }

        private void CreateWatcher()
        {
            _watcher = new SelectionWatcher(_cfg, delegate(Action a) { return InvokeOnUi(a); });
            _watcher.TranslateRequested += delegate(string text) { _engine.TriggerTranslateWith(text); };
            _watcher.Start();
            Log.Write("selection watcher started installed=" + _watcher.Installed + " bubble=" + _cfg.selectionBubble);
        }

        /// <summary>在 UI 线程同步执行（引擎需要拿到阅读窗引用）。</summary>
        private bool InvokeOnUi(Action action)
        {
            if (_widget != null && _widget.IsHandleCreated)
            {
                if (_widget.InvokeRequired)
                {
                    _widget.Invoke((MethodInvoker)delegate { action(); });
                    return true;
                }
            }
            action();
            return true;
        }

        private void InstallHook()
        {
            _hook = new KeyHook();
            _hook.TripleSpace += delegate { _engine.Trigger(); };
            _hook.Configure(_cfg.trigger.spaceCount, _cfg.trigger.windowMs, _cfg.trigger.spaceTriple && _cfg.enabled);
            if (!_hook.Install())
            {
                Toast.ShowNotice("全局键盘钩子安装失败，三连空格不可用（可改用热键）", true, 4000);
            }
        }

        private void Ui(Action action)
        {
            try
            {
                if (_widget != null && _widget.IsHandleCreated)
                {
                    _widget.BeginInvoke((MethodInvoker)delegate { action(); });
                }
            }
            catch
            {
            }
        }

        private void OnEngineState(WidgetState state, string text)
        {
            Ui(delegate
            {
                if (_widget != null) _widget.PostState(state, text);
                if (state == WidgetState.Success || state == WidgetState.Error)
                {
                    ArmStateReset();
                }
            });
        }

        private void OnEngineNotice(string text, bool isError)
        {
            Ui(delegate
            {
                Toast.ShowNotice(text, isError, isError ? Math.Max(3000, _cfg.toastMs * 2) : _cfg.toastMs);
            });
        }

        private void ArmStateReset()
        {
            if (_stateReset == null)
            {
                _stateReset = new Timer();
                _stateReset.Interval = 2600;
                _stateReset.Tick += delegate
                {
                    _stateReset.Stop();
                    if (_widget != null)
                    {
                        _widget.PostState(_cfg.enabled ? WidgetState.Idle : WidgetState.Paused, "");
                    }
                };
            }
            _stateReset.Stop();
            _stateReset.Start();
        }

        private void ToggleEnabled()
        {
            _cfg.enabled = !_cfg.enabled;
            _cfg.Save();
            if (_widget != null) _widget.SetEnabled(_cfg.enabled);
            if (_hook != null)
            {
                _hook.Configure(_cfg.trigger.spaceCount, _cfg.trigger.windowMs, _cfg.trigger.spaceTriple && _cfg.enabled);
            }
            if (_miEnabled != null) _miEnabled.Checked = _cfg.enabled;
            Toast.ShowNotice(_cfg.enabled ? "已启用：三连空格即可润色" : "已暂停：三连空格不再触发", false, 1600);
            Log.Write("enabled=" + _cfg.enabled);
        }

        private void ToggleWidget()
        {
            _cfg.widgetVisible = !_cfg.widgetVisible;
            _cfg.Save();
            if (_cfg.widgetVisible)
            {
                _widget.Show();
                _widget.TopMost = true;
            }
            else
            {
                _widget.Hide();
            }
            if (_miWidget != null) _miWidget.Checked = _cfg.widgetVisible;
        }

        private void OpenSettings()
        {
            try
            {
                using (SettingsForm f = new SettingsForm(_cfg))
                {
                    f.TopMost = true;
                    DialogResult r = f.ShowDialog();
                    if (r == DialogResult.OK)
                    {
                        ApplyConfig();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("settings error: " + ex);
                MessageBox.Show("打开设置失败：" + ex.Message, "InputPolish");
            }
        }

        private void ApplyConfig()
        {
            RegisterHotkeys();
            if (_hook != null)
            {
                _hook.Configure(_cfg.trigger.spaceCount, _cfg.trigger.windowMs, _cfg.trigger.spaceTriple && _cfg.enabled);
            }
            AutoStart.Apply(_cfg.autoStart);
            if (_miAutoStart != null) _miAutoStart.Checked = AutoStart.IsEnabled();

            if (_cfg.widgetVisible)
            {
                if (!_widget.Visible) _widget.Show();
            }
            else if (_widget.Visible)
            {
                _widget.Hide();
            }
            if (_miWidget != null) _miWidget.Checked = _cfg.widgetVisible;

            Toast.ShowNotice("设置已保存", false, 1200);
            Log.Write("settings applied");
        }

        private void OpenPath(string path)
        {
            try
            {
                if (path == null) return;
                if (System.IO.File.Exists(path))
                {
                    System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
                }
                else if (System.IO.Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
                }
            }
            catch (Exception ex)
            {
                Log.Write("open path failed: " + ex.Message);
            }
        }

        private void ExitApp()
        {
            Log.Write("exit requested");
            try
            {
                if (_hook != null) _hook.Uninstall();
                UnregisterHotkeys();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
                if (_engine != null) _engine.Shutdown();
            }
            catch
            {
            }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (_hook != null) _hook.Uninstall();
                }
                catch
                {
                }
                try
                {
                    if (_watcher != null) _watcher.Dispose();
                }
                catch
                {
                }
            }
            base.Dispose(disposing);
        }
    }
}
