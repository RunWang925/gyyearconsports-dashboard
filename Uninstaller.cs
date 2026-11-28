using System.Diagnostics;

namespace DashboardKiosk;

/// <summary>
/// 程序卸载入口（--uninstall 参数）。
/// 安装版：启动 Inno Setup 的卸载程序 unins000.exe（会清注册表、自启、缓存，保留 config.json）。
/// 便携版：提示用户手动删除目录。
/// </summary>
public static class Uninstaller
{
    public static int RunInteractive(string exePath, string exeDir)
    {
        var unins = Path.Combine(exeDir, "unins000.exe");
        if (File.Exists(unins))
        {
            try
            {
                Logger.Init(exeDir);
                Logger.Log("启动卸载程序: " + unins);
                Process.Start(new ProcessStartInfo
                {
                    FileName = unins,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动卸载程序失败：" + ex.Message, "贵渝意尔康体育数据大屏",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        else
        {
            MessageBox.Show(
                "这是免安装版本，卸载方法：\n\n" +
                "1. 先在设置里关闭\"开机自动启动\"\n" +
                "2. 直接删除整个程序文件夹即可",
                "贵渝意尔康体育数据大屏",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        return 0;
    }
}
