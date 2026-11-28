using System.Drawing.Drawing2D;

namespace DashboardKiosk;

/// <summary>圆角胶囊按钮（自绘，带悬停/按下状态）。Ghost=true 时为无背景的文字按钮；Accent=true 时为蓝色主按钮。</summary>
public class KioskButton : Button
{
    private bool _hover;
    private bool _down;
    private readonly bool _ghost;
    private readonly bool _accent;

    // 浅色主题（设置/关于弹窗底色为浅灰，按钮为浅色描边风）
    private static readonly Color NormalBg = Color.FromArgb(240, 244, 250);
    private static readonly Color HoverBg = Color.FromArgb(226, 234, 244);
    private static readonly Color DownBg = Color.FromArgb(212, 222, 236);
    private static readonly Color TextColor = Color.FromArgb(70, 84, 106);
    private static readonly Color GhostNormal = Color.FromArgb(95, 140, 195);
    private static readonly Color GhostHover = Color.FromArgb(43, 163, 232);
    private static readonly Color AccentBg = Color.FromArgb(43, 163, 232);   // 帆软原版登录按钮浅蓝
    private static readonly Color AccentHover = Color.FromArgb(66, 178, 242);
    private static readonly Color AccentDown = Color.FromArgb(28, 136, 200);

    public KioskButton(string text, bool ghost = false, bool accent = false)
    {
        Text = text;
        _ghost = ghost;
        _accent = accent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = ghost ? GhostNormal : TextColor;
        Font = new Font("Microsoft YaHei UI", 9.75f, FontStyle.Regular);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        if (ghost)
        {
            // Ghost 按钮透明底：不遮挡父窗体的渐变背景
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
        Cursor = Cursors.Hand;

        var textSize = TextRenderer.MeasureText(text, Font);
        Size = new Size(textSize.Width + (ghost ? 18 : 34), 34);

        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; _down = false; Invalidate(); };
        MouseDown += (_, _) => { _down = true; Invalidate(); };
        MouseUp += (_, _) => { _down = false; Invalidate(); };
    }

    private GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0) { p.AddRectangle(new Rectangle(0, 0, 1, 1)); return p; }
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (!_ghost) g.Clear(BackColor); // 圆角外露出的底色 = 按钮所在面板的底色（由外部显式设置）

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (_ghost)
        {
            // 无背景，纯文字链接样式
            var color = _hover ? GhostHover : GhostNormal;
            TextRenderer.DrawText(g, Text, Font, rect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var bg = _down ? DownBg : _hover ? HoverBg : NormalBg;
        if (_accent) bg = _down ? AccentDown : _hover ? AccentHover : AccentBg;
        using (var path = Rounded(rect, Height / 2))
        using (var b = new SolidBrush(bg))
        {
            g.FillPath(b, path);
            // 浅色按钮加一圈细描边，避免在浅底上“糊”成文字
            if (!_accent)
                using (var pen = new Pen(Color.FromArgb(212, 221, 234), 1f))
                    g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Text, Font, rect, TextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
