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

        private readonly Config _cfg;
        private FloatWidget _widget;
        private KeyHook _hook;
        private Engine _engine;
        private HotkeyWindow _hotkeys;
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private Timer _stateReset;

        private ToolStripMenuItem _miEnabled;
        private ToolStripMenuItem _miWidget;
        private ToolStripMenuItem _miAutoStart;

        private static Bitmap _iconBitmap;

        public TrayApp(Config cfg)
        {
            _cfg = cfg;

            _engine = new Engine(cfg);
            _engine.OnState = OnEngineState;
            _engine.OnNotice = OnEngineNotice;

            Ipc.StartServer(OnIpcCommand);
            CreateWidget();
            CreateTray();
            CreateHotkeys();
            InstallHook();

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
        }

        private void UnregisterHotkeys()
        {
            try
            {
                Native.UnregisterHotKey(_hotkeys.Handle, HOTKEY_POLISH);
                Native.UnregisterHotKey(_hotkeys.Handle, HOTKEY_UNDO);
            }
            catch
            {
            }
        }

        private void OnHotkey(int id)
        {
            if (id == HOTKEY_POLISH) _engine.Trigger();
            else if (id == HOTKEY_UNDO) _engine.RequestUndo();
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
            }
            base.Dispose(disposing);
        }
    }
}
