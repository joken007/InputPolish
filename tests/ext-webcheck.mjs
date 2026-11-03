// 真实浏览器验证：在独立的 Tabbit 实例（已 --load-extension ext/）里跑双语 / 仅译文切换与还原。
// 前置：Tabbit Browser --user-data-dir=... --load-extension=E:\脚本\input-polish\ext --remote-debugging-port=9333
// 用法：node tests/ext-webcheck.mjs   （CDP 地址可用环境变量 CDP 覆盖）
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const BASE = process.env.CDP || 'http://127.0.0.1:9333';
const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, 'out');
const PAGE_URL = process.env.PAGE_URL || 'http://127.0.0.1:8123/bilingual.html';
const EXT_ID = 'cgafbcpnhlhpjfhjjonoocagediilijg';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function targets() {
  const r = await fetch(BASE + '/json/list');
  return r.json();
}

async function newTarget(url) {
  const r = await fetch(BASE + '/json/new?' + encodeURIComponent(url), { method: 'PUT' });
  if (!r.ok) throw new Error('new target failed ' + r.status + ' ' + (await r.text()));
  return r.json();
}

async function connect(wsUrl) {
  const ws = new WebSocket(wsUrl);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = () => rej(new Error('ws error')); });
  let id = 0;
  const pending = new Map();
  const api = { events: [], onEvent: null };
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) {
      const { res, rej } = pending.get(m.id);
      pending.delete(m.id);
      if (m.error) rej(new Error(JSON.stringify(m.error))); else res(m.result);
      return;
    }
    if (m.method && api.onEvent) api.onEvent(m);
    if (m.method === 'Log.entryAdded') api.events.push({ log: m.params.entry.text, level: m.params.entry.level });
    if (m.method === 'Runtime.consoleAPICalled') {
      api.events.push({ console: (m.params.args || []).map((a) => a.value).join(' ').slice(0, 400), type: m.params.type });
    }
    if (m.method === 'Runtime.exceptionThrown') {
      const d = m.params.exceptionDetails;
      api.events.push({ exception: (d.exception && d.exception.description) || d.text });
    }
  };
  return {
    send(method, params) {
      return new Promise((res, rej) => {
        const i = ++id;
        pending.set(i, { res, rej });
        ws.send(JSON.stringify({ id: i, method, params: params || {} }));
      });
    },
    get events() { return api.events; },
    close() { ws.close(); },
  };
}

async function evaluate(c, expression, awaitPromise = true) {
  const r = await c.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise });
  if (r.exceptionDetails) {
    const d = r.exceptionDetails.exception && r.exceptionDetails.exception.description;
    throw new Error('eval failed: ' + (d || r.exceptionDetails.text));
  }
  return r.result ? r.result.value : undefined;
}

async function waitFor(c, expression, timeoutMs, label) {
  const until = Date.now() + timeoutMs;
  let last;
  while (Date.now() < until) {
    try { last = await evaluate(c, expression); } catch (e) { last = undefined; }
    if (last) return last;
    await sleep(600);
  }
  throw new Error('timeout waiting for ' + label + ' last=' + JSON.stringify(last));
}

async function shot(c, name) {
  try { await evaluate(c, `(window.__ipTick && window.__ipTick(), true)`); } catch (e) { /* ignore */ }
  const r = await c.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  const file = join(OUT, name);
  writeFileSync(file, Buffer.from(r.data, 'base64'));
  return file;
}

const stateExpr = `(() => {
  const sr = document.getElementById('__ip_host') && document.getElementById('__ip_host').shadowRoot;
  const tr = document.querySelectorAll('[data-ip-tr="1"]');
  const src = document.querySelectorAll('[data-ip-src="1"]');
  const inside = document.querySelectorAll('[data-ip-src="in"]');
  const orig = document.querySelectorAll('[data-ip-orig="1"]');
  const sample = src[0];
  const sampleTr = tr[0];
  const srMode = sr && sr.querySelector('.mini:nth-of-type(3)');
  return {
    mode: document.documentElement.getAttribute('data-ip-mode') || 'bil',
    adopted: document.adoptedStyleSheets.length,
    translations: tr.length,
    srcMarks: src.length,
    insideMarks: inside.length,
    origSpans: orig.length,
    sampleSrcDisplay: sample ? getComputedStyle(sample).display : null,
    sampleTrMargin: sampleTr ? getComputedStyle(sampleTr).marginTop : null,
    sampleTrBorder: sampleTr ? getComputedStyle(sampleTr).borderLeftWidth : null,
    ballIcon: sr && sr.querySelector('.ball') ? (sr.querySelector('.ball').innerHTML.slice(0, 200)) : '',
    ballTitle: sr && sr.querySelector('.ball') ? sr.querySelector('.ball').title : '',
    miniCount: sr ? sr.querySelectorAll('.mini').length : 0,
    miniTitles: sr ? Array.from(sr.querySelectorAll('.mini')).map((b) => b.title) : [],
    modeBtnOn: srMode ? srMode.classList.contains('on') : null,
    svgCount: sr ? sr.querySelectorAll('svg').length : 0,
    ipIconSvg: sr ? sr.querySelectorAll('.ball svg').length : 0,
    status: document.getElementById('ip-status') ? document.getElementById('ip-status').textContent : ''
  };
})()`;

const main = async () => {
  // 先把显示方式重置成默认的双语，保证每次跑都是同一套断言
  const seed = await newTarget('chrome-extension://' + EXT_ID + '/options.html');
  const sc = await connect(seed.webSocketDebuggerUrl);
  await sc.send('Page.enable');
  await sleep(900);
  await evaluate(sc, `new Promise((resolve) => chrome.storage.local.set({ pageMode: 'bil' }, () => resolve(true)))`);
  sc.close();

  const t = await newTarget(PAGE_URL);
  const c = await connect(t.webSocketDebuggerUrl);
  const out = { url: PAGE_URL, steps: {} };
  const results = [];
  const check = (name, pass, detail) => {
    results.push({ name, pass: !!pass, detail });
    console.log((pass ? 'PASS ' : 'FAIL ') + name + (detail === undefined ? '' : ' :: ' + JSON.stringify(detail)));
  };

  try {
    await c.send('Page.enable');
    await c.send('Runtime.enable');
    try { await c.send('Log.enable'); } catch (e) { /* ignore */ }
    await fetch(BASE + '/json/activate/' + t.id).catch(() => {});
    await waitFor(c, `!!document.getElementById('__ip_host')`, 15000, 'content script injected');
    await sleep(600);
    const initial = await evaluate(c, stateExpr);
    out.steps.initial = initial;
    check('content script injected (ip-icons.js + content.js)', !!initial.ballIcon, initial.ballIcon);
    check('ball uses SVG icon, no 译 glyph', /<svg/.test(initial.ballIcon) && !/>译</.test(initial.ballIcon), initial.ballIcon);
    check('3 mini buttons (outline/export/mode)', initial.miniCount === 3, initial.miniTitles);
    check('mode button exists', /显示方式/.test((initial.miniTitles[2] || '')), initial.miniTitles[2]);
    check('default mode is bil (no data-ip-mode)', initial.mode === 'bil', initial.mode);

    // 0.1 划词胶囊 + 大纲侧栏的图标（静态检查，不额外消耗模型额度）
    const bubbleInfo = await evaluate(c, `(() => Array.from(document.getElementById('__ip_host').shadowRoot.querySelectorAll('.bub-seg')).map((s) => ({ text: s.textContent.trim(), svg: s.querySelectorAll('svg').length })))()`);
    out.steps.bubble = bubbleInfo;
    check('capsule bubble: translate/note icons + labels', bubbleInfo.length === 2 && bubbleInfo.every((s) => s.svg === 1) && /划词翻译/.test(bubbleInfo[0].text) && /存笔记/.test(bubbleInfo[1].text), bubbleInfo);

    await evaluate(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; sr.querySelectorAll('.mini')[0].click(); return true; })()`);
    const outlineInfo = await waitFor(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; const o = sr.querySelector('.outline'); if (!o.classList.contains('show')) return 0; return { items: o.querySelectorAll('.ol-item').length, searchIcon: o.querySelectorAll('.ol-ico svg').length, closeLabel: o.querySelector('.ol-x').textContent.trim() }; })()`, 6000, 'outline panel');
    out.steps.outline = outlineInfo;
    check('outline panel: headings + search/close icons', outlineInfo.items > 5 && outlineInfo.searchIcon === 1 && /关闭/.test(outlineInfo.closeLabel), outlineInfo);
    await evaluate(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; sr.querySelector('.ol-x').click(); return true; })()`);
    await waitFor(c, `!document.getElementById('__ip_host').shadowRoot.querySelector('.outline').classList.contains('show')`, 4000, 'outline closed');

    // 1) 翻译整页
    await evaluate(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; sr.querySelector('.ball').click(); return true; })()`);
    const startedAt = Date.now();
    let boot = null;
    while (Date.now() - startedAt < 120000) {
      boot = await evaluate(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; return { tr: document.querySelectorAll('[data-ip-tr="1"]').length, toast: sr.querySelector('.toast').textContent, ball: sr.querySelector('.ball').className }; })()`);
      if (boot.tr > 0) break;
      if (Date.now() - startedAt > 6000 && /失败|没连上|没找到/.test(boot.toast || '')) {
        console.log('translation wait: toast=' + boot.toast);
      }
      await sleep(900);
    }
    if (!boot || boot.tr === 0) {
      out.steps.translateBoot = boot;
      out.steps.events = c.events.slice(-20);
      throw new Error('no translation inserted: ' + JSON.stringify({ boot, events: c.events.slice(-10) }));
    }
    console.log('translation started after ' + Math.round((Date.now() - startedAt) / 100) / 10 + 's, nodes=' + boot.tr);
    const done = await waitFor(c,
      `(() => { const sr = document.getElementById('__ip_host').shadowRoot; const b = sr.querySelector('.ball'); return b.classList.contains('done') ? document.querySelectorAll('[data-ip-tr="1"]').length : 0; })()`,
      150000, 'translation finished');
    const bil = await evaluate(c, stateExpr);
    out.steps.bil = bil;
    check('translation inserted', bil.translations > 0, bil.translations);
    check('original blocks marked + visible in bil', bil.srcMarks > 0 && bil.sampleSrcDisplay !== 'none', { marks: bil.srcMarks, display: bil.sampleSrcDisplay });
    check('inside tags wrapped with span[data-ip-orig]', bil.origSpans > 0, bil.origSpans);
    check('bil translation keeps left border', parseFloat(bil.sampleTrBorder) > 0, bil.sampleTrBorder);
    out.bilPng = await shot(c, 'ext-bil.png');

    // 2) 点 columns 按钮切到仅译文
    await evaluate(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; const m = sr.querySelectorAll('.mini'); m[m.length - 1].click(); return true; })()`);
    await waitFor(c, `document.documentElement.getAttribute('data-ip-mode') === 'dst'`, 8000, 'dst mode applied');
    const dst = await evaluate(c, stateExpr);
    out.steps.dst = dst;
    check('dst: html[data-ip-mode=dst]', dst.mode === 'dst', dst.mode);
    check('dst: original blocks hidden', dst.sampleSrcDisplay === 'none', dst.sampleSrcDisplay);
    check('dst: translations still present', dst.translations === bil.translations, { now: dst.translations, was: bil.translations });
    check('dst: inside originals hidden via span', dst.origSpans > 0, dst.origSpans);
    check('dst: translation border/margin stripped', parseFloat(dst.sampleTrBorder) === 0 && parseFloat(dst.sampleTrMargin) === 0, { border: dst.sampleTrBorder, margin: dst.sampleTrMargin });
    check('dst: mode button highlighted', dst.modeBtnOn === true, dst.modeBtnOn);
    out.dstPng = await shot(c, 'ext-dst.png');

    // 2.5 SPA 动态内容：dst 模式下新插入的段落也要自动翻译并隐藏原文
    const lateCount = dst.translations;
    await evaluate(c, `(() => {
      const p = document.createElement('p');
      p.id = 'ip-late';
      p.textContent = 'This paragraph was injected after the initial translation finished, so it exercises the SPA observer while translation-only mode is active.';
      const anchor = document.querySelector('#ip-late-anchor') || document.querySelector('h2');
      anchor.insertAdjacentElement('beforebegin', p);
      return true;
    })()`);
    const late = await waitFor(c, `(() => { const p = document.getElementById('ip-late'); if (!p || !p.hasAttribute('data-ip-src')) return 0; const tr = p.nextElementSibling; return tr && tr.getAttribute('data-ip-tr') === '1' ? { src: p.getAttribute('data-ip-src'), display: getComputedStyle(p).display, text: tr.textContent.slice(0, 40), total: document.querySelectorAll('[data-ip-tr="1"]').length } : 0; })()`, 30000, 'SPA dynamic translation');
    out.steps.spa = { before: lateCount, after: late.total, sample: late };
    check('SPA: late paragraph translated + original hidden in dst', late.src === '1' && late.display === 'none' && late.total > lateCount, late);

    // 3) Alt+D 切回双语（键盘路径）
    const key = async (k, code, vk) => {
      await c.send('Input.dispatchKeyEvent', { type: 'keyDown', modifiers: 1, key: k, code, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk });
      await c.send('Input.dispatchKeyEvent', { type: 'keyUp', modifiers: 1, key: k, code, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk });
    };
    await key('d', 'KeyD', 68);
    await waitFor(c, `!document.documentElement.getAttribute('data-ip-mode')`, 6000, 'Alt+D back to bil');
    const backBil = await evaluate(c, stateExpr);
    out.steps.altD = { mode: backBil.mode, display: backBil.sampleSrcDisplay, translations: backBil.translations };
    check('Alt+D toggles back to bil', backBil.mode === 'bil' && backBil.sampleSrcDisplay !== 'none', out.steps.altD);

    // 4) 输入框里 Alt+D 不应该触发
    await evaluate(c, `(() => { const i = document.querySelector('input[type=text]'); i.focus(); return document.activeElement === i; })()`);
    await key('d', 'KeyD', 68);
    await sleep(900);
    const afterInput = await evaluate(c, stateExpr);
    check('Alt+D ignored inside text input', afterInput.mode === 'bil', afterInput.mode);

    // 5) 仅译文下导出 Markdown（不破坏 Alt+M 路径）
    await evaluate(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; const m = sr.querySelectorAll('.mini'); m[m.length - 1].click(); return true; })()`);
    await waitFor(c, `document.documentElement.getAttribute('data-ip-mode') === 'dst'`, 8000, 'dst again');
    await key('m', 'KeyM', 77);
    await waitFor(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; const card = sr.querySelector('.card'); return card.classList.contains('show') && /已导出|没读到/.test(sr.querySelector('.card-title').textContent); })()`, 20000, 'export card');
    const exportInfo = await evaluate(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; return { title: sr.querySelector('.card-title').textContent, body: sr.querySelector('.card-body').innerText.slice(0, 160) }; })()`);
    out.steps.export = exportInfo;
    check('export Markdown works in dst mode', /已导出/.test(exportInfo.title), exportInfo.title);
    await evaluate(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; const b = Array.from(sr.querySelectorAll('.card-head .card-btn')).find((x) => x.textContent.indexOf('关闭') >= 0); if (b) b.click(); return true; })()`);

    // 6) 还原必须彻底
    await evaluate(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; sr.querySelector('.ball').click(); return true; })()`);
    await waitFor(c, `document.querySelectorAll('[data-ip-tr="1"]').length === 0`, 10000, 'translations removed');
    await sleep(500);
    const restored = await evaluate(c, stateExpr);
    out.steps.restored = restored;
    check('restore: translations removed', restored.translations === 0, restored.translations);
    check('restore: data-ip-src cleaned', restored.srcMarks === 0 && restored.insideMarks === 0, { src: restored.srcMarks, inside: restored.insideMarks });
    check('restore: span wrappers unwrapped', restored.origSpans === 0, restored.origSpans);
    check('restore: data-ip-mode removed', restored.mode === 'bil', restored.mode);
    out.restoredPng = await shot(c, 'ext-restored.png');

    // 7) 选项页（图标 + 显示方式下拉）
    const opt = await newTarget('chrome-extension://' + EXT_ID + '/options.html');
    const oc = await connect(opt.webSocketDebuggerUrl);
    await oc.send('Page.enable');
    await sleep(1200);
    const optInfo = await evaluate(oc, `(() => ({
      hasIcons: window.IPIcons ? true : false,
      svgOnButtons: document.querySelectorAll('button svg').length,
      logo: document.querySelectorAll('#logo svg').length,
      pageModeValue: document.getElementById('pageMode') ? document.getElementById('pageMode').value : null,
      pageModeOptions: document.getElementById('pageMode') ? Array.from(document.getElementById('pageMode').options).map(o => o.value) : []
    }))()`);
    out.steps.options = optInfo;
    check('options page: ip-icons loaded + icons rendered', optInfo.hasIcons && optInfo.svgOnButtons >= 3 && optInfo.logo === 1, optInfo);
    // 最后一次显式切换是 dst（还原原文不会重置显示偏好），设置页应该读回 dst
    check('options page: pageMode persisted from web page', optInfo.pageModeValue === 'dst' && optInfo.pageModeOptions.join(',') === 'bil,dst', optInfo);
    out.optionsPng = await shot(oc, 'ext-options.png');

    // 8) 选项页保存写回 storage（桌面端/其他页面靠 storage 同步）
    await evaluate(oc, `(() => { const s = document.getElementById('pageMode'); s.value = 'bil'; document.getElementById('save').click(); return true; })()`);
    await sleep(700);
    const savedMode = await evaluate(oc, `new Promise((resolve) => chrome.storage.local.get({ pageMode: 'bil' }, (v) => resolve(v.pageMode)))`);
    out.steps.optionsSaved = savedMode;
    check('options page: saving persists pageMode', savedMode === 'bil', savedMode);
    oc.close();

    // 9) popup 页面的图标
    const pop = await newTarget('chrome-extension://' + EXT_ID + '/popup.html');
    const pc = await connect(pop.webSocketDebuggerUrl);
    await pc.send('Page.enable');
    await sleep(1000);
    const popInfo = await evaluate(pc, `(() => ({
      hasIcons: !!window.IPIcons,
      svgButtons: document.querySelectorAll('button svg').length,
      logo: document.querySelectorAll('#logo svg').length,
      texts: Array.from(document.querySelectorAll('button')).map((b) => b.innerText.replace(/\\s+/g, ' ').trim())
    }))()`);
    out.steps.popup = popInfo;
    check('popup: icons rendered, labels kept', popInfo.hasIcons && popInfo.svgButtons >= 4 && popInfo.logo === 1, popInfo);
    check('popup: mode row shows saved preference', /双语对照|仅译文/.test(popInfo.texts[0] || ''), popInfo.texts[0]);
    out.popupPng = await shot(pc, 'ext-popup.png');
    pc.close();

    out.results = results;
    out.pass = results.every((r) => r.pass);
    writeFileSync(join(OUT, 'ext-webcheck.json'), JSON.stringify(out, null, 2), 'utf8');
    console.log('RESULT ' + (out.pass ? 'ALL PASS' : 'SOME FAILED'));
  } finally {
    c.close();
  }
};

main().catch((e) => {
  console.error('CHECK FAILED: ' + (e && e.stack ? e.stack : e));
  process.exit(1);
});
