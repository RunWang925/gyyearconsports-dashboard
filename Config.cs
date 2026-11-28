using System.Text.Json;
using System.Text.Json.Serialization;

namespace DashboardKiosk;

/// <summary>
/// 程序配置。单程序支持两个大屏：驾驶舱（BI 数据）和天气大屏。
/// mode 决定本机启动后加载哪个；两个地址都保存在同一份 config.json 里。
/// </summary>
public class AppConfig
{
    /// <summary>本机显示哪个大屏：cockpit=驾驶舱（默认），weather=天气。</summary>
    public string Mode { get; set; } = "cockpit";

    /// <summary>驾驶舱大屏地址。真实地址不入库（见 config.json / 设置弹窗配置）。</summary>
    public string CockpitUrl { get; set; } = "";

    /// <summary>天气大屏地址。真实地址不入库（见 config.json / 设置弹窗配置）。</summary>
    public string WeatherUrl { get; set; } = "";

    /// <summary>
    /// 公网代理/备用地址（外出时临时使用）。内置默认为空，
    /// config.json 里的 proxyUrl 字段可覆盖。
    /// </summary>
    public string ProxyUrl { get; set; } = "";

    /// <summary>是否启用代理地址。默认 false（直连局域网）；打开后大屏走 <see cref="ProxyUrl"/>。</summary>
    public bool ProxyEnabled { get; set; } = false;

    public string Title { get; set; } = "贵渝意尔康体育数据大屏";
    public bool UseDirectEntry { get; set; } = true;        // 自动把门户链接换成直连大屏地址（无侧边栏/标签栏）
    public int TopHoverSeconds { get; set; } = 3;          // 鼠标停在顶部多少秒弹出控制条
    public int MouseIdleHideSeconds { get; set; } = 8;     // 鼠标闲置多少秒后隐藏
    public int RetrySeconds { get; set; } = 10;            // 加载失败后多少秒重试
    public string[] CssHideSelectors { get; set; } = Array.Empty<string>(); // 兜底隐藏门户元素的 CSS 选择器
    public bool Probe { get; set; } = false;               // 诊断模式：把页面结构写入日志
    public string? ScreenshotPath { get; set; } = null;    // 诊断模式：页面渲染后保存一张截图
    public bool DisableGpu { get; set; } = false;          // 画面花屏/闪烁时设为 true
    public string? ExtraArgs { get; set; } = null;         // 追加给浏览器内核的启动参数
    public DebugLogin? DebugLogin { get; set; } = null;    // 仅测试用：自动填写登录表单

    /// <summary>当前模式对应的大屏地址（代理开关打开且已填代理地址时走代理；不写入配置文件）。</summary>
    [JsonIgnore]
    public string Url => ProxyEnabled && !string.IsNullOrWhiteSpace(ProxyUrl)
        ? ProxyUrl
        : (Mode == "weather" ? WeatherUrl : CockpitUrl);

    public bool IsWeather => Mode == "weather";
}

public class DebugLogin
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>
/// 运行时数据目录（%LOCALAPPDATA%\YuErKangDashboard）：
/// 安装到 Program Files 后该目录普通用户可写，日志 / 登录会话 / config.json 都存这里。
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YuErKangDashboard");

    /// <summary>确保数据目录存在；首次运行时从安装目录初始化 config.json（优先旧版配置，其次默认模板）。</summary>
    public static void Ensure(string exeDir)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var target = Path.Combine(DataDir, "config.json");
            if (!File.Exists(target))
            {
                var src = Path.Combine(exeDir, "config.json");
                if (!File.Exists(src))
                    src = Path.Combine(exeDir, "config.default.json");
                if (File.Exists(src))
                    File.Copy(src, target);
            }
        }
        catch (Exception ex)
        {
            Logger.Log("初始化数据目录失败: " + ex.Message);
        }
    }
}

public static class ConfigLoader
{
    public static AppConfig Load(string exeDir)
    {
        var path = Path.Combine(exeDir, "config.json");
        if (!File.Exists(path))
        {
            // 没有配置文件时直接用内置默认值（AppConfig 字段已内置两个大屏地址），exe 单独分发也能跑
            return new AppConfig();
        }
        try
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            return cfg ?? new AppConfig();
        }
        catch (Exception ex)
        {
            MessageBox.Show("config.json 解析失败：\n" + ex.Message,
                "贵渝意尔康体育数据大屏", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return new AppConfig();
        }
    }
}
