// pedit-test2.mjs — 验证：内置项不给删、自定义项可删（两段式）
const CDP = process.env.CDP || 'http://127.0.0.1:9337';
const list = await (await fetch(CDP + '/json/list')).json();
const page = list.find(t => t.type === 'page' && t.url.indexOf('inpage') >= 0);
if (!page) { console.log('no page'); process.exit(2); }
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r, { once: true }));
let id = 0; const pending = new Map();
ws.addEventListener('message', ev => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } });
function send(method, params) { const mid = ++id; return new Promise(res => { pending.set(mid, res); ws.send(JSON.stringify({ id: mid, method, params: params || {} })); }); }
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function ev(expr) { const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true }); return r.result && r.result.result && r.result.result.value; }
const SR = "document.querySelector('#__ip_host').shadowRoot";
async function state(tag) {
  const s = await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
    const vis = [...p.querySelectorAll('.pedit-btn')].filter(b => b.style.display !== 'none').map(b => b.textContent);
    return JSON.stringify({ tag: '${tag}', title: p.querySelector('b').textContent, badge: p.querySelector('.pedit-h span').textContent,
      name: p.querySelector('input[type=text]').value, chips: sr.querySelectorAll('.chipw').length,
      visibleBtns: vis, labels: [...sr.querySelectorAll('.chip-lb')].map(x => x.textContent) }); })()`);
  console.log(s); return s;
}
console.log('=== A. 内置项：删除按钮应隐藏，恢复默认可见 ===');
await ev(`(() => { const sr = ${SR}; const b = sr.querySelector('.ball'); if (b) b.click(); return 1; })()`);
await sleep(400);
await ev(`(() => { const sr = ${SR}; sr.querySelectorAll('.chipw')[0].querySelector('.chip-pen').click(); return 1; })()`);
await sleep(1000);
await state('builtin');

console.log('=== B. 新建自定义项并保存 ===');
await ev(`(() => { const sr = ${SR}; sr.querySelector('.pedit .pedit-btn:last-child').click(); return 1; })()`); // 取消
await sleep(300);
await ev(`(() => { const sr = ${SR}; sr.querySelector('.chips-row > .chip-plus').click(); return 1; })()`);
await sleep(700);
await state('new');
await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
  p.querySelector('input[type=text]').value = '表格抽取';
  p.querySelector('textarea').value = '把这一页里的表格整理成 Markdown：保留表头与全部数据行，无法确定的值写「—」，不要额外解释。';
  p.querySelector('input[type=number]').value = '1000';
  [...p.querySelectorAll('.pedit-btn')].find(b => b.textContent === '保存').click(); return 1; })()`);
await sleep(2000);
await state('saved');

console.log('=== C. 打开刚建的自定义项：删除应可见 ===');
await ev(`(() => { const sr = ${SR}; const ws = sr.querySelectorAll('.chipw'); ws[ws.length - 1].querySelector('.chip-pen').click(); return 1; })()`);
await sleep(900);
await state('custom');

console.log('=== D. 两段式删除 ===');
await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
  [...p.querySelectorAll('.pedit-btn')].find(x => x.textContent === '删除').click(); return 1; })()`);
await sleep(500);
await state('armed');
await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
  const b = [...p.querySelectorAll('.pedit-btn')].find(x => x.textContent === '确认删除'); if (!b) return 'no-confirm'; b.click(); return 1; })()`);
await sleep(1800);
await state('deleted');
ws.close(); process.exit(0);
