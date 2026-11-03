// 补两张 README 截图：本页总结卡片（底部芯片条）/ 页面大纲侧栏。
// 前置同 ext-shots.mjs：独立 Tabbit 实例 + 本地静态服务 8123。
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const BASE = process.env.CDP || 'http://127.0.0.1:9333';
const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, 'out');
const EXT_ID = 'cgafbcpnhlhpjfhjjonoocagediilijg';
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function open(url) {
  const r = await fetch(BASE + '/json/new?' + encodeURIComponent(url), { method: 'PUT' });
  if (!r.ok) throw new Error('new target failed ' + r.status);
  const t = await r.json();
  const ws = new WebSocket(t.webSocketDebuggerUrl);
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
  const send = (method, params) => new Promise((res, rej) => {
    const i = ++id;
    pending.set(i, { res, rej });
    ws.send(JSON.stringify({ id: i, method, params: params || {} }));
  });
  const ev = async (expression) => {
    const r = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error('eval failed: ' + ((r.exceptionDetails.exception && r.exceptionDetails.exception.description) || r.exceptionDetails.text));
    return r.result ? r.result.value : undefined;
  };
  await send('Runtime.enable');
  await send('Page.enable');
  return { ws, send, ev, target: t };
}

async function waitFor(conn, expr, timeoutMs, label) {
  const until = Date.now() + timeoutMs;
  let last;
  while (Date.now() < until) {
    try { last = await conn.ev(expr); } catch (e) { last = undefined; }
    if (last) return last;
    await sleep(400);
  }
  throw new Error('timeout waiting for ' + label + ' last=' + JSON.stringify(last));
}

async function clipShot(conn, name, clip) {
  const r = await conn.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false, clip: Object.assign({ scale: 1 }, clip) });
  const file = join(OUT, name);
  writeFileSync(file, Buffer.from(r.data, 'base64'));
  return file;
}

const main = async () => {
  const report = {};
  // 先清掉上一轮留下的页面，避免消息发给同名旧标签页
  try {
    const list = await (await fetch(BASE + '/json/list')).json();
    for (const t of list) {
      if (t.type === 'page' && t.url && t.url.indexOf('about:') !== 0) {
        try { await fetch(BASE + '/json/close/' + t.id); } catch (e) { /* ignore */ }
      }
    }
    await sleep(600);
  } catch (e) { /* ignore */ }

  // 1) 页面大纲侧栏
  const op = await open('http://127.0.0.1:8123/outline.html');
  await op.send('Emulation.setDeviceMetricsOverride', { width: 1024, height: 768, deviceScaleFactor: 1, mobile: false });
  try { await op.send('Emulation.setScrollbarsHidden', { hidden: true }); } catch (e) { /* ignore */ }
  await waitFor(op, `!!document.getElementById('__ip_host')`, 15000, 'outline page content script');
  await sleep(600);
  await op.ev(`(() => { document.getElementById('__ip_host').shadowRoot.querySelectorAll('.mini')[0].click(); return true; })()`);
  await waitFor(op, `document.getElementById('__ip_host').shadowRoot.querySelector('.outline').classList.contains('show')`, 8000, 'outline shown');
  await sleep(500);
  const outlinePanel = await op.ev(`(() => { const o = document.getElementById('__ip_host').shadowRoot.querySelector('.outline'); const r = o.getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height, items: o.querySelectorAll('.ol-item').length, search: !!o.querySelector('.ol-ico svg') }; })()`);
  report.outlinePanel = outlinePanel;
  report.outline = await clipShot(op, 'shot-outline.png', { x: 0, y: 0, width: Math.round(outlinePanel.w) + 18, height: 768 });
  op.ws.close();

  // 2) 本页总结卡片
  const pg = await open('http://127.0.0.1:8123/shot-page.html');
  await pg.send('Emulation.setDeviceMetricsOverride', { width: 1024, height: 768, deviceScaleFactor: 1, mobile: false });
  try { await pg.send('Emulation.setScrollbarsHidden', { hidden: true }); } catch (e) { /* ignore */ }
  await waitFor(pg, `!!document.getElementById('__ip_host')`, 15000, 'summary page content script');
  await sleep(700);
  // 用扩展自己的消息通道触发「本页总结」（和桌面端下发的是同一条路径）
  const opt = await open('chrome-extension://' + EXT_ID + '/options.html');
  await sleep(1200);
  const sent = await opt.ev(`new Promise((resolve) => {
    chrome.tabs.query({}, (tabs) => {
      const tab = tabs.find((t) => t.url && t.url.indexOf('127.0.0.1:8123/shot-page.html') >= 0);
      if (!tab) { resolve({ ok: false, error: 'tab not found' }); return; }
      chrome.tabs.sendMessage(tab.id, { type: 'ip-page-command', command: 'summary:brief' }, (r) => {
        resolve({ ok: true, resp: r, err: chrome.runtime.lastError ? chrome.runtime.lastError.message : null });
      });
    });
  })`);
  report.summarySent = sent;
  opt.ws.close();
  await waitFor(pg, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; const c = sr.querySelector('.card'); return c.classList.contains('show') && sr.querySelectorAll('.card-tools.show').length === 1 && /本页总结/.test(sr.querySelector('.card-title').textContent); })()`, 20000, 'summary card open');
  await waitFor(pg, `(() => { const sr = document.getElementById('__ip_host').shadowRoot; return /·.*\\ds/.test(sr.querySelector('.card-title').textContent) || sr.querySelector('.card-body .card-err'); })()`, 120000, 'summary finished');
  await waitFor(pg, `!document.getElementById('__ip_host').shadowRoot.querySelector('.toast').classList.contains('show')`, 15000, 'toast gone');
  await sleep(500);
  const cardInfo = await pg.ev(`(() => {
    const sr = document.getElementById('__ip_host').shadowRoot;
    const c = sr.querySelector('.card');
    const r = c.getBoundingClientRect();
    return {
      x: r.left, y: r.top, w: r.width, h: r.height,
      title: sr.querySelector('.card-title').textContent,
      chips: sr.querySelectorAll('.chipw').length,
      penIcons: sr.querySelectorAll('.chip-pen svg').length,
      plusIcons: sr.querySelectorAll('.chip-plus svg').length,
      toolsShown: sr.querySelector('.card-tools').classList.contains('show')
    };
  })()`);
  report.card = cardInfo;
  // 藏掉网页正文，卡片（在 host 里）单独出图
  await pg.ev(`(() => { document.body.style.visibility = 'hidden'; return true; })()`);
  await sleep(250);
  report.summary = await clipShot(pg, 'shot-summary.png', {
    x: Math.max(0, Math.round(cardInfo.x) - 10),
    y: Math.max(0, Math.round(cardInfo.y) - 10),
    width: Math.round(cardInfo.w) + 20,
    height: Math.round(cardInfo.h) + 20,
    scale: 1.5,
  });
  await pg.ev(`(() => { document.body.style.visibility = ''; return true; })()`);
  pg.ws.close();

  console.log(JSON.stringify(report, null, 2));
};

main().catch((e) => {
  console.error('EXTRA SHOTS FAILED: ' + (e && e.stack ? e.stack : e));
  process.exitCode = 1;
});
