import { writeFileSync, readFileSync } from 'node:fs';

const BASE = process.env.CDP || 'http://127.0.0.1:9333';

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
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = (e) => rej(new Error('ws error')); });
  let id = 0;
  const pending = new Map();
  const api = { onEvent: null };
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (!m.id && m.method && api.onEvent) api.onEvent(m.method, m.params);
    if (m.id && pending.has(m.id)) {
      const { res, rej } = pending.get(m.id);
      pending.delete(m.id);
      if (m.error) rej(new Error(JSON.stringify(m.error))); else res(m.result);
    }
  };
  return {
    get onEvent() { return api.onEvent; },
    set onEvent(fn) { api.onEvent = fn; },
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

function pick(list, frag) {
  if (!frag) return list.find((t) => t.type === 'page');
  const byFrag = (t) => t.url.includes(frag) || (t.title || '').includes(frag);
  return list.find((t) => t.type === 'page' && byFrag(t)) || list.find(byFrag);
}

const [cmd, ...rest] = process.argv.slice(2);

if (cmd === 'list') {
  const list = await targets();
  for (const t of list) console.log([t.type, t.id, t.url, t.title].join(' | '));
} else if (cmd === 'open') {
  const t = await newTarget(rest[0]);
  console.log('opened ' + t.id + ' ' + t.url);
} else if (cmd === 'shot') {
  const list = await targets();
  const t = pick(list, rest[0]);
  if (!t) { console.log('target not found'); process.exit(2); }
  const c = await connect(t.webSocketDebuggerUrl);
  const res = await c.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  writeFileSync(rest[1], Buffer.from(res.data, 'base64'));
  console.log('saved ' + rest[1] + ' target=' + t.url);
  c.close();
} else if (cmd === 'eval') {
  const list = await targets();
  const t = pick(list, rest[0]);
  if (!t) { console.log('target not found'); process.exit(2); }
  const c = await connect(t.webSocketDebuggerUrl);
  const res = await c.send('Runtime.evaluate', { expression: rest[1], returnByValue: true, awaitPromise: true });
  console.log(JSON.stringify(res.result.value === undefined ? res : res.result, null, 2).slice(0, 4000));
  c.close();
} else if (cmd === 'evalfile') {
  const list = await targets();
  const t = pick(list, rest[0]);
  if (!t) { console.log('target not found'); process.exit(2); }
  const exprText = readFileSync(rest[1], 'utf8');
  const c = await connect(t.webSocketDebuggerUrl);
  const res = await c.send('Runtime.evaluate', { expression: exprText, returnByValue: true, awaitPromise: true });
  const payload = res.exceptionDetails ? { exception: res.exceptionDetails.text, detail: res.exceptionDetails.exception && res.exceptionDetails.exception.description } : (res.result.value === undefined ? res.result : res.result.value);
  console.log(typeof payload === 'string' ? payload : JSON.stringify(payload, null, 2).slice(0, 6000));
  c.close();
} else if (cmd === 'evalcs') {
  // evalcs <pageFrag> <originFrag> <file>  —— 按扩展 origin 片段找隔离世界上下文，避免 id 漂移
  const list = await targets();
  const t = pick(list, rest[0]);
  if (!t) { console.log('target not found'); process.exit(2); }
  const c = await connect(t.webSocketDebuggerUrl);
  const contexts = [];
  c.onEvent = (method, params) => {
    if (method === 'Runtime.executionContextCreated') contexts.push(params.context);
  };
  await c.send('Runtime.enable', {});
  await new Promise((r) => setTimeout(r, 900));
  const want = rest[1];
  const ctx = contexts.find((x) => (x.name || '').includes(want))
    || contexts.find((x) => (x.origin || '').includes(want) && x.auxData && x.auxData.type === 'isolated')
    || contexts.find((x) => (x.origin || '').includes(want));
  if (!ctx) { console.log('context not found for ' + want + ' have=' + contexts.map((x) => x.origin + '#' + x.id).join(',')); c.close(); process.exit(3); }
  const exprText = readFileSync(rest[2], 'utf8');
  const res = await c.send('Runtime.evaluate', { expression: exprText, contextId: ctx.id, returnByValue: true, awaitPromise: true });
  const payload = res.exceptionDetails ? { exception: res.exceptionDetails.text, detail: res.exceptionDetails.exception && res.exceptionDetails.exception.description } : (res.result.value === undefined ? res.result : res.result.value);
  console.log(typeof payload === 'string' ? payload : JSON.stringify(payload, null, 2).slice(0, 6000));
  c.close();
} else if (cmd === 'ctxs' || (cmd === 'evalctx')) {
  const list = await targets();
  const t = pick(list, rest[0]);
  if (!t) { console.log('target not found'); process.exit(2); }
  const c = await connect(t.webSocketDebuggerUrl);
  const contexts = [];
  c.onEvent = (method, params) => {
    if (method === 'Runtime.executionContextCreated') contexts.push(params.context);
  };
  await c.send('Runtime.enable');
  await new Promise((r) => setTimeout(r, 700));
  if (cmd === 'ctxs') {
    console.log(JSON.stringify(contexts.map((x) => ({ id: x.id, origin: x.origin, name: x.name, aux: x.auxData })), null, 1));
  } else {
    const ctxId = Number(rest[1]);
    const exprText = readFileSync(rest[2], 'utf8');
    const res = await c.send('Runtime.evaluate', { expression: exprText, contextId: ctxId, returnByValue: true, awaitPromise: true });
    const payload = res.exceptionDetails ? { exception: res.exceptionDetails.text, detail: res.exceptionDetails.exception && res.exceptionDetails.exception.description } : (res.result.value === undefined ? res.result : res.result.value);
    console.log(typeof payload === 'string' ? payload : JSON.stringify(payload, null, 2).slice(0, 6000));
  }
  c.close();
} else if (cmd === 'nav') {
  const list = await targets();
  const t = pick(list, rest[0]);
  if (!t) { console.log('target not found'); process.exit(2); }
  const c = await connect(t.webSocketDebuggerUrl);
  await c.send('Page.navigate', { url: rest[1] });
  console.log('navigated ' + rest[1]);
  c.close();
} else {
  console.log('usage: list | open <url> | shot <frag> <png> | eval <frag> <expr> | nav <frag> <url>');
}
