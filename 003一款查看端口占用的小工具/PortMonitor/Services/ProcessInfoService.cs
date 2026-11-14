using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;

namespace PortMonitor.Services;

/// <summary>
/// 进程信息查询：名称走批量快照，命令行按需单查（带缓存）。
/// </summary>
public static class ProcessInfoService
{
    private static readonly ConcurrentDictionary<int, CacheEntry> CommandLineCache = new();
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly record struct CacheEntry(string? Value, DateTime Stamp);

    /// <summary>
    /// 一次性拿到全部进程名。实测约 11ms，比逐条 WMI 的 72ms×N 强得多。
    /// </summary>
    public static Dictionary<int, string> GetProcessNames()
    {
        var map = new Dictionary<int, string>(256);
        foreach (var process in Process.GetProcesses())
        {
            try { map[process.Id] = process.ProcessName; }
            catch { /* 受保护进程读不到，忽略 */ }
            finally { process.Dispose(); }
        }
        return map;
    }

    /// <summary>
    /// 查询进程完整命令行（WMI，单次约 70ms）。仅在用户选中某一行时调用。
    /// </summary>
    public static string? GetCommandLine(int pid, bool forceRefresh = false)
    {
        if (pid <= 0) return null;

        if (!forceRefresh
            && CommandLineCache.TryGetValue(pid, out var cached)
            && DateTime.UtcNow - cached.Stamp < CacheLifetime)
        {
            return cached.Value;
        }

        string? value = QueryCommandLineCore(pid);
        if (CommandLineCache.Count > 512) CommandLineCache.Clear();
        CommandLineCache[pid] = new CacheEntry(value, DateTime.UtcNow);
        return value;
    }

    private static string? QueryCommandLineCore(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    var text = obj["CommandLine"]?.ToString();
                    if (!string.IsNullOrEmpty(text)) return text;
                }
            }
        }
        catch
        {
            // WMI 不可用时退回主模块路径
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    public static string? GetExecutablePath(int pid)
    {
        if (pid <= 0) return null;
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 结束进程：优先请求正常退出，超时再强制结束（不杀进程树，避免连带误伤）。
    /// </summary>
    public static Task<(bool Ok, string Message)> KillAsync(int pid, bool graceful = true)
        => Task.Run(() =>
        {
            try
            {
                using var process = Process.GetProcessById(pid);

                if (graceful)
                {
                    try
                    {
                        if (process.CloseMainWindow() && process.WaitForExit(2500))
                            return (true, "进程已正常退出");
                    }
                    catch
                    {
                        // 无窗口/无权限，继续走强制结束
                    }
                }

                process.Kill(entireProcessTree: false);
                process.WaitForExit(3000);
                return (true, graceful ? "进程已强制结束" : "进程已结束");
            }
            catch (ArgumentException)
            {
                return (false, "进程已退出");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                return (false, "权限不足，请点击右上角“以管理员身份重启”后重试");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
}

/// <summary>
/// 危险操作保护策略：系统关键进程一律禁止操作。
/// </summary>
public static class ProcessGuard
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "fontdrvhost", "spoolsv", "explorer", "sihost",
        "ctfmon", "audiodg", "wudfhost", "securityhealthservice",
    };

    public static bool IsProtected(int pid, string? processName)
    {
        if (pid <= 4) return true;
        if (string.IsNullOrEmpty(processName)) return false;
        return Protected.Contains(processName);
    }

    public static string ProtectedReason(string processName)
        => $"“{processName}” 是系统关键进程，结束它可能导致系统不稳定或蓝屏，已禁止操作。";
}
