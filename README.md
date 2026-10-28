# InputPolish

> 在**任何**软件、**任何**网页的输入框里敲三下空格，AI 帮你把草稿理顺写回去。原意不变，只改表达。

<p align="center">
  <img src="assets/icon-256.png" width="128" alt="InputPolish 图标">
</p>

<p align="center">
  <img src="assets/screenshots/widget-idle.png" width="150" alt="悬浮球">
  <img src="assets/screenshots/widget-hover.png" width="420" alt="悬浮球展开">
</p>

在任何软件、任何网页的输入框里敲 **三下空格**，就会把草稿交给 AI 改成「更有逻辑、更结构化」的版本，然后自动写回原输入框。原意不变，只改表达。

- 交付物：`dist\InputPolish.exe`（单文件，.NET Framework 4.8，无需安装运行时）
- 配置/日志：`%APPDATA%\InputPolish\`（`config.json`、`log.txt`、`last-result.txt`）
- 语言/依赖：C# 5 + WinForms + UIAutomation，**零第三方依赖**，不联网除 AI 接口外不发任何请求

---

## 一、开始使用

1. 双击 `InputPolish.exe`。右下角出现一个蓝色渐变圆球，上面写着「润」。
2. 打开任意软件或网页，点进一个**可编辑的输入框**，写一段草稿，例如：
   ```
   这个方案有点问题主要是成本太高而且交付周期长
   ```
3. 连续敲 **三下空格**（间隔 600ms 内）。
4. 圆球变橙（处理中）→ 2~6 秒后变绿，输入框内容被替换成：
   ```
   这个方案有点问题，主要是成本太高，而且交付周期长。
   ```
   屏幕提示「已优化 · 2.5s · Ctrl+Alt+Z 撤销」。
5. 如果改得不满意，按 **Ctrl+Alt+Z** 一键还原。

三下空格本身不会留在输入框里（会被撤回）。

---

## 二、三种触发方式

| 方式 | 操作 | 说明 |
| --- | --- | --- |
| 连击空格 | 三下空格 | 主力方式，所有软件通用，可在设置里改成 2~6 下 |
| 热键 | **Ctrl+Alt+L** | 键盘上不方便连按空格时用（如全屏游戏、远程桌面） |
| 悬浮球 / 托盘 | 右键圆球 → 「立即润色当前输入框」 | 也可以用鼠标操作 |

**撤销**：**Ctrl+Alt+Z**，或悬浮球/托盘菜单的「撤销上一次润色」。撤销在 10 分钟内有效，且会自动把原窗口切回前台再改。

---

## 三、桌面悬浮球

始终置顶，鼠标移上去自动横向展开：

```
[ 暂停 / 启用 ] [ 设置 ] [ 撤销 ] [ 退出 ]
```

- **单击**圆球：钉住（展开后不会自动收起）；再单击取消钉住
- **左键拖动**：换位置（位置会记住）
- **双击**：打开设置
- **右键**：完整菜单（含「打开日志」「打开配置目录」「开机自启」「退出」）
- 圆球颜色 = 状态：蓝紫待命 / 橙色转圈处理中 / 绿色成功 / 红色出错 / 灰色斜杠已暂停

不想要悬浮球：设置里取消「显示桌面悬浮框」，或托盘菜单同名项。功能不受影响，托盘图标仍在。

### 图标

整个视觉是同一套：紫蓝渐变圆角方块 + 白色「润」字 + 右上角四角星（代表 AI），悬浮球、托盘图标、exe 图标、设置窗口标题栏图标全部同源。

- 图标源文件：[assets/icon.ico](assets/icon.ico)（16/20/24/32/40/48/64/128/256 九个尺寸；≤48px 用 32 位 BMP 条目，大尺寸用 PNG 条目）
- 导出脚本：[tools/make-icon.py](tools/make-icon.py)（Python + Pillow，超采样 4× 后 LANCZOS 缩放；小尺寸自动去掉四角星避免糊成一团）
- 想换配色：改脚本顶部的 `A`（左上紫）/ `B`（右下蓝），跑 `python tools/make-icon.py` 重新生成全部 PNG + ICO，再 `build.cmd` 即可

---

## 四、设置项（双击圆球 / 右键菜单「设置…」）

### AI 后端与请求参数
| 项 | 默认 | 说明 |
| --- | --- | --- |
| 接口地址 | `http://127.0.0.1:3425/v1` | 任何 OpenAI 兼容的 `/chat/completions` 端点 |
| API Key | 空 | 本机网关免鉴权；用云端服务时填 |
| 模型 | `auto/deepseek-v4.1-flash` | 点「拉取模型列表」可换成网关里的任意模型 |
| 温度 | 0.20 | 越低越稳定，润色建议 0~0.3 |
| 最大 tokens | 2000 | 太小时推理模型可能返回空内容（程序会自动加倍重试一次） |
| 超时 | 30000 ms | 长文本 + 推理模型需要留足时间 |
| 字数上限 | 2000 | 超过就直接拒绝，避免把整篇网页正文拿去润色 |
| 提示毫秒 | 2500 | 屏幕提示停留时间 |
| 剪贴板兜底 | 开 | UIA 读不到时用 Ctrl+A/Ctrl+C 兜底 |

「测试连接」验证地址与模型列表；「测试润色」用固定短句跑一次完整链路。

### 触发方式
连击空格开关 / 连击次数 / 间隔毫秒 / 润色热键 / 撤销热键 / 开机自启 / 显示桌面悬浮框。

### 排除进程
逗号分隔的进程名，默认排除终端与密码管理器：
```
WindowsTerminal,conhost,KeePass,KeePassXC,1Password,Bitwarden
```
在这些程序里三连空格不触发，避免干扰 shell 与误改密码框。

### 系统提示词
决定「怎么改」。默认六条硬规则：不改原意 / 保持原语言与专有名词 / 结构优先（分点、分段、加标点）/ 只输出正文 / 已清晰则最小改动 / 语种一致。想让它更保守或更激进，直接改这里。

---

## 五、兼容性与已知限制

**已验证可用**
- WinForms / Win32 `Edit` 控件（记事本、旧式对话框）
- Chromium 系浏览器网页输入框（Chrome / Edge / Tabbit / Electron 应用，走 UIA ValuePattern）
- 支持 UIA TextPattern 的富文本控件
- 拿不到 UIA 时的剪贴板兜底路径（Ctrl+A / Ctrl+C / Ctrl+V）

**会明确拒绝**
- 只读区域（网页正文、地址栏）→ 提示「当前焦点是只读区域…请点进可编辑的输入框再试」，不调用模型
- 密码框 → 直接拒绝
- 排除名单里的进程 → 不触发
- 内容超过字数上限 → 提示提高上限或只润色选中片段
- 目标输入框已失去焦点 → **绝不往别的窗口打字**，直接取消

**注意**
- AI 生成需要联网（或本机网关在线）；断网时提示失败并保留原文。
- 处理中是串行的：上一次没结束，新的触发会被忽略。
- 写回失败时结果会保存到 `%APPDATA%\InputPolish\last-result.txt`，可手工粘贴。

---

## 六、维护

| 事项 | 做法 |
| --- | --- |
| 看日志 | 托盘菜单「打开日志」，或 `%APPDATA%\InputPolish\log.txt`（1MB 自动轮转） |
| 改配置 | 托盘菜单「打开配置目录」→ `config.json`（GUI 设置更稳） |
| 自检 | `InputPolish.exe --selftest`，报告写到 `%APPDATA%\InputPolish\selftest.txt` |
| 关开机自启 | 设置里取消「开机自启」，或托盘菜单同名项；注册表值 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `InputPolish` |
| 卸载 | 关掉「开机自启」→ 退出程序 → 删掉 exe 与 `%APPDATA%\InputPolish` |

命令行参数：`--selftest`（自检）、`--polish`（立刻润色）、`--undo`（撤销）、`--settings`（打开设置）。重复启动时参数会转发给已运行的实例，不会开第二个。

---

## 七、从源码重新构建

需要 .NET Framework 4.8 自带的 `csc.exe`（Windows 10/11 自带，无需装 SDK）：

```
cd input-polish
build.cmd
```

产物 `dist\InputPolish.exe`（图标已通过 `/win32icon` 嵌入，并同时作为嵌入资源供托盘使用）。

### 目录结构

```
input-polish/
├── src/                     19 个 .cs —— 全部逻辑，C# 5 语法
│   ├── Program.cs           入口 / 命令行 / 单实例转发
│   ├── TrayApp.cs           托盘、主窗口、IPC 服务端
│   ├── FloatWidget.cs       桌面置顶悬浮球（自绘 + 动画）
│   ├── Engine.cs            触发 → 读 → 润色 → 写 → 撤销 的流水线
│   ├── UiaIo.cs             UIA / Win32 / 剪贴板三层读写与校验
│   ├── Native.cs            所有 P/Invoke
│   ├── KeyHook.cs           全局键盘钩子（三连空格判定）
│   ├── HotkeyWindow.cs      RegisterHotKey 消息窗口
│   ├── SettingsForm.cs      设置窗口
│   ├── Polisher.cs          OpenAI 兼容调用 + 输出清洗
│   └── ...
├── assets/
│   ├── icon.ico             多尺寸图标（构建时嵌入）
│   ├── icon-*.png           各尺寸 PNG
│   └── screenshots/         README 用截图
├── tools/make-icon.py       图标生成脚本
├── tests/                   自动化验证脚手架（.cs 源码 + 本地编译的 .exe）
├── build.cmd                csc 构建脚本
├── app.manifest             高 DPI + asInvoker 清单
└── README.md
```

源码在 `src\`（**C# 5 语法**，因为 csc 4.8 不支持插值字符串与 `?.`）。`tests\` 下是自动化验证工具（三连空格注入、浏览器场景、设置窗口点击、菜单点击、截图等）。

> ⚠️ `tests\run-test.cmd` 会设置 `INPUTPOLISH_ACCEPT_INJECTED=1` 后启动程序，让键盘钩子接受 SendInput 注入的空格键——**仅供自动化测试**。日常使用请直接双击 `dist\InputPolish.exe`，否则模拟按键也会触发润色。

---

## 八、许可

[MIT](LICENSE)。自用、改、分发都随意。
