using System.Threading;
using DashboardKiosk;

namespace DashboardKiosk;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var exePath = Environment.ProcessPath!;
        var exeDir = AppContext.BaseDirectory;

        // 看门狗模式
        if (args.Any(a => a == "--watch"))
        {
            int parentPid = 0;
            foreach (var a in args)
            {
                if (a.StartsWith("--parent="))
                    int.TryParse(a["--parent=".Length..], out parentPid);
            }
            if (parentPid > 0)
                return Watchdog.Run(exePath, parentPid);
            return 0;
        }

        // 卸载入口（从"应用和功能"或设置弹窗触发）
        if (args.Any(a => a == "--uninstall"))
            return Uninstaller.RunInteractive(exePath, exeDir);

        // 初始化数据目录（%LOCALAPPDATA%\YuErKangDashboard），日志/会话/配置都存这里
        AppPaths.Ensure(exeDir);
        Logger.Init(AppPaths.DataDir);
        SettingsStore.Dir = AppPaths.DataDir;

        // 单实例
        using var mutex = new Mutex(true, @"Local\DashboardKioskMutex", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("大屏程序已在运行中。", "贵渝意尔康体育数据大屏",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        var cfg = ConfigLoader.Load(AppPaths.DataDir);
        Logger.Log("程序启动 pid=" + Environment.ProcessId + " url=" + cfg.Url);

        Watchdog.Spawn();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(cfg, exeDir));
        return 0;
    }
}
