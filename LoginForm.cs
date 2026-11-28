using System.Drawing.Drawing2D;

namespace DashboardKiosk;

/// <summary>
/// 原生登录窗口（照帆软原版登录页风格）：
/// - 浅灰底 + 居中圆角卡片，左侧蓝色面板（logo 圆环 + 系统名 + 当前模式），右侧白色表单区；
/// - 输入框为“图标 + 下划线”样式，聚焦时下划线变蓝；账号密码可一键清空；
/// - 标题栏 ✕ / Esc = 退出整个程序（有确认）；账号密码自动回填，模式文字随“大屏设置”即时刷新。
/// </summary>
public class LoginForm : Form
{
    private readonly AppConfig _cfg;

    private FieldRow _rowUser = null!;
    private FieldRow _rowPass = null!;
    private CheckBox _chkRemember = null!;
    private Label _lblStatus = null!;
    private KioskButton _btnLogin = null!;
    private Label _lblMode = null!;

    // 卡片几何
    private static readonly Rectangle Card = new(30, 34, 600, 330);
    private static Rectangle BlueArea => new(Card.X, Card.Y, 240, Card.Height);
    private static int FieldX => Card.X + 270;
    private static int FieldW => Card.Right - 40 - FieldX;

    // 配色（取自帆软原版登录页）
    private static readonly Color PageBg = Color.FromArgb(237, 241, 247);
    private static readonly Color BlueTop = Color.FromArgb(31, 140, 205);
    private static readonly Color BlueBottom = Color.FromArgb(20, 100, 158);
    private static readonly Color BlueFlat = Color.FromArgb(27, 127, 191);
    private static readonly Color Underline = Color.FromArgb(213, 220, 230);
    private static readonly Color UnderlineFocus = Color.FromArgb(43, 163, 232);
    private static readonly Color FieldText = Color.FromArgb(51, 63, 82);
    private static readonly Color IconGray = Color.FromArgb(136, 149, 168);

    /// <summary>登录流程内部关闭（不弹退出确认）。</summary>
    private bool _internalClose;

    /// <summary>用户点击“登录”：参数为 (账号, 密码, 记住密码)。</summary>
    public event Action<string, string, bool>? LoginRequested;
    /// <summary>用户点击“大屏设置”。</summary>
    public event Action? OpenSettingsRequested;

    public LoginForm(AppConfig cfg)
    {
        _cfg = cfg;

        Text = cfg.Title;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(660, 420);
        // 高 DPI：布局按 96 DPI 设计，构造末尾用 Dpi.ScaleForm 按 DeviceDpi/96 等比放大控件（字体不重复缩放）
        AutoScaleMode = AutoScaleMode.None;
        BackColor = PageBg;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        Paint += DrawBackdrop;
        BuildUi();
        Dpi.ScaleForm(this);
    }

    private void BuildUi()
    {
        int fx = FieldX, fw = FieldW;

        // —— 左蓝色面板文字（透明底，露出渐变蓝） ——
        var lblTitle = new Label
        {
            Text = "意尔康BI系统",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            Location = new Point(BlueArea.X, BlueArea.Y + 148),
            Size = new Size(BlueArea.Width, 30),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(lblTitle);

        _lblMode = new Label
        {
            Text = ModeText(),
            ForeColor = Color.FromArgb(205, 228, 245),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Location = new Point(BlueArea.X, BlueArea.Y + 182),
            Size = new Size(BlueArea.Width, 20),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_lblMode);

        // —— 右侧表单 ——
        _rowUser = new FieldRow(FieldRowKind.User, fx, Card.Y + 60, fw);
        _rowUser.Box.PlaceholderText = "用户名";
        // 用户手动删空账号 = 不再记住该账号（下次打开为空白登录界面）
        _rowUser.Box.TextChanged += (_, _) =>
        {
            if (_rowUser.Box.Text.Trim().Length == 0) CredentialStore.Clear();
        };
        Controls.Add(_rowUser);

        _rowPass = new FieldRow(FieldRowKind.Pass, fx, Card.Y + 122, fw);
        _rowPass.Box.PlaceholderText = "密码";
        _rowPass.Box.UseSystemPasswordChar = true;
        Controls.Add(_rowPass);

        _chkRemember = new CheckBox
        {
            Text = "记住密码",
            ForeColor = Color.FromArgb(106, 120, 142),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(fx, Card.Y + 180),
            Size = new Size(150, 24),
            Checked = CredentialStore.TryGet() != null,
            Cursor = Cursors.Hand
        };
        Controls.Add(_chkRemember);

        _lblStatus = new Label
        {
            ForeColor = Color.FromArgb(214, 92, 84),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Location = new Point(fx, Card.Y + 206),
            Size = new Size(fw, 18),
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(_lblStatus);

        _btnLogin = new KioskButton("登 录", accent: true)
        {
            Location = new Point(fx, Card.Y + 232),
            Size = new Size(fw, 42),
            BackColor = Color.White // 圆角四角露出白色卡片底，而不是灰底
        };
        _btnLogin.Click += (_, _) => Submit();
        Controls.Add(_btnLogin);

        // 设置入口：登录按钮正下方居中的“齿轮图标 + 设置”按钮
        var btnSettings = new GearButton
        {
            Location = new Point(fx + (fw - 64) / 2, Card.Y + 284),
            Size = new Size(64, 32),
            BackColor = Color.White
        };
        btnSettings.Click += (_, _) => OpenSettingsRequested?.Invoke();
        Controls.Add(btnSettings);
    }

    private string ModeText() => "当前模式：" + (_cfg.IsWeather ? "天气大屏" : "驾驶舱大屏");

    /// <summary>背景：浅灰底 + 角落淡几何装饰 + 圆角卡片（左蓝右白）。所有像素按 DPI 等比换算。</summary>
    private void DrawBackdrop(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        g.Clear(PageBg);
        int S(int v) => Dpi.S(this, v);
        float SF(float v) => Dpi.SF(this, v);

        var card = new Rectangle(S(Card.X), S(Card.Y), S(Card.Width), S(Card.Height));
        var blue = new Rectangle(card.X, card.Y, S(240), card.Height);

        // 角落淡淡的六边形装饰（模仿原版的浅几何纹理）
        foreach (var (hx, hy, hs) in new[] {
            (60, 50, 34), (130, 30, 22), (590, 60, 30), (640, 120, 20),
            (40, 370, 26), (620, 380, 30) })
            DrawHex(g, S(hx), S(hy), S(hs), 40);

        // 卡片：整体圆角，左蓝右白
        using (var cardPath = Rounded(card, S(10)))
        {
            g.SetClip(cardPath);
            using (var lg = new LinearGradientBrush(blue, BlueTop, BlueBottom, LinearGradientMode.Vertical))
                g.FillRectangle(lg, blue);
            // 蓝面板上的白色六边形纹理
            foreach (var (hx, hy, hs, alpha) in new[] {
                (40, 60, 24, 36), (195, 40, 18, 30),
                (30, 250, 26, 30), (200, 270, 20, 26),
                (120, 300, 16, 24) })
                DrawHex(g, blue.X + S(hx), blue.Y + S(hy), S(hs), alpha);
            using (var white = new SolidBrush(Color.White))
                g.FillRectangle(white, card.X + blue.Width, card.Y, card.Width - blue.Width, card.Height);
            g.ResetClip();

            using var pen = new Pen(Color.FromArgb(214, 222, 232), SF(1f));
            g.DrawPath(pen, cardPath);
        }

        // 左面板 logo：白色圆环 + 模式图标
        int cx = blue.X + blue.Width / 2, cy = blue.Y + S(88), r = S(30);
        using (var ring = new Pen(Color.FromArgb(190, Color.White), SF(2f)))
            g.DrawEllipse(ring, cx - r, cy - r, r * 2, r * 2);
        using (var halo = new SolidBrush(Color.FromArgb(26, Color.White)))
            g.FillEllipse(halo, cx - r - S(8), cy - r - S(8), (r + S(8)) * 2, (r + S(8)) * 2);
        DrawModeIcon(g, cx - S(13), cy - S(13));
    }

    private void DrawModeIcon(Graphics g, int x, int y)
    {
        int S(int v) => Dpi.S(this, v);
        if (!_cfg.IsWeather)
        {
            // BI 柱状图（白色系）
            int[] heights = { S(8), S(13), S(18) };
            Color[] cols = { Color.FromArgb(170, Color.White), Color.FromArgb(210, Color.White), Color.White };
            for (int i = 0; i < 3; i++)
                using (var b = new SolidBrush(cols[i]))
                    g.FillRectangle(b, x + i * S(9), y + S(20) - heights[i], S(5), heights[i]);
        }
        else
        {
            // 太阳 + 云（白色系）
            using (var sun = new SolidBrush(Color.FromArgb(246, 197, 96)))
                g.FillEllipse(sun, x + S(2), y, S(12), S(12));
            using (var cloud = new SolidBrush(Color.White))
            {
                g.FillEllipse(cloud, x + S(8), y + S(8), S(15), S(10));
                g.FillEllipse(cloud, x + S(14), y + S(5), S(11), S(8));
                g.FillRectangle(cloud, x + S(8), y + S(12), S(17), S(6));
            }
        }
    }

    private static void DrawHex(Graphics g, int cx, int cy, int size, int alpha)
    {
        var pts = new PointF[6];
        for (int i = 0; i < 6; i++)
        {
            var ang = (60 * i - 30) * Math.PI / 180;
            pts[i] = new PointF(cx + (float)(size * Math.Cos(ang)), cy + (float)(size * Math.Sin(ang)));
        }
        using var pen = new Pen(Color.FromArgb(alpha, 140, 165, 200), 1.2f);
        g.DrawPolygon(pen, pts);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // 自动回填记住的账号（全选：直接输入即可覆盖）
        var cred = CredentialStore.TryGet();
        if (cred != null)
        {
            _rowUser.Box.Text = cred.Value.User;
            _rowPass.Box.Text = cred.Value.Pass;
        }
        _rowUser.Box.Focus();
        _rowUser.Box.SelectAll();

        // 测试模式：自动填写并提交
        if (_cfg.DebugLogin != null)
        {
            _rowUser.Box.Text = _cfg.DebugLogin.Username;
            _rowPass.Box.Text = _cfg.DebugLogin.Password;
            SetStatus("测试模式：自动登录中…", false);
            var tm = new System.Windows.Forms.Timer { Interval = 600 };
            tm.Tick += (_, _) => { tm.Stop(); tm.Dispose(); Submit(); };
            tm.Start();
        }
    }

    /// <summary>“大屏设置”保存后立即刷新模式文字、图标与配色。</summary>
    public void RefreshMode(AppConfig cfg)
    {
        _lblMode.Text = "当前模式：" + (cfg.IsWeather ? "天气大屏" : "驾驶舱大屏");
        Invalidate(); // logo 图标随模式重画
    }

    /// <summary>登录流程内部关闭：跳过“退出程序”确认。</summary>
    public void CloseSilently()
    {
        _internalClose = true;
        Close();
    }

    private void Submit()
    {
        if (_btnLogin.Enabled == false) return; // 正在登录中
        var u = _rowUser.Box.Text.Trim();
        var p = _rowPass.Box.Text;
        if (u.Length == 0) { SetStatus("请输入账号", true); _rowUser.Box.Focus(); return; }
        if (p.Length == 0) { SetStatus("请输入密码", true); _rowPass.Box.Focus(); return; }
        SetBusy(true);
        LoginRequested?.Invoke(u, p, _chkRemember.Checked);
    }

    /// <summary>显示状态提示。isError=true 红色，否则灰色。</summary>
    public void SetStatus(string msg, bool isError)
    {
        _lblStatus.Text = msg;
        _lblStatus.ForeColor = isError ? Color.FromArgb(214, 92, 84) : Color.FromArgb(130, 152, 182);
    }

    /// <summary>登录进行中：禁用按钮与输入框；结束后恢复。</summary>
    public void SetBusy(bool busy)
    {
        _btnLogin.Enabled = !busy;
        _btnLogin.Text = busy ? "登录中…" : "登 录";
        _rowUser.Box.Enabled = !busy;
        _rowPass.Box.Enabled = !busy;
        _chkRemember.Enabled = !busy;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter) { e.Handled = true; Submit(); }
        else if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_internalClose || e.CloseReason != CloseReason.UserClosing) return;

        // 用户主动点标题栏 ✕ / Esc：等于退出整个程序
        e.Cancel = true;
        if (MessageBox.Show("确定退出大屏程序吗？", _cfg.Title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _internalClose = true;
            Application.Exit();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        LoginRequested = null;
        OpenSettingsRequested = null;
    }

    // ================= 输入行：图标 + 无边框文本框 + 下划线 + 清空按钮 =================

    /// <summary>“齿轮图标 + 设置”按钮（悬停变蓝）。</summary>
    private class GearButton : Control
    {
        private bool _hover;

        public GearButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var color = _hover ? Color.FromArgb(43, 163, 232) : Color.FromArgb(150, 165, 190);

            // 齿轮图标（左侧），尺寸按 DPI 换算
            int cx = Dpi.S(this, 18), cy = Height / 2, r = Dpi.S(this, 7);
            using (var pen = new Pen(color, Dpi.SF(this, 2f)))
            {
                g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                for (int i = 0; i < 8; i++)
                {
                    var ang = 45 * i * Math.PI / 180;
                    float x1 = cx + (float)(r * Math.Cos(ang)), y1 = cy + (float)(r * Math.Sin(ang));
                    float x2 = cx + (float)((r + Dpi.SF(this, 3.5f)) * Math.Cos(ang)), y2 = cy + (float)((r + Dpi.SF(this, 3.5f)) * Math.Sin(ang));
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }
            int hr = Dpi.S(this, 3);
            using (var hole = new Pen(color, Dpi.SF(this, 1.6f)))
                g.DrawEllipse(hole, cx - hr, cy - hr, hr * 2, hr * 2);

            // “设置”文字（右侧）
            using var font = new Font("Microsoft YaHei UI", 9f);
            int tx = Dpi.S(this, 36);
            TextRenderer.DrawText(g, "设置", font, new Rectangle(tx, 0, Width - tx, Height), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private enum FieldRowKind { User, Pass }

    /// <summary>仿帆软的下划线输入行：左侧人形/锁形图标，聚焦时下划线变蓝，右侧 × 一键清空。</summary>
    private class FieldRow : Panel, IDpiSelfLayout
    {
        public readonly TextBox Box;
        private readonly FieldRowKind _kind;
        private readonly int _designWidth; // 96 DPI 下的设计行宽，用于推算当前缩放比例
        private bool _focus;

        public FieldRow(FieldRowKind kind, int x, int y, int width)
        {
            _kind = kind;
            _designWidth = width;
            Location = new Point(x, y);
            Size = new Size(width, 46);
            BackColor = Color.White;
            DoubleBuffered = true;

            // 注意：这里一律用 96 DPI 的逻辑坐标，实际像素由 Relayout 按 "当前行宽/设计行宽" 换算。
            // 两个原因：① TextBox.AutoSize 默认为 true，Dpi.ScaleForm 对它只挪位置不改宽度，
            // 直接用逻辑值可避免漏缩放；② 若在这里就用 Dpi.S 换算，ScaleForm 遍历子控件时会再乘一次
            // 倍率（二次缩放）→ 输入框右移、盖住下划线、清空按钮被挤出框外。
            Box = new TextBox
            {
                Location = new Point(30, 10),
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = FieldText,
                Font = new Font("Microsoft YaHei UI", 10.5f)
            };
            Controls.Add(Box);
            Box.GotFocus += (_, _) => { _focus = true; Invalidate(); };
            Box.LostFocus += (_, _) => { _focus = false; Invalidate(); };

            var clear = new Label
            {
                Text = "×",
                ForeColor = IconGray,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            clear.Click += (_, _) => { Box.Text = ""; Box.Focus(); };
            clear.MouseEnter += (_, _) => clear.ForeColor = FieldText;
            clear.MouseLeave += (_, _) => clear.ForeColor = IconGray;
            Controls.Add(clear);
            Relayout();
        }

        /// <summary>父容器完成 DPI 放大后重排内部控件（Dpi.ScaleForm 回调，避免子控件被二次缩放）。</summary>
        public void OnDpiScale() => Relayout();

        /// <summary>
        /// 按当前行宽重排输入框与清空按钮。缩放倍率直接由 "当前行宽 ÷ 设计行宽" 得出，
        /// 不依赖 DeviceDpi，因此构造期、缩放中、显示后任何时机调用结果都一致（幂等）。
        /// </summary>
        private void Relayout()
        {
            if (Box == null || _designWidth <= 0) return; // 构造早期（Size 赋值触发 OnResize 时）子控件尚未创建
            float k = Width / (float)_designWidth;
            int Px(int v) => (int)Math.Round(v * k);

            Box.Location = new Point(Px(30), Px(10));
            Box.Width = Width - Px(60);
            foreach (Control c in Controls)
            {
                if (c == Box) continue;
                c.Location = new Point(Width - Px(26), Px(11));
                c.Size = new Size(Px(20), Px(22));
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Relayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);

            // 图标（位置/尺寸按 DPI 换算）
            DrawIcon(g, Dpi.S(this, 2), Dpi.S(this, 12));

            // 下划线（聚焦变蓝、加粗）
            using (var pen = new Pen(_focus ? UnderlineFocus : Underline, Dpi.SF(this, _focus ? 2f : 1.4f)))
                g.DrawLine(pen, 0, Height - Dpi.S(this, 2), Width, Height - Dpi.S(this, 2));
        }

        private void DrawIcon(Graphics g, int x, int y)
        {
            int S(int v) => Dpi.S(this, v);
            using var pen = new Pen(IconGray, Dpi.SF(this, 1.6f));
            if (_kind == FieldRowKind.User)
            {
                // 人形：头 + 肩
                g.DrawEllipse(pen, x + S(5), y, S(8), S(8));
                g.DrawArc(pen, x, y + S(10), S(18), S(12), 180, 180);
            }
            else
            {
                // 锁：锁体 + 锁梁
                g.DrawRectangle(pen, x + S(2), y + S(8), S(14), S(11));
                g.DrawArc(pen, x + S(5), y, S(8), S(10), 180, 180);
                using var dot = new SolidBrush(IconGray);
                g.FillEllipse(dot, x + S(8), y + S(12), S(3), S(3));
            }
        }
    }
}
