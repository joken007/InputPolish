async function send(msg) {
  return new Promise((resolve) => {
    chrome.runtime.sendMessage(msg, (res) => {
      if (chrome.runtime.lastError) { resolve({ ok: false, error: chrome.runtime.lastError.message }); return; }
      resolve(res || { ok: false, error: '没有响应' });
    });
  });
}

async function activeTab() {
  const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
  return tabs && tabs[0];
}

// 矢量图标：和网页内悬浮球、桌面端用同一套 path，不再用单个汉字当图标
function decorate() {
  const api = window.IPIcons;
  if (!api) return;
  const logo = document.getElementById('logo');
  if (logo) logo.appendChild(api.make('brand_small', { size: 18 }));
  for (const el of document.querySelectorAll('[data-icon]')) {
    try {
      const svg = api.make(el.getAttribute('data-icon'), { size: 15, strokeScale: 1.05 });
      el.insertBefore(svg, el.firstChild);
    } catch (e) { /* ignore */ }
  }
}

async function refreshStatus() {
  const el = document.getElementById('status');
  const h = await send({ type: 'health', body: {} });
  if (h && h.ok) {
    el.className = 'status ok';
    el.textContent = '已连接：' + h.model + '（端口 ' + h.port + '）';
  } else {
    el.className = 'status bad';
    el.textContent = '未连接：' + ((h && h.error) || '请确认 InputPolish 正在运行');
  }
}

// ---------------- 显示方式：双语对照 / 仅译文 ----------------
let pageMode = 'bil';

function updateModeText() {
  const btn = document.getElementById('mode');
  const txt = document.getElementById('mode-txt');
  if (!btn || !txt) return;
  txt.textContent = '显示方式：' + (pageMode === 'dst' ? '仅译文' : '双语对照');
  btn.title = pageMode === 'dst'
    ? '当前只显示译文，点一下切回双语对照（Alt+D）'
    : '当前为双语对照，点一下切到仅译文（Alt+D）';
}

function refreshMode() {
  // 先按本机保存的偏好显示（没注入内容脚本的页面也能看到当前设置），再问一下当前标签页的真实状态
  chrome.storage.local.get({ pageMode: 'bil' }, (s) => {
    pageMode = s.pageMode === 'dst' ? 'dst' : 'bil';
    updateModeText();
  });
  activeTab().then((tab) => {
    if (!tab) return;
    chrome.tabs.sendMessage(tab.id, { type: 'ip-page-command', command: 'status' }, (res) => {
      if (chrome.runtime.lastError || !res || !res.ok) return;
      pageMode = res.mode === 'dst' ? 'dst' : 'bil';
      updateModeText();
    });
  });
}

document.getElementById('mode').addEventListener('click', async () => {
  const tab = await activeTab();
  if (!tab) return;
  const next = pageMode === 'dst' ? 'bil' : 'dst';
  chrome.tabs.sendMessage(tab.id, { type: 'ip-page-command', command: 'mode:' + next }, (res) => {
    if (chrome.runtime.lastError) {
      const el = document.getElementById('status');
      el.className = 'status bad';
      el.textContent = '这个页面不能注入（可能是浏览器内置页）';
      return;
    }
    if (res && res.ok) {
      pageMode = res.mode === 'dst' ? 'dst' : 'bil';
      updateModeText();
    }
  });
});

document.getElementById('translate').addEventListener('click', async () => {
  const tab = await activeTab();
  if (!tab) return;
  chrome.tabs.sendMessage(tab.id, { type: 'ip-page-command', command: 'translate' }, () => {
    if (chrome.runtime.lastError) {
      document.getElementById('status').textContent = '这个页面不能注入（可能是浏览器内置页）';
    } else {
      window.close();
    }
  });
});

document.getElementById('restore').addEventListener('click', async () => {
  const tab = await activeTab();
  if (!tab) return;
  chrome.tabs.sendMessage(tab.id, { type: 'ip-page-command', command: 'restore' }, () => window.close());
});

function pageCommand(cmd, closeAfter) {
  return async () => {
    const tab = await activeTab();
    if (!tab) return;
    chrome.tabs.sendMessage(tab.id, { type: 'ip-page-command', command: cmd }, () => {
      if (chrome.runtime.lastError) {
        const el = document.getElementById('status');
        el.className = 'status bad';
        el.textContent = '这个页面不能注入（可能是浏览器内置页）';
      } else if (closeAfter !== false) {
        window.close();
      }
    });
  };
}

document.getElementById('outline').addEventListener('click', pageCommand('outline'));
document.getElementById('export').addEventListener('click', pageCommand('export'));

document.getElementById('options').addEventListener('click', () => {
  chrome.runtime.openOptionsPage();
});

decorate();
updateModeText();
refreshMode();
refreshStatus();
