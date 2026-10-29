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
  };

  // ---------------- 和后台 service worker 通信 ----------------
  function send(msg) {
    return new Promise((resolve) => {
      try {
        chrome.runtime.sendMessage(msg, (res) => {
          const err = chrome.runtime.lastError;
          if (err) {
            resolve({ ok: false, error: '扩展后台没有响应：' + err.message });
            return;
          }
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
    '.ball-txt { pointer-events: none; }',
    '.toast { position: fixed; left: 50%; bottom: 90px; transform: translateX(-50%); max-width: 78vw;',
    '  background: rgba(23, 26, 38, .94); color: #f2f4f8; font: 13px/1.5 "Microsoft YaHei", system-ui, sans-serif;',
    '  padding: 9px 14px; border-radius: 10px; box-shadow: 0 8px 26px rgba(0,0,0,.28); opacity: 0;',
    '  transition: opacity .16s ease; pointer-events: none; white-space: pre-wrap; text-align: center; }',
    '.toast.show { opacity: 1; }',
    '.bubble { position: fixed; display: none; align-items: center; gap: 5px; height: 30px; padding: 0 11px;',
    '  border-radius: 15px; cursor: pointer; color: #fff; font: 600 13px/1 "Microsoft YaHei", system-ui, sans-serif;',
    '  background: linear-gradient(135deg, #5b6dff 0%, #a855f7 100%); box-shadow: 0 4px 14px rgba(70,80,180,.4);',
    '  user-select: none; white-space: nowrap; }',
    '.bubble.show { display: inline-flex; }',
    '.bubble .ico { font-size: 13px; }',
    '.card { position: fixed; display: none; width: 380px; max-width: 92vw; background: #ffffff; color: #1b1f24;',
    '  border-radius: 12px; box-shadow: 0 14px 40px rgba(15, 20, 30, .28); overflow: hidden;',
    '  font: 14px/1.65 "Microsoft YaHei", system-ui, sans-serif; }',
    '.card.show { display: block; }',
    '.card-head { display: flex; align-items: center; gap: 8px; padding: 8px 10px; background: #f4f6fb;',
    '  border-bottom: 1px solid #e6e9f2; cursor: default; }',
    '.card-title { font-weight: 600; font-size: 13px; color: #4b5563; flex: 1; }',
    '.card-btn { border: 0; background: transparent; color: #6b7280; cursor: pointer; font: 12px/1 "Microsoft YaHei", sans-serif;',
    '  padding: 4px 6px; border-radius: 6px; }',
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
    '  color: #939db4; font: 11px/1.2 "Microsoft YaHei", system-ui, sans-serif; }',
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
    '.ask-btn { padding: 0 14px; border-radius: 8px; border: 0; cursor: pointer; color: #fff;',
    '  font: 600 13px/1 "Microsoft YaHei", system-ui, sans-serif;',
    '  background: linear-gradient(135deg, #5b6dff 0%, #a855f7 100%); }',
    '.ask-btn:hover { filter: brightness(1.06); }',
    '.card-stat { margin-top: 8px; color: #9aa3b5; font: 11.5px/1.4 "Microsoft YaHei", system-ui, sans-serif; }',
    '.card-q { font: 600 13.5px/1.55 "Microsoft YaHei", system-ui, sans-serif; color: #4b5563;',
    '  padding: 7px 10px; margin: 0 0 10px 0; background: #f4f6fb; border-radius: 8px;',
    '  border-left: 3px solid #5b6dff; white-space: pre-wrap; word-break: break-word; }',
  ].join('\n');
  shadow.appendChild(styleEl);

  const ball = document.createElement('button');
  ball.className = 'ball';
  ball.title = '输入框润色 · 网页内翻译（Alt+A 翻译/还原整页）';
  const ballTxt = document.createElement('span');
  ballTxt.className = 'ball-txt';
  ballTxt.textContent = '译';
  ball.appendChild(ballTxt);
  ball.style.display = 'none';
  shadow.appendChild(ball);

  const toast = document.createElement('div');
  toast.className = 'toast';
  shadow.appendChild(toast);

  const bubble = document.createElement('div');
  bubble.className = 'bubble';
  const bubbleIco = document.createElement('span');
  bubbleIco.className = 'ico';
  bubbleIco.textContent = '译';
  const bubbleLabel = document.createElement('span');
  bubbleLabel.textContent = '划词翻译';
  bubble.appendChild(bubbleIco);
  bubble.appendChild(bubbleLabel);
  shadow.appendChild(bubble);

  const card = document.createElement('div');
  card.className = 'card';
  const cardHead = document.createElement('div');
  cardHead.className = 'card-head';
  const cardTitle = document.createElement('span');
  cardTitle.className = 'card-title';
  cardTitle.textContent = '划词翻译';
  const cardCopy = document.createElement('button');
  cardCopy.className = 'card-btn';
  cardCopy.textContent = '复制';
  const cardClose = document.createElement('button');
  cardClose.className = 'card-btn';
  cardClose.textContent = '关闭';
  cardHead.appendChild(cardTitle);
  cardHead.appendChild(cardCopy);
  cardHead.appendChild(cardClose);
  const cardNote = document.createElement('button');
  cardNote.className = 'card-btn';
  cardNote.textContent = '存笔记';
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
  askBtn.textContent = '问';
  askRow.appendChild(askIn);
  askRow.appendChild(askBtn);
  // 加号单独放在滚动容器外面：芯片条横向滚到底也不会把「+」滚丢。
  const chipsRow = document.createElement('div');
  chipsRow.className = 'chips-row';
  const chipPlus = document.createElement('button');
  chipPlus.className = 'chip-plus';
  chipPlus.type = 'button';
  chipPlus.textContent = '+';
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
    ballTxt.textContent = text || '译';
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

  function isHidden(el) {
    if (!el.isConnected) return true;
    const cs = getComputedStyle(el);
    if (!cs) return false;
    if (cs.display === 'none' || cs.visibility === 'hidden') return true;
    if (cs.opacity === '0') return true;
    const r = el.getBoundingClientRect();
    if (r.width < 2 || r.height < 2) return true;
    return false;
  }

  function normText(el) {
    const raw = el.innerText || el.textContent || '';
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
      if (el.dataset && el.dataset.ipSrc === '1' && !includeTranslated) return;
      if (el.dataset && el.dataset.ipTr === '1') return;
      if (isHidden(el)) return;

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

  function insertTranslation(unit, translated) {
    const el = unit.el;
    if (!el.isConnected) return false;
    const node = document.createElement('div');
    node.dataset.ipTr = '1';
    node.setAttribute('dir', 'auto');
    node.setAttribute('lang', state.target === 'en' ? 'en' : 'zh-CN');
    node.textContent = translated;
    node.style.cssText = styleForTranslation();
    try {
      if (INSIDE_TAGS.has(el.tagName)) el.appendChild(node);
      else el.insertAdjacentElement('afterend', node);
    } catch (e) {
      return false;
    }
    el.dataset.ipSrc = '1';
    state.nodes.push(node);
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
    const marked = document.querySelectorAll('[data-ip-src="1"]');
    for (const el of marked) el.removeAttribute('data-ip-src');
    setBall('', '译');
    ball.title = '输入框润色 · 网页内翻译（Alt+A 翻译/还原整页）';
  }

  // ---------------- 动态内容（SPA / 滚动加载） ----------------
  let observerTimer = 0;
  function startObserver() {
    if (state.observer) return;
    state.observer = new MutationObserver(() => {
      if (!state.translated || state.translating) return;
      clearTimeout(observerTimer);
      observerTimer = setTimeout(translateNewNodes, 900);
    });
    state.observer.observe(document.body, { childList: true, subtree: true });
  }

  async function translateNewNodes() {
    if (state.translating) return;
    const units = [];
    const all = collectUnits();
    for (const u of all) {
      if (shouldSkipByLanguage(u.text)) continue;
      if (u.text.length > 1200) continue;
      units.push(u);
    }
    if (!units.length) return;
    state.translating = true;
    try {
      const chunk = units.slice(0, 12);
      const res = await translateTexts(chunk.map((u) => u.text));
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
    const w = 96;
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
    return parts.join('\n');
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
      pen.textContent = '\u270E';
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
  }

  async function runPageSummary(mode) {
    const useMode = mode || activeMode || 'brief';
    activeMode = useMode;
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
    translateSelection(state.pendingSelection);
  });

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
    const node = cardBody.querySelector('.card-tr') || cardBody.querySelector('.card-rich');
    if (!node) return;
    const isSummary = !cardBody.querySelector('.card-tr');
    try {
      await navigator.clipboard.writeText(node.innerText || node.textContent || '');
      showToast(isSummary ? '已复制总结' : '已复制译文');
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
    if (e.key === 'Escape') { hideCard(); hideBubble(); return; }
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
      const s = await ensureSettings();
      if (s.showBall !== false) ball.style.display = '';
      const h = await send({ type: 'health', body: {} });
      if (!h || !h.ok) {
        showToast('本机 InputPolish 没连上：' + ((h && h.error) || '请确认程序在运行') + '\n可在扩展选项里检查端口和连接码', 6000);
      }
    } catch (e) {
      /* ignore */
    }
  }

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
        } else if (msg.command === 'status') {
          sendResponse({ ok: true, translated: state.nodes.length > 0, done: state.done, total: state.total });
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
