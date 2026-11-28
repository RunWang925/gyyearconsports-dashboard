namespace DashboardKiosk;

/// <summary>
/// “关于本程序”弹窗：程序名 / 版本 / 发布者 / 联系邮箱 / 功能说明 / 快捷操作 / 完整说明入口。
/// </summary>
public class AboutDialog : Form
{
    public const string AppName = "贵渝意尔康体育数据大屏";
    public const string Publisher = "Run";
    /// <summary>联系邮箱不入库，构建发布时在本地填写；为空时"关于"页隐藏联系作者行。</summary>
    public const string ContactEmail = "";

    // 版本检查更新（手动点击触发）：查询 GitHub Releases 的 latest release，公开仓库免凭证
    public const string RepoOwner = "RunWang925";
    public const string RepoName = "gyyearconsports-dashboard";
    public static string UpdateApiUrl => $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    public AboutDialog()
    {
        Text = "关于本程序";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(540, 456);
        // 高 DPI：布局按 96 DPI 设计，构造末尾用 Dpi.ScaleForm 等比放大（字体不重复缩放，防重叠）
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(237, 241, 247);
        ShowInTaskbar = false;

        var version = "v" + (System.Reflection.Assembly.GetEntryAssembly()?
            .GetName().Version?.ToString(3) ?? "2.0.0");

        // 头部：图标 + 名称/版本
        var iconPanel = new Panel { Location = new Point(24, 20), Size = new Size(56, 56), BackColor = Color.Transparent };
        iconPanel.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int S(int v) => Dpi.S(iconPanel, v);
            using (var b = new SolidBrush(Color.FromArgb(238, 247, 253)))
                g.FillRectangle(b, 0, 0, S(55), S(55));
            using (var p = new Pen(Color.FromArgb(214, 222, 232), Dpi.SF(iconPanel, 1f)))
                g.DrawRectangle(p, 0, 0, S(55), S(55));
            using (var b1 = new SolidBrush(Color.FromArgb(58, 120, 210))) g.FillRectangle(b1, S(12), S(36), S(8), S(12));
            using (var b2 = new SolidBrush(Color.FromArgb(91, 147, 224))) g.FillRectangle(b2, S(23), S(30), S(8), S(18));
            using (var b3 = new SolidBrush(Color.FromArgb(127, 177, 236))) g.FillRectangle(b3, S(34), S(24), S(8), S(24));
        };

        var lblName = new Label
        {
            Text = AppName,
            ForeColor = Color.FromArgb(51, 63, 82),
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            Location = new Point(94, 24),
            AutoSize = true
        };
        var lblVersion = new Label
        {
            Text = "版本 " + version + "　·　发布者：" + Publisher,
            ForeColor = Color.FromArgb(106, 120, 142),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(94, 52),
            AutoSize = true
        };

        // 联系作者（ContactEmail 为空时隐藏该行）
        var lblContact = new Label
        {
            Text = "联系作者：",
            ForeColor = Color.FromArgb(106, 120, 142),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(94, 74),
            AutoSize = true,
            Visible = !string.IsNullOrWhiteSpace(ContactEmail)
        };
        var linkMail = new LinkLabel
        {
            Text = ContactEmail,
            LinkColor = Color.FromArgb(43, 163, 232),
            ActiveLinkColor = Color.FromArgb(23, 130, 195),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(170, 74),
            AutoSize = true,
            Visible = !string.IsNullOrWhiteSpace(ContactEmail)
        };
        linkMail.LinkBehavior = LinkBehavior.HoverUnderline;
        linkMail.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mailto:" + ContactEmail,
                UseShellExecute = true
            }); }
            catch { }
        };

        // 功能说明（文字放进区块面板内部，避免被面板遮挡）
        var box1 = MakeSection("功能说明", new Rectangle(24, 100, 492, 146));
        var lblFeature = new Label
        {
            Text = "一个程序同时支持驾驶舱大屏和天气大屏：安装后在设置里选择本机显示哪一个，" +
                   "两个大屏地址均可随时修改。\n\n" +
                   "打开程序 → 登录帆软账号（勾选“保持登录状态”）→ 自动全屏显示大屏。" +
                   "断网自动重连，程序异常自动重启；账号无权限时程序停留在窗口显示错误信息，" +
                   "可在设置中切换大屏或切换账号。",
            ForeColor = Color.FromArgb(90, 104, 128),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(16, 26),
            Size = new Size(460, 112),
            BackColor = Color.Transparent
        };
        box1.Controls.Add(lblFeature);

        // 快捷操作
        var box2 = MakeSection("快捷操作", new Rectangle(24, 254, 492, 118));
        var lblKeys = new Label
        {
            Text = "Esc 键　　　　　　　退出全屏\n" +
                   "双击屏幕　　　　　　退出全屏 / 恢复全屏\n" +
                   "鼠标移到屏幕顶部停 3 秒　弹出控制条\n" +
                   "登录页左上角　　　　「大屏设置」入口",
            ForeColor = Color.FromArgb(90, 104, 128),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(16, 26),
            Size = new Size(460, 86),
            BackColor = Color.Transparent
        };
        box2.Controls.Add(lblKeys);

        // 按钮
        var btnHelp = new KioskButton("查看完整说明") { Location = new Point(24, 400), Size = new Size(130, 34) };
        btnHelp.Click += (_, _) => { using var f = new HelpForm(); f.ShowDialog(this); };

        // 检查更新：手动点击才发起网络请求
        var btnUpdate = new KioskButton("检查更新") { Location = new Point(170, 400), Size = new Size(110, 34) };
        var lblUpdate = new Label
        {
            Text = "",
            ForeColor = Color.FromArgb(106, 120, 142),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Location = new Point(296, 408),
            AutoSize = true
        };
        btnUpdate.Click += (_, _) => _ = CheckForUpdateAsync(btnUpdate, lblUpdate);

        var btnClose = new KioskButton("关闭", accent: true) { Size = new Size(96, 34), Location = new Point(ClientSize.Width - 120, 400) };
        btnClose.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            iconPanel, lblName, lblVersion, lblContact, linkMail,
            box1, box2,
            btnHelp, btnUpdate, lblUpdate, btnClose
        });

        Dpi.ScaleForm(this);
    }

    /// <summary>
    /// 手动检查更新：GET GitHub Releases latest → 对比版本号。
    /// 任何异常（断网/仓库无 release/接口变动）都只提示，不影响其他功能。
    /// </summary>
    private async System.Threading.Tasks.Task CheckForUpdateAsync(KioskButton btn, Label lbl)
    {
        var current = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(2, 0, 0);
        btn.Enabled = false;
        btn.Text = "检查中…";
        lbl.Text = "正在从 GitHub 获取最新版本…";
        lbl.ForeColor = Color.FromArgb(106, 120, 142);
        try
        {
            string tag = "", url = "", error = "";
            try
            {
                (tag, url, error) = await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                        // GitHub API 要求带 User-Agent
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("DashboardKiosk-UpdateCheck");
                        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                        var body = http.GetStringAsync(UpdateApiUrl).GetAwaiter().GetResult();
                        using var doc = System.Text.Json.JsonDocument.Parse(body);
                        var t = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                        var u = doc.RootElement.GetProperty("html_url").GetString() ?? "";
                        return (t, u, "");
                    }
                    catch (Exception ex)
                    {
                        return ("", "", ex.Message);
                    }
                });
            }
            catch (Exception ex) { error = ex.Message; }

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(tag))
            {
                lbl.Text = "检查失败：网络原因或暂无发布版本";
                lbl.ForeColor = Color.FromArgb(196, 108, 60);
                Logger.Log("检查更新失败: " + (error.Length > 200 ? error[..200] : error));
                return;
            }

            if (TryParseVersion(tag, out var latest) && latest > current)
            {
                lbl.Text = $"发现新版本 {latest}（当前 v{current.ToString(3)}）";
                lbl.ForeColor = Color.FromArgb(43, 163, 232);
                var go = MessageBox.Show(
                    $"发现新版本 {latest}\n当前版本：v{current.ToString(3)}\n\n是否打开下载页面？",
                    "检查更新", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (go == DialogResult.Yes && !string.IsNullOrEmpty(url))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
            else
            {
                lbl.Text = "已是最新版本";
                lbl.ForeColor = Color.FromArgb(64, 152, 106);
            }
        }
        finally
        {
            btn.Enabled = true;
            btn.Text = "检查更新";
        }
    }

    /// <summary>解析 "v2.2.0" / "2.2.0" 形式版本号，失败返回 false。</summary>
    private static bool TryParseVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        var s = (text ?? "").Trim().TrimStart('v', 'V');
        return Version.TryParse(s, out version!);
    }

    private static Panel MakeSection(string title, Rectangle rect)
    {
        var panel = new Panel
        {
            Location = rect.Location,
            Size = rect.Size,
            BackColor = Color.White
        };
        var lbl = new Label
        {
            Text = title,
            ForeColor = Color.FromArgb(106, 120, 142),
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(16, 10),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        panel.Controls.Add(lbl);
        panel.Paint += (_, e) =>
        {
            using var p = new Pen(Color.FromArgb(214, 222, 232), 1f);
            e.Graphics.DrawRectangle(p, 0, 0, panel.Width - 1, panel.Height - 1);
        };
        return panel;
    }
}

/// <summary>完整使用说明（可滚动，分节彩色排版）。</summary>
public class HelpForm : Form
{
    public HelpForm()
    {
        Text = "使用说明 - " + AboutDialog.AppName;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 560);
        // 高 DPI：布局按 96 DPI 设计，末尾 Dpi.ScaleForm 等比放大（字体不重复缩放）
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.White;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var rtb = new RichTextBox
        {
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(51, 63, 82),
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 9.5f)
        };
        rtb.AppendText(AboutDialog.AppName + "　使用说明\n");
        AppendStyled(rtb, "版本 " + VersionText + "　·　发布者：" + AboutDialog.Publisher + "\n\n",
            Color.FromArgb(120, 140, 165), 9f, false);

        foreach (var (title, body) in Sections)
        {
            AppendStyled(rtb, "▎ " + title + "\n", Color.FromArgb(43, 163, 232), 10.5f, true);
            AppendStyled(rtb, body.Trim() + "\n\n", Color.FromArgb(70, 84, 106), 9.5f, false);
        }
        if (!string.IsNullOrWhiteSpace(AboutDialog.ContactEmail))
        {
            AppendStyled(rtb, "▎ 联系作者\n", Color.FromArgb(43, 163, 232), 10.5f, true);
            AppendStyled(rtb, AboutDialog.ContactEmail + "（发布者：" + AboutDialog.Publisher + "）\n",
                Color.FromArgb(70, 84, 106), 9.5f, false);
        }

        rtb.Select(0, 0);
        Controls.Add(rtb);
        Dpi.ScaleForm(this);
    }

    public static string VersionText => "v" + (System.Reflection.Assembly.GetEntryAssembly()?
        .GetName().Version?.ToString(3) ?? "2.0.0");

    private static void AppendStyled(RichTextBox rtb, string text, Color color, float size, bool bold)
    {
        rtb.SelectionStart = rtb.TextLength;
        rtb.SelectionColor = color;
        rtb.SelectionFont = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        rtb.AppendText(text);
    }

    private static readonly (string Title, string Body)[] Sections =
    {
        ("程序简介",
         "贵渝意尔康体育数据大屏外壳程序。一个程序同时支持驾驶舱大屏（BI 数据）和天气大屏，\n" +
         "安装后在设置里选择本机显示哪一个，两个大屏地址均可随时修改，无需重新安装。"),
        ("首次使用",
         "1. 双击程序，出现登录窗口（帆软 BI 系统登录页）\n" +
         "2. 输入账号密码，勾选“保持登录状态”后登录\n" +
         "3. 登录成功后程序自动进入全屏，只显示大屏内容\n" +
         "4. 如果账号没有当前大屏的权限，程序会停留在窗口显示错误信息，\n" +
         "　　此时可在设置中切换到有权限的大屏，或用控制条的“切换账号”换号登录"),
        ("快捷操作",
         "- Esc 键：退出全屏 / 恢复全屏\n" +
         "- 双击屏幕任意位置：退出全屏 / 恢复全屏\n" +
         "- 鼠标移到屏幕最顶部停 3 秒：弹出控制条\n" +
         "　　控制条按钮：进入/退出全屏、刷新页面、切换账号（窗口模式且已登录时显示）、退出程序\n" +
         "- 登录页左上角：「大屏设置」入口（旁边会显示当前大屏模式）"),
        ("大屏设置",
         "- 本机显示：选择驾驶舱大屏或天气大屏，保存后立即生效\n" +
         "- 大屏地址：两个大屏的地址分别维护，后期数据接口变化时直接粘贴新地址\n" +
         "- 代理地址：留空 = 直连局域网地址；在外网时可填写公网代理地址（如内网穿透/服务器映射的网址），大屏走该地址\n" +
         "- 开机自动启动：勾选后开机自动运行本程序\n" +
         "- 保留登录信息：勾选后登录会话保存在本机，下次打开免登录"),
        ("其他说明",
         "- 断网时页面自动倒计时重试，恢复网络后自动重新加载\n" +
         "- 程序意外退出会被自动重新拉起（看门狗机制）\n" +
         "- 页面的自动刷新、轮播翻页等行为由帆软页面自身控制，程序不影响\n" +
         "- 画面出现花屏/闪烁时，可在 config.json 中将 disableGpu 设为 true")
    };
}
