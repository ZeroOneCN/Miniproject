using System.IO;

namespace ScreenOffManager.Services;

/// <summary>
/// 极简文件日志。出问题时至少能知道"哪一步失败、返回了什么错误码"，
/// 而不是只有一句「设置失败」让你自己猜。
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenOffManager", "logs");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var file = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志写失败不能影响主流程
        }
    }
}
