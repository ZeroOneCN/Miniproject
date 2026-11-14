namespace PortMonitor.Models;

/// <summary>
/// 占用某个文件（或文件夹内文件）的进程。
/// 两种来源：重启管理器（快速、无需管理员）、句柄扫描（深度、需管理员）。
/// </summary>
public class FileLockInfo
{
    public int Pid { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    /// <summary>结果来源：重启管理器 / 句柄扫描 / 两者皆是</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>重启管理器返回的应用名</summary>
    public string? AppName { get; init; }

    /// <summary>重启管理器返回的服务短名（svchost 托管的服务会给出）</summary>
    public string? ServiceName { get; init; }

    /// <summary>句柄值，0 表示该结果来自重启管理器、没有句柄信息</summary>
    public ulong HandleValue { get; init; }

    /// <summary>句柄指向的实际对象路径（\Device\HarddiskVolumeX\...）</summary>
    public string? MatchedPath { get; init; }

    public bool CanCloseHandle => HandleValue != 0;

    public string HandleText => HandleValue == 0 ? "-" : $"0x{HandleValue:X}";

    /// <summary>说明列</summary>
    public string Description
    {
        get
        {
            if (!string.IsNullOrEmpty(ServiceName)) return $"服务: {ServiceName}";
            if (!string.IsNullOrEmpty(AppName)) return AppName;
            return "-";
        }
    }

    public string Key => $"{Pid}|{HandleValue}";
}
