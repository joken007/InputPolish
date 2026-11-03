// InputPolish 网页内翻译 —— 后台路由：内容脚本不直接访问本机接口，统一走这里。
// build: 2026-10-29 lp1
try { globalThis.__ipbg = 'lp1'; } catch (e) {}
const DEFAULTS = {
  port: 8790,
  token: '',
  target: 'auto',
  showBall: true,
  fontSize: '0.95',
  pageMode: 'bil',   // 'bil' 双语对照 / 'dst' 仅译文
};

async function getSettings() {
  const stored = await chrome.storage.local.get(DEFAULTS);
  return Object.assign({}, DEFAULTS, stored);
}

async function callHost(path, body) {
  const cfg = await getSettings();
  const url = 'http://127.0.0.1:' + cfg.port + path;
  let res;
  try {
    res = await fetch(url, {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'x-ip-token': cfg.token || '' },
      body: JSON.stringify(body || {}),
    });
  } catch (e) {
    return { ok: false, error: '连不上本机 InputPolish（' + (e && e.message ? e.message : e) + '），请确认它正在运行且端口是 ' + cfg.port };
  }
  let data = null;
  try { data = await res.json(); } catch (e) { data = null; }
  if (!data) {
    return { ok: false, error: '本机接口返回了非 JSON 内容（HTTP ' + res.status + '）' };
  }
  if (!res.ok) {
    return { ok: false, error: data.error || ('HTTP ' + res.status) };
  }
  return data;
}

async function callHostGet(path) {
  const cfg = await getSettings();
  const url = 'http://127.0.0.1:' + cfg.port + path;
  let res;
  try {
    res = await fetch(url, { cache: 'no-store', headers: { 'x-ip-token': cfg.token || '' } });
  } catch (e) {
    return { ok: false, error: '连不上本机 InputPolish（' + (e && e.message ? e.message : e) + '）' };
  }
  let data = null;
  try { data = await res.json(); } catch (e) { data = null; }
  if (!data) return { ok: false, error: '本机接口返回了非 JSON 内容（HTTP ' + res.status + '）' };
  if (!res.ok) return { ok: false, error: data.error || ('HTTP ' + res.status) };
  return data;
}

// ---------------- 自动配对：从本机 InputPolish 拿端口和连接码 ----------------
// 首选读扩展目录里的 ip-pair.json（InputPolish 每次启动都会写）：
// 这个文件只有本机文件权限和扩展自己能读，网页伪造不出来，也不用用户手填连接码。
async function pairFromFile() {
  try {
    const url = chrome.runtime.getURL('ip-pair.json');
    const res = await fetch(url + '?t=' + Date.now(), { cache: 'no-store' });
    if (!res.ok) return { ok: false, error: 'pair file HTTP ' + res.status };
    const d = await res.json();
    if (!d || !d.token) return { ok: false, error: 'pair file 里没有连接码' };
    await chrome.storage.local.set({ port: d.port || 8790, token: d.token });
    return { ok: true, port: d.port || 8790, version: d.version || '' };
  } catch (e) {
    return { ok: false, error: '没有读到 ip-pair.json（' + (e && e.message ? e.message : e) + '）' };
  }
}

async function autoPair() {
  const fromFile = await pairFromFile();
  if (fromFile.ok) return fromFile;
  // 不再走 HTTP 扫描：service worker 的 fetch 不带 Origin，拿不到安全校验，
  // 开一个谁都能读的连接码接口风险更大。读不到文件就提示用户手动填。
  return {
    ok: false,
    error: '没读到 ip-pair.json（' + (fromFile.error || '') + '）。请先启动 InputPolish，再点自动连接；或把设置里的连接码手动填进来'
  };
}

// ---------------- 长轮询：桌面悬浮球 / 快捷键下发的命令 ----------------
const poll = { running: false, connected: false, lastError: '', lastCmd: '', lastCmdAt: 0 };

// 找「当前活动标签」：面板/悬浮球抢过焦点时 lastFocusedWindow 可能查不到东西，
// 所以逐级放宽；最后兜底在全部标签里挑一个 http/file 页面。
async function pickTab() {
  let tabs = [];
  try { tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true }); } catch (e) { tabs = []; }
  if (!tabs || !tabs.length) {
    try { tabs = await chrome.tabs.query({ active: true }); } catch (e) { tabs = []; }
  }
  if (tabs && tabs.length) return tabs[0];
  try {
    const all = await chrome.tabs.query({});
    for (const t of all) {
      if (!t || !t.id) continue;
      if (t.url && (t.url.indexOf('http') === 0 || t.url.indexOf('file') === 0)) return t;
    }
  } catch (e) { /* ignore */ }
  return null;
}

function sendToTab(tabId, command) {
  return new Promise((resolve) => {
    try {
      chrome.tabs.sendMessage(tabId, { type: 'ip-page-command', command: command, fromHost: true }, (resp) => {
        const err = chrome.runtime.lastError;
        resolve({ ok: !err, resp: resp === undefined ? null : resp, error: err ? String(err.message || err) : '' });
      });
    } catch (e) {
      resolve({ ok: false, resp: null, error: String(e && e.message ? e.message : e) });
    }
  });
}

async function sendCommandToActiveTab(command) {
  for (let attempt = 0; attempt < 4; attempt++) {
    const tab = await pickTab();
    if (!tab || !tab.id) {
      poll.lastError = '找不到可用的标签页';
      await new Promise((r) => setTimeout(r, 400));
      continue;
    }
    const out = await sendToTab(tab.id, command);
    poll.lastCmd = command;
    poll.lastCmdAt = Date.now();
    if (out.ok) { poll.lastError = ''; return; }
    poll.lastError = '转发到标签失败：' + out.error;
    // 内容脚本可能还没注入（刚打开的页面），等一会再试
    await new Promise((r) => setTimeout(r, 500));
  }
}

async function pollLoop() {
  if (poll.running) return;
  poll.running = true;
  let netFails = 0;
  while (true) {
    let cfg = await getSettings();
    if (!cfg.token) {
      const paired = await autoPair();
      poll.connected = false;
      if (!paired.ok) poll.lastError = paired.error;
      cfg = await getSettings();
      if (!cfg.token) {
        await new Promise((r) => setTimeout(r, 4000));
        continue;
      }
    }
    let res = null;
    try {
      const url = 'http://127.0.0.1:' + cfg.port + '/poll?token=' + encodeURIComponent(cfg.token) + '&wait=25';
      const r = await fetch(url, { cache: 'no-store' });
      if (r.status === 401) {
        poll.connected = false;
        poll.lastError = '连接码不对';
        // 连接码被重置过（换端口、重装 exe、手动改过设置）时清掉旧的，下一轮从 ip-pair.json 重新配对。
        await chrome.storage.local.set({ token: '' });
        await new Promise((x) => setTimeout(x, 2000));
        continue;
      }
      res = await r.json();
      poll.connected = !!res && res.ok === true;
      netFails = 0;
    } catch (e) {
      poll.connected = false;
      poll.lastError = String(e && e.message ? e.message : e);
      netFails++;
      // 连续连不上（宿主没开、换了端口、重装了 exe）时清掉缓存的连接码，
      // 下一轮会重新读 ip-pair.json —— 否则会一直朝着一个已经不存在的端口重试到浏览器重启。
      if (netFails >= 3) {
        netFails = 0;
        await chrome.storage.local.set({ token: '' });
      }
      await new Promise((x) => setTimeout(x, 3000));
      continue;
    }
    const cmd = res && res.command ? res.command : 'idle';
    if (cmd && cmd !== 'idle') {
      await sendCommandToActiveTab(cmd);
    }
    await new Promise((x) => setTimeout(x, 120));
  }
}

chrome.runtime.onStartup.addListener(() => { pollLoop(); });
chrome.runtime.onInstalled.addListener(() => { pollLoop(); });
pollLoop();

chrome.runtime.onMessage.addListener(function (msg, sender, sendResponse) {
  (async function () {
    try {
      if (!msg || !msg.type) { sendResponse({ ok: false, error: 'bad message' }); return; }
      if (msg.type === 'settings') { sendResponse({ ok: true, settings: await getSettings() }); return; }
      if (msg.type === 'translate') { sendResponse(await callHost('/translate', msg.body)); return; }
      if (msg.type === 'selection') { sendResponse(await callHost('/selection', msg.body)); return; }
      if (msg.type === 'summary') { sendResponse(await callHost('/summary', msg.body)); return; }
      if (msg.type === 'ask') { sendResponse(await callHost('/ask', msg.body)); return; }
      if (msg.type === 'note') { sendResponse(await callHost('/note', msg.body)); return; }
      if (msg.type === 'export') { sendResponse(await callHost('/export', msg.body)); return; }
      if (msg.type === 'run') { sendResponse(await callHost('/run', msg.body)); return; }
      if (msg.type === 'modes') { sendResponse(await callHost('/modes', {})); return; }
      if (msg.type === 'prompts') { sendResponse(await callHost('/prompts', msg.body || {})); return; }
      if (msg.type === 'promptsList') { sendResponse(await callHostGet('/prompts')); return; }
      if (msg.type === 'promptOne') {
        const id = msg.body && msg.body.id ? msg.body.id : '';
        if (!id) { sendResponse({ ok: false, error: '没有指定提示词' }); return; }
        sendResponse(await callHostGet('/prompts?id=' + encodeURIComponent(id))); return;
      }
      if (msg.type === 'promptDefault') {
        const id = msg.body && msg.body.id ? msg.body.id : '';
        sendResponse(await callHostGet('/prompts?default=1&id=' + encodeURIComponent(id))); return;
      }
      if (msg.type === 'health') { sendResponse(await callHost('/health', {})); return; }
      if (msg.type === 'pair') { sendResponse(await autoPair()); return; }
      if (msg.type === 'pollstate') {
        sendResponse({ ok: true, connected: poll.connected, error: poll.lastError, lastCmd: poll.lastCmd, lastCmdAgo: poll.lastCmdAt ? Date.now() - poll.lastCmdAt : -1 });
        return;
      }
      if (msg.type === 'openOptions') { chrome.runtime.openOptionsPage(); sendResponse({ ok: true }); return; }
      sendResponse({ ok: false, error: 'unknown message ' + msg.type });
    } catch (e) {
      sendResponse({ ok: false, error: String(e && e.message ? e.message : e) });
    }
  })();
  return true;
});
