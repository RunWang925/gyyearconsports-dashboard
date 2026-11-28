using System.Text.Json;

namespace DashboardKiosk;

public class KioskSettings
{
    public bool AutoStart { get; set; } = false;
    public bool KeepLogin { get; set; } = true;
    /// <summary>QQ 式自动登录：启动时若有记住的账号，自动执行登录并进入全屏（默认关）。</summary>
    public bool AutoLogin { get; set; } = false;
}

/// <summary>设置持久化（settings.json）+ 开机自启注册表管理。</summary>
public static class SettingsStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DashboardKiosk";

    public static string Dir = AppContext.BaseDirectory;

    public static KioskSettings Load()
    {
        try
        {
            var path = Path.Combine(Dir, "settings.json");
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<KioskSettings>(File.ReadAllText(path)) ?? new KioskSettings();
            }
        }
        catch { }
        return new KioskSettings();
    }

    public static void Save(KioskSettings s)
    {
        try
        {
            File.WriteAllText(Path.Combine(Dir, "settings.json"),
                JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Log("保存设置失败: " + ex.Message);
        }
    }

    /// <summary>注册/取消当前用户开机自启（无需管理员权限）。</summary>
    public static void ApplyAutoStart(bool on)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (on)
            {
                key.SetValue(RunValueName, "\"" + Environment.ProcessPath + "\"");
                Logger.Log("开机自启已开启");
            }
            else
            {
                key.DeleteValue(RunValueName, false);
                Logger.Log("开机自启已关闭");
            }
        }
        catch (Exception ex)
        {
            Logger.Log("设置开机自启失败: " + ex.Message);
        }
    }
}
