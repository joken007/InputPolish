const DEFAULTS = { port: 8790, token: '', target: 'auto', showBall: true, fontSize: '0.95', pageMode: 'bil' };

// 矢量图标：和网页悬浮球、桌面端同一套 path（ip-icons.js 先加载）
function decorate() {
  const api = window.IPIcons;
  if (!api) return;
  const logo = document.getElementById('logo');
  if (logo) logo.appendChild(api.make('brand_small', { size: 20 }));
  for (const el of document.querySelectorAll('[data-icon]')) {
    try {
      const svg = api.make(el.getAttribute('data-icon'), { size: 15, strokeScale: 1.05 });
      el.insertBefore(svg, el.firstChild);
    } catch (e) { /* ignore */ }
  }
}

async function load() {
  const s = await chrome.storage.local.get(DEFAULTS);
  document.getElementById('port').value = s.port;
  document.getElementById('token').value = s.token;
  document.getElementById('target').value = s.target;
  document.getElementById('fontSize').value = s.fontSize;
  document.getElementById('pageMode').value = s.pageMode === 'dst' ? 'dst' : 'bil';
  document.getElementById('showBall').checked = s.showBall !== false;
}

function status(text, bad) {
  const el = document.getElementById('status');
  el.textContent = text;
  el.className = 'status ' + (bad ? 'bad' : 'ok');
}

document.getElementById('save').addEventListener('click', async () => {
  const port = parseInt(document.getElementById('port').value, 10) || 8790;
  const token = document.getElementById('token').value.trim();
  const target = document.getElementById('target').value;
  const fontSize = document.getElementById('fontSize').value;
  const showBall = document.getElementById('showBall').checked;
  const pageMode = document.getElementById('pageMode').value === 'dst' ? 'dst' : 'bil';
  await chrome.storage.local.set({ port, token, target, fontSize, showBall, pageMode });
  status('已保存', false);
  setTimeout(() => { document.getElementById('status').textContent = ''; }, 1800);
});

document.getElementById('pair').addEventListener('click', () => {
  status('正在找本机 InputPolish…', false);
  chrome.runtime.sendMessage({ type: 'pair' }, async (res) => {
    if (chrome.runtime.lastError) { status('后台没有响应：' + chrome.runtime.lastError.message, true); return; }
    if (!res || !res.ok) { status((res && res.error) || '没找到，请先启动 InputPolish', true); return; }
    await load();
    status('已自动连接（端口 ' + res.port + '），正在验证…', false);
    chrome.runtime.sendMessage({ type: 'health', body: {} }, (h) => {
      if (h && h.ok) status('连接成功：' + h.model + '（端口 ' + h.port + '）', false);
      else status('已拿到连接码，但接口没响应：' + ((h && h.error) || ''), true);
    });
  });
});

document.getElementById('test').addEventListener('click', async () => {
  const port = parseInt(document.getElementById('port').value, 10) || 8790;
  const token = document.getElementById('token').value.trim();
  await chrome.storage.local.set({ port, token });
  status('测试中…', false);
  chrome.runtime.sendMessage({ type: 'health', body: {} }, (res) => {
    if (chrome.runtime.lastError) { status('后台没有响应：' + chrome.runtime.lastError.message, true); return; }
    if (res && res.ok) status('连接成功：' + res.model + '（端口 ' + res.port + '）', false);
    else status('连接失败：' + ((res && res.error) || '未知错误'), true);
  });
});

decorate();
load();
