// 产出 README 用的干净截图：双语 / 仅译文 / 划词胶囊（不带调试浮层与 toast）。
// 前置：独立 Tabbit 实例 --load-extension=E:\脚本\input-polish\ext --remote-debugging-port=9333，
//       本地静态服务 http://127.0.0.1:8123/shot-page.html（python -m http.server 8123，cwd=tests）。
// 用法：node tests/ext-shots.mjs
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const BASE = process.env.CDP || 'http://127.0.0.1:9333';
const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, 'out');
const PAGE_URL = process.env.SHOT_URL || 'http://127.0.0.1:8123/shot-page.html';
const EXT_ID = 'cgafbcpnhlhpjfhjjonoocagediilijg';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

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
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) {
      const { res, rej } = pending.get(m.id);
      pending.delete(m.id);
      if (m.error) rej(new Error(JSON.stringify(m.error))); else res(m.result);
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
    close() { ws.close(); },
  };
}

async function evaluate(c, expression) {
  const r = await c.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) throw new Error('eval failed: ' + ((r.exceptionDetails.exception && r.exceptionDetails.exception.description) || r.exceptionDetails.text));
  return r.result ? r.result.value : undefined;
}

async function waitFor(c, expression, timeoutMs, label) {
  const until = Date.now() + timeoutMs;
  let last;
  while (Date.now() < until) {
    try { last = await evaluate(c, expression); } catch (e) { last = undefined; }
    if (last) return last;
    await sleep(400);
  }
  throw new Error('timeout waiting for ' + label + ' last=' + JSON.stringify(last));
}

async function shot(c, name, clip) {
  const params = { format: 'png', captureBeyondViewport: false };
  if (clip) params.clip = Object.assign({ scale: 1 }, clip);
  const r = await c.send('Page.captureScreenshot', params);
  const file = join(OUT, name);
  writeFileSync(file, Buffer.from(r.data, 'base64'));
  return file;
}

const main = async () => {
  // 显示方式先回到默认的双语对照，截图可重复
  const seed = await newTarget('chrome-extension://' + EXT_ID + '/options.html');
  const sc = await connect(seed.webSocketDebuggerUrl);
  await sc.send('Page.enable');
  await sleep(900);
  await evaluate(sc, `new Promise((resolve) => chrome.storage.local.set({ pageMode: 'bil' }, () => resolve(true)))`);
  sc.close();

  const t = await newTarget(PAGE_URL);
  const c = await connect(t.webSocketDebuggerUrl);
  const report = {};
  try {
    await c.send('Page.enable');
    await c.send('Runtime.enable');
    await fetch(BASE + '/json/activate/' + t.id).catch(() => {});
    await c.send('Emulation.setDeviceMetricsOverride', { width: 1024, height: 768, deviceScaleFactor: 1, mobile: false });
    try { await c.send('Emulation.setScrollbarsHidden', { hidden: true }); } catch (e) { /* ignore */ }
    await waitFor(c, `!!document.getElementById('__ip_host')`, 15000, 'content script');
    await waitFor(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; const b = sr.querySelector('.ball'); return b && getComputedStyle(b).display !== 'none'; })()`, 10000, 'floating ball visible');
    await sleep(500);

    // 翻译
    await evaluate(c, `(() => { document.getElementById('__ip_host').shadowRoot.querySelector('.ball').click(); return true; })()`);
    await waitFor(c, `document.querySelectorAll('[data-ip-tr="1"]').length > 0`, 60000, 'translation started');
    await waitFor(c, `(() => { const b = document.getElementById('__ip_host').shadowRoot.querySelector('.ball'); return b.classList.contains('done'); })()`, 180000, 'translation done');
    await waitFor(c, `!document.getElementById('__ip_host').shadowRoot.querySelector('.toast').classList.contains('show')`, 15000, 'toast gone');
    await sleep(500);
    report.bil = await shot(c, 'shot-bil.png');
    report.stateBil = await evaluate(c, `(() => ({ tr: document.querySelectorAll('[data-ip-tr]').length, mode: document.documentElement.getAttribute('data-ip-mode') || 'bil', btn: Array.from(document.getElementById('__ip_host').shadowRoot.querySelectorAll('.mini')).map((b) => b.className) }))()`);

    // 仅译文
    await evaluate(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; const m = sr.querySelectorAll('.mini'); m[m.length - 1].click(); return true; })()`);
    await waitFor(c, `document.documentElement.getAttribute('data-ip-mode') === 'dst'`, 8000, 'dst mode');
    await waitFor(c, `!document.getElementById('__ip_host').shadowRoot.querySelector('.toast').classList.contains('show')`, 15000, 'toast gone (dst)');
    await sleep(500);
    report.dst = await shot(c, 'shot-dst.png');
    report.stateDst = await evaluate(c, `(() => ({ mode: document.documentElement.getAttribute('data-ip-mode'), modeBtnOn: (() => { const m = document.getElementById('__ip_host').shadowRoot.querySelectorAll('.mini'); return m[m.length - 1].classList.contains('on'); })(), hidden: (() => { const p = document.querySelector('[data-ip-src="1"]'); return p ? getComputedStyle(p).display : null; })() }))()`);

    // 划词胶囊：切回双语对照再截（和实际用法一致）
    await evaluate(c, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; const m = sr.querySelectorAll('.mini'); m[m.length - 1].click(); return true; })()`);
    await waitFor(c, `!document.documentElement.getAttribute('data-ip-mode')`, 8000, 'back to bil');
    await waitFor(c, `!document.getElementById('__ip_host').shadowRoot.querySelector('.toast').classList.contains('show')`, 15000, 'toast gone (bil again)');

    const sel = await evaluate(c, `(() => {
      const p = document.querySelector('.lede');
      const r = document.createRange();
      r.selectNodeContents(p);
      const s = getSelection();
      s.removeAllRanges();
      s.addRange(r);
      const b = r.getBoundingClientRect();
      return { x: Math.round(b.left + b.width / 2), y: Math.round(b.top + Math.min(18, b.height / 2)), text: s.toString().slice(0, 40) };
    })()`);
    await c.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: sel.x, y: sel.y, button: 'left', clickCount: 1 });
    await waitFor(c, `document.getElementById('__ip_host').shadowRoot.querySelector('.bubble').classList.contains('show')`, 8000, 'selection bubble');
    await sleep(350);
    const bubble = await evaluate(c, `(() => {
      const b = document.getElementById('__ip_host').shadowRoot.querySelector('.bubble');
      const r = b.getBoundingClientRect();
      return { x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height), text: b.textContent.trim() };
    })()`);
    // 截胶囊局部：先临时藏掉页面正文（host 挂在 <html> 下，不受 body 影响），得到干净白底
    await evaluate(c, `(() => { document.body.style.visibility = 'hidden'; return true; })()`);
    await sleep(250);
    const pad = 10;
    const clip = {
      x: Math.max(0, bubble.x - pad),
      y: Math.max(0, bubble.y - pad),
      width: bubble.w + pad * 2,
      height: bubble.h + pad * 2,
      scale: 2,
    };
    report.selection = await shot(c, 'shot-selection.png', clip);
    report.selectionInfo = { bubble, clip, selected: sel.text };
    await evaluate(c, `(() => { document.body.style.visibility = ''; const s = getSelection(); s.removeAllRanges(); return true; })()`);
  } finally {
    c.close();
  }
  console.log(JSON.stringify(report, null, 2));
};

main().catch((e) => {
  console.error('SHOTS FAILED: ' + (e && e.stack ? e.stack : e));
  process.exit(1);
});
