using System.Text.Json;

namespace DashboardKiosk;

/// <summary>
/// 登录页“设置”弹窗：本机显示（驾驶舱/天气卡片选择）、双大屏地址、开机自启、保留登录信息、关于入口。
/// </summary>
public class SettingsDialog : Form
{
    private readonly AppConfig _cfg;
    private readonly KioskSettings _settings;

    private ModeCard _cardCockpit = null!;
    private ModeCard _cardWeather = null!;
    private TextBox _txtCockpit = null!;
    private TextBox _txtWeather = null!;
    private TextBox _txtProxy = null!;
    private ToggleSwitch _swProxy = null!;
    private CheckBox _chkAutoStart = null!;
    private CheckBox _chkKeepLogin = null!;
    private CheckBox _chkAutoLogin = null!;

    public AppConfig Result { get; private set; } = new();
    public KioskSettings ResultSettings { get; private set; } = new();

    public SettingsDialog(AppConfig current, KioskSettings settings)
    {
        _cfg = current;
        _settings = settings;
        Text = "大屏设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 510);
        // 高 DPI：布局按 96 DPI 设计，构造末尾用 Dpi.ScaleForm 等比放大（字体不重复缩放，防重叠）
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(237, 241, 247);
        ShowInTaskbar = false;

        var lblTitle = new Label
        {
            Text = "大屏设置",
            ForeColor = Color.FromArgb(51, 63, 82),
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            Location = new Point(24, 16),
            AutoSize = true
        };

        var lblMode = new Label
        {
            Text = "本机显示（点击选择，保存后立即生效）",
            ForeColor = Color.FromArgb(106, 120, 142),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(24, 58),
            AutoSize = true
        };

        _cardCockpit = new ModeCard(false, new Point(24, 82), new Size(248, 64),
            "驾驶舱大屏", "BI 数据 / 销售驾驶舱");
        _cardCockpit.OnSelected += () => Pick(false);
        _cardWeather = new ModeCard(true, new Point(288, 82), new Size(248, 64),
            "天气大屏", "贵渝区域天气及预报");
        _cardWeather.OnSelected += () => Pick(true);
        Pick(_cfg.IsWeather);

        var lblUrl = new Label
        {
            Text = "大屏地址（接口变化时直接粘贴新地址）",
            ForeColor = Color.FromArgb(106, 120, 142),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(24, 160),
            AutoSize = true
        };

        _txtCockpit = MakeUrlBox(new Point(24, 184), new Point(24, 184));
        var lblCockpit = new Label
        {
            Text = "驾驶舱",
            ForeColor = Color.FromArgb(106, 120, 142),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(24, 188),
            AutoSize = true
        };
        _txtCockpit.Location = new Point(84, 184);
        _txtCockpit.Width = 452;
        _txtCockpit.Text = current.CockpitUrl;

        _txtWeather = MakeUrlBox(new Point(84, 222), new Point(84, 222));
        _txtWeather.Width = 452;
        _txtWeather.Text = current.WeatherUrl;
        var lblWeather = new Label
        {
            Text = "天气　　",
            ForeColor = Color.FromArgb(106, 120, 142),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(24, 226),
            AutoSize = true
        };

        // 公网代理/备用地址（外出时用）：地址 + 启用开关（默认关闭，手动打开才生效）
        _txtProxy = MakeUrlBox(new Point(84, 260), new Point(84, 260));
        _txtProxy.Width = 452;
        _txtProxy.Text = current.ProxyUrl;
        var lblProxy = new Label
        {
            Text = "代理地址",
            ForeColor = Color.FromArgb(106, 120, 142),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(24, 264),
            AutoSize = true
        };
        _swProxy = new ToggleSwitch(current.ProxyEnabled) { Location = new Point(84, 288), Size = new Size(44, 20) };
        var lblProxySwitch = new Label
        {
            Text = "启用代理",
            ForeColor = Color.FromArgb(70, 84, 106),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(136, 289),
            AutoSize = true
        };
        var lblProxyTip = new Label
        {
            Text = "关闭 = 直连内网；打开后走上方代理地址",
            ForeColor = Color.FromArgb(150, 163, 184),
            Font = new Font("Microsoft YaHei UI", 8f),
            Location = new Point(200, 292),
            AutoSize = true
        };
        _swProxy.CheckedChanged += () => ApplyProxyEnabled();
        ApplyProxyEnabled();

        _chkAutoStart = MakeCheck("开机自动启动本程序", settings.AutoStart, new Point(24, 316));
        _chkKeepLogin = MakeCheck("保留登录信息（下次打开免登录）", settings.KeepLogin, new Point(24, 344));
        _chkAutoLogin = MakeCheck("启动时自动登录（有记住的账号就直接进入大屏）", settings.AutoLogin, new Point(24, 372));

        // 记住的账号（登录页自动填写；可手动删除）
        var saved = CredentialStore.TryGet();
        var lblSaved = new Label
        {
            Text = saved != null ? $"记住的账号：{saved.Value.User}（密码已保存，显示为 ●●●●●●）" : "记住的账号：无（下次登录成功后自动记住）",
            ForeColor = saved != null ? Color.FromArgb(70, 84, 106) : Color.FromArgb(150, 163, 184),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(24, 404),
            AutoSize = true
        };
        var btnClearCred = new KioskButton("删除记录")
        {
            Location = new Point(430, 398),
            Size = new Size(106, 28),
            Enabled = saved != null
        };
        btnClearCred.Click += (_, _) =>
        {
            if (MessageBox.Show("将删除本机记住的账号和密码，下次登录需要重新输入。确定删除吗？",
                "大屏设置", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            CredentialStore.Clear();
            lblSaved.Text = "记住的账号：无（下次登录成功后自动记住）";
            lblSaved.ForeColor = Color.FromArgb(150, 163, 184);
            btnClearCred.Enabled = false;
        };

        var tip = new Label
        {
            Text = "提示：账号无权限时程序会停留在窗口显示错误信息，可在设置中切换大屏或切换账号。",
            ForeColor = Color.FromArgb(150, 163, 184),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Location = new Point(24, 436),
            AutoSize = true
        };

        var btnAbout = new KioskButton("关于本程序")
        {
            Location = new Point(24, 462),
            Size = new Size(110, 34)
        };
        btnAbout.Click += (_, _) => { using var f = new AboutDialog(); f.ShowDialog(this); };

        var btnSave = new KioskButton("保存", accent: true) { Size = new Size(96, 34), Location = new Point(ClientSize.Width - 120, 462) };
        var btnCancel = new KioskButton("取消") { Size = new Size(80, 34), Location = new Point(ClientSize.Width - 228, 462) };

        btnSave.Click += (_, _) =>
        {
            var cUrl = _txtCockpit.Text.Trim();
            var wUrl = _txtWeather.Text.Trim();
            var pUrl = _txtProxy.Text.Trim();
            if (!ValidUrl(cUrl) || !ValidUrl(wUrl))
            {
                MessageBox.Show("大屏地址格式不正确，请填写以 http:// 或 https:// 开头的完整地址。",
                    "大屏设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (pUrl.Length > 0 && !ValidUrl(pUrl))
            {
                MessageBox.Show("代理地址格式不正确：请填写以 http:// 或 https:// 开头的完整地址。",
                    "大屏设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_swProxy.Checked && pUrl.Length == 0)
            {
                MessageBox.Show("代理开关已打开，但代理地址为空：请填写代理地址，或先关闭开关。",
                    "大屏设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = new AppConfig
            {
                Mode = _cardCockpit.Selected ? "cockpit" : "weather",
                CockpitUrl = cUrl,
                WeatherUrl = wUrl,
                ProxyUrl = pUrl,
                ProxyEnabled = _swProxy.Checked,
                Title = _cfg.Title,
                UseDirectEntry = _cfg.UseDirectEntry,
                TopHoverSeconds = _cfg.TopHoverSeconds,
                MouseIdleHideSeconds = _cfg.MouseIdleHideSeconds,
                RetrySeconds = _cfg.RetrySeconds,
                CssHideSelectors = _cfg.CssHideSelectors,
                Probe = _cfg.Probe,
                ScreenshotPath = _cfg.ScreenshotPath,
                DisableGpu = _cfg.DisableGpu,
                ExtraArgs = _cfg.ExtraArgs
            };
            ResultSettings = new KioskSettings
            {
                AutoStart = _chkAutoStart.Checked,
                KeepLogin = _chkKeepLogin.Checked,
                AutoLogin = _chkAutoLogin.Checked
            };
            DialogResult = DialogResult.OK;
            Close();
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[]
        {
            lblTitle, lblMode, _cardCockpit, _cardWeather,
            lblUrl, lblCockpit, _txtCockpit, lblWeather, _txtWeather,
            lblProxy, _txtProxy, _swProxy, lblProxySwitch, lblProxyTip,
            _chkAutoStart, _chkKeepLogin, _chkAutoLogin, lblSaved, btnClearCred, tip,
            btnAbout, btnSave, btnCancel
        });

        Dpi.ScaleForm(this);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private static bool ValidUrl(string u)
    {
        return Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
               (uri.Scheme == "http" || uri.Scheme == "https");
    }

    private void Pick(bool weather)
    {
        _cardCockpit.Selected = !weather;
        _cardWeather.Selected = weather;
    }

    /// <summary>代理开关联动：关闭时地址框置灰（仍显示内容，只是不可编辑）。</summary>
    private void ApplyProxyEnabled()
    {
        bool on = _swProxy.Checked;
        _txtProxy.Enabled = on;
        _txtProxy.BackColor = on ? Color.White : Color.FromArgb(233, 236, 242);
    }

    private TextBox MakeUrlBox(Point loc, Point _)
    {
        return new TextBox
        {
            Location = loc,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(51, 63, 82),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f)
        };
    }

    /// <summary>
    /// 地址输入框宽度重排。TextBox.AutoSize 默认 true，Dpi.ScaleForm 只挪位置不改宽度，
    /// 高 DPI 下宽度会停在 96 DPI 值 → URL 显示不全。这里按 "当前客户区宽 ÷ 设计宽" 的比例铺满
    /// （左边距 84、右边距 24），不依赖 DeviceDpi，任何时机重算结果都一致。
    /// </summary>
    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        if (_txtCockpit == null) return; // 构造早期尚未创建
        float k = ClientSize.Width / 560f; // 560 = 96 DPI 下的设计客户区宽
        int x = (int)Math.Round(84 * k);
        int w = ClientSize.Width - (int)Math.Round(108 * k);
        foreach (var tb in new[] { _txtCockpit, _txtWeather, _txtProxy })
        {
            if (tb == null) continue;
            tb.Location = new Point(x, tb.Top);
            tb.Width = w;
        }
    }

    private CheckBox MakeCheck(string text, bool isChecked, Point loc)
    {
        return new CheckBox
        {
            Text = text,
            ForeColor = Color.FromArgb(70, 84, 106),
            Font = new Font("Microsoft YaHei UI", 10f),
            Location = loc,
            AutoSize = true,
            Checked = isChecked
        };
    }

    /// <summary>模式选择卡片：自绘图标 + 选中态高亮。</summary>
    private class ModeCard : Panel
    {
        private readonly bool _weather;
        private readonly string _title;
        private readonly string _sub;

        private bool _selected;
        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }
        public event Action? OnSelected;

        public ModeCard(bool weather, Point loc, Size size, string title, string sub)
        {
            _weather = weather;
            _title = title;
            _sub = sub;
            Location = loc;
            Size = size;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            Click += (_, _) => OnSelected?.Invoke();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Color.FromArgb(237, 241, 247));
            int S(int v) => Dpi.S(this, v);

            var border = Selected ? Color.FromArgb(43, 163, 232) : Color.FromArgb(214, 222, 232);
            var fill = Selected ? Color.FromArgb(238, 247, 253) : Color.White;
            using (var path = Rounded(ClientRect, S(10)))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(border, Selected ? Dpi.SF(this, 2f) : Dpi.SF(this, 1.5f))) g.DrawPath(p, path);
            }

            // 图标
            DrawIcon(g, S(14), Height / 2 - S(15));

            // 文本
            TextRenderer.DrawText(g, _title, new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
                new Rectangle(S(52), S(12), Width - S(90), S(22)),
                Selected ? Color.FromArgb(51, 63, 82) : Color.FromArgb(106, 120, 142),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, _sub, new Font("Microsoft YaHei UI", 8f),
                new Rectangle(S(52), S(36), Width - S(90), S(16)),
                Selected ? Color.FromArgb(106, 120, 142) : Color.FromArgb(150, 163, 184),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // 选中圆点
            var dotRect = new Rectangle(Width - S(30), Height / 2 - S(10), S(20), S(20));
            if (Selected)
            {
                using var b = new SolidBrush(Color.FromArgb(43, 163, 232));
                g.FillEllipse(b, dotRect);
                using var p2 = new Pen(Color.White, Dpi.SF(this, 1.8f));
                p2.StartCap = p2.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawLine(p2, dotRect.X + S(4), dotRect.Y + S(10), dotRect.X + S(8), dotRect.Y + S(14));
                g.DrawLine(p2, dotRect.X + S(8), dotRect.Y + S(14), dotRect.X + S(15), dotRect.Y + S(5));
            }
            else
            {
                using var p = new Pen(Color.FromArgb(180, 192, 208), Dpi.SF(this, 1.5f));
                g.DrawEllipse(p, dotRect);
            }
        }

        private Rectangle ClientRect => new(1, 1, Width - 3, Height - 3);

        private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void DrawIcon(Graphics g, int x, int y)
        {
            int S(int v) => Dpi.S(this, v);
            if (!_weather)
            {
                // BI 柱状图
                int[] heights = { S(12), S(19), S(26) };
                for (int i = 0; i < 3; i++)
                {
                    var alpha = Selected ? 255 - i * 40 : 150 - i * 30;
                    using var b = new SolidBrush(Color.FromArgb(alpha, 58 + i * 16, 120 + i * 20, 210 + i * 10));
                    g.FillRectangle(b, x + i * S(9), y + S(30) - heights[i], S(6), heights[i]);
                }
            }
            else
            {
                // 太阳 + 云
                using (var sun = new SolidBrush(Color.FromArgb(Selected ? 255 : 170, 239, 159, 39)))
                    g.FillEllipse(sun, x + S(1), y + S(1), S(13), S(13));
                using (var cloud = new SolidBrush(Color.FromArgb(Selected ? 255 : 170, 201, 210, 224)))
                {
                    g.FillEllipse(cloud, x + S(6), y + S(13), S(16), S(11));
                    g.FillEllipse(cloud, x + S(12), y + S(9), S(13), S(10));
                    g.FillRectangle(cloud, x + S(6), y + S(17), S(19), S(7));
                }
            }
        }

    }
}

/// <summary>
/// 滑动开关（iOS 风格）：默认关闭（灰），点击切换，开启时变蓝。
/// 绘制全部按控件自身宽高等比计算，DPI 缩放后不需要额外处理。
/// </summary>
public class ToggleSwitch : Control
{
    private bool _checked;
    public bool Checked
    {
        get => _checked;
        set { if (_checked != value) { _checked = value; Invalidate(); } }
    }

    /// <summary>开关状态变化（点击切换后触发）。</summary>
    public event Action? CheckedChanged;

    private static readonly Color OffTrack = Color.FromArgb(203, 210, 221);
    private static readonly Color OnTrack = Color.FromArgb(43, 163, 232);

    public ToggleSwitch(bool isChecked)
    {
        Checked = isChecked;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
        CheckedChanged?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int knob = Height - 4;                       // 滑块直径（上下各留 2px）
        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var b = new SolidBrush(Checked ? OnTrack : OffTrack))
            g.FillEllipse(b, track);                 // 胶囊轨道（宽>高自动成胶囊形）
        int x = Checked ? Width - knob - 3 : 2;      // 开=右，关=左
        using (var b = new SolidBrush(Color.White))
            g.FillEllipse(b, x, 2, knob, knob);
    }
}

/// <summary>配置文件字段更新（保留其他配置项）。</summary>
public static class ConfigUpdater
{
    /// <summary>把指定字段写回 config.json；返回是否成功。值支持 string / bool 等（按运行时类型序列化成对应 JSON 类型）。</summary>
    public static bool UpdateFields(string exeDir, Dictionary<string, object?> fields)
    {
        try
        {
            var path = Path.Combine(exeDir, "config.json");
            if (!File.Exists(path)) return false;
            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            var dict = new Dictionary<string, JsonElement>();
            foreach (var p in doc.RootElement.EnumerateObject())
                dict[p.Name] = p.Value.Clone();

            foreach (var kv in fields)
                dict[kv.Key] = JsonSerializer.Deserialize<JsonElement>(
                    JsonSerializer.Serialize(kv.Value, kv.Value?.GetType() ?? typeof(object)));

            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            int i = 0;
            foreach (var item in dict)
            {
                sb.Append("  \"").Append(item.Key).Append("\": ").Append(item.Value.GetRawText());
                if (++i < dict.Count) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("}\n");
            File.WriteAllText(path, sb.ToString());
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log("更新 config.json 失败: " + ex.Message);
            return false;
        }
    }
}
