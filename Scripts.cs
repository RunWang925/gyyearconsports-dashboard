namespace DashboardKiosk;

/// <summary>注入页面的 JS 片段（返回 JSON 字符串，供日志/诊断用）。</summary>
public static class Scripts
{
    /// <summary>
    /// 注入页面的悬浮控制条（每个文档创建时执行）：
    /// 浅色毛玻璃胶囊条；鼠标移入页面顶部自动弹出（全屏状态停 3 秒防误触），离开后 3 秒收起；
    /// 按钮按需显示；点击通过 WebMessage 通知宿主。
    /// </summary>
    public const string InjectControlBar = @"
(() => {
  const bar = document.createElement('div');
  bar.id = 'kiosk-bar';
  bar.style.cssText = [
    'position:fixed', 'top:10px', 'left:50%', 'transform:translateX(-50%)',
    'z-index:2147483646', 'display:none', 'align-items:center', 'gap:4px',
    'padding:6px 10px', 'border-radius:999px',
    'background:rgba(255,255,255,0.88)',
    'backdrop-filter:blur(14px)', '-webkit-backdrop-filter:blur(14px)',
    'box-shadow:0 4px 20px rgba(0,0,0,0.16)',
    'border:1px solid rgba(255,255,255,0.55)',
    'font:13px/1 ""Microsoft YaHei UI"",sans-serif', 'color:#2b3240',
    'user-select:none'
  ].join(';');
  const mk = (action) => {
    const b = document.createElement('div');
    b.style.cssText = [
      'padding:8px 16px', 'border-radius:999px', 'cursor:pointer',
      'color:#2b3240', 'transition:background .15s,color .15s', 'white-space:nowrap'
    ].join(';');
    b.addEventListener('mouseenter', () => { b.style.background = 'rgba(59,130,246,0.14)'; b.style.color = '#1d4ed8'; });
    b.addEventListener('mouseleave', () => { b.style.background = 'transparent'; b.style.color = '#2b3240'; });
    b.addEventListener('click', () => {
      bar.style.display = 'none';
      try { window.chrome.webview.postMessage('kiosk:bar:' + action); } catch (e) {}
    });
    bar.appendChild(b);
    return b;
  };
  const btnToggle = mk('toggle');
  const btnRefresh = mk('refresh');
  const btnLogout = mk('logout');
  const btnExit = mk('exit');

  const st = { fullscreen: false, loggedIn: false, loginPage: false };
  const render = () => {
    btnToggle.textContent = st.fullscreen ? '退出全屏' : '进入全屏';
    btnRefresh.textContent = '刷新页面';
    btnLogout.textContent = '退出登录';
    btnExit.textContent = '退出程序';
    btnLogout.style.display = (st.loggedIn && !st.loginPage) ? '' : 'none';
  };

  // 文档创建时 body 尚不存在，等 DOMContentLoaded 再挂载
  const mount = () => {
    try { (document.body || document.documentElement).appendChild(bar); } catch (e) {}
  };
  if (document.body) mount();
  else document.addEventListener('DOMContentLoaded', mount);

  // 宿主推送状态（更新文案/按钮可见性；forceHide 时收起）
  try {
    window.chrome.webview.addEventListener('message', ev => {
      try {
        const d = ev.data;
        if (!d || d.type !== 'kioskbar') return;
        st.fullscreen = !!d.fullscreen;
        st.loggedIn = !!d.loggedIn;
        st.loginPage = !!d.loginPage;
        if (d.hoverMs) st.hoverMs = d.hoverMs;
        if (d.forceHide) bar.style.display = 'none';
        if (bar.style.display === 'flex') render();
      } catch (e) {}
    });
  } catch (e) {}
  try { window.chrome.webview.postMessage('kiosk:bar:injected'); } catch (e) {}

  // 鼠标顶部热区：弹出（全屏停 3 秒防误触）；离开顶部后 3 秒收起
  let showTimer = null, hideTimer = null;
  document.addEventListener('mousemove', e => {
    if (e.clientY <= 8) {
      if (hideTimer) { clearTimeout(hideTimer); hideTimer = null; }
      if (bar.style.display !== 'flex' && showTimer === null) {
        const delay = st.fullscreen ? (st.hoverMs || 3000) : 0;
        showTimer = setTimeout(() => { showTimer = null; render(); bar.style.display = 'flex'; }, delay);
      }
    } else if (e.clientY > 80) {
      if (showTimer) { clearTimeout(showTimer); showTimer = null; }
      if (bar.style.display === 'flex' && hideTimer === null) {
        hideTimer = setTimeout(() => { hideTimer = null; bar.style.display = 'none'; }, 3000);
      }
    }
  }, { passive: true });
})()";





    /// <summary>权限检测：抓取页面可见文本（前 3000 字），由宿主判断是否为无权限提示页。</summary>
    public const string PermissionCheck = @"
JSON.stringify({
  url: location.href,
  text: document.body ? document.body.innerText.slice(0, 3000) : ''
})";

    public const string ReadyProbe = @"
JSON.stringify({
  ready: document.readyState,
  iframes: document.querySelectorAll('iframe').length,
  textLen: document.body ? document.body.innerText.length : 0,
  painted: (() => {
    let n = 0;
    for (const e of document.querySelectorAll('body *')) {
      const r = e.getBoundingClientRect();
      if (r.width > 200 && r.height > 200) n++;
      if (n > 2) break;
    }
    return n;
  })(),
  isLogin: location.href.indexOf('/login') >= 0
})";

    /// <summary>
    /// 触发帆软门户“全屏”：优先找标签栏的 ⋯ 菜单，再点菜单里的“全屏”；
    /// 若报表工具栏上直接有“全屏”按钮则优先点它。返回 JSON 结果用于日志。
    /// </summary>
    public const string TriggerFullscreen = @"
new Promise(resolve => {
  const log = [];
  const all = () => Array.from(document.querySelectorAll('body *'));
  const vis = el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
  const txt = el => (el.textContent || '').trim();
  const clickEl = el => {
    try {
      el.dispatchEvent(new MouseEvent('mousedown', {bubbles: true}));
      el.dispatchEvent(new MouseEvent('mouseup', {bubbles: true}));
      el.click();
      return true;
    } catch (e) { return false; }
  };
  const MORE_TEXTS = ['⋯', '···', '...', '⋯⋯', '…', '···'];
  const result = {clicked: null, ok: false, steps: log};

  // 1) 报表工具栏直接有“全屏”按钮的情况
  const direct = all().filter(e => e.children.length <= 1 && txt(e) === '全屏' && vis(e));
  log.push('direct-fullscreen-candidates=' + direct.length);
  if (direct.length === 0) {
    // 2) 先打开 ⋯ 菜单
    const more = all().filter(e => e.children.length <= 1 &&
      (MORE_TEXTS.indexOf(txt(e)) >= 0) && vis(e));
    log.push('more-candidates=' + more.length);
    if (more.length > 0) {
      clickEl(more[more.length - 1]);
      log.push('clicked-more');
      setTimeout(() => {
        const items = all().filter(e => e.children.length <= 1 && txt(e) === '全屏' && vis(e));
        log.push('menu-items=' + items.length);
        if (items.length > 0) {
          clickEl(items[items.length - 1]);
          result.ok = true; result.clicked = 'menu-fullscreen';
        }
        setTimeout(() => resolve(JSON.stringify(result)), 800);
      }, 350);
      return;
    }
  } else {
    clickEl(direct[direct.length - 1]);
    result.ok = true; result.clicked = 'direct-fullscreen';
    setTimeout(() => resolve(JSON.stringify(result)), 800);
    return;
  }
  setTimeout(() => resolve(JSON.stringify(result)), 100);
})";

    /// <summary>诊断模式：导出页面结构摘要。</summary>
    public const string ProbeDump = @"
(() => {
  const vis = el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
  const txt = el => (el.textContent || '').trim();
  const sum = el => ({
    tag: el.tagName,
    cls: (el.className && el.className.toString ? el.className.toString() : '').slice(0, 120),
    id: el.id || '',
    text: txt(el).slice(0, 40),
    rect: (r => [Math.round(r.x), Math.round(r.y), Math.round(r.width), Math.round(r.height)])(el.getBoundingClientRect()),
    vis: vis(el)
  });
  const out = {
    url: location.href,
    iframes: Array.from(document.querySelectorAll('iframe')).map(f => ({src: (f.src||'').slice(0,300), rect: (r => [Math.round(r.x), Math.round(r.y), Math.round(r.width), Math.round(r.height)])(f.getBoundingClientRect())})),
    bodyChildren: Array.from(document.body.children).map(sum),
    fullscreenTextEls: Array.from(document.querySelectorAll('body *')).filter(e => e.children.length <= 1 && txt(e) === '全屏').map(sum),
    moreTextEls: Array.from(document.querySelectorAll('body *')).filter(e => e.children.length <= 1 && ['⋯','···','...','…'].indexOf(txt(e)) >= 0).map(sum),
    topRightEls: Array.from(document.querySelectorAll('body *')).filter(e => {
      const r = e.getBoundingClientRect();
      return r.width > 0 && r.height > 0 && r.y < 120 && r.x > window.innerWidth * 0.6;
    }).slice(0, 40).map(sum),
    bottomEls: Array.from(document.querySelectorAll('body *')).filter(e => {
      const r = e.getBoundingClientRect();
      return r.width > 0 && r.height > 0 && r.y + r.height > window.innerHeight - 80;
    }).slice(0, 25).map(sum)
  };
  return JSON.stringify(out);
})()";

    /// <summary>应用兜底 CSS（隐藏门户元素）。</summary>
    public static string ApplyCss(string[] selectors)
    {
        var css = string.Join("\n", selectors.Select(s =>
            s + " { display: none !important; }"));
        return @"
(() => {
  let st = document.getElementById('kiosk-hide-css');
  if (!st) {
    st = document.createElement('style');
    st.id = 'kiosk-hide-css';
    document.head.appendChild(st);
  }
  st.textContent = " + Json(css) + @";
  return 'ok';
})()";
    }

    /// <summary>
    /// 隐藏帆软页面自带的工具条/按钮：
    /// 1) 应用配置里的 CSS 选择器；
    /// 2) 启发式：把只含“全屏/退出全屏/刷新”等按钮的浮动工具条整块隐藏；
    /// 3) 给 body 去掉滚动条，避免出现底部横向滚动条。
    /// </summary>
    public static string HideChrome(string[] selectors)
    {
        var css = string.Join("\n", (selectors ?? Array.Empty<string>()).Select(s => s + " { display: none !important; }"));
        return @"
(() => {
  const extraCss = " + Json(css) + @";
  const hidden = [];

  const applyDoc = (doc) => {
    if (!doc) return;
    try {
      let st = doc.getElementById('kiosk-hide-css');
      if (!st) {
        st = doc.createElement('style');
        st.id = 'kiosk-hide-css';
        (doc.head || doc.documentElement).appendChild(st);
      }
      st.textContent = extraCss + '\nhtml,body{overflow:hidden !important;}';

      const isToolbarText = t => {
        t = (t || '').replace(/\s+/g, '');
        return t === '全屏' || t === '退出全屏' || t === '刷新' || t === '停止' || t === '停止刷新';
      };
      for (const el of Array.from(doc.querySelectorAll('body *'))) {
        if (el.children.length > 1) continue;
        if (!isToolbarText((el.textContent || '').trim())) continue;
        const r = el.getBoundingClientRect();
        if (r.width === 0 || r.height === 0) continue;
        if (!(r.y < 120 || r.x > window.innerWidth * 0.7)) continue;
        let box = el;
        for (let i = 0; i < 4 && box.parentElement; i++) {
          const pr = box.parentElement.getBoundingClientRect();
          if (pr.width > 0 && pr.height > 0 && pr.height < 80 && pr.width < window.innerWidth * 0.6) {
            box = box.parentElement;
          } else break;
        }
        if (box.style) {
          box.style.setProperty('display', 'none', 'important');
          hidden.push(box.tagName + '.' + (box.className || '').toString().slice(0, 60));
        }
      }
    } catch (e) { /* 跨域 iframe 忽略 */ }
  };

  applyDoc(document);
  for (const f of Array.from(document.querySelectorAll('iframe'))) {
    try { applyDoc(f.contentDocument); } catch (e) {}
  }
  return JSON.stringify({ ok: true, hidden: hidden });
})()";
    }

    /// <summary>
    /// 在（被登录窗盖住的）帆软登录页上真实填表并点击“登录”。
    /// 这样沿用帆软自己的登录逻辑（加密/校验/跳转），宿主只负责填值和判断结果。
    /// 执行结果写入 window.__kioskPageLogin：submitted / no-fields / no-button。
    /// </summary>
    public static string PageLogin(string user, string pass) => $@"
(() => {{
  window.__kioskPageLogin = 'pending';
  const wantU = {Json(user)}, wantP = {Json(pass)};
  const setVal = (el, v) => {{
    el.focus();
    el.value = '';
    el.dispatchEvent(new Event('input', {{bubbles: true}}));
    el.value = v;
    el.dispatchEvent(new Event('input', {{bubbles: true}}));
    el.dispatchEvent(new Event('change', {{bubbles: true}}));
  }};
  let rounds = 0;
  const attempt = () => {{
    rounds++;
    const u = document.querySelector('input[placeholder=""用户名""]') ||
              document.querySelector('input[type=""text""]');
    const p = document.querySelector('input[type=""password""]');
    if (!u || !p) {{
      if (rounds < 40) setTimeout(attempt, 250);
      else window.__kioskPageLogin = 'no-fields';
      return;
    }}
    setVal(u, wantU);
    setVal(p, wantP);
    setTimeout(() => {{
      // 校验：页面脚本可能在我们填完后又自动回填旧值（切换账号场景），此时重填
      if (u.value === wantU && p.value === wantP) {{
        let btn = null;
        for (const e of document.querySelectorAll('div,button,a,span')) {{
          if ((e.textContent || '').trim() === '登录' && e.children.length <= 1) btn = e;
        }}
        if (btn) {{ btn.click(); window.__kioskPageLogin = 'submitted'; }}
        else window.__kioskPageLogin = 'no-button';
      }} else if (rounds < 6) {{
        setTimeout(attempt, 250);
      }} else {{
        window.__kioskPageLogin = 'fill-fail';
      }}
    }}, 300);
  }};
  attempt();
  return 'started';
}})()";

    /// <summary>读取页面登录状态：{path, state, err}。</summary>
    public const string PageLoginProbe = @"
(() => {
  let err = '';
  try {
    const t = (document.body ? document.body.innerText : '').slice(0, 4000);
    const keys = ['用户名或密码', '密码错误', '账号或密码错误', '用户不存在', '登录失败', '账号已锁定', '访问被拒绝'];
    for (const k of keys) {
      const i = t.indexOf(k);
      if (i >= 0) {
        err = t.substr(i, 30).split(String.fromCharCode(10))[0];
        break;
      }
    }
  } catch(e) {}
  return JSON.stringify({
    path: location.pathname,
    state: window.__kioskPageLogin || null,
    err: err
  });
})()";

    private static string Json(string s)
        => System.Text.Json.JsonSerializer.Serialize(s);


}
