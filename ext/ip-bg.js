// InputPolish 网页内翻译 —— 后台路由：内容脚本不直接访问本机接口，统一走这里。
// build: 2026-10-29 lp1
try { globalThis.__ipbg = 'lp1'; } catch (e) {}
const DEFAULTS = {
  port: 8790,
  token: '',
  target: 'auto',
  showBall: true,
  fontSize: '0.95',
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

// ---------------- 自动配对：从本机 InputPolish 拿端口和连接码 ----------------
// 首选读扩展目录里的 _pair.json（InputPolish 每次启动都会写）：
// 这个文件只有本机文件权限和扩展自己能读，网页伪造不出来，也不用用户手填连接码。
async function pairFromFile() {
  try {
    const url = chrome.runtime.getURL('_pair.json');
    const res = await fetch(url + '?t=' + Date.now(), { cache: 'no-store' });
    if (!res.ok) return { ok: false, error: 'pair file HTTP ' + res.status };
    const d = await res.json();
    if (!d || !d.token) return { ok: false, error: 'pair file 里没有连接码' };
    await chrome.storage.local.set({ port: d.port || 8790, token: d.token });
    return { ok: true, port: d.port || 8790, version: d.version || '' };
  } catch (e) {
    return { ok: false, error: '没有读到 _pair.json（' + (e && e.message ? e.message : e) + '）' };
  }
}

async function autoPair() {
  const fromFile = await pairFromFile();
  if (fromFile.ok) return fromFile;
  // 不再走 HTTP 扫描：service worker 的 fetch 不带 Origin，拿不到安全校验，
  // 开一个谁都能读的连接码接口风险更大。读不到文件就提示用户手动填。
  return {
    ok: false,
    error: '没读到 _pair.json（' + (fromFile.error || '') + '）。请先启动 InputPolish，再点自动连接；或把设置里的连接码手动填进来'
  };
}

// ---------------- 长轮询：桌面悬浮球 / 快捷键下发的命令 ----------------
const poll = { running: false, connected: false, lastError: '' };

async function sendCommandToActiveTab(command) {
  try {
    const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    const tab = tabs && tabs[0];
    if (!tab || !tab.id) return;
    chrome.tabs.sendMessage(tab.id, { type: 'ip-page-command', command: command, fromHost: true }, () => {
      void chrome.runtime.lastError;
    });
  } catch (e) {
    poll.lastError = String(e && e.message ? e.message : e);
  }
}

async function pollLoop() {
  if (poll.running) return;
  poll.running = true;
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
        // 连接码被重置过（换端口、重装 exe、手动改过设置）时清掉旧的，下一轮从 _pair.json 重新配对。
        await chrome.storage.local.set({ token: '' });
        await new Promise((x) => setTimeout(x, 2000));
        continue;
      }
      res = await r.json();
      poll.connected = !!res && res.ok === true;
    } catch (e) {
      poll.connected = false;
      poll.lastError = String(e && e.message ? e.message : e);
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
      if (msg.type === 'health') { sendResponse(await callHost('/health', {})); return; }
      if (msg.type === 'pair') { sendResponse(await autoPair()); return; }
      if (msg.type === 'pollstate') {
        sendResponse({ ok: true, connected: poll.connected, error: poll.lastError });
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
