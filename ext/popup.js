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

refreshStatus();
