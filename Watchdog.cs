using System.Diagnostics;
using System.Threading;

namespace DashboardKiosk;

/// <summary>
/// 看门狗：由主程序以隐藏子进程方式启动，负责在主程序意外退出（崩溃/被杀）时把它重新拉起来。
/// 主程序正常退出时会通过命名事件通知看门狗退出。
/// </summary>
public static class Watchdog
{
    public const string StopEventPrefix = @"Local\DashboardKioskStop_";

    public static int Run(string exePath, int parentPid)
    {
        EventWaitHandle stop;
        try
        {
            stop = EventWaitHandle.OpenExisting(StopEventPrefix + parentPid);
        }
        catch
        {
            // 主事件不存在（异常情况），直接退出避免无限重启
            return 0;
        }

        while (true)
        {
            if (stop.WaitOne(2000)) return 0; // 主程序正常退出，看门狗跟着退出

            bool alive;
            try
            {
                using var p = Process.GetProcessById(parentPid);
                alive = !p.HasExited;
            }
            catch { alive = false; }

            if (!alive)
            {
                Logger.Log("[watchdog] 主程序意外退出，3 秒后重新启动");
                Thread.Sleep(3000);
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Logger.Log("[watchdog] 重启失败: " + ex.Message);
                    Thread.Sleep(5000);
                    continue; // 继续尝试
                }
                return 0;
            }
        }
    }

    private static EventWaitHandle? _stopHandle;

    /// <summary>主程序启动时调用：创建停止事件并拉起看门狗子进程。</summary>
    public static void Spawn()
    {
        var pid = Environment.ProcessId;
        _stopHandle = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventPrefix + pid);
        var exePath = Environment.ProcessPath!;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--watch --parent={pid}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Logger.Log($"[watchdog] 已启动看门狗进程 (主进程 pid={pid})");
        }
        catch (Exception ex)
        {
            Logger.Log("[watchdog] 启动看门狗失败: " + ex.Message);
        }
    }

    /// <summary>主程序正常退出前调用：通知看门狗退出。</summary>
    public static void SignalStop()
    {
        try { _stopHandle?.Set(); } catch { }
    }
}
