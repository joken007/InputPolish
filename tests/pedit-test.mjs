// pedit-test.mjs — 网页内提示词编辑框全流程测试（加号新建 / 铅笔编辑 / 恢复默认 / 两段式删除）
const CDP = process.env.CDP || 'http://127.0.0.1:9337';
const list = await (await fetch(CDP + '/json/list')).json();
const page = list.find(t => t.type === 'page' && t.url.indexOf('inpage') >= 0);
if (!page) { console.log('no page'); process.exit(2); }
const ws = new WebSocket(page.webSocketDebuggerUrl);
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
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function ev(expr, awaitPromise) {
  const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: !!awaitPromise, returnByValue: true });
  if (r.result && r.result.exceptionDetails) return { err: r.result.exceptionDetails.text + ' ' + JSON.stringify(r.result.exceptionDetails.exception && r.result.exceptionDetails.exception.description || '') };
  return r.result && r.result.result && r.result.result.value;
}
const SR = "document.querySelector('#__ip_host').shadowRoot";
async function state(tag) {
  const s = await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
    const btns = [...p.querySelectorAll('.pedit-btn')].map(b => b.textContent + (b.classList.contains('danger') ? '(danger)' : '') + (b.style.display === 'none' ? '(hidden)' : ''));
    return JSON.stringify({ tag: '${tag}', cls: p.className, title: p.querySelector('b').textContent, badge: p.querySelector('.pedit-h span').textContent,
      name: p.querySelector('input[type=text]').value, bodyLen: p.querySelector('textarea').value.length, tok: p.querySelector('input[type=number]').value,
      status: p.querySelector('.pedit-st').textContent, btns: btns, chips: sr.querySelectorAll('.chipw').length,
      labels: [...sr.querySelectorAll('.chip-lb')].map(x => x.textContent) }); })()`);
  console.log(s);
  return s;
}

console.log('=== 1. 初始状态 ===');
await ev(`(() => { const sr = ${SR}; const b = sr.querySelector('.ball'); if (b) b.click(); return 'ball'; })()`);
await sleep(400);
await state('after-open');

console.log('=== 2. 点加号（新建）===');
console.log(await ev(`(() => { const sr = ${SR}; sr.querySelector('.chips-row > .chip-plus').click(); return 'plus-clicked'; })()`));
await sleep(800);
await state('new');

console.log('=== 3. 填内容并保存 ===');
console.log(await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
  p.querySelector('input[type=text]').value = '英文邮件';
  p.querySelector('textarea').value = '把选中的中文改写成得体、简洁的英文商务邮件：保留全部数字与专有名词，语气礼貌但不啰嗦，只输出邮件正文。';
  p.querySelector('input[type=number]').value = '1200';
  return 'filled'; })()`));
console.log(await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
  const save = [...p.querySelectorAll('.pedit-btn')].find(b => b.textContent === '保存'); save.click(); return 'save-clicked'; })()`));
await sleep(1800);
await state('after-save');

console.log('=== 4. 铅笔打开刚新建的（最后一条）===');
console.log(await ev(`(() => { const sr = ${SR}; const ws = sr.querySelectorAll('.chipw'); const last = ws[ws.length - 1];
  last.querySelector('.chip-pen').click(); return 'pen-' + last.querySelector('.chip-lb').textContent; })()`));
await sleep(900);
await state('edit-new');

console.log('=== 5. 改名保存 ===');
console.log(await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
  p.querySelector('input[type=text]').value = '英文邮件B';
  [...p.querySelectorAll('.pedit-btn')].find(b => b.textContent === '保存').click(); return 'renamed'; })()`));
await sleep(1800);
await state('after-rename');

console.log('=== 6. 打开内置项（第一条）看「恢复默认」是否出现 ===');
console.log(await ev(`(() => { const sr = ${SR}; sr.querySelectorAll('.chipw')[0].querySelector('.chip-pen').click(); return 'pen0'; })()`));
await sleep(900);
await state('edit-builtin');

console.log('=== 7. 改内置项内容并保存，再恢复默认 ===');
console.log(await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
  p.querySelector('textarea').value = '临时改坏的提示词内容，用来验证恢复默认';
  [...p.querySelectorAll('.pedit-btn')].find(b => b.textContent === '保存').click(); return 'saved-broken'; })()`));
await sleep(1600);
console.log(await ev(`(() => { const sr = ${SR}; sr.querySelectorAll('.chipw')[0].querySelector('.chip-pen').click(); return 'reopen0'; })()`));
await sleep(900);
console.log('恢复前 bodyLen=' + await ev(`(() => { const sr = ${SR}; return sr.querySelector('.pedit textarea').value.length; })()`));
console.log(await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
  const b = [...p.querySelectorAll('.pedit-btn')].find(x => x.textContent === '恢复默认'); if (!b) return 'no-reset-btn';
  b.click(); return 'reset-clicked'; })()`));
await sleep(1200);
await state('after-reset');
console.log('恢复后 bodyLen=' + await ev(`(() => { const sr = ${SR}; return sr.querySelector('.pedit textarea').value.length; })()`));

console.log('=== 8. 两段式删除（先点删除，应变成确认删除；再点才删）===');
console.log(await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
  const b = [...p.querySelectorAll('.pedit-btn')].find(x => x.textContent === '删除'); if (!b) return 'no-del-btn'; b.click(); return 'del-1'; })()`));
await sleep(500);
await state('armed');
console.log(await ev(`(() => { const sr = ${SR}; const p = sr.querySelector('.pedit');
  const b = [...p.querySelectorAll('.pedit-btn')].find(x => x.textContent === '确认删除'); if (!b) return 'no-confirm-btn'; b.click(); return 'del-2'; })()`));
await sleep(1600);
await state('after-delete');

ws.close();
process.exit(0);
