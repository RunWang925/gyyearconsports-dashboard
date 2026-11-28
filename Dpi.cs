namespace DashboardKiosk;

/// <summary>
/// 自我排版的容器：内部子控件（如输入框、清空按钮）的位置/尺寸由容器自己按当前大小算。
/// <see cref="Dpi.ScaleForm"/> 只放大这类容器自身，不再递归放大它的子控件——
/// 否则容器的换算结果会被再乘一次倍率，出现"二次缩放"（输入框右移、盖住下划线、清空按钮跑到框外）。
/// </summary>
public interface IDpiSelfLayout
{
    /// <summary>容器自身尺寸已按 DPI 放大完毕，重新计算内部子控件布局。</summary>
    void OnDpiScale();
}

/// <summary>
/// 高 DPI 布局缩放。
/// 本项目所有窗体都按 96 DPI（100% 缩放）的像素常量来布局。在高缩放屏（如 4K + 300%）上，
/// 字号是"点"单位、系统会自动放大 3 倍，但控件的位置和尺寸不会——结果就是"字大框小、全部重叠"。
/// 因此窗体构造末尾调用 <see cref="ScaleForm"/>，把窗体与全部子控件的位置/尺寸按 DeviceDpi/96 等比放大。
/// 字体不做二次缩放（点单位已随 DPI 生效），自绘图标等用 <see cref="S"/> 换算即可。
/// 调试：设置环境变量 YERKANG_DPI=3 可在普通屏上模拟 300% 缩放（测试用）。
/// </summary>
public static class Dpi
{
    /// <summary>缩放倍率：96 DPI = 1.0；300% 缩放 = 3.0。环境变量 YERKANG_DPI 可强制指定（测试用）。</summary>
    public static float Factor(Control c)
    {
        var ov = Environment.GetEnvironmentVariable("YERKANG_DPI");
        if (!string.IsNullOrEmpty(ov) && float.TryParse(ov, out var f) && f > 0.1f && f < 8f)
            return f;
        return c.DeviceDpi / 96f;
    }

    /// <summary>把 96 DPI 下的像素值换算为当前 DPI 的像素（用于自绘图标/间距）。</summary>
    public static int S(Control c, int v) => (int)Math.Round(v * Factor(c));

    /// <summary>浮点版换算（线宽等）。</summary>
    public static float SF(Control c, float v) => v * Factor(c);

    /// <summary>窗体构造最后一步调用：按 DPI 等比放大窗体与全部子控件。</summary>
    public static void ScaleForm(Form form)
    {
        float f = Factor(form);
        if (f <= 1.001f) return;

        form.SuspendLayout();
        ScaleChildren(form, f);
        form.ClientSize = new Size(
            (int)Math.Round(form.ClientSize.Width * f),
            (int)Math.Round(form.ClientSize.Height * f));
        form.ResumeLayout(true);

        Logger.Log($"[DPI] {form.GetType().Name} factor={f:0.##} client={form.ClientSize.Width}x{form.ClientSize.Height}");
    }

    private static void ScaleChildren(Control parent, float f)
    {
        foreach (Control c in parent.Controls)
        {
            c.SuspendLayout();
            if (c.Dock == DockStyle.None)
            {
                var b = c.Bounds;
                int x = (int)Math.Round(b.X * f), y = (int)Math.Round(b.Y * f);
                if (c.AutoSize)
                {
                    // 自动尺寸控件（Label/CheckBox 等）尺寸由字体决定，字体已随 DPI 放大，只需移动位置
                    c.Location = new Point(x, y);
                }
                else
                {
                    c.Bounds = new Rectangle(x, y,
                        (int)Math.Round(b.Width * f), (int)Math.Round(b.Height * f));
                }
            }

            // 自我排版容器：子控件由它自己换算，绝不能再乘一次 f
            if (c is IDpiSelfLayout self) self.OnDpiScale();
            else ScaleChildren(c, f);

            c.ResumeLayout(true);
        }
    }
}
