using System.Text.Json;

namespace DashboardKiosk;

/// <summary>
/// 本机记住上次登录的账号（供切换账号/重新打开时自动填写）。
/// 存储于 %LOCALAPPDATA%\YuErKangDashboard\credentials.json，内容做 Base64 混淆。
/// 用户可在“大屏设置”中手动删除记录。
/// </summary>
public static class CredentialStore
{
    private static string FilePath => Path.Combine(AppPaths.DataDir, "credentials.json");

    /// <summary>读取记住的账号；没有记录时返回 null。</summary>
    public static (string User, string Pass)? TryGet()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var doc = JsonSerializer.Deserialize<CredFile>(File.ReadAllText(FilePath));
            if (doc?.U == null || doc.U.Length == 0) return null;
            return (Decode(doc.U), Decode(doc.P ?? ""));
        }
        catch { return null; }
    }

    /// <summary>记住账号（登录成功提交时由页面脚本上报）。</summary>
    public static void Save(string user, string pass)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new CredFile
            {
                U = Encode(user),
                P = Encode(pass)
            }));
            Logger.Log($"已记住账号 {user}（下次登录自动填写）");
        }
        catch (Exception ex) { Logger.Log("保存账号信息失败: " + ex.Message); }
    }

    /// <summary>手动删除记住的账号（设置里的“删除记录”按钮）。</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            Logger.Log("已删除记住的账号信息");
        }
        catch (Exception ex) { Logger.Log("删除账号信息失败: " + ex.Message); }
    }

    private static string Encode(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
    private static string Decode(string s)
    {
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        catch { return ""; }
    }

    private sealed class CredFile
    {
        public string? U { get; set; }
        public string? P { get; set; }
    }
}
