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
            if (cfg.inPage) HttpApi.Start(cfg);
            CreateWidget();
            StartForegroundWatch();
            CreateTray();
            CreateHotkeys();
            InstallHook();
            CreateWatcher();

            AutoStart.Apply(cfg.autoStart);
            UpdateTrayToggles();
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
                    else if (string.Equals(command, "restore", StringComparison.OrdinalIgnoreCase)) _engine.TriggerPageRestore();
                    else if (string.Equals(command, "installext", StringComparison.OrdinalIgnoreCase)) InstallExtension();
                    else if (string.Equals(command, "summary", StringComparison.OrdinalIgnoreCase)) _engine.TriggerPageSummary();
                    else if (string.Equals(command, "panel", StringComparison.OrdinalIgnoreCase)) ShowQuickPanel();
                    else if (command != null && command.StartsWith("summary=", StringComparison.OrdinalIgnoreCase))
                    {
                        _engine.TriggerPageSummaryWith(command.Substring(8).Trim());
                    }
                    else if (command != null && command.StartsWith("mode=", StringComparison.OrdinalIgnoreCase))
                    {
                        _engine.TriggerPageSummaryMode(command.Substring(5).Trim());
                    }
                    else if (string.Equals(command, "ask", StringComparison.OrdinalIgnoreCase)) ShowQuickPanelAsk();
                    else if (command != null && command.StartsWith("ask=", StringComparison.OrdinalIgnoreCase))
                    {
                        _engine.TriggerPageAsk(command.Substring(4));
                    }
                    else if (string.Equals(command, "notes", StringComparison.OrdinalIgnoreCase)) OpenNotesFolder();
                    else if (string.Equals(command, "exports", StringComparison.OrdinalIgnoreCase)) OpenExportsFolder();
                    else if (string.Equals(command, "outline", StringComparison.OrdinalIgnoreCase)) _engine.TriggerPageOutline();
                    else if (string.Equals(command, "export", StringComparison.OrdinalIgnoreCase)) _engine.TriggerPageExport();
                    else if (string.Equals(command, "prompts", StringComparison.OrdinalIgnoreCase)) ShowPromptEditor();
                    else if (string.Equals(command, "restoreprompts", StringComparison.OrdinalIgnoreCase)) RestoreBuiltinPrompts();
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

        private Timer _fgWatch;

        /// <summary>1000ms 记一次「前台是浏览器」的时刻，供悬浮球 / 面板按钮判断要不要走网页内通道。</summary>
        private void StartForegroundWatch()
        {
            _fgWatch = new Timer();
            _fgWatch.Interval = 1000;
            _fgWatch.Tick += delegate { Engine.NoteForeground(); };
            _fgWatch.Start();
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

            // 兜底：用共享矢量图标画「渐变 squircle + 白色钢笔尖」，不再拿「润」字当图标
            _iconBitmap = Icons.AppTile(SystemInformation.SmallIconSize.Width);
            IntPtr h = _iconBitmap.GetHicon();
            return Icon.FromHandle(h);
        }

        // 托盘菜单图标：统一 16px 深墨色矢量图（全部来自 IconsData，不再用汉字/符号当图标）
        private static Image MenuIcon(string id)
        {
            return Icons.MenuImage(id);
        }

        private void CreateTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = "InputPolish · 三连空格 AI 润色";
            _tray.Visible = true;

            ContextMenuStrip m = new ContextMenuStrip();
            m.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            m.ImageScalingSize = new Size(16, 16);

            _miEnabled = new ToolStripMenuItem("启用（三连空格润色）");
            _miEnabled.CheckOnClick = false;
            _miEnabled.Click += delegate { ToggleEnabled(); };
            m.Items.Add(_miEnabled);

            _miWidget = new ToolStripMenuItem("显示桌面悬浮框");
            _miWidget.Click += delegate { ToggleWidget(); };
            m.Items.Add(_miWidget);

            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miNow = new ToolStripMenuItem("立即润色当前输入框");
            miNow.Image = MenuIcon("pen");
            miNow.Click += delegate { _engine.Trigger(); };
            m.Items.Add(miNow);

            ToolStripMenuItem miUndo = new ToolStripMenuItem("撤销上一次润色");
            miUndo.Image = MenuIcon("undo");
            miUndo.Click += delegate { _engine.RequestUndo(); };
            m.Items.Add(miUndo);

            ToolStripMenuItem miStyle = new ToolStripMenuItem("润色风格");
            miStyle.Image = MenuIcon("summarize");
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
            miTranslate.Image = MenuIcon("translate");
            miTranslate.Click += delegate { _engine.TriggerTranslate(); };
            m.Items.Add(miTranslate);

            ToolStripMenuItem miPage = new ToolStripMenuItem("翻译整个网页");
            miPage.Image = MenuIcon("translate");
            miPage.Click += delegate { _engine.TriggerPageTranslate(); };
            m.Items.Add(miPage);

            ToolStripMenuItem miRestore = new ToolStripMenuItem("还原网页为原文");
            miRestore.Image = MenuIcon("undo");
            miRestore.Click += delegate { _engine.TriggerPageRestore(); };
            m.Items.Add(miRestore);

            ToolStripMenuItem miSummary = new ToolStripMenuItem("总结当前网页");
            miSummary.Image = MenuIcon("summarize");
            miSummary.Click += delegate { _engine.TriggerPageSummary(); };
            PromptItem[] presets = PromptLib.All();
            for (int mi = 0; mi < presets.Length; mi++)
            {
                PromptItem pm = presets[mi];
                // 自定义提示词以前用「☆」前缀，现在换成 note 图标区分（builtin 用 summarize）
                string text = pm.Builtin ? pm.Label + "　" + pm.Hint : pm.Label + "　" + pm.Hint;
                ToolStripMenuItem sub = new ToolStripMenuItem(text);
                sub.Image = MenuIcon(pm.Builtin ? "summarize" : "note");
                sub.Click += delegate { _engine.TriggerPageSummaryMode(pm.Id); };
                miSummary.DropDownItems.Add(sub);
            }
            miSummary.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem miEditPrompt = new ToolStripMenuItem("编辑提示词…");
            miEditPrompt.Image = MenuIcon("pen");
            miEditPrompt.Click += delegate { ShowPromptEditor(); };
            miSummary.DropDownItems.Add(miEditPrompt);
            ToolStripMenuItem miRebuild = new ToolStripMenuItem("恢复内置提示词");
            miRebuild.Image = MenuIcon("undo");
            miRebuild.Click += delegate { RestoreBuiltinPrompts(); };
            miSummary.DropDownItems.Add(miRebuild);
            m.Items.Add(miSummary);

            ToolStripMenuItem miAsk = new ToolStripMenuItem("问这一页…");
            miAsk.Image = MenuIcon("chat");
            miAsk.Click += delegate { ShowQuickPanelAsk(); };
            m.Items.Add(miAsk);

            ToolStripMenuItem miOutline = new ToolStripMenuItem("页面大纲（网页内左侧目录）");
            miOutline.Image = MenuIcon("outline");
            miOutline.Click += delegate { _engine.TriggerPageOutline(); };
            m.Items.Add(miOutline);

            ToolStripMenuItem miExport = new ToolStripMenuItem("导出本页 Markdown");
            miExport.Image = MenuIcon("export");
            miExport.Click += delegate { _engine.TriggerPageExport(); };
            m.Items.Add(miExport);

            ToolStripMenuItem miNotes = new ToolStripMenuItem("打开笔记目录");
            miNotes.Image = MenuIcon("note");
            miNotes.Click += delegate { OpenNotesFolder(); };
            m.Items.Add(miNotes);

            ToolStripMenuItem miExports = new ToolStripMenuItem("打开导出目录");
            miExports.Image = MenuIcon("export");
            miExports.Click += delegate { OpenExportsFolder(); };
            m.Items.Add(miExports);

            ToolStripMenuItem miPanel = new ToolStripMenuItem("快捷面板（提示词 · 总结 · 翻译）");
            miPanel.Image = MenuIcon("columns");
            miPanel.Click += delegate { ShowQuickPanel(); };
            m.Items.Add(miPanel);

            ToolStripMenuItem miInstall = new ToolStripMenuItem("安装浏览器扩展…");
            miInstall.Image = MenuIcon("plus");
            miInstall.Click += delegate { InstallExtension(); };
            m.Items.Add(miInstall);

            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miSettings = new ToolStripMenuItem("设置…");
            miSettings.Image = MenuIcon("settings");
            miSettings.Click += delegate { OpenSettings(); };
            m.Items.Add(miSettings);

            ToolStripMenuItem miLog = new ToolStripMenuItem("打开日志");
            miLog.Image = MenuIcon("note");
            miLog.Click += delegate { OpenPath(Log.LogPath); };
            m.Items.Add(miLog);

            ToolStripMenuItem miDir = new ToolStripMenuItem("打开配置目录");
            miDir.Image = MenuIcon("outline");
            miDir.Click += delegate { OpenPath(Config.Dir); };
            m.Items.Add(miDir);

            m.Items.Add(new ToolStripSeparator());

            _miAutoStart = new ToolStripMenuItem("开机自启");
            _miAutoStart.Click += delegate
            {
                bool target = !AutoStart.IsEnabled();
                AutoStart.Apply(target);
                _cfg.autoStart = AutoStart.IsEnabled();
                _cfg.Save();
                UpdateTrayToggles();
                Toast.ShowNotice(target ? "已设置开机自启" : "已取消开机自启", false, 1400);
            };
            m.Items.Add(_miAutoStart);

            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Image = MenuIcon("quit");
            miExit.Click += delegate { ExitApp(); };
            m.Items.Add(miExit);

            _menu = m;
            _tray.ContextMenuStrip = m;
            _tray.DoubleClick += delegate { OpenSettings(); };
            UpdateTrayToggles();
        }

        /// <summary>
        /// 三个开关项（启用 / 悬浮框 / 开机自启）不用 Checked：ToolStripMenuItem 的勾和图标
        /// 都画在同一个左侧 margin 里，会互相盖住。改成「图标 + 文字」一起表达状态。
        /// </summary>
        private void UpdateTrayToggles()
        {
            if (_miEnabled != null)
            {
                _miEnabled.Text = _cfg.enabled ? "启用（三连空格润色）" : "已暂停（点此启用）";
                _miEnabled.Image = MenuIcon(_cfg.enabled ? "play" : "pause");
            }
            if (_miWidget != null)
            {
                _miWidget.Text = _cfg.widgetVisible ? "隐藏桌面悬浮框" : "显示桌面悬浮框";
                _miWidget.Image = MenuIcon("brand");
            }
            if (_miAutoStart != null)
            {
                bool on = AutoStart.IsEnabled();
                _miAutoStart.Text = on ? "开机自启：开" : "开机自启：关";
                _miAutoStart.Image = MenuIcon(on ? "check" : "off");
            }
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

        /// <summary>Ctrl+` 面板：提示词芯片 + 补充要求 + 自由指令 + 总结/翻译本页。</summary>
        private void ShowQuickPanel()
        {
            Log.Write("quick panel requested prompts=" + PromptLib.Count());
            QuickPanel.ShowPanel(_cfg,
                delegate(string prompt, string mode)
                {
                    if (mode.Length > 0 && PromptLib.IsValid(mode) && !string.Equals(_cfg.summaryMode, mode, StringComparison.Ordinal))
                    {
                        _cfg.summaryMode = mode;
                        _cfg.Save();   // 记住上次选的提示词，下次打开面板还停在它上面
                        Log.Write("summary mode remembered: " + mode);
                    }
                    _engine.TriggerPageSummaryWith(prompt, mode);
                },
                delegate(string question) { _engine.TriggerPageAsk(question); },
                delegate(string instruction) { _engine.TriggerPageRun(instruction); },
                delegate { _engine.TriggerPageTranslate(); });
        }

        /// <summary>托盘「编辑提示词…」：没有选中项时打开第一条；Esc 取消不改任何东西。</summary>
        /// <summary>把用户删掉/改坏的内置方案补回来（托盘与 IPC 共用）。</summary>
        private void RestoreBuiltinPrompts()
        {
            int added = PromptLib.RestoreBuiltins();
            Log.Write("prompt lib restore added=" + added);
            Toast.ShowNotice(added > 0 ? "已补回 " + added + " 条内置提示词" : "内置提示词都在，没有需要补的", false, 1600);
        }

        private void ShowPromptEditor()
        {
            try
            {
                PromptItem[] all = PromptLib.All();
                if (all.Length == 0) return;
                string id = _cfg.summaryMode;
                if (!PromptLib.IsValid(id)) id = all[0].Id;
                PromptItem saved;
                bool changed = PromptEditForm.Show(null, id, out saved);
                Log.Write("prompt editor from tray changed=" + changed + " id=" + id);
            }
            catch (Exception ex)
            {
                Log.Write("prompt editor failed: " + ex.Message);
            }
        }

        /// <summary>打开面板并把光标放进「问这一页」。</summary>
        private void ShowQuickPanelAsk()
        {
            ShowQuickPanel();
            QuickPanel.FocusAsk();
        }

        /// <summary>打开 %APPDATA%\InputPolish\notes（存下来的网页笔记都在这里）。</summary>
        private void OpenNotesFolder()
        {
            try
            {
                string dir = Notes.Dir();
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                Log.Write("notes folder opened " + dir);
            }
            catch (Exception ex)
            {
                Log.Write("open notes folder failed: " + ex.Message);
            }
        }

        /// <summary>打开 %APPDATA%\InputPolish\exports（导出的 Markdown 都在这里）。</summary>
        private void OpenExportsFolder()
        {
            try
            {
                string dir = Exports.Dir();
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                Log.Write("exports folder opened " + dir);
            }
            catch (Exception ex)
            {
                Log.Write("open exports folder failed: " + ex.Message);
            }
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
            UpdateTrayToggles();
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
            UpdateTrayToggles();
        }

        /// <summary>一次性装扩展引导：打开 ext 目录 + 路径进剪贴板 + 说明窗口（扩展只需装一次）。</summary>
        private void InstallExtension()
        {
            string dir = Config.ExtDir();
            if (dir == null || dir.Length == 0)
            {
                Log.Write("install extension: ext dir not found");
                MessageBox.Show(
                    "没找到 ext 扩展目录。\n\n请确认 InputPolish.exe 旁边有 ext 文件夹（解压安装包时整个文件夹都要留着）。",
                    "安装浏览器扩展", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); }
            catch (Exception ex) { Log.Write("open ext folder failed: " + ex.Message); }

            // 路径放进剪贴板：文件选择框里 Ctrl+L 粘贴就能直接到这一层，避免选成上一层
            try { Clipboard.SetText(dir); }
            catch (Exception ex) { Log.Write("clipboard for ext path failed: " + ex.Message); }

            Log.Write("install extension guide opened ext=" + dir);
            MessageBox.Show(
                "浏览器扩展只需装一次，装完开机自动连上，不用再填连接码。\n\n" +
                "1）浏览器地址栏输入：chrome://extensions\n" +
                "2）右上角打开「开发者模式」\n" +
                "3）点「加载已解压的扩展程序」\n" +
                "4）在弹出的选择框里按 Ctrl+L，再按 Ctrl+V（扩展路径已复制到剪贴板），回车\n" +
                "5）回到这里打开「设置 → 网页内翻译」，状态显示「扩展状态：已连接」就成功了\n\n" +
                "要选的是 ext 这一层：打开后能直接看到 manifest.json 和 ip-bg.js。\n" +
                "选成它的上一层（input-polish）会提示「清单文件缺失或不可读取」，那是选错层了。\n\n" +
                "路径：" + dir,
                "安装浏览器扩展", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            if (_cfg.inPage)
            {
                if (!HttpApi.Running || HttpApi.Port != _cfg.apiPort) HttpApi.Start(_cfg);
                else Config.WritePairFile(_cfg);
            }
            else
            {
                HttpApi.Stop();
            }

            if (_cfg.widgetVisible)
            {
                if (!_widget.Visible) _widget.Show();
            }
            else if (_widget.Visible)
            {
                _widget.Hide();
            }
            UpdateTrayToggles();

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
                HttpApi.Stop();
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
