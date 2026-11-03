// InputPolish 网页内翻译 —— 内容脚本
// 目标：译文直接插在原网页里（双语对照），划词翻译用页面内浮层，不再弹外部窗口。
(() => {
  'use strict';
  if (window.__ipInPageLoaded) return;
  window.__ipInPageLoaded = true;

  const state = {
    settings: null,
    translating: false,
    translated: false,
    nodes: [],
    done: 0,
    total: 0,
    failed: 0,
    lastError: '',
    target: 'zh',
    observer: null,
    cache: new Map(),
    pendingSelection: null,
    bubbleTimer: 0,
    summaryLive: false,
    copyText: '',
    pageMode: 'bil',   // 'bil' = 双语对照（原文 + 译文），'dst' = 仅译文（原文隐藏）
  };

  // 调试开关：页面地址带 ?ipdebug=1 时在控制台打印翻译 / 观察器状态，平时完全静默
  const DBG = /[?&]ipdebug=1/.test(location.search);
  function dbg() {
    if (!DBG) return;
    try {
      const args = Array.prototype.slice.call(arguments).map((a) => {
        if (a && typeof a === 'object') {
          try { return JSON.stringify(a); } catch (e) { return String(a); }
        }
        return a;
      });
      console.debug.apply(console, ['[InputPolish]'].concat(args));
    } catch (e) { /* ignore */ }
  }

  // ---------------- 和后台 service worker 通信 ----------------
  function send(msg) {
    return new Promise((resolve) => {
      try {
        chrome.runtime.sendMessage(msg, (res) => {
          const err = chrome.runtime.lastError;
          if (err) {
            dbg('send error', msg && msg.type, err.message);
            resolve({ ok: false, error: '扩展后台没有响应：' + err.message });
            return;
          }
          dbg('send ok', msg && msg.type, res && res.ok);
          resolve(res || { ok: false, error: '扩展后台没有返回内容' });
        });
      } catch (e) {
        resolve({ ok: false, error: String(e && e.message ? e.message : e) });
      }
    });
  }

  async function ensureSettings() {
    if (state.settings) return state.settings;
    const r = await send({ type: 'settings' });
    state.settings = (r && r.settings) || { port: 8790, token: '', target: 'auto', showBall: true, fontSize: '0.95' };
    return state.settings;
  }

  // ---------------- 矢量图标（统一取 ip-icons.js，和桌面端同一套 path） ----------------
  // 以前悬浮球 / mini 按钮 / 芯片上用的是单个汉字当图标，小尺寸糊、换语言就失效；
  // 现在一律换成 24x24 线条 SVG，中文标签文字照旧保留。
  function ipIcon(name, size, strokeScale) {
    const px = size || 18;
    try {
      if (window.IPIcons && typeof window.IPIcons.make === 'function' && window.IPIcons.has(name)) {
        return window.IPIcons.make(name, { size: px, strokeScale: strokeScale || 1 });
      }
    } catch (e) { /* ignore */ }
    // 兜底：ip-icons.js 没加载时不能把按钮变空，留一个中性符号
    const fallback = document.createElement('span');
    fallback.textContent = '·';
    fallback.style.fontSize = px + 'px';
    fallback.style.lineHeight = '1';
    return fallback;
  }

  // 只留图标（圆形按钮 / 铅笔 / 加号）
  function setIcon(el, name, size, strokeScale) {
    try { el.textContent = ''; } catch (e) { /* ignore */ }
    el.appendChild(ipIcon(name, size, strokeScale));
    return el;
  }

  // 图标 + 中文短标签
  function setIconLabel(btn, name, label, size, strokeScale) {
    btn.textContent = '';
    const box = document.createElement('span');
    box.className = 'ip-ico';
    box.appendChild(ipIcon(name, size || 14, strokeScale || 1));
    btn.appendChild(box);
    if (label) {
      const t = document.createElement('span');
      t.textContent = label;
      btn.appendChild(t);
    }
    return btn;
  }

  // mini 按钮的忙碌态：spinner 转圈；恢复时换回 idleName 图标
  function setMiniBusy(btn, busy, idleName) {
    btn.classList.toggle('busy', !!busy);
    setIcon(btn, busy ? 'spinner' : idleName, 18, 1.05);
    const svg = btn.firstChild;
    if (busy && svg && svg.classList) svg.classList.add('ip-spin');
  }

  // ---------------- 页面内 UI（Shadow DOM，隔离页面样式） ----------------
  const host = document.createElement('div');
  host.id = '__ip_host';
  host.style.cssText = 'all: initial; position: fixed; left: 0; top: 0; width: 0; height: 0; z-index: 2147483600;';
  const shadow = host.attachShadow({ mode: 'open' });

  const styleEl = document.createElement('style');
  styleEl.textContent = [
    ':host { all: initial; }',
    '.ball { position: fixed; right: 16px; bottom: 150px; width: 46px; height: 46px; border-radius: 23px;',
    '  border: 0; padding: 0; cursor: pointer; color: #fff; font: 600 17px/1 "Microsoft YaHei", system-ui, sans-serif;',
    '  background: linear-gradient(135deg, #5b6dff 0%, #a855f7 100%); box-shadow: 0 6px 18px rgba(70,80,180,.35);',
    '  transition: transform .12s ease, box-shadow .12s ease; user-select: none; }',
    '.ball:hover { transform: scale(1.06); box-shadow: 0 8px 22px rgba(70,80,180,.45); }',
    '.ball.busy { background: linear-gradient(135deg, #f59e0b 0%, #ef4444 100%); }',
    '.ball.done { background: linear-gradient(135deg, #10b981 0%, #059669 100%); }',
    '.ball-txt { pointer-events: none; font: 600 11px/1 "Microsoft YaHei", system-ui, sans-serif; }',
    '.ball { display: flex; align-items: center; justify-content: center; gap: 1px; }',
    '.ball-ico { display: inline-flex; align-items: center; justify-content: center; }',
    '.ip-ico { display: inline-flex; align-items: center; justify-content: center; }',
    '.ip-spin { animation: ip-spin 1s linear infinite; }',
    '@keyframes ip-spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }',
    '.toast { position: fixed; left: 50%; bottom: 90px; transform: translateX(-50%); max-width: 78vw;',
    '  background: rgba(23, 26, 38, .94); color: #f2f4f8; font: 13px/1.5 "Microsoft YaHei", system-ui, sans-serif;',
    '  padding: 9px 14px; border-radius: 10px; box-shadow: 0 8px 26px rgba(0,0,0,.28); opacity: 0;',
    '  transition: opacity .16s ease; pointer-events: none; white-space: pre-wrap; text-align: center; }',
    '.toast.show { opacity: 1; }',
    // 划词浮标：左边「译」直接翻译，右边「存」直接存成笔记，两个动作互不影响。
    '.bubble { position: fixed; display: none; align-items: stretch; height: 30px; padding: 0; overflow: hidden;',
    '  border-radius: 15px; color: #fff; font: 600 13px/1 "Microsoft YaHei", system-ui, sans-serif;',
    '  background: linear-gradient(135deg, #5b6dff 0%, #a855f7 100%); box-shadow: 0 4px 14px rgba(70,80,180,.4);',
    '  user-select: none; white-space: nowrap; }',
    '.bubble.show { display: inline-flex; }',
    '.bub-seg { display: inline-flex; align-items: center; gap: 5px; padding: 0 10px; cursor: pointer; }',
    '.bub-seg:hover { background: rgba(255,255,255,.18); }',
    '.bub-seg + .bub-seg { border-left: 1px solid rgba(255,255,255,.32); }',
    '.bub-seg .ico { font-size: 13px; display: inline-flex; align-items: center; }',
    '.card { position: fixed; display: none; width: 380px; max-width: 92vw; background: #ffffff; color: #1b1f24;',
    '  border-radius: 12px; box-shadow: 0 14px 40px rgba(15, 20, 30, .28); overflow: hidden;',
    '  font: 14px/1.65 "Microsoft YaHei", system-ui, sans-serif; }',
    '.card.show { display: block; }',
    '.card-head { display: flex; align-items: center; gap: 8px; padding: 8px 10px; background: #f4f6fb;',
    '  border-bottom: 1px solid #e6e9f2; cursor: default; }',
    '.card-title { font-weight: 600; font-size: 13px; color: #4b5563; flex: 1; }',
    '.card-btn { border: 0; background: transparent; color: #6b7280; cursor: pointer; font: 12px/1 "Microsoft YaHei", sans-serif;',
    '  padding: 4px 6px; border-radius: 6px; }',
    '.card-btn { display: inline-flex; align-items: center; gap: 4px; }',
    '.card-btn:hover { background: #e8ebf5; color: #374151; }',
    '.card-body { padding: 11px 13px 13px 13px; max-height: 56vh; overflow: auto; }',
    '.card-tr { font-size: 15px; line-height: 1.7; white-space: pre-wrap; word-break: break-word; }',
    '.card-ex { margin-top: 9px; padding-top: 9px; border-top: 1px dashed #e0e4ee; color: #4b5563; font-size: 13px;',
    '  white-space: pre-wrap; word-break: break-word; }',
    '.card-ex-t { font-weight: 600; color: #6b7280; margin-bottom: 3px; }',
    '.card.wide { width: 470px; }',
    '.card-rich { font-size: 14px; line-height: 1.72; color: #1b1f24; }',
    '.card-rich p { margin: 0 0 9px 0; }',
    '.card-rich p:last-child { margin-bottom: 0; }',
    '.card-rich h4 { margin: 12px 0 6px 0; font-size: 14px; font-weight: 700; color: #111827; }',
    '.card-rich h4:first-child { margin-top: 0; }',
    '.card-rich ul, .card-rich ol { margin: 0 0 9px 0; padding-left: 20px; }',
    '.card-rich li { margin: 2px 0; }',
    '.card-rich strong { font-weight: 700; color: #111827; }',
    '.card-rich code { font-family: Consolas, monospace; font-size: 12.5px; background: #f1f3f8;',
    '  padding: 1px 4px; border-radius: 4px; }',
    '.card-loading { color: #6b7280; font-size: 13px; }',
    '.card-err { color: #b42318; font-size: 13px; }',
    '.card-tools { display: none; padding: 0 13px 12px 13px; }',
    '.card-tools.show { display: block; }',
    // 芯片：单行横排 + 横向滚动，绝不换行；每条右侧挂一支铅笔，末尾钉一个加号。
    '.chips-row { display: flex; align-items: center; gap: 6px; }',
    '.chips { flex: 1 1 auto; min-width: 0; display: flex; flex-wrap: nowrap; align-items: center; gap: 6px;',
    // 只声明 overflow 本身：一旦写 scrollbar-width / scrollbar-color，Chrome 会切到「标准滚动条」
    // 并忽略下面的 ::-webkit-scrollbar 系列（标准滚动条会画 ◀ ▶ 箭头按钮，很难看）。
    '  padding: 2px 0 4px 0; overflow-x: auto; overflow-y: hidden; }',
    '.chips::-webkit-scrollbar { height: 5px; width: 5px; }',
    '.chips::-webkit-scrollbar-thumb { background: #ccd4e8; border-radius: 3px; }',
    '.chips::-webkit-scrollbar-track { background: transparent; }',
    '.chips::-webkit-scrollbar-button { display: none; width: 0; height: 0; }',
    '.chipw { flex: 0 0 auto; display: inline-flex; align-items: center; border: 1px solid #dce2f0;',
    '  border-radius: 999px; background: #fff; color: #48506a; overflow: hidden; }',
    '.chipw:hover { border-color: #b9c4e8; background: #f7f9ff; color: #2b3a67; }',
    '.chipw.on { background: linear-gradient(135deg, #5b6dff 0%, #a855f7 100%); border-color: transparent; color: #fff; }',
    '.chipw.busy { opacity: .55; }',
    '.chip-lb { padding: 4px 4px 4px 10px; max-width: 108px; overflow: hidden; text-overflow: ellipsis;',
    '  white-space: nowrap; cursor: pointer; font: 12px/1.2 "Microsoft YaHei", system-ui, sans-serif; }',
    '.chip-pen { padding: 4px 8px 4px 2px; border: 0; background: transparent; cursor: pointer;',
    '  color: #939db4; font: 11px/1.2 "Microsoft YaHei", system-ui, sans-serif;',
    '  display: inline-flex; align-items: center; justify-content: center; }',
    '.chip-pen:hover { color: #5b6dff; }',
    '.chipw.on .chip-pen { color: rgba(255,255,255,.85); }',
    '.chip-plus { flex: 0 0 auto; width: 24px; height: 24px; border-radius: 999px; border: 1px dashed #c3cbe4;',
    '  background: #fff; color: #7c88a8; cursor: pointer; font: 14px/1 "Microsoft YaHei", system-ui, sans-serif;',
    '  display: inline-flex; align-items: center; justify-content: center; padding: 0; }',
    '.chip-plus:hover { border-color: #5b6dff; color: #5b6dff; background: #f4f6ff; }',
    '.pedit { display: none; position: fixed; z-index: 3; width: 470px; box-sizing: border-box;',
    '  background: #fff; border-radius: 14px; border: 1px solid #e2e7f4; padding: 16px 18px 14px 18px;',
    '  box-shadow: 0 18px 48px rgba(18,24,45,.22); font: 13px/1.5 "Microsoft YaHei", system-ui, sans-serif; color: #1b1f24; }',
    '.pedit.show { display: block; }',
    '.pedit-h { display: flex; align-items: baseline; justify-content: space-between; margin-bottom: 10px; }',
    '.pedit-h b { font-size: 14px; }',
    '.pedit-h span { color: #9aa3b5; font-size: 11.5px; }',
    '.pedit label { display: block; color: #7c88a8; font-size: 11.5px; margin: 8px 0 4px 0; }',
    '.pedit input[type=text], .pedit textarea, .pedit input[type=number] { width: 100%; box-sizing: border-box;',
    '  border: 1px solid #dce2f0; border-radius: 8px; padding: 7px 10px; outline: none; background: #fff;',
    '  font: 13px/1.45 "Microsoft YaHei", system-ui, sans-serif; color: #1b1f24; }',
    '.pedit textarea { height: 168px; resize: vertical; font-family: Consolas, "Microsoft YaHei", monospace; font-size: 12.5px; }',
    '.pedit input:focus, .pedit textarea:focus { border-color: #8b9bff; box-shadow: 0 0 0 3px rgba(91,109,255,.12); }',
    '.pedit input[type=number] { width: 96px; }',
    '.pedit-foot { display: flex; gap: 8px; margin-top: 12px; align-items: center; }',
    '.pedit-st { flex: 1; color: #9aa3b5; font-size: 11.5px; }',
    '.pedit-btn { padding: 7px 14px; border-radius: 8px; border: 1px solid #dce2f0; background: #fff;',
    '  color: #48506a; cursor: pointer; font: 13px/1 "Microsoft YaHei", system-ui, sans-serif; }',
    '.pedit-btn:hover { background: #f7f9ff; }',
    '.pedit-btn.pri { border: 0; color: #fff; background: linear-gradient(135deg, #5b6dff 0%, #a855f7 100%); }',
    '.pedit-btn.pri:hover { filter: brightness(1.06); }',
    '.pedit-btn.danger { border-color: #f0c6c6; background: #fff5f5; color: #d14343; font-weight: 600; }',
    '.pedit-btn.danger:hover { background: #ffecec; }',
    '.ask-row { display: flex; gap: 6px; margin-top: 9px; }',
    '.ask-in { flex: 1; min-width: 0; padding: 7px 10px; border-radius: 8px; border: 1px solid #dce2f0;',
    '  font: 13px/1.35 "Microsoft YaHei", system-ui, sans-serif; color: #1b1f24; outline: none; background: #fff; }',
    '.ask-in:focus { border-color: #8b9bff; box-shadow: 0 0 0 3px rgba(91,109,255,.12); }',
    '.ask-btn { display: inline-flex; align-items: center; gap: 5px; padding: 0 14px; border-radius: 8px; border: 0; cursor: pointer; color: #fff;',
    '  font: 600 13px/1 "Microsoft YaHei", system-ui, sans-serif;',
    '  background: linear-gradient(135deg, #5b6dff 0%, #a855f7 100%); }',
    '.ask-btn:hover { filter: brightness(1.06); }',
    '.card-stat { margin-top: 8px; color: #9aa3b5; font: 11.5px/1.4 "Microsoft YaHei", system-ui, sans-serif; }',
    '.card-q { font: 600 13.5px/1.55 "Microsoft YaHei", system-ui, sans-serif; color: #4b5563;',
    '  padding: 7px 10px; margin: 0 0 10px 0; background: #f4f6fb; border-radius: 8px;',
    '  border-left: 3px solid #5b6dff; white-space: pre-wrap; word-break: break-word; }',
    // 右下角小工具条：悬浮球旁边挂「纲 / 导」两个小按钮（大纲侧栏、导出 Markdown）。
    '.dock { position: fixed; right: 22px; bottom: 208px; display: none; flex-direction: column; gap: 8px; }',
    '.dock.show { display: flex; }',
    '.mini { width: 34px; height: 34px; border-radius: 17px; border: 0; padding: 0; cursor: pointer; color: #fff;',
    '  font: 600 13px/1 "Microsoft YaHei", system-ui, sans-serif; background: rgba(91,109,255,.92);',
    '  box-shadow: 0 4px 12px rgba(70,80,180,.30); opacity: .85;',
    '  display: inline-flex; align-items: center; justify-content: center;',
    '  transition: opacity .12s ease, transform .12s ease; }',
    '.mini:hover { opacity: 1; transform: scale(1.08); }',
    '.mini.on { background: linear-gradient(135deg, #10b981 0%, #059669 100%); opacity: 1; }',
    '.mini.busy { background: linear-gradient(135deg, #f59e0b 0%, #ef4444 100%); opacity: 1; }',
    // 页面大纲侧栏：贴在网页左边，列标题层级，点一下就滚过去。
    '.outline { position: fixed; left: 0; top: 0; bottom: 0; width: 272px; display: none; flex-direction: column;',
    '  background: rgba(255,255,255,.97); -webkit-backdrop-filter: blur(20px) saturate(1.3); backdrop-filter: blur(20px) saturate(1.3);',
    '  border-right: 1px solid #e3e8f4;',
    '  box-shadow: 4px 0 26px rgba(18,24,45,.14); color: #1b1f24;',
    '  font: 13px/1.5 "Microsoft YaHei", system-ui, sans-serif; }',
    '.outline.show { display: flex; }',
    '.ol-head { display: flex; align-items: center; gap: 8px; padding: 12px 10px 9px 14px;',
    '  border-bottom: 1px solid #eef1f8; }',
    '.ol-head b { font-size: 13.5px; color: #2b3a67; }',
    '.ol-count { flex: 1; color: #9aa3b5; font-size: 11.5px; }',
    '.ol-x { border: 0; background: transparent; color: #8b95ad; cursor: pointer; font: 12px/1 "Microsoft YaHei", sans-serif;',
    '  padding: 4px 6px; border-radius: 6px; display: inline-flex; align-items: center; gap: 4px; }',
    '.ol-x:hover { background: #eef1f8; color: #374151; }',
    '.ol-search { position: relative; margin: 9px 12px 6px 12px; }',
    '.ol-search .ol-ico { position: absolute; left: 8px; top: 50%; margin-top: -7px; color: #9aa3b5;',
    '  pointer-events: none; display: flex; }',
    '.ol-filter { margin: 0; padding: 6px 9px 6px 30px; border: 1px solid #dce2f0; border-radius: 7px;',
    '  width: 100%; box-sizing: border-box;',
    '  outline: none; background: #fff; color: #1b1f24;',
    '  font: 12.5px/1.3 "Microsoft YaHei", system-ui, sans-serif; }',
    '.ol-filter:focus { border-color: #8b9bff; box-shadow: 0 0 0 3px rgba(91,109,255,.10); }',
    '.ol-list { flex: 1; overflow-y: auto; padding: 4px 8px 18px 8px; }',
    '.ol-list::-webkit-scrollbar { width: 6px; }',
    '.ol-list::-webkit-scrollbar-thumb { background: #d3dbee; border-radius: 3px; }',
    '.ol-item { display: block; width: 100%; box-sizing: border-box; text-align: left; border: 0;',
    '  background: transparent; cursor: pointer; padding: 5px 8px; border-radius: 6px; color: #3b4358;',
    '  font: 12.5px/1.45 "Microsoft YaHei", system-ui, sans-serif; white-space: nowrap; overflow: hidden;',
    '  text-overflow: ellipsis; border-left: 2px solid transparent; }',
    '.ol-item:hover { background: #f2f6ff; color: #2b3a67; }',
    '.ol-item.on { background: #edf1ff; color: #3341b5; border-left-color: #5b6dff; font-weight: 600; }',
    '.ol-item .lv { color: #aab3c8; font-size: 10.5px; margin-right: 5px; font-weight: 400; }',
    '.ol-item.on .lv { color: #7d8ae0; }',
    '.ol-empty { padding: 16px 10px; color: #9aa3b5; font-size: 12px; line-height: 1.6; }',
  ].join('\n');
  shadow.appendChild(styleEl);

  // 页面级样式（不进 Shadow DOM）：dst（仅译文）模式下隐藏原文，让译文占原位。
  // 规则只在 <html data-ip-mode="dst"> 时生效，其他时候完全不动页面。
  // 优先用 constructable stylesheet（不受页面 CSP 限制），不支持时退回 <style>。
  const MODE_CSS = [
    'html[data-ip-mode="dst"] [data-ip-src="1"] { display: none !important; }',
    'html[data-ip-mode="dst"] [data-ip-orig="1"] { display: none !important; }',
    'html[data-ip-mode="dst"] [data-ip-tr="1"] {',
    '  display: block !important; margin: 0 !important; padding: 0 !important;',
    '  border-left: 0 !important; background: transparent !important; }',
  ].join('\n');
  let modeCssInstalled = false;

  function installModeCss() {
    if (modeCssInstalled) return;
    try {
      if (typeof CSSStyleSheet === 'function' && 'adoptedStyleSheets' in document) {
        const sheet = new CSSStyleSheet();
        sheet.replaceSync(MODE_CSS);
        document.adoptedStyleSheets = document.adoptedStyleSheets.concat([sheet]);
        modeCssInstalled = true;
        return;
      }
    } catch (e) { /* 退回 <style> */ }
    try {
      const st = document.createElement('style');
      st.id = '__ip_page_css';
      st.textContent = MODE_CSS;
      (document.head || document.documentElement).appendChild(st);
      modeCssInstalled = true;
    } catch (e) { /* ignore */ }
  }

  const ball = document.createElement('button');
  ball.className = 'ball';
  ball.title = '输入框润色 · 网页内翻译（Alt+A 翻译/还原整页）';
  const ballIco = document.createElement('span');
  ballIco.className = 'ball-ico';
  const ballTxt = document.createElement('span');
  ballTxt.className = 'ball-txt';
  ballTxt.style.display = 'none';
  ball.appendChild(ballIco);
  ball.appendChild(ballTxt);
  setIcon(ballIco, 'translate', 24, 1.15);
  ball.style.display = 'none';
  shadow.appendChild(ball);

  const toast = document.createElement('div');
  toast.className = 'toast';
  shadow.appendChild(toast);

  const bubble = document.createElement('div');
  bubble.className = 'bubble';
  function bubSeg(ico, label, act, title) {
    const seg = document.createElement('span');
    seg.className = 'bub-seg';
    seg.setAttribute('data-act', act);
    seg.title = title || label;
    const i = document.createElement('span');
    i.className = 'ico';
    i.appendChild(ipIcon(ico, 14, 1.15));
    const t = document.createElement('span');
    t.textContent = label;
    seg.appendChild(i);
    seg.appendChild(t);
    bubble.appendChild(seg);
    return seg;
  }
  bubSeg('translate', '划词翻译', 'translate', '翻译选中的文字（Alt+S）');
  bubSeg('note', '存笔记', 'note', '把选中的文字存成本地 Markdown 笔记');
  shadow.appendChild(bubble);

  // 悬浮球上方的小工具条：大纲 / 导出 Markdown / 显示方式（双语对照 ↔ 仅译文）
  const dock = document.createElement('div');
  dock.className = 'dock';
  const btnOutline = document.createElement('button');
  btnOutline.type = 'button';
  btnOutline.className = 'mini';
  setIcon(btnOutline, 'outline', 18, 1.05);
  btnOutline.title = '页面大纲（Alt+O）';
  btnOutline.setAttribute('aria-label', '页面大纲');
  const btnExport = document.createElement('button');
  btnExport.type = 'button';
  btnExport.className = 'mini';
  setIcon(btnExport, 'export', 18, 1.05);
  btnExport.title = '导出本页 Markdown（Alt+M）';
  btnExport.setAttribute('aria-label', '导出 Markdown');
  const btnMode = document.createElement('button');
  btnMode.type = 'button';
  btnMode.className = 'mini';
  setIcon(btnMode, 'columns', 18, 1.05);
  btnMode.title = '显示方式：双语对照（Alt+D 切换）';
  btnMode.setAttribute('aria-label', '切换显示方式：双语对照 / 仅译文');
  dock.appendChild(btnOutline);
  dock.appendChild(btnExport);
  dock.appendChild(btnMode);
  shadow.appendChild(dock);

  // 页面大纲侧栏（长文跳转）
  const outline = document.createElement('div');
  outline.className = 'outline';
  const olHead = document.createElement('div');
  olHead.className = 'ol-head';
  const olTitle = document.createElement('b');
  olTitle.textContent = '页面大纲';
  const olCount = document.createElement('span');
  olCount.className = 'ol-count';
  const olClose = document.createElement('button');
  olClose.type = 'button';
  olClose.className = 'ol-x';
  setIconLabel(olClose, 'close', '关闭', 13, 1.1);
  olClose.title = '关闭大纲（Esc）';
  olHead.appendChild(olTitle);
  olHead.appendChild(olCount);
  olHead.appendChild(olClose);
  const olFilter = document.createElement('input');
  olFilter.className = 'ol-filter';
  olFilter.type = 'text';
  olFilter.setAttribute('spellcheck', 'false');
  olFilter.setAttribute('placeholder', '筛选标题…');
  // 筛选框左侧挂放大镜，和桌面快捷面板一套手感
  const olSearch = document.createElement('div');
  olSearch.className = 'ol-search';
  const olSearchIco = document.createElement('span');
  olSearchIco.className = 'ol-ico';
  olSearchIco.appendChild(ipIcon('search', 13, 1.1));
  olSearch.appendChild(olSearchIco);
  olSearch.appendChild(olFilter);
  const olList = document.createElement('div');
  olList.className = 'ol-list';
  outline.appendChild(olHead);
  outline.appendChild(olSearch);
  outline.appendChild(olList);
  shadow.appendChild(outline);

  const card = document.createElement('div');
  card.className = 'card';
  const cardHead = document.createElement('div');
  cardHead.className = 'card-head';
  const cardTitle = document.createElement('span');
  cardTitle.className = 'card-title';
  cardTitle.textContent = '划词翻译';
  const cardCopy = document.createElement('button');
  cardCopy.className = 'card-btn';
  setIconLabel(cardCopy, 'copy', '复制', 13, 1.1);
  cardCopy.title = '复制译文 / 总结 / Markdown';
  const cardClose = document.createElement('button');
  cardClose.className = 'card-btn';
  setIconLabel(cardClose, 'close', '关闭', 13, 1.1);
  cardClose.title = '关闭卡片（Esc）';
  cardHead.appendChild(cardTitle);
  cardHead.appendChild(cardCopy);
  cardHead.appendChild(cardClose);
  const cardNote = document.createElement('button');
  cardNote.className = 'card-btn';
  setIconLabel(cardNote, 'note', '存笔记', 13, 1.1);
  cardNote.title = '把当前内容存成本地 Markdown 笔记';
  cardNote.style.display = 'none';
  const cardBody = document.createElement('div');
  cardBody.className = 'card-body';

  // 卡片底部工具区：总结方案 chips + 「问这一页」输入
  const cardTools = document.createElement('div');
  cardTools.className = 'card-tools';
  const cardChips = document.createElement('div');
  cardChips.className = 'chips';
  const askRow = document.createElement('div');
  askRow.className = 'ask-row';
  const askIn = document.createElement('input');
  askIn.className = 'ask-in';
  askIn.setAttribute('placeholder', '问这一页：比如「核心结论是什么？」');
  askIn.setAttribute('spellcheck', 'false');
  const askBtn = document.createElement('button');
  askBtn.className = 'ask-btn';
  setIconLabel(askBtn, 'chat', '问', 13, 1.1);
  askRow.appendChild(askIn);
  askRow.appendChild(askBtn);
  // 加号单独放在滚动容器外面：芯片条横向滚到底也不会把加号滚丢。
  const chipsRow = document.createElement('div');
  chipsRow.className = 'chips-row';
  const chipPlus = document.createElement('button');
  chipPlus.className = 'chip-plus';
  chipPlus.type = 'button';
  setIcon(chipPlus, 'plus', 14, 1.1);
  chipPlus.title = '新建一条提示词';
  chipsRow.appendChild(cardChips);
  chipsRow.appendChild(chipPlus);
  cardTools.appendChild(chipsRow);
  cardTools.appendChild(askRow);

  cardHead.appendChild(cardNote);
  card.appendChild(cardHead);
  card.appendChild(cardBody);
  card.appendChild(cardTools);
  shadow.appendChild(card);

  // 提示词编辑框：卡片里点铅笔/加号时浮在上面，不另开窗口，也不会打断当前页面的翻译状态。
  const pedit = document.createElement('div');
  pedit.className = 'pedit';
  const peditHead = document.createElement('div');
  peditHead.className = 'pedit-h';
  const peditTitle = document.createElement('b');
  peditTitle.textContent = '编辑提示词';
  const peditBadge = document.createElement('span');
  peditHead.appendChild(peditTitle);
  peditHead.appendChild(peditBadge);
  pedit.appendChild(peditHead);
  function peditField(text, el) {
    const l = document.createElement('label');
    l.textContent = text;
    pedit.appendChild(l);
    pedit.appendChild(el);
    return el;
  }
  const peditName = document.createElement('input');
  peditName.type = 'text';
  peditName.setAttribute('spellcheck', 'false');
  peditField('名称（芯片上显示，太长会被省略）', peditName);
  const peditBody = document.createElement('textarea');
  peditBody.setAttribute('spellcheck', 'false');
  peditField('提示词（发给模型的系统指令，越具体越稳）', peditBody);
  const peditTok = document.createElement('input');
  peditTok.type = 'number';
  peditTok.min = '200';
  peditTok.max = '8000';
  peditTok.step = '100';
  peditField('最大 tokens', peditTok);
  const peditFoot = document.createElement('div');
  peditFoot.className = 'pedit-foot';
  const peditSt = document.createElement('div');
  peditSt.className = 'pedit-st';
  const peditSave = document.createElement('button');
  peditSave.className = 'pedit-btn pri';
  peditSave.textContent = '保存';
  const peditDel = document.createElement('button');
  peditDel.className = 'pedit-btn';
  peditDel.textContent = '删除';
  const peditReset = document.createElement('button');
  peditReset.className = 'pedit-btn';
  peditReset.textContent = '恢复默认';
  const peditCancel = document.createElement('button');
  peditCancel.className = 'pedit-btn';
  peditCancel.textContent = '取消';
  peditFoot.appendChild(peditSt);
  peditFoot.appendChild(peditDel);
  peditFoot.appendChild(peditReset);
  peditFoot.appendChild(peditCancel);
  peditFoot.appendChild(peditSave);
  pedit.appendChild(peditFoot);
  shadow.appendChild(pedit);

  function mountUi() {
    if (!document.documentElement.contains(host)) document.documentElement.appendChild(host);
    installModeCss();
  }

  let toastTimer = 0;
  function showToast(text, ms) {
    toast.textContent = text;
    toast.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => toast.classList.remove('show'), ms || 2600);
  }

  function setBall(mode, text) {
    ball.classList.remove('busy');
    ball.classList.remove('done');
    if (mode === 'busy') ball.classList.add('busy');
    if (mode === 'done') ball.classList.add('done');
    const name = mode === 'busy' ? 'spinner' : (mode === 'done' ? 'check' : 'translate');
    const size = mode === 'busy' ? 16 : 24;
    ballIco.textContent = '';
    const svg = ipIcon(name, size, 1.15);
    if (mode === 'busy' && svg.classList) svg.classList.add('ip-spin');
    ballIco.appendChild(svg);
    const label = mode === 'busy' ? String(text || '') : '';
    ballTxt.textContent = label;
    ballTxt.style.display = label ? '' : 'none';
  }

  // ---------------- 文本工具 ----------------
  const SKIP_TAGS = new Set([
    'SCRIPT', 'STYLE', 'NOSCRIPT', 'CODE', 'PRE', 'TEXTAREA', 'INPUT', 'SELECT', 'OPTION', 'OPTGROUP',
    'SVG', 'CANVAS', 'IFRAME', 'VIDEO', 'AUDIO', 'OBJECT', 'EMBED', 'KBD', 'SAMP', 'VAR', 'IMG', 'PICTURE',
    'MATH', 'TEMPLATE', 'LINK', 'META', 'HEAD', 'TITLE',
  ]);

  const BLOCK_TAGS = new Set([
    'DIV', 'P', 'LI', 'UL', 'OL', 'TABLE', 'TBODY', 'THEAD', 'TFOOT', 'TR', 'TD', 'TH', 'SECTION', 'ARTICLE',
    'HEADER', 'FOOTER', 'ASIDE', 'MAIN', 'NAV', 'BLOCKQUOTE', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'FIGURE',
    'FIGCAPTION', 'DL', 'DT', 'DD', 'FORM', 'FIELDSET', 'DETAILS', 'SUMMARY', 'ADDRESS', 'CENTER', 'BODY',
    'HTML', 'HR', 'CAPTION', 'COLGROUP', 'OPTION',
  ]);

  const INSIDE_TAGS = new Set(['TD', 'TH', 'LI', 'DT', 'DD', 'FIGCAPTION', 'CAPTION', 'SUMMARY']);

  function cjkRatio(text) {
    if (!text) return 0;
    let cjk = 0;
    let letters = 0;
    const n = Math.min(text.length, 600);
    for (let i = 0; i < n; i++) {
      const c = text.charCodeAt(i);
      if (c >= 0x4e00 && c <= 0x9fff) { cjk++; letters++; }
      else if ((c >= 0x41 && c <= 0x5a) || (c >= 0x61 && c <= 0x7a)) letters++;
    }
    if (!letters) return 0;
    return cjk / letters;
  }

  function hasMeaningfulText(text) {
    if (!text) return false;
    if (text.length < 2) return false;
    return /[A-Za-z\u00c0-\u024f\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af]/.test(text);
  }

  // allowHidden=true 时跳过「自己不可见」的判断：dst（仅译文）模式下原文被 CSS 隐藏，
  // 但总结 / 导出仍然需要读它的文字。
  function isHidden(el, allowHidden) {
    if (!el.isConnected) return true;
    const cs = getComputedStyle(el);
    if (!cs) return false;
    if (!allowHidden) {
      if (cs.display === 'none' || cs.visibility === 'hidden') return true;
      if (cs.opacity === '0') return true;
      const r = el.getBoundingClientRect();
      if (r.width < 2 || r.height < 2) return true;
    }
    return false;
  }

  function normText(el) {
    // INSIDE_TAGS（li/td/dt…）里原文被包进 span[data-ip-orig]：dst 模式下这个 span 是隐藏的，
    // el.innerText 会跳过它、把可见的译文 div 算进来。总结 / 问这一页要的永远是原文，
    // 所以有包装时直接取包装里的 textContent。
    let raw = '';
    try {
      const orig = el.querySelector(':scope > [data-ip-orig="1"]');
      if (orig) raw = orig.textContent || '';
    } catch (e) { /* ignore */ }
    if (!raw) raw = el.innerText || el.textContent || '';
    return raw.replace(/\s+/g, ' ').trim();
  }

  // ---------------- 收集可翻译段落 ----------------
  // includeTranslated=true 时把「已经翻译过」的源元素也算进来（总结要用原文，
  // 否则翻过一遍之后源元素都带上了 data-ip-src，collectUnits 会返回空）。
  function collectUnits(includeTranslated) {
    const units = [];
    const seen = new Set();
    const walk = (el) => {
      if (!(el instanceof HTMLElement)) return;
      if (SKIP_TAGS.has(el.tagName)) return;
      if (el.id === '__ip_host') return;
      if (el.isContentEditable) return;
      if (el.getAttribute('aria-hidden') === 'true') return;
      if (el.dataset && el.dataset.ipSrc && !includeTranslated) return;
      if (el.dataset && el.dataset.ipTr === '1') return;
      // 总结 / 导出要读原文：dst 模式下原文段落被 CSS 隐藏，这里放行（只在翻译过的块内部）
      const underTranslated = !!(includeTranslated && el.closest && el.closest('[data-ip-src]'));
      if (isHidden(el, underTranslated)) return;

      let hasBlockChild = false;
      for (let i = 0; i < el.children.length; i++) {
        const child = el.children[i];
        if (child instanceof HTMLElement && BLOCK_TAGS.has(child.tagName)) { hasBlockChild = true; break; }
      }
      if (hasBlockChild) {
        for (let i = 0; i < el.children.length; i++) walk(el.children[i]);
        return;
      }

      const text = normText(el);
      if (!hasMeaningfulText(text)) return;
      if (el === document.body || el === document.documentElement) return;   // 整页当一个段落会毁掉 dst 隐藏规则
      if (seen.has(el)) return;
      seen.add(el);
      units.push({ el: el, text: text });
    };
    walk(document.body);
    return units;
  }

  function shouldSkipByLanguage(text) {
    const ratio = cjkRatio(text);
    if (state.target === 'zh') {
      if (ratio > 0.5) return true;          // 已经是中文
    } else if (state.target === 'en') {
      if (ratio < 0.02) return true;         // 已经是英文
    }
    return false;
  }

  // ---------------- 插入译文 ----------------
  function styleForTranslation() {
    const size = (state.settings && state.settings.fontSize) || '0.95';
    return [
      'display: block',
      'margin: 5px 0 2px 0',
      'padding: 1px 0 1px 9px',
      'border-left: 2px solid rgba(96,125,255,.5)',
      'color: inherit',
      'font-size: ' + size + 'em',
      'line-height: 1.75',
      'font-family: inherit',
      'white-space: pre-wrap',
      'word-break: break-word',
      'background: transparent',
      'text-align: inherit',
    ].join(';');
  }

  // INSIDE_TAGS（td/li/dt…）不能整块隐藏，否则同一元素里的译文也一起消失：
  // 插入译文前先把原有子节点包进 span[data-ip-orig="1"]，dst 模式只隐藏这个 span。
  function wrapOriginalChildren(el) {
    try {
      if (el.querySelector(':scope > [data-ip-orig="1"]')) return;
      const span = document.createElement('span');
      span.setAttribute('data-ip-orig', '1');
      while (el.firstChild) span.appendChild(el.firstChild);
      el.appendChild(span);
    } catch (e) { /* ignore */ }
  }

  function insertTranslation(unit, translated) {
    const el = unit.el;
    if (!el.isConnected) return false;
    const inside = INSIDE_TAGS.has(el.tagName);
    if (inside) wrapOriginalChildren(el);
    const node = document.createElement('div');
    node.dataset.ipTr = '1';
    node.setAttribute('dir', 'auto');
    node.setAttribute('lang', state.target === 'en' ? 'en' : 'zh-CN');
    node.textContent = translated;
    node.style.cssText = styleForTranslation();
    try {
      if (inside) el.appendChild(node);
      else el.insertAdjacentElement('afterend', node);
    } catch (e) {
      return false;
    }
    // 普通叶子块用 '1'（dst 直接隐藏）；INSIDE_TAGS 用 'in'，隐藏的是里面的 span
    el.dataset.ipSrc = inside ? 'in' : '1';
    state.nodes.push(node);
    if (state.pageMode === 'dst') document.documentElement.setAttribute('data-ip-mode', 'dst');
    return true;
  }

  // ---------------- 整页翻译 ----------------
  function resolveTarget() {
    const wanted = (state.settings && state.settings.target) || 'auto';
    if (wanted === 'zh' || wanted === 'en') return wanted;
    const sample = (document.body && document.body.innerText) ? document.body.innerText.slice(0, 3000) : '';
    return cjkRatio(sample) > 0.15 ? 'en' : 'zh';
  }

  async function translateTexts(texts) {
    const r = await send({ type: 'translate', body: { texts: texts, target: state.target } });
    return r || { ok: false, error: '没有响应' };
  }

  async function runPageTranslate() {
    dbg('runPageTranslate enter', { translating: state.translating, nodes: state.nodes.length });
    if (state.translating) return;
    await ensureSettings();
    state.target = resolveTarget();
    const all = collectUnits();
    const units = [];
    for (const u of all) {
      if (shouldSkipByLanguage(u.text)) continue;
      if (u.text.length > 1200) continue;   // 超长块先跳过，避免一次吃满模型
      units.push(u);
    }
    if (!units.length) {
      showToast('这个页面没找到需要翻译的正文');
      return;
    }

    state.translating = true;
    state.done = 0;
    state.total = units.length;
    state.failed = 0;
    state.lastError = '';
    setBall('busy', '0/' + units.length);
    showToast('开始翻译 ' + units.length + ' 段…');

    const queue = units.slice();
    const BATCH = 6;
    const CONC = 3;

    const worker = async () => {
      while (queue.length) {
        const chunk = queue.splice(0, BATCH);
        const texts = chunk.map((u) => u.text);
        let res;
        try {
          res = await translateTexts(texts);
        } catch (e) {
          res = { ok: false, error: String(e && e.message ? e.message : e) };
        }
        if (res && res.ok && Array.isArray(res.results)) {
          for (let i = 0; i < chunk.length; i++) {
            const item = res.results[i];
            const out = item && item.text ? String(item.text).trim() : '';
            if (out && insertTranslation(chunk[i], out)) state.done++;
            else state.failed++;
          }
        } else {
          state.failed += chunk.length;
          state.lastError = (res && res.error) || '翻译失败';
        }
        setBall('busy', state.done + '/' + state.total);
      }
    };

    try {
      const workers = [];
      for (let i = 0; i < CONC; i++) workers.push(worker());
      await Promise.all(workers);
    } finally {
      state.translating = false;
    }

    state.translated = state.nodes.length > 0;
    applyPageMode();
    dbg('runPageTranslate done', { done: state.done, failed: state.failed, nodes: state.nodes.length });
    if (state.done > 0) {
      setBall('done', '原');
      ball.title = '已翻译 ' + state.done + ' 段 · 点一下还原原文';
      const tail = state.failed > 0 ? ('，' + state.failed + ' 段没翻成' + (state.lastError ? '：' + state.lastError : '')) : '';
      showToast('已翻译 ' + state.done + ' 段' + tail + '\n点悬浮球可还原为原文', 3600);
      startObserver();
      syncBallState();
    } else {
      setBall('', '译');
      showToast('翻译失败：' + (state.lastError || '模型没有返回内容'), 5000);
    }
  }

  function clearTranslations() {
    for (const n of state.nodes) {
      try { n.remove(); } catch (e) { /* ignore */ }
    }
    state.nodes = [];
    state.translated = false;
    state.done = 0;
    state.total = 0;
    state.failed = 0;
    // 先删译文节点，再拆 INSIDE_TAGS 的原文包装，最后清标记和模式属性 —— 一步都不能少
    const wrapped = document.querySelectorAll('[data-ip-orig="1"]');
    for (const span of wrapped) {
      const p = span.parentNode;
      if (!p) continue;
      while (span.firstChild) p.insertBefore(span.firstChild, span);
      p.removeChild(span);
    }
    const marked = document.querySelectorAll('[data-ip-src]');
    for (const el of marked) el.removeAttribute('data-ip-src');
    document.documentElement.removeAttribute('data-ip-mode');
    setBall('', '译');
    ball.title = '输入框润色 · 网页内翻译（Alt+A 翻译/还原整页）';
  }

  // ---------------- 显示方式：双语对照（bil）/ 仅译文（dst） ----------------
  // 两种模式都照常插入译文节点；区别只在 <html data-ip-mode="dst"> 时是否隐藏原文
  // （规则在页面级 pageCss 里，见文件开头）。默认 bil，老行为完全不变。
  function normMode(v) { return v === 'dst' ? 'dst' : 'bil'; }
  function isDstMode() { return state.pageMode === 'dst'; }
  function modeLabel(mode) { return mode === 'dst' ? '仅译文' : '双语对照'; }
  const PAGE_MODE_KEY = 'pageMode';

  function applyPageMode() {
    try {
      if (state.pageMode === 'dst' && state.nodes.length > 0) {
        document.documentElement.setAttribute('data-ip-mode', 'dst');
      } else {
        document.documentElement.removeAttribute('data-ip-mode');
      }
    } catch (e) { /* ignore */ }
  }

  function updateModeButton() {
    try {
      btnMode.classList.toggle('on', state.pageMode === 'dst');
      btnMode.title = '显示方式：' + modeLabel(state.pageMode) + '（Alt+D 切换）';
    } catch (e) { /* ignore */ }
  }

  async function setMode(mode, opts) {
    const next = normMode(mode);
    const changed = next !== state.pageMode;
    state.pageMode = next;
    applyPageMode();
    updateModeButton();
    if (!opts || opts.persist !== false) {
      try {
        const patch = {};
        patch[PAGE_MODE_KEY] = next;
        await chrome.storage.local.set(patch);
      } catch (e) { /* ignore */ }
    }
    if (changed && (!opts || opts.toast !== false)) {
      showToast(next === 'dst'
        ? '已切换到「仅译文」：原文隐藏，译文占原位（Alt+D 切回）'
        : '已切换到「双语对照」：原文 + 译文对照（Alt+D 切换）');
    }
    return next;
  }

  function toggleMode() {
    return setMode(state.pageMode === 'dst' ? 'bil' : 'dst');
  }

  // ---------------- 动态内容（SPA / 滚动加载） ----------------
  let observerTimer = 0;
  function startObserver() {
    if (state.observer) return;
    dbg('startObserver');
    state.observer = new MutationObserver(() => {
      dbg('observer fired', { translated: state.translated, translating: state.translating });
      if (!state.translated || state.translating) return;
      // 节流而不是防抖：页面上每 500ms 就有的小改动（计时器、广告、动画）不能把定时器
      // 一直往后推，否则 SPA 动态内容永远等不到翻译。
      if (observerTimer) return;
      observerTimer = setTimeout(() => {
        observerTimer = 0;
        translateNewNodes();
      }, 900);
    });
    state.observer.observe(document.body, { childList: true, subtree: true });
  }

  async function translateNewNodes() {
    dbg('translateNewNodes enter', { translating: state.translating, translated: state.translated });
    if (state.translating) return;
    const units = [];
    const all = collectUnits();
    for (const u of all) {
      if (shouldSkipByLanguage(u.text)) continue;
      if (u.text.length > 1200) continue;
      units.push(u);
    }
    dbg('translateNewNodes units', units.length);
    if (!units.length) return;
    state.translating = true;
    try {
      const chunk = units.slice(0, 12);
      const res = await translateTexts(chunk.map((u) => u.text));
      dbg('translateNewNodes res', { ok: res && res.ok, err: res && res.error, n: res && res.results && res.results.length });
      if (res && res.ok && Array.isArray(res.results)) {
        for (let i = 0; i < chunk.length; i++) {
          const out = res.results[i] && res.results[i].text ? String(res.results[i].text).trim() : '';
          if (out) insertTranslation(chunk[i], out);
        }
      }
    } catch (e) {
      /* ignore */
    } finally {
      state.translating = false;
      syncBallState();
    }
  }

  // 悬浮球状态跟真实 DOM 对齐（并发路径下也要正确显示「原 / 译」）
  function syncBallState() {
    const count = document.querySelectorAll('[data-ip-tr="1"]').length;
    if (count > 0) {
      state.translated = true;
      setBall('done', '原');
      ball.title = '已翻译 ' + count + ' 段 · 点一下还原原文';
    } else if (!state.translating) {
      state.translated = false;
      setBall('', '译');
      ball.title = '输入框润色 · 网页内翻译（Alt+A 翻译/还原整页）';
    }
  }

  function stopObserver() {
    if (state.observer) {
      state.observer.disconnect();
      state.observer = null;
    }
  }

  // ---------------- 划词浮层 ----------------
  function selectionInfo() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return null;
    const text = sel.toString().replace(/\s+/g, ' ').trim();
    if (!text) return null;
    const range = sel.getRangeAt(sel.rangeCount - 1);
    let rect = range.getBoundingClientRect();
    if (!rect || (rect.width === 0 && rect.height === 0)) {
      const rects = range.getClientRects();
      if (!rects || !rects.length) return null;
      rect = rects[rects.length - 1];
    }
    if (!rect || (rect.width === 0 && rect.height === 0)) return null;
    return { text: text, rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height } };
  }

  function clampX(x, w) {
    const min = 8;
    const max = Math.max(min, window.innerWidth - w - 8);
    return Math.min(Math.max(x, min), max);
  }

  function showBubble(info) {
    state.pendingSelection = info;
    bubble.classList.add('show');
    const w = Math.round(bubble.getBoundingClientRect().width) || 150;
    const left = clampX(info.rect.right - 10, w);
    let top = info.rect.bottom + 8;
    if (top + 34 > window.innerHeight) top = Math.max(8, info.rect.top - 38);
    bubble.style.left = left + 'px';
    bubble.style.top = top + 'px';
  }

  function hideBubble() {
    bubble.classList.remove('show');
  }

  function hideCard() {
    card.classList.remove('show');
  }

  function escapeHtml(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  function inlineMd(s) {
    let out = escapeHtml(s);
    out = out.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
    out = out.replace(/`([^`]+)`/g, '<code>$1</code>');
    return out;
  }

  // 把模型返回的轻量 Markdown 渲染成有样式的 DOM（只处理标题/列表/加粗/行内代码）
  function renderRichText(container, text) {
    const lines = String(text || '').replace(/\r\n/g, '\n').split('\n');
    const html = [];
    let list = null;   // 'ul' | 'ol'
    const closeList = () => { if (list) { html.push('</' + list + '>'); list = null; } };
    for (const raw of lines) {
      const line = raw.replace(/\s+$/, '');
      if (!line.trim()) { closeList(); continue; }
      const h = line.match(/^\s*#{1,6}\s+(.+)$/);
      if (h) { closeList(); html.push('<h4>' + inlineMd(h[1]) + '</h4>'); continue; }
      const ul = line.match(/^\s*[-*•]\s+(.+)$/);
      if (ul) {
        if (list !== 'ul') { closeList(); html.push('<ul>'); list = 'ul'; }
        html.push('<li>' + inlineMd(ul[1]) + '</li>');
        continue;
      }
      const ol = line.match(/^\s*(\d+)[.、)]\s+(.+)$/);
      if (ol) {
        if (list !== 'ol') { closeList(); html.push('<ol>'); list = 'ol'; }
        html.push('<li>' + inlineMd(ol[2]) + '</li>');
        continue;
      }
      closeList();
      html.push('<p>' + inlineMd(line.trim()) + '</p>');
    }
    closeList();
    container.innerHTML = html.join('');
  }

  function renderSelectionCard(info, res) {
    const anchorTop = parseFloat(bubble.style.top || '0') + 34;
    card.classList.remove('wide');
    cardTools.classList.remove('show');
    cardNote.style.display = 'none';
    state.summaryLive = false;
    state.copyText = '';
    const width = Math.min(380, window.innerWidth - 16);
    const left = clampX(parseFloat(bubble.style.left || '8'), width);
    card.style.left = left + 'px';
    card.style.width = width + 'px';
    card.style.top = '0px';
    card.classList.add('show');

    if (!res) {
      cardTitle.textContent = '划词翻译 · 翻译中…';
      cardBody.innerHTML = '<div class="card-loading">正在翻译 ' + escapeHtml(String(info.text.length)) + ' 个字符…</div>';
    } else if (!res.ok) {
      cardTitle.textContent = '划词翻译';
      cardBody.innerHTML = '<div class="card-err">' + escapeHtml(res.error || '翻译失败') + '</div>';
    } else {
      cardTitle.textContent = '划词翻译 → ' + (res.target || '');
      let html = '<div class="card-tr">' + escapeHtml(res.translation || '') + '</div>';
      if (res.explain && String(res.explain).trim()) {
        html += '<div class="card-ex"><div class="card-ex-t">讲解</div>' + escapeHtml(res.explain) + '</div>';
      }
      cardBody.innerHTML = html;
      // 划词翻译出结果后也允许「存笔记」：把原文 + 译文一起存下来
      lastResult = {
        kind: '划词翻译',
        text: '**原文**\n\n' + info.text + '\n\n**译文**\n\n' + (res.translation || '') +
          (res.explain && String(res.explain).trim() ? '\n\n**讲解**\n\n' + res.explain : '') +
          '\n\n> 原地址：' + location.href,
      };
      cardNote.style.display = '';
    }

    // 定位：优先放在浮标下方，放不下就放到选区上方
    const h = card.getBoundingClientRect().height || 160;
    let top = anchorTop;
    if (top + h > window.innerHeight - 8 && info.rect.top - h - 12 > 8) top = info.rect.top - h - 12;
    if (top < 8) top = 8;
    card.style.top = top + 'px';
    cardCopy.style.display = res && res.ok ? '' : 'none';
  }

  // ---------------- 整页总结 ----------------
  function pageTextForSummary(limit) {
    const units = collectUnits(true);
    const parts = [];
    let total = 0;
    for (const u of units) {
      const t = u.text;
      if (!t) continue;
      if (total + t.length > limit) break;
      parts.push(t);
      total += t.length;
    }
    const joined = parts.join('\n');
    dbg('summary source', { cjk: Math.round(cjkRatio(joined) * 100) / 100, len: joined.length, sample: joined.slice(0, 160) });
    return joined;
  }

  function placeCardCentered() {
    card.classList.add('wide');
    card.style.width = '';
    card.style.display = 'block';
    const w = card.getBoundingClientRect().width || 470;
    card.style.left = clampX((window.innerWidth - w) / 2, w) + 'px';
    card.style.top = Math.max(24, Math.round(window.innerHeight * 0.12)) + 'px';
    card.style.display = 'block';
    card.classList.add('show');
  }

  // ---------------- 整页总结 / 问答 ----------------
  // 总结方案由本机程序提供（GET /modes），这里只留一份兜底，保证连不上时 UI 不空。
  const MODE_FALLBACK = [
    { id: 'brief', label: '简明总结' },
    { id: 'bullets', label: '要点清单' },
    { id: 'outline', label: '结构大纲' },
    { id: 'tldr', label: '一句话' },
    { id: 'deep', label: '深度解读' },
    { id: 'data', label: '关键数据' },
    { id: 'action', label: '行动清单' },
    { id: 'questions', label: '追问清单' },
    { id: 'terms', label: '术语表' },
  ];
  let modeList = null;
  let activeMode = 'brief';
  let pageCache = { text: '', title: '', at: 0 };
  let lastResult = null;

  async function loadModes() {
    if (modeList && modeList.length) return modeList;
    const r = await send({ type: 'modes', body: {} });
    if (r && r.ok && Array.isArray(r.modes) && r.modes.length) modeList = r.modes;
    else modeList = MODE_FALLBACK.slice();
    return modeList;
  }

  function stopEvt(e) { e.preventDefault(); e.stopPropagation(); }

  function scrollChipIntoView() {
    const on = cardChips.querySelector('.chipw.on');
    if (!on) return;
    const left = on.offsetLeft;
    const right = left + on.offsetWidth;
    if (left < cardChips.scrollLeft) cardChips.scrollLeft = Math.max(0, left - 8);
    else if (right > cardChips.scrollLeft + cardChips.clientWidth) {
      cardChips.scrollLeft = right - cardChips.clientWidth + 8;
    }
  }

  // 芯片条：单行横排、可横向滚动，每条右侧一支铅笔，末尾一个加号（和桌面快捷面板一套手感）。
  function renderChips(active, busy) {
    cardChips.innerHTML = '';
    const list = modeList || MODE_FALLBACK;
    for (const m of list) {
      const w = document.createElement('span');
      w.className = 'chipw' + (m.id === active ? ' on' : '') + (busy ? ' busy' : '');
      const lb = document.createElement('span');
      lb.className = 'chip-lb';
      lb.textContent = m.label;
      lb.title = m.hint ? (m.label + '：' + m.hint) : m.label;
      lb.addEventListener('mousedown', stopEvt);
      lb.addEventListener('click', (e) => {
        stopEvt(e);
        if (busy) return;
        if (m.id === activeMode && cardBody.querySelector('.card-rich') && state.summaryLive) return;
        runPageSummary(m.id);
      });
      const pen = document.createElement('button');
      pen.className = 'chip-pen';
      pen.type = 'button';
      pen.setAttribute('aria-label', '编辑这条提示词');
      setIcon(pen, 'pen', 12, 1.1);
      pen.title = '编辑这条提示词';
      pen.addEventListener('mousedown', stopEvt);
      pen.addEventListener('click', (e) => { stopEvt(e); openPromptEditor(m.id); });
      w.appendChild(lb);
      w.appendChild(pen);
      cardChips.appendChild(w);
    }
    setTimeout(scrollChipIntoView, 0);
  }

  // ---------------- 提示词编辑框 ----------------
  let peditId = '';
  let peditDelArmedAt = 0;
  let peditDelTimer = 0;

  function peditDisarmDelete() {
    peditDelArmedAt = 0;
    if (peditDelTimer) { clearTimeout(peditDelTimer); peditDelTimer = 0; }
    if (peditDel) { peditDel.textContent = '删除'; peditDel.classList.remove('danger'); }
  }

  function peditClose() {
    pedit.classList.remove('show');
    peditId = '';
    peditDisarmDelete();
  }

  function peditPlace() {
    const w = pedit.offsetWidth || 470;
    pedit.style.left = Math.round(Math.max(8, (window.innerWidth - w) / 2)) + 'px';
    pedit.style.top = Math.max(24, Math.round(window.innerHeight * 0.14)) + 'px';
  }

  function applyModes(modes, selected) {
    if (Array.isArray(modes) && modes.length) modeList = modes;
    if (selected) activeMode = selected;
    renderChips(activeMode, false);
  }

  async function openPromptEditor(id) {
    peditId = id || '';
    peditDisarmDelete();
    peditTitle.textContent = peditId ? '编辑提示词' : '新建提示词';
    peditBadge.textContent = '';
    peditSt.textContent = '';
    peditName.value = '';
    peditBody.value = '';
    peditTok.value = '1800';
    peditDel.style.display = peditId ? '' : 'none';
    peditReset.style.display = 'none';
    peditPlace();
    pedit.classList.add('show');
    if (!peditId) {
      peditBadge.textContent = '新建 · 会存进你自己的提示词库';
      setTimeout(() => peditName.focus(), 30);
      return;
    }
    const r = await send({ type: 'promptOne', body: { id: peditId } });
    if (!r || !r.ok || !r.item) {
      peditSt.textContent = (r && r.error) || '没读到这条提示词';
      return;
    }
    peditName.value = r.item.label || '';
    peditBody.value = r.item.prompt || '';
    peditTok.value = String(r.item.maxTokens || 1800);
    peditBadge.textContent = r.item.builtin ? '内置提示词（可以改，也能恢复默认）' : '自定义提示词';
    peditReset.style.display = r.item.builtin ? '' : 'none';
    peditDel.style.display = r.item.builtin ? 'none' : '';
    setTimeout(() => peditName.focus(), 30);
  }

  async function peditDoSave() {
    const label = peditName.value.trim();
    const prompt = peditBody.value.trim();
    let tokens = parseInt(peditTok.value, 10);
    if (!tokens || isNaN(tokens)) tokens = 1800;
    if (tokens < 200) tokens = 200;
    if (tokens > 8000) tokens = 8000;
    if (prompt.length < 4) { peditSt.textContent = '提示词太短了，至少写一句话'; return; }
    peditSt.textContent = '正在保存…';
    const r = await send({ type: 'prompts', body: { action: 'save', id: peditId, label: label, prompt: prompt, maxTokens: tokens } });
    if (!r || !r.ok) { peditSt.textContent = (r && r.error) || '保存失败'; return; }
    const savedId = (r.item && r.item.id) || peditId;
    const wasNew = !peditId;
    peditClose();
    applyModes(r.modes, savedId);
    showToast(wasNew ? '已新增提示词' : '提示词已保存');
  }

  // 两段式删除：点一下变成「确认删除」，5 秒内再点一次才真删。
  // 不能用 window.confirm —— 模态框会把整个页面渲染线程卡住，卡片和浮球全都没响应。
  async function peditDoDelete() {
    if (!peditId) return;
    if (!peditDelArmedAt) {
      peditDelArmedAt = Date.now();
      peditDel.textContent = '确认删除';
      peditDel.classList.add('danger');
      peditSt.textContent = '再点一次「确认删除」就真删，5 秒后自动取消';
      if (peditDelTimer) clearTimeout(peditDelTimer);
      peditDelTimer = setTimeout(() => {
        if (peditDelArmedAt && Date.now() - peditDelArmedAt >= 4800) peditDisarmDelete();
      }, 5000);
      return;
    }
    const id = peditId;
    peditDisarmDelete();
    peditSt.textContent = '正在删除…';
    const r = await send({ type: 'prompts', body: { action: 'delete', id: id } });
    if (!r || !r.ok) { peditSt.textContent = (r && r.error) || '删除失败'; return; }
    peditClose();
    activeMode = 'brief';
    applyModes(r.modes, 'brief');
    showToast('已删除');
  }

  function showResponsePlaceholder() {
    showToast('先把指令写进面板的输入框');
  }

  // 自由指令：把整页当输入，按用户的话做（翻译、改写、提炼都行）
  async function runPageInstruction(instruction) {
    await ensureSettings();
    hideBubble();
    cardTitle.textContent = '执行指令';
    cardBody.innerHTML = '<div class="card-q"></div><div class="card-loading">正在按你的指令处理这一页…</div>';
    cardBody.querySelector('.card-q').textContent = instruction;
    cardCopy.style.display = 'none';
    cardNote.style.display = 'none';
    cardTools.classList.remove('show');
    card.classList.remove('wide');
    placeCardCentered();
    const text = freshPageText(14000);
    if (!text.trim()) {
      cardBody.innerHTML = '<div class="card-err">这个页面没读到可处理的文字</div>';
      return;
    }
    const res = await send({ type: 'run', body: { text: text, title: document.title, instruction: instruction } });
    if (!res || !res.ok) {
      cardBody.innerHTML = '<div class="card-err">' + escapeHtml((res && res.error) || '执行失败') + '</div>';
      placeCardCentered();
      return;
    }
    const rich = document.createElement('div');
    rich.className = 'card-rich';
    renderRichText(rich, res.answer || '');
    cardBody.innerHTML = '<div class="card-q"></div>';
    cardBody.querySelector('.card-q').textContent = instruction;
    cardBody.appendChild(rich);
    cardCopy.style.display = '';
    cardNote.style.display = '';
    lastResult = { kind: '页面指令', text: res.answer || '' };
    cardTitle.textContent = '执行指令 · ' + (res.ms ? (res.ms / 1000).toFixed(1) + 's' : '完成');
    placeCardCentered();
  }

  async function peditDoReset() {
    if (!peditId) return;
    const r = await send({ type: 'promptDefault', body: { id: peditId } });
    if (!r || !r.ok || !r.item) { peditSt.textContent = (r && r.error) || '没有默认内容'; return; }
    peditName.value = r.item.label || '';
    peditBody.value = r.item.prompt || '';
    peditTok.value = String(r.item.maxTokens || 1800);
    peditSt.textContent = '已填入出厂内容，点「保存」生效';
  }

  chipPlus.addEventListener('mousedown', stopEvt);
  chipPlus.addEventListener('click', (e) => { stopEvt(e); openPromptEditor(''); });
  peditSave.addEventListener('click', (e) => { stopEvt(e); peditDoSave(); });
  peditDel.addEventListener('click', (e) => { stopEvt(e); peditDoDelete(); });
  peditReset.addEventListener('click', (e) => { stopEvt(e); peditDoReset(); });
  peditCancel.addEventListener('click', (e) => { stopEvt(e); peditClose(); });
  pedit.addEventListener('mousedown', (e) => e.stopPropagation());
  peditBody.addEventListener('keydown', (e) => { if (e.key === 'Escape') peditClose(); });
  peditName.addEventListener('keydown', (e) => { if (e.key === 'Escape') peditClose(); });

  function placeCardCentered() {
    card.classList.add('wide');
    card.style.width = '';
    card.style.display = 'block';
    const w = card.getBoundingClientRect().width || 470;
    card.style.left = clampX((window.innerWidth - w) / 2, w) + 'px';
    card.style.top = Math.max(24, Math.round(window.innerHeight * 0.12)) + 'px';
    card.style.display = 'block';
    card.classList.add('show');
  }

  // 页面正文缓存 2 分钟：换方案 / 追问不用重复扫描 DOM
  function freshPageText(limit) {
    const now = Date.now();
    if (pageCache.text && now - pageCache.at < 120000) return pageCache.text;
    const text = pageTextForSummary(limit || 14000);
    pageCache = { text: text, title: document.title, at: now };
    return text;
  }

  function showPageTools() {
    cardTools.classList.add('show');
    cardNote.style.display = '';
    state.copyText = '';
  }

  async function runPageSummary(mode) {
    const useMode = mode || activeMode || 'brief';
    activeMode = useMode;
    state.copyText = '';
    await ensureSettings();
    hideBubble();
    const list = await loadModes();
    let label = '';
    for (const m of list) { if (m.id === useMode) label = m.label; }
    if (!label) label = '本页总结';
    cardTitle.textContent = '本页总结 · ' + label;
    cardBody.innerHTML = '<div class="card-loading">正在按「' + escapeHtml(label) + '」整理这一页…</div>';
    cardCopy.style.display = 'none';
    cardNote.style.display = 'none';
    cardTools.classList.add('show');
    renderChips(useMode, true);
    placeCardCentered();
    const text = freshPageText(14000);
    if (!text.trim()) {
      cardBody.innerHTML = '<div class="card-err">这个页面没读到可总结的文字</div>';
      renderChips(useMode, false);
      return;
    }
    showToast('正在总结 ' + text.length + ' 字…');
    const res = await send({ type: 'summary', body: { text: text, title: document.title, mode: useMode } });
    if (!res || !res.ok) {
      cardTitle.textContent = '本页总结 · ' + label;
      cardBody.innerHTML = '<div class="card-err">' + escapeHtml((res && res.error) || '总结失败') + '</div>';
      renderChips(useMode, false);
      cardTools.classList.add('show');
      placeCardCentered();
      return;
    }
    state.summaryLive = true;
    cardTitle.textContent = '本页总结 · ' + (res.modeLabel || label) + ' · ' + (res.chars || text.length) + ' 字 / ' +
      Math.round((res.ms || 0) / 100) / 10 + 's';
    cardBody.innerHTML = '<div class="card-rich"></div>';
    renderRichText(cardBody.querySelector('.card-rich'), res.text || '');
    lastResult = { kind: '网页总结 · ' + (res.modeLabel || label), text: res.text || '' };
    cardCopy.style.display = '';
    showPageTools();
    renderChips(useMode, false);
    placeCardCentered();
  }

  async function askPage(question) {
    const q = String(question || '').trim();
    if (!q) { showToast('先写一个想问的问题'); return; }
    await ensureSettings();
    hideBubble();
    const list = await loadModes();
    cardTitle.textContent = '问这一页';
    state.copyText = '';
    cardBody.innerHTML = '<div class="card-loading">正在读这一页并回答…</div>';
    cardCopy.style.display = 'none';
    cardNote.style.display = 'none';
    cardTools.classList.add('show');
    renderChips(activeMode, false);
    placeCardCentered();
    const text = freshPageText(16000);
    if (!text.trim()) {
      cardBody.innerHTML = '<div class="card-err">这个页面没读到可阅读的文字</div>';
      return;
    }
    showToast('正在回答…');
    const res = await send({ type: 'ask', body: { text: text, title: document.title, question: q } });
    if (!res || !res.ok) {
      cardTitle.textContent = '问这一页';
      cardBody.innerHTML = '<div class="card-err">' + escapeHtml((res && res.error) || '回答失败') + '</div>';
      placeCardCentered();
      return;
    }
    state.summaryLive = false;
    cardTitle.textContent = '问这一页 · ' + Math.round((res.ms || 0) / 100) / 10 + 's';
    cardBody.innerHTML = '<div class="card-q"></div><div class="card-rich"></div>';
    cardBody.querySelector('.card-q').textContent = 'Q  ' + q;
    renderRichText(cardBody.querySelector('.card-rich'), res.answer || '');
    lastResult = { kind: '网页问答', text: '问：' + q + '\n\n' + (res.answer || '') };
    cardCopy.style.display = '';
    showPageTools();
    placeCardCentered();
  }

  async function translateSelection(info) {
    await ensureSettings();
    hideBubble();
    renderSelectionCard(info, null);
    const res = await send({ type: 'selection', body: { text: info.text, target: (state.settings && state.settings.target) || 'auto', explain: true } });
    renderSelectionCard(info, res || { ok: false, error: '没有响应' });
  }

  // ---------------- 页面大纲（左侧目录，长文跳转） ----------------
  let olItems = [];
  let olRaf = 0;

  // 导航 / 侧栏 / 页眉页脚里的标题不算正文大纲（GitHub 的 About、Stars、Languages 就属于这类噪音）
  const NAV_SEL = 'nav, aside, header, footer, [role="navigation"], [role="complementary"], ' +
    '[role="banner"], [role="contentinfo"], [role="menu"], [role="toolbar"], [role="search"]';

  function headingInNav(el) {
    try { return !!el.closest(NAV_SEL); } catch (e) { return false; }
  }

  function headingsUnder(root) {
    const out = [];
    if (!root || !root.querySelectorAll) return out;
    const all = root.querySelectorAll('h1,h2,h3,h4,h5,h6');
    for (let i = 0; i < all.length; i++) {
      const el = all[i];
      if (el.id === '__ip_host' || (el.closest && el.closest('#__ip_host'))) continue;
      if (el.getAttribute('aria-hidden') === 'true') continue;
      const text = normText(el);
      if (!text || !hasMeaningfulText(text)) continue;
      if (isHidden(el)) continue;
      if (headingInNav(el)) continue;
      out.push({ el: el, level: parseInt(el.tagName.charAt(1), 10) || 2, text: text });
    }
    return out;
  }

  // 先找正文容器（一篇 README / 一篇文章）里的标题；正文标题够多就只用它，
  // 否则退回整页（长文站点之外，很多页面没有 article/main）。
  function collectHeadings() {
    if (!document.body) return [];
    const roots = [document.querySelector('article'), document.querySelector('main'),
      document.querySelector('[role="main"]'), document.querySelector('.markdown-body'),
      document.querySelector('#content'), document.querySelector('.post'), document.querySelector('.article')];
    let best = [];
    // 按「越具体越优先」的顺序取第一个够用的容器（article 里 5 个标题比 main 里含导航的 10 个更准）
    for (const r of roots) {
      if (!r) continue;
      const got = headingsUnder(r);
      if (got.length >= 3) { best = got; break; }
      if (got.length > best.length) best = got;
    }
    let list = best.length >= 3 ? best : headingsUnder(document.body);
    if (!list.length) list = headingsUnder(document.body);
    // 同一标题（同层级、同文字）重复出现时只留第一个，避免站点把标题复制一份做锚点
    const seen = new Set();
    const dedup = [];
    for (const it of list) {
      const key = it.level + '|' + it.text;
      if (seen.has(key)) continue;
      seen.add(key);
      dedup.push(it);
    }
    let minLevel = 6;
    for (const it of dedup) if (it.level < minLevel) minLevel = it.level;
    for (const it of dedup) it.indent = (it.level - minLevel) * 12;
    return dedup;
  }

  function setOutlineActive(btn) {
    const nodes = olList.querySelectorAll('.ol-item');
    for (const n of nodes) {
      if (n === btn) n.classList.add('on');
      else n.classList.remove('on');
    }
    if (!btn) return;
    const listTop = olList.getBoundingClientRect().top;
    const top = btn.getBoundingClientRect().top - listTop + olList.scrollTop;
    const h = olList.clientHeight;
    if (top < olList.scrollTop + 6) olList.scrollTop = Math.max(0, top - 6);
    else if (top + btn.offsetHeight > olList.scrollTop + h - 6) olList.scrollTop = top + btn.offsetHeight - h + 6;
  }

  function scrollToHeading(it, btn) {
    if (!it || !it.el) return;
    try {
      const top = it.el.getBoundingClientRect().top + (window.pageYOffset || 0) - 76;
      window.scrollTo({ top: Math.max(0, top), behavior: 'smooth' });
      const old = it.el.style.backgroundColor;
      it.el.style.transition = 'background-color .55s ease';
      it.el.style.backgroundColor = 'rgba(255, 233, 140, .55)';
      setTimeout(() => { it.el.style.backgroundColor = old || ''; }, 1200);
    } catch (e) {
      try { it.el.scrollIntoView({ block: 'start' }); } catch (e2) { it.el.scrollIntoView(); }
    }
    setOutlineActive(btn);
    showToast('已跳到：' + (it.text.length > 24 ? it.text.slice(0, 24) + '…' : it.text), 1400);
  }

  function renderOutline(filterText) {
    olItems = collectHeadings();
    const q = String(filterText || '').trim().toLowerCase();
    const shown = q ? olItems.filter((it) => it.text.toLowerCase().indexOf(q) >= 0) : olItems;
    olList.innerHTML = '';
    if (!shown.length) {
      const empty = document.createElement('div');
      empty.className = 'ol-empty';
      empty.textContent = olItems.length
        ? '没有匹配「' + q + '」的标题，换个词试试'
        : '这一页没有标题（h1~h6），生成不了大纲。';
      olList.appendChild(empty);
      olCount.textContent = olItems.length ? '0 / ' + olItems.length : '0 条';
      return;
    }
    const frag = document.createDocumentFragment();
    for (const it of shown) {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'ol-item';
      btn.style.paddingLeft = (8 + it.indent) + 'px';
      btn.title = it.text;
      const lv = document.createElement('span');
      lv.className = 'lv';
      lv.textContent = 'H' + it.level;
      btn.appendChild(lv);
      btn.appendChild(document.createTextNode(it.text.length > 56 ? it.text.slice(0, 56) + '…' : it.text));
      btn.addEventListener('mousedown', stopEvt);
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        scrollToHeading(it, btn);
      });
      it.node = btn;
      frag.appendChild(btn);
    }
    olList.appendChild(frag);
    olCount.textContent = shown.length + ' 条';
    syncOutlineActive();
  }

  function syncOutlineActive() {
    if (!outline.classList.contains('show') || !olItems.length) return;
    const edge = 96;
    let cur = null;
    for (const it of olItems) {
      if (!it.node) continue;
      if (it.el.getBoundingClientRect().top <= edge) cur = it;
      else break;
    }
    if (!cur) cur = olItems[0];
    if (cur && cur.node) setOutlineActive(cur.node);
  }

  function onOutlineScroll() {
    if (olRaf) return;
    olRaf = window.requestAnimationFrame(() => { olRaf = 0; syncOutlineActive(); });
  }

  function openOutline() {
    mountUi();
    outline.classList.add('show');
    btnOutline.classList.add('on');
    renderOutline(olFilter.value);
    syncOutlineActive();
  }

  function closeOutline() {
    outline.classList.remove('show');
    btnOutline.classList.remove('on');
  }

  function toggleOutline() {
    if (outline.classList.contains('show')) closeOutline();
    else openOutline();
  }

  // ---------------- 整页导出 Markdown ----------------
  const MD_SKIP = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEMPLATE', 'SVG', 'CANVAS', 'IFRAME', 'VIDEO', 'AUDIO',
    'OBJECT', 'EMBED', 'MAP', 'LINK', 'META', 'HEAD', 'TITLE']);
  // 导出时要跳过的容器（注意：不能直接复用 SKIP_TAGS —— 它为了「翻译切段」把 PRE/CODE/IMG
  // 也排除了，可导出 Markdown 恰恰要保留代码块与图片）
  const EXPORT_SKIP = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEMPLATE', 'SVG', 'CANVAS', 'IFRAME', 'VIDEO',
    'AUDIO', 'OBJECT', 'EMBED', 'MAP', 'LINK', 'META', 'HEAD', 'TITLE', 'TEXTAREA', 'INPUT', 'SELECT',
    'OPTION', 'OPTGROUP', 'MATH', 'PICTURE']);

  function absoluteUrl(href) {
    try { return new URL(href, location.href).href; } catch (e) { return href; }
  }

  function cleanInline(s) {
    return String(s == null ? '' : s).replace(/[ \t]+/g, ' ').replace(/ *\n */g, '\n').trim();
  }

  // 单个节点的行内 Markdown（文本 / 加粗 / 斜体 / 链接 / 图片 / 行内代码）
  // dst 模式下取「这一段现在看得见的文字」：优先用译文，没有译文再退回原文
  function translatedTextOf(el) {
    const tr = el.querySelector('[data-ip-tr="1"]');
    if (tr) return cleanInline(String(tr.textContent || ''));
    return inlineMdOf(el);
  }

  function inlineNode(n) {
    if (!n) return '';
    if (n.nodeType === 3) return String(n.nodeValue || '').replace(/[\t\r\n]+/g, ' ');
    if (n.nodeType !== 1) return '';
    const t = n.tagName;
    if (t === 'BR') return '\n';
    if (MD_SKIP.has(t)) return '';
    // INSIDE_TAGS 的原文包装：dst 下是隐藏的，导出时不要重复算一遍
    if (n.hasAttribute && n.hasAttribute('data-ip-orig')) return isDstMode() ? '' : inlineMdOf(n);
    // 译文节点永远不参与行内导出：bil 只导原文，dst 由 blockMdOf / listToMd / tableToMd 显式取译文
    if (n.hasAttribute && n.hasAttribute('data-ip-tr')) return '';
    if (t === 'BUTTON' || t === 'INPUT' || t === 'TEXTAREA' || t === 'SELECT' || t === 'OPTION') return '';
    if (t === 'IMG') {
      const src = absoluteUrl(n.currentSrc || n.getAttribute('src') || '');
      const alt = String(n.getAttribute('alt') || '').replace(/\s+/g, ' ').trim();
      return src ? '![' + alt + '](' + src + ')' : '';
    }
    if (t === 'CODE') {
      const c = String(n.textContent || '').replace(/\s+/g, ' ');
      return c ? '`' + c + '`' : '';
    }
    if (t === 'STRONG' || t === 'B') { const c = inlineMdOf(n); return c ? '**' + c + '**' : ''; }
    if (t === 'EM' || t === 'I') { const c = inlineMdOf(n); return c ? '*' + c + '*' : ''; }
    if (t === 'DEL' || t === 'S' || t === 'STRIKE') { const c = inlineMdOf(n); return c ? '~~' + c + '~~' : ''; }
    if (t === 'A') {
      const href = n.getAttribute('href') || '';
      const c = inlineMdOf(n);
      if (!c) return '';
      if (!href || href.charAt(0) === '#') return c;
      return '[' + c + '](' + absoluteUrl(href) + ')';
    }
    if (t === 'UL' || t === 'OL' || t === 'PRE' || t === 'TABLE' || t === 'BLOCKQUOTE') return '';
    return inlineMdOf(n);
  }

  function inlineMdOf(el) {
    if (!el) return '';
    let out = '';
    const kids = el.childNodes;
    for (let i = 0; i < kids.length; i++) out += inlineNode(kids[i]);
    return cleanInline(out);
  }

  function hasBlockChild(el) {
    for (let i = 0; i < el.children.length; i++) {
      const c = el.children[i];
      if (c instanceof HTMLElement && BLOCK_TAGS.has(c.tagName)) return true;
    }
    return false;
  }

  function fenceFor(pre) {
    const codeEl = pre.querySelector('code');
    const raw = (codeEl ? codeEl.textContent : pre.textContent) || '';
    const cls = String((codeEl && codeEl.className) || pre.className || '');
    const m = /(?:language-|lang-)([\w+#.-]+)/.exec(cls);
    const body = String(raw).replace(/\r\n/g, '\n').replace(/\n+$/, '');
    const fence = '```';
    return fence + (m ? m[1] : '') + '\n' + body + '\n' + fence;
  }

  function listToMd(el, depth) {
    const pad = new Array(depth * 2 + 1).join(' ');
    const lines = [];
    const dst = isDstMode();
    let n = 0;
    for (let i = 0; i < el.children.length; i++) {
      const li = el.children[i];
      if (!(li instanceof HTMLElement) || li.tagName !== 'LI') continue;
      n++;
      const marker = el.tagName === 'OL' ? n + '. ' : '- ';
      let head = '';
      const sub = [];
      for (let k = 0; k < li.childNodes.length; k++) {
        const node = li.childNodes[k];
        if (node.nodeType === 3) { head += String(node.nodeValue || '').replace(/[\t\r\n]+/g, ' '); continue; }
        if (node.nodeType !== 1) continue;
        const t = node.tagName;
        // dst 导出看到的是译文；双语导出保留原来的原文
        if (node.hasAttribute && node.hasAttribute('data-ip-tr')) { if (dst) head += String(node.textContent || '').replace(/[\t\r\n]+/g, ' '); continue; }
        if (node.hasAttribute && node.hasAttribute('data-ip-orig')) { if (!dst) head += inlineNode(node); continue; }
        if (t === 'UL' || t === 'OL') { sub.push(listToMd(node, depth + 1)); continue; }
        if (t === 'PRE') { sub.push(fenceFor(node)); continue; }
        if (t === 'P' || t === 'DIV' || t === 'SECTION' || t === 'BLOCKQUOTE' || hasBlockChild(node)) {
          const blocks = blockMdOf(node, depth + 1);
          if (!head.trim() && blocks.length) head = blocks.shift();
          for (const b of blocks) sub.push(b);
          continue;
        }
        head += inlineNode(node);
      }
      lines.push(pad + marker + cleanInline(head).replace(/\n+/g, ' '));
      for (const s of sub) {
        const txt = String(s || '').replace(/\s+$/, '');
        if (!txt) continue;
        const parts = txt.split('\n');
        for (const p of parts) lines.push(pad + '  ' + p);
      }
    }
    return lines.join('\n');
  }

  function tableToMd(tbl) {
    const rows = [];
    const dst = isDstMode();
    const trs = tbl.querySelectorAll('tr');
    for (const tr of trs) {
      const cells = [];
      for (const cell of tr.children) {
        if (cell.tagName !== 'TD' && cell.tagName !== 'TH') continue;
        // dst 下原文被 CSS 隐藏，导出译文；双语保持原来的原文导出
        const raw = dst ? translatedTextOf(cell) : inlineMdOf(cell);
        const txt = cleanInline(raw).replace(/\n+/g, ' ').replace(/\|/g, '\\|');
        cells.push(txt || ' ');
      }
      if (cells.length) rows.push(cells);
    }
    if (!rows.length) return '';
    let width = 0;
    for (const r of rows) width = Math.max(width, r.length);
    const line = (r) => {
      const c = r.slice();
      while (c.length < width) c.push(' ');
      return '| ' + c.join(' | ') + ' |';
    };
    const out = [line(rows[0]), '|' + new Array(width + 1).join(' --- |')];
    for (let i = 1; i < rows.length; i++) out.push(line(rows[i]));
    return out.join('\n');
  }

  // 站点级的「编辑」小控件 / 导航盒 / 分类栏：内容不是正文，导出时一律丢掉
  const EDIT_CHROME_CLS = /(^|\s)(mw-editsection|mw-editsection-bracket|mw-jump-link|mw-indicators|navbox|catlinks|printfooter|noprint|toc|mw-toc-heading|editsection|vte)(\s|$)/i;

  function isEditChrome(el) {
    try {
      const cls = String(el.className || '');
      if (cls && EDIT_CHROME_CLS.test(cls)) return true;
      const txt = String(el.textContent || '').replace(/\s+/g, '');
      if (txt === '编辑' || txt === '[编辑]' || txt === 'edit' || txt === '[edit]') {
        if (el.querySelector && el.querySelector('a[href*="action=edit"], a[href*="/edit"]')) return true;
      }
    } catch (e) { }
    return false;
  }

  function blockMdOf(el, depth) {
    const out = [];
    const dst = isDstMode();
    const push = (s) => {
      const t = String(s == null ? '' : s).replace(/\s+$/, '');
      if (t.trim()) out.push(t);
    };
    const kids = el.children;
    for (let i = 0; i < kids.length; i++) {
      const c = kids[i];
      if (!(c instanceof HTMLElement)) continue;
      if (c.id === '__ip_host') continue;
      const tag = c.tagName;
      if (MD_SKIP.has(tag) || EXPORT_SKIP.has(tag)) continue;
      // 导航 / 侧栏 / 页眉页脚整块跳过：它们的内容不是正文，导出时只会变成垃圾链接列表
      try { if (c.matches && c.matches(NAV_SEL)) continue; } catch (e) { }
      if (isEditChrome(c)) continue;
      if (c.getAttribute('aria-hidden') === 'true') continue;
      if (c.getAttribute('role') === 'navigation' || c.getAttribute('role') === 'banner' ||
        c.getAttribute('role') === 'contentinfo' || c.getAttribute('role') === 'complementary') continue;
      // dst 模式下译文节点就是要导出的正文；双语模式下沿用原来的「不导出译文」
      if (c.dataset && c.dataset.ipTr === '1') {
        if (dst) { const t = inlineMdOf(c); if (t) push(t); }
        continue;
      }
      // dst 下原文块是隐藏的，跳过它、导出旁边的译文节点；双语模式照常导出原文
      if (c.dataset && c.dataset.ipSrc === '1' && dst) continue;
      if (isHidden(c)) continue;

      if (/^H[1-6]$/.test(tag)) { const t = inlineMdOf(c); if (t) push(new Array(parseInt(tag.charAt(1), 10) + 1).join('#') + ' ' + t); continue; }
      if (tag === 'PRE') { push(fenceFor(c)); continue; }
      if (tag === 'HR') { push('---'); continue; }
      if (tag === 'BLOCKQUOTE') {
        let inner = blockMdOf(c, depth);
        if (!inner.length) { const t = inlineMdOf(c); if (t) inner = [t]; }
        if (inner.length) {
          push(inner.join('\n\n').split('\n').map((l) => (l ? '> ' + l : '>')).join('\n'));
        }
        continue;
      }
      if (tag === 'UL' || tag === 'OL') { const s = listToMd(c, 0); if (s) push(s); continue; }
      if (tag === 'TABLE') { const s = tableToMd(c); if (s) push(s); continue; }
      if (tag === 'P' || tag === 'DT' || tag === 'DD' || tag === 'FIGCAPTION' || tag === 'CAPTION' || tag === 'SUMMARY' || tag === 'ADDRESS') {
        const t = dst ? translatedTextOf(c) : inlineMdOf(c);
        if (t) push(t); continue;
      }
      if (tag === 'IMG') { const t = inlineNode(c); if (t) push(t); continue; }
      if (hasBlockChild(c)) {
        const inner = blockMdOf(c, depth);
        for (const s of inner) push(s);
        continue;
      }
      const t = dst ? translatedTextOf(c) : inlineMdOf(c);
      if (t) push(t);
    }
    return out;
  }

  // 正文容器候选：从「最像正文」到「最像整页」排序，取第一个文字量够的。
  // 站点的 <main> 往往还包着导航页签（维基百科的 条目/讨论/阅读/编辑 就是这样），
  // 所以更靠前的 #mw-content-text / .markdown-body / article 优先。
  const ROOT_SELECTORS = [
    'article',
    '[role="main"] article',
    '.markdown-body',
    '#mw-content-text .mw-parser-output',
    '#mw-content-text',
    '.post-content, .entry-content, .article-content, .article-body, .post-body',
    'main article, main .content, main .post',
    'main, [role="main"]',
    '#content, .content',
  ];

  function pickRoot() {
    for (const sel of ROOT_SELECTORS) {
      let nodes = [];
      try { nodes = Array.prototype.slice.call(document.querySelectorAll(sel)); } catch (e) { continue; }
      for (const el of nodes) {
        if (!el || el.id === '__ip_host') continue;
        if (el.closest && el.closest('#__ip_host')) continue;
        if (isHidden(el)) continue;
        const text = String(el.innerText || '').trim();
        // 正文至少要有 400 字，且不能只是整页的一小块（避免挑到侧栏卡片）
        if (text.length < 400) continue;
        return el;
      }
    }
    const main = document.querySelector('main, [role="main"]');
    if (main && String(main.innerText || '').trim().length >= 400) return main;
    return document.body;
  }

  function stampNow() {
    const d = new Date();
    const p = (n) => (n < 10 ? '0' + n : '' + n);
    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + ' ' + p(d.getHours()) + ':' + p(d.getMinutes());
  }

  function showMarkdownCard(name, dir, chars, md) {
    state.summaryLive = false;
    state.copyText = md;
    card.classList.remove('wide');
    cardTools.classList.remove('show');
    cardNote.style.display = 'none';
    cardTitle.textContent = '已导出 Markdown · ' + chars + ' 字符';
    cardBody.innerHTML = '';
    const rich = document.createElement('div');
    rich.className = 'card-rich';
    const p1 = document.createElement('p');
    p1.innerHTML = '文件：<code>' + escapeHtml(name) + '</code>';
    const p2 = document.createElement('p');
    p2.innerHTML = '目录：<code>' + escapeHtml(dir || '') + '</code>';
    const p3 = document.createElement('p');
    p3.textContent = '托盘菜单 →「打开导出目录」可以直接找到它；点右上角「复制」能把整份 Markdown 复制走。';
    rich.appendChild(p1);
    rich.appendChild(p2);
    rich.appendChild(p3);
    cardBody.appendChild(rich);
    cardCopy.style.display = '';
    card.style.width = Math.min(430, window.innerWidth - 16) + 'px';
    card.style.left = clampX(Math.round((window.innerWidth - 430) / 2), 430) + 'px';
    card.style.top = '78px';
    card.classList.add('show');
  }

  async function exportMarkdown() {
    await ensureSettings();
    setMiniBusy(btnExport, true, 'export');
    showToast('正在把这一页转成 Markdown…', 6000);
    try {
      const root = pickRoot();
      const blocks = blockMdOf(root, 0);
      let md = blocks.join('\n\n').replace(/\n{3,}/g, '\n\n').trim();
      dbg('export markdown', { len: md.length, cjk: Math.round(cjkRatio(md) * 100) / 100, head: md.slice(0, 3000) });
      const title = (document.title || '').trim() || '网页';
      const head = '# ' + title + '\n\n' +
        '> 来源：' + location.href + '\n' +
        '> 导出：' + stampNow() + '  ·  InputPolish\n\n---\n\n';
      const full = head + md + '\n';
      if (full.length < 40) {
        showToast('这一页没读到可导出的正文');
        return;
      }
      const res = await send({ type: 'export', body: { title: title, markdown: full, url: location.href } });
      if (res && res.ok) {
        showMarkdownCard(res.name || 'export.md', res.dir || '', full.length, full);
        showToast('已导出：' + (res.name || ''));
      } else {
        // 本机程序没开也照样能导出：退回到浏览器下载
        const blob = new Blob([full], { type: 'text/markdown;charset=utf-8' });
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = (title.replace(/[\\/:*?"<>|]/g, '_').slice(0, 60) || 'page') + '.md';
        document.documentElement.appendChild(a);
        a.click();
        setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 4000);
        showToast('本机程序没连上，已改用浏览器下载：' + a.download, 5000);
      }
    } catch (e) {
      showToast('导出失败：' + String(e && e.message ? e.message : e));
    } finally {
      setMiniBusy(btnExport, false, 'export');
    }
  }

  // ---------------- 划词存笔记 ----------------
  async function saveSelectionNote(info) {
    const text = info && info.text ? String(info.text).trim() : '';
    if (!text) { showToast('先选中要存的文字'); return; }
    const title = (document.title || '').trim() || text.slice(0, 24);
    const r = await send({ type: 'note', body: { title: title, body: text, kind: '划词笔记', url: location.href } });
    if (r && r.ok) {
      showToast('已存为笔记：' + String(r.path || '').split('\\').pop());
    } else {
      showToast('保存失败：' + ((r && r.error) || '未知原因'));
    }
  }

  // ---------------- 事件绑定 ----------------
  ball.addEventListener('click', async (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (state.translating) return;
    if (state.nodes.length > 0) {
      stopObserver();
      clearTranslations();
      showToast('已还原为原文');
      return;
    }
    await runPageTranslate();
  });

  bubble.addEventListener('mousedown', (e) => { e.preventDefault(); e.stopPropagation(); });
  bubble.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (!state.pendingSelection) return;
    let act = '';
    const seg = e.target && e.target.closest ? e.target.closest('.bub-seg') : null;
    if (seg) act = seg.getAttribute('data-act') || '';
    if (!act) act = 'translate';
    if (act === 'note') {
      const info = state.pendingSelection;
      hideBubble();
      saveSelectionNote(info);
      return;
    }
    translateSelection(state.pendingSelection);
  });

  // 悬浮球旁边的小按钮：大纲 / 导出 Markdown / 显示方式
  btnOutline.addEventListener('mousedown', (e) => { e.preventDefault(); e.stopPropagation(); });
  btnOutline.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    toggleOutline();
  });
  btnExport.addEventListener('mousedown', (e) => { e.preventDefault(); e.stopPropagation(); });
  btnExport.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    exportMarkdown();
  });
  btnMode.addEventListener('mousedown', (e) => { e.preventDefault(); e.stopPropagation(); });
  btnMode.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    toggleMode();
  });

  olClose.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); closeOutline(); });
  olFilter.addEventListener('mousedown', (e) => { e.stopPropagation(); });
  olFilter.addEventListener('keydown', (e) => {
    e.stopPropagation();
    if (e.key === 'Escape') { e.preventDefault(); olFilter.blur(); }
    if (e.key === 'Enter') { e.preventDefault(); renderOutline(olFilter.value); }
  });
  olFilter.addEventListener('input', () => { renderOutline(olFilter.value); });
  window.addEventListener('scroll', onOutlineScroll, { passive: true });
  window.addEventListener('resize', () => { if (outline.classList.contains('show')) syncOutlineActive(); });

  cardClose.addEventListener('click', () => hideCard());

  // 卡片工具区：方案 chips 由 renderChips 生成；这里绑「问这一页」
  cardTools.addEventListener('mousedown', (e) => { e.stopPropagation(); });
  askIn.addEventListener('mousedown', (e) => { e.stopPropagation(); });
  askIn.addEventListener('keydown', (e) => {
    e.stopPropagation();
    if (e.key === 'Enter') {
      e.preventDefault();
      const q = askIn.value;
      if (q && q.trim()) askPage(q);
    } else if (e.key === 'Escape') {
      e.stopPropagation();
      askIn.blur();
    }
  });
  askBtn.addEventListener('mousedown', (e) => { e.preventDefault(); e.stopPropagation(); });
  askBtn.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    const q = askIn.value;
    if (!q || !q.trim()) { showToast('先写一个想问的问题'); askIn.focus(); return; }
    askPage(q);
  });

  // 存笔记：把当前总结 / 问答写成本机 Markdown 文件
  cardNote.addEventListener('mousedown', (e) => { e.preventDefault(); e.stopPropagation(); });
  cardNote.addEventListener('click', async (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (!lastResult || !lastResult.text) { showToast('还没有可保存的内容'); return; }
    const r = await send({
      type: 'note',
      body: { title: document.title, body: lastResult.text, kind: lastResult.kind, url: location.href },
    });
    if (r && r.ok) {
      showToast('已存为笔记：' + String(r.path || '').split('\\').pop());
    } else {
      showToast('保存失败：' + ((r && r.error) || '未知原因'));
    }
  });
  cardCopy.addEventListener('click', async () => {
    let text = '';
    let what = '';
    if (state.copyText) {
      text = state.copyText;
      what = '已复制 Markdown';
    } else {
      const node = cardBody.querySelector('.card-tr') || cardBody.querySelector('.card-rich');
      if (!node) return;
      text = node.innerText || node.textContent || '';
      what = cardBody.querySelector('.card-tr') ? '已复制译文' : '已复制总结';
    }
    try {
      await navigator.clipboard.writeText(text);
      showToast(what);
    } catch (e) {
      showToast('复制失败：' + (e && e.message ? e.message : e));
    }
  });

  document.addEventListener('mouseup', (e) => {
    if (!state.settings || state.settings.showBall === false) { /* 仍然允许划词 */ }
    const path = e.composedPath ? e.composedPath() : [];
    if (path.indexOf(host) >= 0) return;
    const t = e.target;
    if (t && t.closest) {
      const tag = t.tagName ? t.tagName.toUpperCase() : '';
      if (tag === 'INPUT' || tag === 'TEXTAREA' || t.isContentEditable) return;
    }
    clearTimeout(state.bubbleTimer);
    state.bubbleTimer = setTimeout(() => {
      const info = selectionInfo();
      if (!info) { hideBubble(); return; }
      if (info.text.length > 1200) {
        hideBubble();
        showToast('选中的内容太长（' + info.text.length + ' 字符），请选少一点再试');
        return;
      }
      showBubble(info);
    }, 220);
  }, true);

  document.addEventListener('mousedown', (e) => {
    const path = e.composedPath ? e.composedPath() : [];
    if (path.indexOf(host) >= 0) return;
    hideBubble();
    hideCard();
  }, true);

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') { hideCard(); hideBubble(); if (outline.classList.contains('show')) closeOutline(); return; }
    // Alt+D：双语对照 ↔ 仅译文（在输入框 / 可编辑区域里不抢键）
    if (e.altKey && !e.ctrlKey && !e.metaKey && (e.key === 'd' || e.key === 'D' || e.code === 'KeyD')) {
      const t = e.target;
      const tag = t && t.tagName ? String(t.tagName).toUpperCase() : '';
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (t && t.isContentEditable)) return;
      e.preventDefault();
      toggleMode();
      return;
    }
    // Alt+O：页面大纲侧栏开关
    if (e.altKey && !e.ctrlKey && !e.metaKey && (e.key === 'o' || e.key === 'O' || e.code === 'KeyO')) {
      e.preventDefault();
      toggleOutline();
      return;
    }
    // Alt+M：整页导出 Markdown
    if (e.altKey && !e.ctrlKey && !e.metaKey && (e.key === 'm' || e.key === 'M' || e.code === 'KeyM')) {
      e.preventDefault();
      exportMarkdown();
      return;
    }
    if (e.altKey && !e.ctrlKey && !e.metaKey && (e.key === 'a' || e.key === 'A' || e.code === 'KeyA')) {
      e.preventDefault();
      if (state.translating) return;
      if (state.nodes.length > 0) { stopObserver(); clearTranslations(); showToast('已还原为原文'); }
      else runPageTranslate();
      return;
    }
    if (e.altKey && !e.ctrlKey && !e.metaKey && (e.key === 's' || e.key === 'S' || e.code === 'KeyS')) {
      const info = selectionInfo();
      if (!info) return;
      e.preventDefault();
      translateSelection(info);
    }
  }, true);

  window.addEventListener('scroll', () => { hideBubble(); }, true);

  // ---------------- 启动 ----------------
  async function boot() {
    try {
      mountUi();
      // 先读持久化的显示方式，再画 dock 上的 columns 按钮状态
      try {
        const stored = await chrome.storage.local.get({ pageMode: 'bil' });
        state.pageMode = normMode(stored.pageMode);
      } catch (e) { /* ignore */ }
      updateModeButton();
      const s = await ensureSettings();
      if (s.showBall !== false) {
        ball.style.display = '';
        dock.classList.add('show');
      }
      const h = await send({ type: 'health', body: {} });
      if (!h || !h.ok) {
        showToast('本机 InputPolish 没连上：' + ((h && h.error) || '请确认程序在运行') + '\n可在扩展选项里检查端口和连接码', 6000);
      }
    } catch (e) {
      /* ignore */
    }
  }

  // 选项页 / popup 改了显示方式时，已经打开的页面立刻跟着变
  try {
    chrome.storage.onChanged.addListener((changes, area) => {
      if (area !== 'local' || !changes || !changes.pageMode) return;
      const next = normMode(changes.pageMode.newValue);
      if (next !== state.pageMode) setMode(next, { persist: false });
    });
  } catch (e) { /* ignore */ }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }

  // 供 popup 调用
  chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (!msg || msg.type !== 'ip-page-command') return;
    (async () => {
      try {
        if (msg.command === 'translate') {
          // 已经是译文状态就当作「还原为原文」，让热键/悬浮球按钮变成开关
          if (state.nodes.length > 0) {
            stopObserver();
            clearTranslations();
            showToast('已还原为原文');
            sendResponse({ ok: true, restored: true });
            return;
          }
          runPageTranslate().catch(() => {});
          sendResponse({ ok: true, started: true });
        } else if (msg.command === 'restore') {
          stopObserver();
          clearTranslations();
          sendResponse({ ok: true });
        } else if (msg.command === 'selection') {
          const info = selectionInfo();
          if (!info) { showToast('先在网页里选中要翻译的文字'); sendResponse({ ok: false, error: 'no selection' }); return; }
          translateSelection(info).catch(() => {});
          sendResponse({ ok: true, started: true, chars: info.text.length });
        } else if (msg.command === 'summary' || msg.command.indexOf('summary:') === 0) {
          // summary 用上次的方案；summary:bullets 这种是桌面端指定方案
          const want = msg.command.indexOf('summary:') === 0 ? msg.command.slice(8) : '';
          runPageSummary(want || '').catch(() => {});
          sendResponse({ ok: true, started: true, mode: want || activeMode });
        } else if (msg.command === 'ask' || msg.command.indexOf('ask:') === 0) {
          const q = msg.command.indexOf('ask:') === 0 ? msg.command.slice(4) : '';
          if (!q) { sendResponse({ ok: false, error: 'no question' }); return; }
          askPage(q).catch(() => {});
          sendResponse({ ok: true, started: true });
        } else if (msg.command === 'focusask') {
          askIn.focus();
          sendResponse({ ok: true });
        } else if (msg.command.indexOf('run:') === 0) {
          // 桌面面板里填的自由指令（WebPilot 那种「对页面下达任务」），允许改写和翻译
          const ins = msg.command.slice(4);
          if (!ins) { showResponsePlaceholder(); sendResponse({ ok: false, error: 'no instruction' }); return; }
          runPageInstruction(ins).catch(() => {});
          sendResponse({ ok: true, started: true });
        } else if (msg.command === 'outline') {
          // 桌面悬浮球 / 托盘菜单发来的「页面大纲」：没开就打开，已经开着就重排一次
          if (outline.classList.contains('show')) renderOutline(olFilter.value);
          else openOutline();
          sendResponse({ ok: true, open: true, headings: olItems.length });
        } else if (msg.command === 'export') {
          exportMarkdown().catch(() => {});
          sendResponse({ ok: true, started: true });
        } else if (msg.command === 'status') {
          sendResponse({ ok: true, translated: state.nodes.length > 0, done: state.done, total: state.total, mode: state.pageMode });
        } else if (msg.command === 'mode' || msg.command === 'mode:toggle') {
          if (msg.command === 'mode:toggle') await toggleMode();
          sendResponse({ ok: true, mode: state.pageMode, translated: state.nodes.length > 0 });
        } else if (msg.command === 'mode:bil' || msg.command === 'mode:dst') {
          // 桌面端 / popup 下发：立刻作用于已经翻译过的页面
          const m = await setMode(msg.command.slice(5));
          sendResponse({ ok: true, mode: m, translated: state.nodes.length > 0 });
        } else {
          sendResponse({ ok: false, error: 'unknown command' });
        }
      } catch (e) {
        sendResponse({ ok: false, error: String(e && e.message ? e.message : e) });
      }
    })();
    return true;
  });
})();
