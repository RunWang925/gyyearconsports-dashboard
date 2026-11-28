using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DashboardKiosk;

public class MainForm : Form
{
    private readonly AppConfig _cfg;
    private readonly string _exeDir;
    private readonly GlobalHooks _hooks;
    private DateTime _lastToggle = DateTime.MinValue;

    private WebView2 _wv = null!;
    private Panel _overlay = null!;
    private Label _overlayLabel = null!;
    private Panel _fxCover = null!; // 全屏/窗口切换过渡遮罩：盖住 WebView2 换尺寸瞬间的黑帧
    private System.Windows.Forms.Timer _fxTimer = null!;
    private System.Windows.Forms.Timer _uiTimer = null!;
    private System.Windows.Forms.Timer _netTimer = null!;
    private KioskSettings _settings;
    /// <summary>本次运行中是否成功登录过（含手动退出账号的情况）。
    /// 用于区分"冷启动首次弹登录窗"和"会话过期/退出账号后的登录窗"：
    /// 自动登录只在冷启动时尝试一次，运行中弹出的登录窗一律等用户手动操作。</summary>
    private bool _loginSucceededOnce;
    private bool _onLoginPage;

    private bool _fullscreen;
    private bool _dashboardSeen;
    private bool _busy; // 防止并发触发全屏流程
    private bool _startupParked; // 启动阶段主窗口停屏幕外，等导航结果决定搬回屏幕 or 弹登录窗

    // 鼠标隐藏
    private Point _lastMouse;
    private DateTime _lastMove = DateTime.Now;
    private bool _cursorHidden;

    // 断网重试
    private int _retryCountdown;

    /// <summary>
    /// 启动阶段先把主窗口停在屏幕外（保持“已显示”状态，OnLoad/WebView2 初始化照常运行），
    /// 等导航完成判断出「登录页 / 大屏」后再由 RevealMainWindow 搬回屏幕。
    /// 不能直接不显示窗口：OnLoad 里做 WebView2 初始化，窗口不显示就完全不初始化。
    /// </summary>
    /// <summary>
    /// 亮出主窗口。两个场景：
    /// 1. 启动停放：搬回正常位置再显示（此前一直"已显示"但在屏幕外）；
    /// 2. 退出账号→重新登录：主窗口被 ShowLoginWindow 隐藏过，必须重新 Show，
    ///    否则全屏保活会用 SWP_SHOWWINDOW 把隐藏窗口弹出来——WebView2 在隐藏期间
    ///    错过了布局/渲染，弹出来就是全黑且无法交互（黑屏卡死的根因）。
    /// </summary>
    private void RevealMainWindow()
    {
        HideCover(); // 保险：别让全屏切换的过渡遮罩盖住回来的大屏
        if (_startupParked)
        {
            _startupParked = false;
            ShowInTaskbar = true;
        }
        // 非全屏尺寸按当前屏幕实时计算（屏幕 80% 居中），换分辨率/换机器都不会出问题
        if (!_fullscreen) Bounds = ComputeWindowedBounds();
        if (!Visible) Show();
    }

    /// <summary>非全屏（窗口模式）的目标区域：主屏幕可用区域的 80%，居中。</summary>
    private static Rectangle ComputeWindowedBounds()
    {
        var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int w = (int)(scr.Width * 0.8), h = (int)(scr.Height * 0.8);
        return new Rectangle(scr.X + (scr.Width - w) / 2, scr.Y + (scr.Height - h) / 2, w, h);
    }

    public MainForm(AppConfig cfg, string exeDir)
    {
        _cfg = cfg;
        _exeDir = exeDir;
        _settings = SettingsStore.Load();
        _hooks = new GlobalHooks();
        _hooks.OnKeyDown = vk => BeginInvoke(() => HandleKey(vk));
        _hooks.OnLButtonDown = (x, y) => BeginInvoke(() => HandleDoubleClick(x, y));

        Text = cfg.Title;
        StartPosition = FormStartPosition.Manual;
        var windowed = ComputeWindowedBounds();
        Size = windowed.Size;
        MinimumSize = new Size(800, 600);
        BackColor = Color.FromArgb(16, 20, 28);

        // 正常显示时该用的位置（退出全屏时用 ComputeWindowedBounds 实时计算）
        Bounds = new Rectangle(-20000, -20000, windowed.Width, windowed.Height);
        ShowInTaskbar = false;
        _startupParked = true;

        BuildUi();
        FormClosed += (_, _) => { _hooks.Dispose(); Watchdog.SignalStop(); RestoreCursor(); };
    }

    private void HandleKey(int vk)
    {
        // 防止按键重复触发导致的"卡顿感"
        var now = DateTime.Now;
        if (vk == 0x1B) // Esc
        {
            // 登录窗自身处理 Esc（关闭 = 退出程序确认），这里不干预
            if (_fullscreen && (now - _lastToggle).TotalMilliseconds > 800)
            {
                _lastToggle = now;
                Logger.Log("Esc 退出全屏");
                ExitFullscreen();
            }
        }
        else if (vk == 0x7A) // F11：切换全屏
        {
            if ((now - _lastToggle).TotalMilliseconds > 800)
            {
                _lastToggle = now;
                ToggleFullscreen();
            }
        }
    }

    private void HandleDoubleClick(int x, int y)
    {
        // 登录页不响应双击全屏（登录页就应该保持窗口状态）
        if (_onLoginPage)
        {
            Logger.Log("登录页忽略双击（不切换全屏）");
            return;
        }
        // 顶部控制条区域内的点击不当作双击（避免点控制条按钮时误触发全屏切换）
        var clientPt = PointToClient(new Point(x, y));
        if (clientPt.Y <= 90) return;

        // 双击任意位置切换全屏
        var now = DateTime.Now;
        if ((now - _lastToggle).TotalMilliseconds > 800)
        {
            _lastToggle = now;
            Logger.Log("双击切换全屏 (当前全屏=" + _fullscreen + ")");
            ToggleFullscreen();
        }
    }

    // ================= UI 构建 =================

    private void BuildUi()
    {
        _wv = new WebView2 { Dock = DockStyle.Fill };

        // 断网遮罩
        _overlay = new Panel { BackColor = Color.FromArgb(235, 16, 20, 28), Dock = DockStyle.Fill, Visible = false };
        _overlayLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gainsboro,
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Regular),
            Text = ""
        };
        _overlay.Controls.Add(_overlayLabel);

        // 全屏/窗口切换过渡遮罩（纯色，盖在 WebView2 之上，切换完自动撤掉）
        _fxCover = new Panel { BackColor = Color.FromArgb(16, 20, 28), Dock = DockStyle.Fill, Visible = false };
        _fxTimer = new System.Windows.Forms.Timer { Interval = 600 };
        _fxTimer.Tick += (_, _) => HideCover();

        Controls.Add(_wv);
        Controls.Add(_overlay); // 最后加入 = 最顶层
        Controls.Add(_fxCover);

        _uiTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _uiTimer.Tick += (_, _) => UiTick();
        _uiTimer.Start();

        _netTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _netTimer.Tick += (_, _) => NetTick();
    }

    // ================= 页面内控制条状态下发 =================

    /// <summary>把控制条的当前状态推送给页面内注入的控制条（文案/按钮可见性）；forceHide 时同时收起控制条。</summary>
    private void SendBarState(bool forceHide = false)
    {
        try
        {
            if (_wv.CoreWebView2 == null) return;
            _wv.CoreWebView2.PostWebMessageAsJson(
                "{\"type\":\"kioskbar\"" +
                (forceHide ? ",\"forceHide\":true" : "") +
                ",\"hoverMs\":" + (Math.Max(1, _cfg.TopHoverSeconds) * 1000) +
                ",\"fullscreen\":" + (_fullscreen ? "true" : "false") +
                ",\"loggedIn\":" + (!_onLoginPage ? "true" : "false") +
                ",\"loginPage\":" + (_onLoginPage ? "true" : "false") + "}");
        }
        catch { }
    }

    private string ModeName() => _cfg.IsWeather ? "天气大屏" : "驾驶舱大屏";
    private string ModeColor() => _cfg.IsWeather ? "#ef9f27" : "#58a0e6";

    private void ShowSettings(Form? owner = null)
    {
        using var dlg = new SettingsDialog(_cfg, _settings);
        // 从置顶的原生登录窗打开时必须同样置顶，否则会被登录窗盖住
        if (owner != null && owner.TopMost) dlg.TopMost = true;
        if (dlg.ShowDialog(owner ?? this) == DialogResult.OK)
        {
            var newCfg = dlg.Result;
            var oldSettings = _settings;
            _settings = dlg.ResultSettings;
            SettingsStore.Save(_settings);
            if (oldSettings.AutoStart != _settings.AutoStart)
                SettingsStore.ApplyAutoStart(_settings.AutoStart);
            if (!_settings.KeepLogin)
            {
                // 关闭保留登录信息：立即清掉 Cookie 与已保存的会话
                DeleteSessionCookies();
                try
                {
                    _ = _wv.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.Cookies);
                    Logger.Log("设置：已关闭保留登录信息并清除 Cookie");
                }
                catch { }
            }
            Logger.Log($"设置已保存: Mode={newCfg.Mode}, AutoStart={_settings.AutoStart}, KeepLogin={_settings.KeepLogin}, AutoLogin={_settings.AutoLogin}");

            // 写回 config.json：模式 + 两个地址 + 代理（只写有变化的字段）
            var fields = new Dictionary<string, object?>();
            if (newCfg.Mode != _cfg.Mode) fields["mode"] = newCfg.Mode;
            if (newCfg.CockpitUrl != _cfg.CockpitUrl) fields["cockpitUrl"] = newCfg.CockpitUrl;
            if (newCfg.WeatherUrl != _cfg.WeatherUrl) fields["weatherUrl"] = newCfg.WeatherUrl;
            if (newCfg.ProxyUrl != _cfg.ProxyUrl) fields["proxyUrl"] = newCfg.ProxyUrl;
            if (newCfg.ProxyEnabled != _cfg.ProxyEnabled) fields["proxyEnabled"] = newCfg.ProxyEnabled;

            if (fields.Count == 0) return;

            if (ConfigUpdater.UpdateFields(AppPaths.DataDir, fields))
            {
                _cfg.Mode = newCfg.Mode;
                _cfg.CockpitUrl = newCfg.CockpitUrl;
                _cfg.WeatherUrl = newCfg.WeatherUrl;
                _cfg.ProxyUrl = newCfg.ProxyUrl;
                _cfg.ProxyEnabled = newCfg.ProxyEnabled;
                _dashboardSeen = false;
                Logger.Log($"大屏配置已变更: Mode={_cfg.Mode}, Url={_cfg.Url}" +
                           (_cfg.ProxyEnabled ? "（代理开启）" : "（直连）"));
                // 登录窗开着时：模式文字/图标立即刷新
                _loginWin?.RefreshMode(_cfg);
                var target = DeriveEntryUrl(_cfg.Url) ?? _cfg.Url;
                try { _wv.CoreWebView2.Navigate(target); } catch { }
            }
            else
            {
                MessageBox.Show("保存配置失败，请查看日志。", "大屏设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    // ================= 启动 WebView2 =================

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            var userDataDir = Path.Combine(AppPaths.DataDir, "UserData");
            Directory.CreateDirectory(userDataDir);

            // 大屏场景内核参数：防止窗口失焦/后台时定时器被降速（保证页面自动刷新、轮播正常）
            var args = "--disable-background-timer-throttling " +
                       "--disable-backgrounding-occluded-windows " +
                       "--disable-renderer-backgrounding";
            if (_cfg.DisableGpu) args += " --disable-gpu";
            if (!string.IsNullOrWhiteSpace(_cfg.ExtraArgs)) args += " " + _cfg.ExtraArgs.Trim();
            Logger.Log("内核参数: " + args);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataDir,
                new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = args });
            await _wv.EnsureCoreWebView2Async(env);

            var s = _wv.CoreWebView2.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreDevToolsEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsBuiltInErrorPageEnabled = true;

            _wv.CoreWebView2.DocumentTitleChanged += (_, _) => Text = _cfg.Title;
            // 每个文档创建时注入悬浮控制条（浅色毛玻璃，随状态显隐）
            // 必须等待注册完成再导航，否则首个页面会漏注入
            await _wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(Scripts.InjectControlBar);
            // 页面内按钮点击消息：控制条按钮
            _wv.CoreWebView2.WebMessageReceived += (_, ev) =>
            {
                try
                {
                    var msg = ev.TryGetWebMessageAsString();
                    switch (msg)
                    {
                        case "kiosk:bar:injected":
                            Logger.Log("页面控制条注入成功");
                            SendBarState();
                            break;
                        case "kiosk:settings":
                            BeginInvoke(() => ShowSettings(_loginWin));
                            break;
                        case "kiosk:bar:toggle":
                            BeginInvoke(() => { ToggleFullscreen(); HideBar(); });
                            break;
                        case "kiosk:bar:refresh":
                            BeginInvoke(() => { try { _wv.CoreWebView2?.Reload(); } catch { } HideBar(); });
                            break;
                        case "kiosk:bar:logout":
                            BeginInvoke(SwitchAccount);
                            break;
                        case "kiosk:bar:exit":
                            BeginInvoke(() =>
                            {
                                HideBar();
                                if (MessageBox.Show("确定退出大屏程序吗？", _cfg.Title,
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                    Close();
                            });
                            break;
                    }
                }
                catch { }
            };
            _wv.CoreWebView2.NewWindowRequested += (_, ev) =>
            {
                ev.Handled = true;
                try { _wv.CoreWebView2.Navigate(ev.Uri); } catch { }
            };
            _wv.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            Logger.Log("WebView2 初始化完成，打开 " + _cfg.Url);

            // 设置里关闭了“保留登录信息”：启动时清掉 Cookie，强制重新登录
            if (!_settings.KeepLogin)
            {
                try
                {
                    await _wv.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.Cookies);
                    Logger.Log("启动：保留登录信息已关闭，已清除 Cookie");
                }
                catch (Exception ex) { Logger.Log("启动清 Cookie 失败: " + ex.Message); }
            }
            else
            {
                // 帆软登录令牌是会话 Cookie，Chromium 重启即丢弃：
                // 启动时先恢复上次保存的 Cookie（登录成功后会自动保存），实现免登录直达大屏
                RestoreSessionCookies();
            }

            var target = _cfg.Url;
            if (_cfg.UseDirectEntry)
            {
                var direct = DeriveEntryUrl(_cfg.Url);
                if (direct != null)
                {
                    target = direct;
                    Logger.Log("使用直连大屏地址: " + direct);
                }
            }
            _wv.CoreWebView2.Navigate(target);
        }
        catch (Exception ex)
        {
            Logger.Log("WebView2 初始化失败: " + ex);
            ShowOverlay("浏览器组件初始化失败：" + ex.Message + "\n\n请确认本机已安装 WebView2 Runtime（Win10/11 一般自带）");
        }
    }

    // ================= 导航 / 页面状态 =================

    /// <summary>
    /// 从门户链接推导大屏直连地址：decision#?activeTab=&lt;guid&gt; → decision/v10/entry/access/&lt;guid&gt;
    /// 直连地址没有目录树、标签栏、页头，天然就是干净的全屏。
    /// </summary>
    public static string? DeriveEntryUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.Contains("/entry/access/", StringComparison.OrdinalIgnoreCase)) return url; // 已经是直连地址
        var m = System.Text.RegularExpressions.Regex.Match(url,
            @"activeTab=([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        if (!m.Success) return null;
        var uri = new Uri(url);
        return $"{uri.Scheme}://{uri.Authority}/webroot/decision/v10/entry/access/{m.Groups[1].Value}";
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            Logger.Log("页面加载失败，进入重试流程");
            RevealMainWindow(); // 必须可见，否则重试提示看不到
            ShowOverlay("网络连接中断或页面加载失败\n\n" + _cfg.RetrySeconds + " 秒后自动重试…");
            return;
        }

        var url = _wv.CoreWebView2.Source ?? "";
        Logger.Log("导航完成: " + url);

        if (url.Contains("/decision/login", StringComparison.OrdinalIgnoreCase))
        {
            // 帆软登录页 = 会话已失效：弹原生登录窗（隐藏网页显示深色背景，登录在其背后完成）
            _dashboardSeen = false;
            _onLoginPage = true;
            SendBarState();
            if (_fullscreen) ExitFullscreen();
            ShowLoginWindow();
            return;
        }
        _onLoginPage = false;
        SendBarState(); // 同步“已登录”状态
        HideOverlay();
        RevealMainWindow(); // 大屏有效：现在才亮出主窗口（此前一直隐藏，不闪框）
        _ = DashboardReadyThenFullscreen();
    }

    private async Task DashboardReadyThenFullscreen()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            // 等页面渲染就绪（最多 40 秒）；期间页面跳走（如注销跳登录页）则立即中止
            for (int i = 0; i < 66; i++)
            {
                await Task.Delay(600);
                var curUrl = _wv.CoreWebView2.Source ?? "";
                if (curUrl.Contains("/decision/login", StringComparison.OrdinalIgnoreCase) ||
                    curUrl.Contains("/logout", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log("等待就绪期间页面已跳转，中止全屏流程: " + curUrl);
                    return;
                }
                var state = await RunScript(Scripts.ReadyProbe);
                if (state != null &&
                    state.Value.TryGetProperty("isLogin", out var isLogin) &&
                    isLogin.GetBoolean())
                {
                    // 等待期间被踢回登录页
                    return;
                }
                if (state != null &&
                    state.Value.TryGetProperty("ready", out var ready) &&
                    ready.GetString() == "complete")
                {
                    // DOM 完成后再给渲染留一点时间
                    await Task.Delay(2500);
                    break;
                }
            }

            // 权限智能检测：页面若是“无权限”提示页，保持窗口模式方便用户看到提示并自救
            var perm = await RunScript(Scripts.PermissionCheck);
            if (perm != null &&
                perm.Value.TryGetProperty("text", out var txtEl) &&
                LooksLikePermissionError(txtEl.GetString() ?? ""))
            {
                Logger.Log("检测到无权限提示页，保持窗口模式: " +
                    (perm.Value.TryGetProperty("url", out var uEl) ? uEl.GetString() : ""));
                if (_fullscreen) ExitFullscreen();
                return;
            }

            var url = _wv.CoreWebView2.Source ?? "";
            if (url.Contains("/decision/login", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("/logout", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log("就绪检测后页面已跳转，中止全屏流程: " + url);
                return;
            }
            var isDirectEntry = url.Contains("/entry/access/", StringComparison.OrdinalIgnoreCase);
            if (isDirectEntry)
            {
                // 只有直连大屏地址才自动进全屏（登录页/注销页/门户首页一律保持窗口）
                if (!_dashboardSeen)
                {
                    _dashboardSeen = true;
                    if (!_fullscreen)
                    {
                        if (_loginWin != null)
                        {
                            // 登录窗还盖着页面：先不搬动窗口，等它关掉的那一刻再进全屏
                            _pendingFullscreen = true;
                            Logger.Log("大屏已就绪，登录窗仍在 → 暂缓全屏");
                        }
                        else EnterFullscreen();
                    }
                }
                Logger.Log("直连大屏页面，跳过门户全屏点击");
            }
            else
            {
                // 登录后落到了帆软门户首页：转到直连大屏地址，避免露出带目录/工具栏的门户页面
                var direct = DeriveEntryUrl(_cfg.Url);
                if (_cfg.UseDirectEntry && direct != null)
                {
                    Logger.Log("非直连页面，转向直连大屏: " + direct);
                    try { _wv.CoreWebView2.Navigate(direct); } catch { }
                }
                else
                {
                    Logger.Log("非直连大屏页面，保持窗口模式: " + url);
                }
            }

            await RunScript(Scripts.HideChrome(_cfg.CssHideSelectors));

            // 只有确认在直连大屏页（登录有效）才把 Cookie 存盘，避免污染免登录存档
            if (isDirectEntry) await SaveSessionCookies();

            if (_cfg.Probe)
            {
                var dump = await RunScript(Scripts.ProbeDump);
                Logger.Log("PROBE: " + (dump?.Raw ?? "(no result)"));
            }

            // 诊断：保存一张当前画面截图，便于远程确认视觉效果
            if (!string.IsNullOrWhiteSpace(_cfg.ScreenshotPath))
            {
                try
                {
                    var path = Path.IsPathRooted(_cfg.ScreenshotPath)
                        ? _cfg.ScreenshotPath
                        : Path.Combine(AppPaths.DataDir, _cfg.ScreenshotPath);
                    await Task.Delay(1200);
                    await using var fs = File.Create(path);
                    await _wv.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
                    Logger.Log("已保存自检截图: " + path);
                }
                catch (Exception ex)
                {
                    Logger.Log("截图失败: " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log("DashboardReadyThenFullscreen 异常: " + ex);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>判断页面文本是否为“无权限”类提示页（命中任意关键词即认为无权限）。</summary>
    private static bool LooksLikePermissionError(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] keywords =
        {
            "无权限", "权限不足", "没有权限", "权限受限", "权限不够",
            "无权访问", "请联系管理员", "未授权", "访问被拒绝"
        };
        // 文本极短且命中关键词时才判定（正常大屏内容也可能包含个别词，需页面本身是提示页）
        var compact = text.Replace("\n", "").Replace(" ", "").Replace("\t", "").Replace("\r", "");
        foreach (var k in keywords)
        {
            if (compact.Contains(k, StringComparison.OrdinalIgnoreCase) && compact.Length < 300)
            {
                return true;
            }
        }
        return false;
    }

    // ================= 全屏 / 控制条 =================

    private void ToggleFullscreen()
    {
        if (_fullscreen) ExitFullscreen(); else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        if (_fullscreen) return;
        HideCover(); // 若过渡遮罩还在，先撤掉，别盖住全屏大屏
        if (!Visible) Show(); // 保险：必须可见再进全屏（隐藏状态下改尺寸 WebView2 会黑屏）
        // 关键：不能用 FormBorderStyle=None（会触发 RecreateHandle 重建窗口句柄，
        // WebView2 子窗口随之失联 → 黑屏/无法交互）。改为 SetWindowLong 直接去掉
        // 标题栏和可拉伸边框，窗口句柄不变，WebView2 继续正常渲染。
        try
        {
            _savedStyle = GetWindowLong(Handle, GWL_STYLE);
            SetWindowLong(Handle, GWL_STYLE, _savedStyle & ~(WS_CAPTION | WS_THICKFRAME));
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        catch { }
        Bounds = Screen.FromControl(this).Bounds;
        TopMost = true;
        _fullscreen = true;
        HideBar();
        // 进入全屏默认隐藏光标，动鼠标才出现
        RestoreCursor();
        Cursor.Hide();
        _cursorHidden = true;
        _lastMove = DateTime.Now;
        Logger.Log("进入全屏");
    }

    private void ExitFullscreen()
    {
        if (!_fullscreen) return;
        _fullscreen = false; // 先置 false，避免保活逻辑在过渡期间把窗口拉回全屏
        HideBar();
        // WebView2 在改边框/缩放窗口的瞬间会渲染黑帧：先用纯色遮罩盖住，切完再撤
        ShowCover(600);
        // 恢复标题栏（同样走 SetWindowLong，不重建句柄）
        try
        {
            if (_savedStyle != 0)
            {
                SetWindowLong(Handle, GWL_STYLE, _savedStyle);
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
        }
        catch { }
        Bounds = ComputeWindowedBounds(); // 按当前屏幕实时算（80% 居中），不依赖历史坐标
        TopMost = false;
        Logger.Log("退出全屏");
    }

    /// <summary>显示过渡遮罩 ms 毫秒后自动撤掉。</summary>
    private void ShowCover(int ms)
    {
        _fxCover.Visible = true;
        _fxCover.BringToFront();
        _fxTimer.Interval = ms;
        _fxTimer.Stop();
        _fxTimer.Start();
    }

    private void HideCover()
    {
        _fxTimer.Stop();
        if (_fxCover.Visible) _fxCover.Visible = false;
    }

    private void HideBar()
    {
        SendBarState(forceHide: true);
    }

    // ================= 全屏保活（防遮挡露任务栏） =================

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4,
        SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;   // 标题栏 + 边框
    private const int WS_THICKFRAME = 0x00040000; // 可拉伸边框
    private int _savedStyle; // 进入全屏前的窗口样式（退出全屏时恢复）

    private DateTime _lastTopmostFix = DateTime.MinValue;

    /// <summary>全屏状态下每秒检查一次：窗口尺寸被改则恢复，置顶被降级则重新置顶（不抢焦点）。</summary>
    private void EnsureKioskFullscreen()
    {
        if (!_fullscreen) return;
        if ((DateTime.Now - _lastTopmostFix).TotalMilliseconds < 1000) return;
        _lastTopmostFix = DateTime.Now;
        try
        {
            var scr = Screen.FromControl(this).Bounds;
            if (Bounds != scr) Bounds = scr;
            if (!TopMost) TopMost = true;
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        catch { }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // 失去焦点立刻重新置顶一次（不等下一个周期）
        _lastTopmostFix = DateTime.MinValue;
        EnsureKioskFullscreen();
    }

    // ================= 登录 Cookie 持久化（保持登录跨重启） =================

    private string SessionCookieFile => Path.Combine(AppPaths.DataDir, "session_cookies.json");

    private sealed class SavedCookie
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Domain { get; set; } = "";
        public string Path { get; set; } = "/";
        public bool Secure { get; set; }
        public bool HttpOnly { get; set; }
    }

    /// <summary>启动时恢复上次保存的登录 Cookie（帆软令牌是会话 Cookie，重启即丢，需手动还原）。</summary>
    private void RestoreSessionCookies()
    {
        try
        {
            if (!File.Exists(SessionCookieFile)) return;
            var list = JsonSerializer.Deserialize<List<SavedCookie>>(File.ReadAllText(SessionCookieFile));
            if (list == null || list.Count == 0) return;
            var cm = _wv.CoreWebView2.CookieManager;
            int n = 0;
            foreach (var c in list)
            {
                try
                {
                    var ck = cm.CreateCookie(c.Name, c.Value, c.Domain, c.Path);
                    ck.IsSecure = c.Secure;
                    ck.IsHttpOnly = c.HttpOnly;
                    // 会话 Cookie 设为 30 天有效期，浏览器才会写盘；服务端过期时登录页自然出现
                    ck.Expires = DateTime.UtcNow.AddDays(30);
                    cm.AddOrUpdateCookie(ck);
                    n++;
                }
                catch { }
            }
            Logger.Log($"已恢复 {n} 个登录 Cookie（保持登录跨重启）");
        }
        catch (Exception ex) { Logger.Log("恢复登录 Cookie 失败: " + ex.Message); }
    }

    /// <summary>大屏登录成功后把当前 Cookie 存盘，供下次启动恢复。</summary>
    private async Task SaveSessionCookies()
    {
        if (!_settings.KeepLogin) return;
        try
        {
            var url = _wv.CoreWebView2.Source;
            if (string.IsNullOrEmpty(url)) return;
            var cookies = await _wv.CoreWebView2.CookieManager.GetCookiesAsync(url);
            var list = cookies.Select(c => new SavedCookie
            {
                Name = c.Name,
                Value = c.Value,
                Domain = c.Domain,
                Path = c.Path,
                Secure = c.IsSecure,
                HttpOnly = c.IsHttpOnly
            }).ToList();
            if (list.Count == 0) return;
            File.WriteAllText(SessionCookieFile,
                JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            Logger.Log($"已保存 {list.Count} 个登录 Cookie");
        }
        catch (Exception ex) { Logger.Log("保存登录 Cookie 失败: " + ex.Message); }
    }

    private void DeleteSessionCookies()
    {
        try { if (File.Exists(SessionCookieFile)) File.Delete(SessionCookieFile); } catch { }
    }

    // ================= 原生登录窗口 / 认证流程 =================

    private LoginForm? _loginWin;
    private bool _authBusy; // 登录流程进行中，防止并发
    private bool _pendingFullscreen; // 大屏已就绪但因登录窗未关而暂缓进入全屏

    /// <summary>
    /// 弹出原生登录窗：屏幕上只留小登录窗（主窗口随后收起，登录窗先出现避免闪黑）；
    /// 登录动作在（隐藏的）浏览器里完成，登录成功后主窗口带着大屏回来。
    /// </summary>
    private void ShowLoginWindow()
    {
        if (_loginWin != null) { _loginWin.Activate(); return; }
        Logger.Log("弹出原生登录窗（主窗口随后收起）");
        LoginForm win;
        try
        {
            win = new LoginForm(_cfg);
        }
        catch (Exception ex)
        {
            Logger.Log("登录窗创建失败: " + ex); // 不让异常击穿 UI 线程
            return;
        }
        _loginWin = win;
        win.LoginRequested += (u, p, remember) => _ = HandleLoginAsync(u, p, remember);
        win.OpenSettingsRequested += () => ShowSettings(win);
        win.Disposed += (_, _) => _loginWin = null;
        win.Show();          // 先让登录窗可见
        win.Activate();
        BeginInvoke(Hide);   // 下一帧再收主窗口，避免中间闪黑/闪桌面

        // QQ 式自动登录：冷启动（本次运行尚未登录过）+ 设置勾选 + 有记住的账号
        // → 弹出登录窗的同时后台自动执行登录，成功即直接全屏；失败则停在登录窗显示原因。
        // 运行中的登录窗（会话过期、手动退出账号）不自动登录——否则"退出账号"就没有意义了。
        if (_settings.AutoLogin && !_loginSucceededOnce && !_authBusy)
        {
            var cred = CredentialStore.TryGet();
            if (cred != null)
            {
                Logger.Log("自动登录：冷启动 + 设置开启 + 已保存账号 → 自动登录 " + cred.Value.User);
                win.SetStatus("正在自动登录…", false);
                win.SetBusy(true);
                BeginInvoke(() => _ = HandleLoginAsync(cred.Value.User, cred.Value.Pass, remember: true));
            }
            else
            {
                Logger.Log("自动登录：设置开启但没有记住的账号，跳过（等待手动登录）");
            }
        }
    }

    private void CloseLoginWindow()
    {
        var hadPending = _pendingFullscreen;
        _pendingFullscreen = false;
        var win = _loginWin;
        _loginWin = null;
        try { win?.CloseSilently(); } catch { }
        RevealMainWindow(); // 主窗口带着大屏回来
        if (hadPending && !_fullscreen)
        {
            try { EnterFullscreen(); } catch { }
        }
    }

    /// <summary>
    /// 完整登录流程（登录窗背后完成，用户看不到任何帆软页面）：
    /// 1. 把（被遮住的）浏览器导航到帆软登录页；2. 页面内加密密码并 POST 登录接口；
    /// 3. 导航直连大屏地址验证结果；4. 成功 → 关登录窗、记住账号、走就绪全屏流程。
    /// </summary>
    private async Task HandleLoginAsync(string user, string pass, bool remember)
    {
        if (_authBusy) return;
        _authBusy = true;
        var win = _loginWin;
        if (win == null) { _authBusy = false; return; }
        try
        {
            win.SetStatus("正在连接服务器…", false);

            // 1. 导航到帆软登录页（若当前不在）
            var baseUri = new Uri(_cfg.Url);
            var loginUrl = new Uri(baseUri, "/webroot/decision/login").ToString();
            var cur = _wv.CoreWebView2.Source ?? "";
            if (!cur.Contains("/decision/login", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log("登录：导航到帆软登录页（登录窗覆盖中）");
                var okNav = await NavigateAndWaitAsync(loginUrl, 15);
                if (!okNav)
                {
                    win.SetStatus("无法连接服务器，请检查网络后重试", true);
                    win.SetBusy(false);
                    return;
                }
                // 保险：若因旧会话残留被帆软弹回非登录页（自动跳转门户/大屏），
                // 再清一次 Cookie 并重导登录页，避免在错误页面上填表单。
                var after = _wv.CoreWebView2.Source ?? "";
                if (!after.Contains("/decision/login", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log("登录：登录页被重定向到 " + after + "（疑似旧会话残留），清 Cookie 后重导");
                    try
                    {
                        await _wv.CoreWebView2.Profile.ClearBrowsingDataAsync(
                            CoreWebView2BrowsingDataKinds.Cookies);
                    }
                    catch { }
                    okNav = await NavigateAndWaitAsync(loginUrl, 15);
                    if (!okNav)
                    {
                        win.SetStatus("无法连接服务器，请检查网络后重试", true);
                        win.SetBusy(false);
                        return;
                    }
                }
                await Task.Delay(800); // 给页面框架留出初始化时间
            }

            // 2. 在（被盖住的）页面上填写账号密码并点击“登录”（沿用帆软自己的登录逻辑）
            win.SetStatus("正在验证账号…", false);
            var startRes = await RunScript(Scripts.PageLogin(user, pass));
            Logger.Log("登录：已提交页面登录，脚本返回 " + (startRes?.Raw ?? "(null)"));

            var ok = false;
            var errText = "";
            Logger.Log("登录：开始轮询结果");
            for (var i = 0; i < 100; i++) // 最多约 30 秒
            {
                try
                {
                    await Task.Delay(300);
                    var cur0 = _wv.CoreWebView2?.Source ?? "";
                    if (!cur0.Contains("/decision/login", StringComparison.OrdinalIgnoreCase))
                    {
                        ok = true; // 已跳离登录页 = 登录成功
                        Logger.Log("登录：页面已跳离登录页 → " + cur0);
                        break;
                    }
                    var pr = await RunScript(Scripts.PageLoginProbe);
                    if (pr == null || pr.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
                    {
                        if (i % 10 == 0) Logger.Log($"登录轮询 {i}: 探针无返回");
                        continue;
                    }
                    var state = pr.Value.TryGetProperty("state", out var stEl) ? stEl.GetString() ?? "" : "";
                    var err = pr.Value.TryGetProperty("err", out var erEl) ? erEl.GetString() ?? "" : "";
                    if (i % 5 == 0) Logger.Log($"登录轮询 {i}: state={state} err={err}");
                    if (state == "no-fields" || state == "no-button")
                    {
                        errText = "登录页面加载异常，请重试";
                        break;
                    }
                    if (state == "fill-fail")
                    {
                        errText = "登录页自动回填了旧账号且无法覆盖，请重试";
                        break;
                    }
                    if (err.Length > 0)
                    {
                        // 关键：页面可能残留上一次失败的报错文字（切换账号重试场景）。
                        // 等 600ms 复核——若期间页面已跳离登录页，说明这次登录其实成功了。
                        await Task.Delay(600);
                        var curNow = _wv.CoreWebView2?.Source ?? "";
                        if (!curNow.Contains("/decision/login", StringComparison.OrdinalIgnoreCase))
                        {
                            ok = true;
                            Logger.Log("登录：读到报错但页面已跳离登录页 → 视为登录成功 " + curNow);
                            break;
                        }
                        errText = err;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("登录轮询异常: " + ex.GetType().Name + " " + ex.Message);
                    break;
                }
            }

            if (!ok)
            {
                win.SetStatus(string.IsNullOrEmpty(errText) ? "登录超时，请检查网络后重试" : errText, true);
                win.SetBusy(false);
                Logger.Log("登录失败，停留在登录窗: " + user + " 原因: " + errText);
                return;
            }

            // 3. 登录成功：等直连大屏真正加载后再关登录窗（避免露出帆软门户页/中转页）
            win.SetStatus("登录成功，正在打开大屏…", false);
            var direct = (_cfg.UseDirectEntry ? DeriveEntryUrl(_cfg.Url) : null) ?? _cfg.Url;
            var reachedDash = false;
            for (var i = 0; i < 40; i++) // 最多约 20 秒
            {
                var u = _wv.CoreWebView2?.Source ?? "";
                if (u.Contains("/entry/access/", StringComparison.OrdinalIgnoreCase))
                {
                    reachedDash = true;
                    break;
                }
                if (i % 6 == 0)
                {
                    try { _wv.CoreWebView2?.Navigate(direct); } catch { }
                }
                await Task.Delay(500);
            }
            Logger.Log("登录：直连大屏 " + (reachedDash ? "已打开" : "未打开") + "，当前 " + (_wv.CoreWebView2?.Source ?? ""));
            if (!reachedDash)
            {
                win.SetStatus("登录状态未保持，请重试", true);
                win.SetBusy(false);
                return;
            }
            // 3.5 登录成功：登录窗继续盖住页面，直到大屏真正画好并进入全屏再消失
            if (remember) CredentialStore.Save(user, pass); else CredentialStore.Clear();
            _loginSucceededOnce = true; // 本次运行已成功登录过，之后弹出的登录窗不再自动登录
            Logger.Log("登录成功: " + user);
            win.SetStatus("登录成功，正在进入大屏…", false);
            SendBarState();

            var entered = false;
            for (var i = 0; i < 50; i++) // 最多约 15 秒
            {
                if (_fullscreen || _pendingFullscreen) { entered = true; break; }
                if (_loginWin == null) break;
                await Task.Delay(300);
            }
            Logger.Log("登录：大屏" + (entered ? "已就绪，关闭登录窗并全屏" : "未在限时内就绪，关闭登录窗"));
            CloseLoginWindow();
            if (_pendingFullscreen)
            {
                _pendingFullscreen = false;
                EnterFullscreen();
            }
        }
        catch (Exception ex)
        {
            Logger.Log("登录流程异常: " + ex);
            try
            {
                win.SetStatus("登录失败：" + ex.Message, true);
                win.SetBusy(false);
            }
            catch { }
        }
        finally
        {
            _authBusy = false;
        }
    }

    /// <summary>导航并等待完成。返回 false 表示加载失败或超时。</summary>
    private async Task<bool> NavigateAndWaitAsync(string url, int timeoutSeconds)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? h = null;
        h = (s, e) =>
        {
            _wv.CoreWebView2.NavigationCompleted -= h;
            tcs.TrySetResult(e.IsSuccess);
        };
        _wv.CoreWebView2.NavigationCompleted += h;
        try { _wv.CoreWebView2.Navigate(url); }
        catch (Exception ex)
        {
            _wv.CoreWebView2.NavigationCompleted -= h;
            Logger.Log("导航失败: " + ex.Message);
            return false;
        }
        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutSeconds * 1000)) == tcs.Task;
        if (!done) _wv.CoreWebView2.NavigationCompleted -= h;
        return done && tcs.Task.Result;
    }

    /// <summary>
    /// 退出登录：清掉本机会话 Cookie，回到登录状态（原生登录窗）。
    /// 记住的账号不清（供下次回填）；登录时若已不在登录页，登录流程会自动重新导航过去。
    /// </summary>
    private void SwitchAccount()
    {
        HideBar();
        Logger.Log("退出登录：清除登录会话，回到登录状态（记住的账号保留，供回填）");
        DeleteSessionCookies();
        try
        {
            _ = _wv.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.Cookies);
        }
        catch { }
        _onLoginPage = true;
        _dashboardSeen = false;
        ShowLoginWindow(); // 登录窗先出现（此刻主窗口画面还是正常的大屏，不会露黑）
        // ShowLoginWindow 里已排队“下一帧隐藏主窗口”；等它隐藏之后再做退出全屏的
        // 边框/尺寸切换，WebView2 的黑帧只发生在不可见的主窗口里，屏幕上看不到。
        BeginInvoke(new Action(async () =>
        {
            if (_fullscreen) ExitFullscreen();
            SendBarState();
            // 关键：必须等 Cookie 真正清空后再导登录页。旧会话残留时帆软处理新账号
            // 登录会报"账号或密码错误"（客户现场实测），重试几次才成功。
            try
            {
                await _wv.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.Cookies);
                Logger.Log("退出登录：Cookie 已清空");
            }
            catch (Exception ex) { Logger.Log("退出登录清 Cookie 失败: " + ex.Message); }
            // 后台把页面导到帆软登录页（登录窗背后进行，用户看不到）
            try
            {
                var loginUrl = new Uri(new Uri(_cfg.Url), "/webroot/decision/login").ToString();
                _wv.CoreWebView2.Navigate(loginUrl);
            }
            catch { }
        }));
    }

    // ================= 鼠标 / 热区 / 光标隐藏 =================

    private void UiTick()
    {
        var now = DateTime.Now;
        var p = Cursor.Position;

        // 全屏保活：被其他窗口抢占后周期性重新置顶，防止任务栏/其他窗口盖上来
        EnsureKioskFullscreen();

        if (p != _lastMouse)
        {
            _lastMouse = p;
            _lastMove = now;
            if (_cursorHidden) RestoreCursor();
        }

        // 顶部热区与控制条显隐由页面内注入的控制条自行处理（见 Scripts.InjectControlBar）

        // 闲置隐藏鼠标（仅全屏）
        if (_fullscreen && !_cursorHidden &&
            (now - _lastMove).TotalSeconds >= Math.Max(2, _cfg.MouseIdleHideSeconds))
        {
            Cursor.Hide();
            _cursorHidden = true;
        }
    }

    private void RestoreCursor()
    {
        if (_cursorHidden)
        {
            Cursor.Show();
            _cursorHidden = false;
        }
    }

    // ================= 断网遮罩 / 自动重试 =================

    private void ShowOverlay(string msg)
    {
        _overlayLabel.Text = msg;
        _overlay.Visible = true;
        _overlay.BringToFront();
        _retryCountdown = Math.Max(3, _cfg.RetrySeconds);
        _netTimer.Start();
    }

    private void HideOverlay()
    {
        _overlay.Visible = false;
        _netTimer.Stop();
    }

    private void NetTick()
    {
        _retryCountdown--;
        if (_retryCountdown <= 0)
        {
            _netTimer.Stop();
            Logger.Log("重试重新加载页面");
            try { _wv.CoreWebView2?.Reload(); } catch { }
        }
        else
        {
            var baseText = _overlayLabel.Text;
            int idx = baseText.IndexOf('\n');
            if (idx > 0)
                _overlayLabel.Text = baseText.Substring(0, idx) + "\n\n" + _retryCountdown + " 秒后自动重试…";
        }
    }

    // ================= JS 执行辅助 =================

    private record ScriptResult(string Raw, System.Text.Json.JsonElement Value);

    private async Task<ScriptResult?> RunScript(string js)
    {
        try
        {
            if (_wv.CoreWebView2 == null) return null;
            // 页面正在导航/重建执行上下文时 ExecuteScriptAsync 可能长时间不返回，加超时保护
            var exec = _wv.CoreWebView2.ExecuteScriptAsync(js);
            if (await Task.WhenAny(exec, Task.Delay(8000)) != exec)
            {
                Logger.Log("RunScript 超时(8s)，脚本忽略执行: " + (js.Length > 40 ? js[..40] : js));
                return null;
            }
            var raw = await exec;
            var opts = new System.Text.Json.JsonSerializerOptions();
            var el = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(raw, opts);
            // ExecuteScriptAsync 会把返回值再包一层字符串，需要二次解析
            while (el.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var inner = el.GetString();
                if (string.IsNullOrEmpty(inner) ||
                    !(inner.TrimStart().StartsWith("{") || inner.TrimStart().StartsWith("[")))
                    break;
                el = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(inner, opts);
            }
            return new ScriptResult(raw, el);
        }
        catch (Exception ex)
        {
            Logger.Log("RunScript 异常: " + ex.Message);
            return null;
        }
    }
}
