namespace NeoPulse;

public static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NeoPulse", "logs");

    private static readonly string LogFile = Path.Combine(LogDir, $"monitor-{DateTime.Now:yyyy-MM-dd}.log");
    private static readonly object _lock = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}\n{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogFile,
                    $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}\n");
            }
        }
        catch { }
    }

    /// <summary>
    /// 按日期自动清理，只保留最近 7 天的日志
    /// </summary>
    public static void Cleanup()
    {
        try
        {
            if (!Directory.Exists(LogDir)) return;
            foreach (var file in Directory.GetFiles(LogDir, "monitor-*.log"))
            {
                var fi = new FileInfo(file);
                if (fi.LastWriteTime < DateTime.Now.AddDays(-7))
                    fi.Delete();
            }
        }
        catch { }
    }
}
