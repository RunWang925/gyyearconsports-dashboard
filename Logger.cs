namespace DashboardKiosk;

public static class Logger
{
    private static readonly object _lock = new();
    private static string _dir = "logs";

    public static void Init(string exeDir)
    {
        _dir = Path.Combine(exeDir, "logs");
        Directory.CreateDirectory(_dir);
    }

    public static void Log(string msg)
    {
        try
        {
            lock (_lock)
            {
                var file = Path.Combine(_dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                File.AppendAllText(file,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
            }
        }
        catch { /* 日志失败不影响运行 */ }
    }
}
