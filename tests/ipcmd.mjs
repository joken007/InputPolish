// ipcmd.mjs — 直接连扩展 service worker，把命令发给当前活动标签（测试用，绕开宿主轮询的竞争）
// 用法: node ipcmd.mjs <command>
const CDP = process.env.CDP || 'http://127.0.0.1:9337';
const cmd = process.argv[2] || 'summary';

const list = await (await fetch(CDP + '/json/list')).json();
const sw = list.find(t => t.type === 'service_worker' && t.url.indexOf('ip-bg.js') >= 0);
if (!sw) { console.log('no service worker'); process.exit(2); }
const ws = new WebSocket(sw.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r, { once: true }));
let id = 0;
const pending = new Map();
ws.addEventListener('message', ev => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
});
function send(method, params) {
  const mid = ++id;
  return new Promise(res => { pending.set(mid, res); ws.send(JSON.stringify({ id: mid, method, params: params || {} })); });
}
await send('Runtime.enable');
const expr = `(async () => {
  const tabs = await chrome.tabs.query({});
  const t = tabs.find(x => x.url && (x.url.indexOf('inpage') >= 0 || x.url.indexOf('file://') === 0)) || tabs[0];
  try {
    const r = await chrome.tabs.sendMessage(t.id, { type: 'ip-page-command', command: ${JSON.stringify(cmd)}, fromHost: true });
    return JSON.stringify({ ok: true, tab: t.id, url: (t.url || '').slice(0, 60), resp: r });
  } catch (e) { return JSON.stringify({ ok: false, tab: t.id, err: String(e) }); }
})()`;
const res = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
console.log(JSON.stringify(res.result && res.result.result && res.result.result.value));
ws.close();
process.exit(0);
