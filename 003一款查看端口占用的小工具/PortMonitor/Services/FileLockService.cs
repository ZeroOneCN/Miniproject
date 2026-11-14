using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using PortMonitor.Models;

namespace PortMonitor.Services;

/// <summary>
/// 文件占用（句柄）查询：
/// 1) 重启管理器（Restart Manager）—— 官方接口，无需管理员，快；
/// 2) 系统句柄枚举（NtQuerySystemInformation）—— 深度扫描，需管理员，能列出具体句柄。
/// </summary>
public static class FileLockService
{
    // ── 路径处理 ───────────────────────────────────────────────────────

    public static bool TryResolvePath(string input, out string fullPath, out string? error)
    {
        fullPath = string.Empty;
        error = null;

        var text = (input ?? string.Empty).Trim().Trim('"');
        if (text.Length == 0)
        {
            error = "请输入文件或文件夹路径";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(text).TrimEnd('\\');
        }
        catch (Exception ex)
        {
            error = $"路径无效：{ex.Message}";
            return false;
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            error = $"路径不存在：{fullPath}";
            return false;
        }

        return true;
    }

    // ── 重启管理器 ─────────────────────────────────────────────────────

    /// <summary>
    /// RM_PROCESS_INFO 的正确尺寸是 668 字节。结构体对齐写错（例如忘了 Pack=4）
    /// 不会报错，只会静默读出乱码进程名，因此留一个可断言的探针。
    /// </summary>
    public static int RmProcessInfoSize => Marshal.SizeOf<NativeMethods.RmProcessInfo>();

    public static List<FileLockInfo> FindByRestartManager(string fullPath)
    {
        var results = new List<FileLockInfo>();
        if (fullPath.Length >= 260) return results;   // RM 受 MAX_PATH 限制

        // 会话键必须是唯一字符串，长度不超过 CCH_RM_SESSION_KEY+1(33)，用 32 位无连字符 GUID
        string sessionKey = Guid.NewGuid().ToString("N");
        if (NativeMethods.RmStartSession(out uint session, 0, sessionKey) != 0)
            return results;

        try
        {
            string[] resources = { fullPath };
            if (NativeMethods.RmRegisterResources(session, (uint)resources.Length, resources, 0, null, 0, null) != 0)
                return results;

            uint needed = 0;
            uint count = 0;
            int rc = NativeMethods.RmGetList(session, out needed, ref count, null, out _);

            if (rc == (int)NativeMethods.ERROR_MORE_DATA && needed > 0)
            {
                var infos = new NativeMethods.RmProcessInfo[needed];
                count = needed;
                rc = NativeMethods.RmGetList(session, out needed, ref count, infos, out _);
                if (rc != 0) return results;

                var names = ProcessInfoService.GetProcessNames();

                for (int i = 0; i < count && i < infos.Length; i++)
                {
                    var info = infos[i];
                    var appName = info.strAppName ?? string.Empty;

                    results.Add(new FileLockInfo
                    {
                        Pid = info.Process.dwProcessId,
                        // 统一与端口页一致的进程名；RM 的 appName 作为兜底
                        ProcessName = names.TryGetValue(info.Process.dwProcessId, out var name)
                            ? name
                            : string.IsNullOrWhiteSpace(appName) ? "未知进程" : Path.GetFileName(appName),
                        Source = "重启管理器",
                        AppName = appName,
                        ServiceName = string.IsNullOrWhiteSpace(info.strServiceShortName)
                            ? null
                            : info.strServiceShortName,
                    });
                }
            }
        }
        catch
        {
            // 重启管理器异常（服务被禁用等）时静默降级到句柄扫描
        }
        finally
        {
            NativeMethods.RmEndSession(session);
        }

        return results;
    }

    // ── 句柄深度扫描 ───────────────────────────────────────────────────

    public static List<FileLockInfo> FindByHandleScan(
        string fullPath, IProgress<string>? progress = null, int timeoutMs = 25000)
    {
        var targets = BuildDevicePathCandidates(fullPath);
        if (targets.Count == 0)
            throw new InvalidOperationException("无法解析该路径所在的磁盘卷，暂不支持该类型的路径。");

        bool isDirectory = Directory.Exists(fullPath);
        var results = new List<FileLockInfo>();

        progress?.Report("正在创建探针句柄...");

        // 用一个自己打开的临时文件反推 “File” 对象类型索引，避免写死 36/37（不同 Windows 版本会变）
        string probePath = Path.Combine(Path.GetTempPath(), "pm_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
        using var probe = new FileStream(
            probePath, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose);

        IntPtr probeHandle = probe.SafeFileHandle.DangerousGetHandle();

        progress?.Report("正在枚举系统句柄...");
        var handles = EnumerateFileHandles(probeHandle, out int totalHandles, out int fileTypeIndex);

        progress?.Report($"系统句柄 {totalHandles} 个 · 文件类 {handles.Count} 个，正在比对路径...");

        var names = ProcessInfoService.GetProcessNames();
        var stopwatch = Stopwatch.StartNew();
        int processed = 0;
        bool timedOut = false;

        foreach (var group in handles.GroupBy(h => (int)h.UniqueProcessId))
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                timedOut = true;
                break;
            }

            int pid = group.Key;
            IntPtr processHandle = NativeMethods.OpenProcess(NativeMethods.PROCESS_DUP_HANDLE, false, pid);
            if (processHandle == IntPtr.Zero) continue;   // 权限不足或进程已退出

            try
            {
                foreach (var entry in group)
                {
                    processed++;

                    if (!NativeMethods.DuplicateHandle(
                            processHandle, entry.HandleValue, NativeMethods.GetCurrentProcess(),
                            out IntPtr duplicated, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                        continue;

                    try
                    {
                        // 命名管道/控制台也属于 File 类型：先用 GetFileType 过滤，
                        // 既排除无关句柄，也规避 NtQueryObject 在同步管道上可能卡死的问题
                        if (NativeMethods.GetFileType(duplicated) != NativeMethods.FILE_TYPE_DISK)
                            continue;

                        string? objectPath = QueryObjectName(duplicated);
                        if (objectPath is null) continue;
                        if (!MatchesTarget(objectPath, targets, isDirectory)) continue;

                        names.TryGetValue(pid, out var processName);
                        results.Add(new FileLockInfo
                        {
                            Pid = pid,
                            ProcessName = processName ?? "无法访问",
                            Source = "句柄扫描",
                            HandleValue = (ulong)entry.HandleValue.ToInt64(),
                            MatchedPath = objectPath,
                        });
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(duplicated);
                    }
                }
            }
            finally
            {
                NativeMethods.CloseHandle(processHandle);
            }
        }

        progress?.Report(timedOut
            ? $"扫描超时（已检查 {processed} 个句柄），结果可能不完整。"
            : $"扫描完成：检查 {processed} 个文件句柄，命中 {results.Count} 条。");

        if (fileTypeIndex < 0)
            progress?.Report("警告：未能识别文件对象类型，已退化为全量过滤，结果可能偏慢。");

        return results;
    }

    /// <summary>
    /// 枚举系统句柄，只保留 “File” 类型，同时通过探针句柄反推类型索引。
    /// </summary>
    private static List<NativeMethods.SystemHandleEntry> EnumerateFileHandles(
        IntPtr probeHandle, out int totalHandles, out int fileTypeIndex)
    {
        totalHandles = 0;
        fileTypeIndex = -1;

        var collected = new List<NativeMethods.SystemHandleEntry>(4096);

        int size = 1 << 20;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            while (true)
            {
                if (buffer == IntPtr.Zero) buffer = Marshal.AllocHGlobal(size);

                uint status = NativeMethods.NtQuerySystemInformation(
                    NativeMethods.SystemExtendedHandleInformation, buffer, size, out int needed);

                if (status == NativeMethods.ERROR_SUCCESS) break;

                if (status is NativeMethods.STATUS_INFO_LENGTH_MISMATCH
                    or NativeMethods.STATUS_BUFFER_OVERFLOW
                    or NativeMethods.STATUS_BUFFER_TOO_SMALL)
                {
                    Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                    size = Math.Max(size * 2, needed + (1 << 16));
                    continue;
                }

                throw new InvalidOperationException($"枚举系统句柄失败 (NTSTATUS 0x{status:X8})");
            }

            long count = Marshal.ReadIntPtr(buffer).ToInt64();
            totalHandles = count > int.MaxValue ? int.MaxValue : (int)count;

            int entrySize = Marshal.SizeOf<NativeMethods.SystemHandleEntry>();
            int selfPid = Environment.ProcessId;
            IntPtr cursor = buffer + IntPtr.Size * 2;

            // 第一遍：找到自己打开探针文件的句柄，读出 File 类型索引
            for (long i = 0; i < count; i++, cursor += entrySize)
            {
                var entry = Marshal.PtrToStructure<NativeMethods.SystemHandleEntry>(cursor);
                if ((int)entry.UniqueProcessId == selfPid && entry.HandleValue == probeHandle)
                {
                    fileTypeIndex = entry.ObjectTypeIndex;
                    break;
                }
            }

            // 第二遍：只收文件类句柄。类型索引未知时全收，由 GetFileType 兜底过滤
            cursor = buffer + IntPtr.Size * 2;
            for (long i = 0; i < count; i++, cursor += entrySize)
            {
                var entry = Marshal.PtrToStructure<NativeMethods.SystemHandleEntry>(cursor);
                if (fileTypeIndex >= 0 && entry.ObjectTypeIndex != fileTypeIndex) continue;
                collected.Add(entry);
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }

        return collected;
    }

    /// <summary>
    /// 查询句柄对应的对象名（\Device\HarddiskVolumeX\...）。
    /// </summary>
    private static string? QueryObjectName(IntPtr handle)
    {
        int size = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            uint status = NativeMethods.NtQueryObject(
                handle, NativeMethods.ObjectNameInformation, buffer, size, out int needed);

            if (status is NativeMethods.STATUS_BUFFER_OVERFLOW or NativeMethods.STATUS_INFO_LENGTH_MISMATCH
                or NativeMethods.STATUS_BUFFER_TOO_SMALL)
            {
                Marshal.FreeHGlobal(buffer);
                size = Math.Max(needed, size * 2);
                buffer = Marshal.AllocHGlobal(size);

                status = NativeMethods.NtQueryObject(
                    handle, NativeMethods.ObjectNameInformation, buffer, size, out _);
            }

            if (status != NativeMethods.ERROR_SUCCESS) return null;

            var unicode = Marshal.PtrToStructure<NativeMethods.UnicodeString>(buffer);
            if (unicode.Buffer == IntPtr.Zero || unicode.Length == 0) return null;

            return Marshal.PtrToStringUni(unicode.Buffer, unicode.Length / 2);
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// 把用户输入的 DOS 路径转换成句柄里出现的设备路径。
    /// </summary>
    private static List<string> BuildDevicePathCandidates(string fullPath)
    {
        var candidates = new List<string>(2);

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var tail = fullPath.TrimStart('\\');
            candidates.Add(@"\Device\Mup\" + tail);
            candidates.Add(@"\??\UNC\" + tail);
            return candidates;
        }

        string? root = Path.GetPathRoot(fullPath);
        if (root is null || root.Length < 2 || root[1] != ':') return candidates;

        string drive = root[..2];
        var buffer = new StringBuilder(1024);
        if (NativeMethods.QueryDosDevice(drive, buffer, buffer.Capacity) != 0)
        {
            string device = buffer.ToString().Split('\0')[0].TrimEnd('\\');
            if (device.Length > 0)
                candidates.Add(device + fullPath[2..]);
        }

        return candidates;
    }

    private static bool MatchesTarget(string objectPath, List<string> targets, bool isDirectory)
    {
        foreach (var target in targets)
        {
            if (string.Equals(objectPath, target, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(objectPath, target + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            if (isDirectory && objectPath.StartsWith(target + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // ── 强制关闭句柄 ───────────────────────────────────────────────────

    /// <summary>
    /// 强制关闭另一个进程里的句柄（DUPLICATE_CLOSE_SOURCE 会把源进程中的句柄一并关掉）。
    /// 注意：被关闭句柄的程序可能因此崩溃或数据丢失，仅作最后手段。
    /// </summary>
    public static bool CloseRemoteHandle(int pid, ulong handleValue, out string? error)
    {
        error = null;

        IntPtr processHandle = NativeMethods.OpenProcess(NativeMethods.PROCESS_DUP_HANDLE, false, pid);
        if (processHandle == IntPtr.Zero)
        {
            error = "无法打开目标进程（权限不足或进程已退出）";
            return false;
        }

        try
        {
            if (!NativeMethods.DuplicateHandle(
                    processHandle, (IntPtr)(long)handleValue, NativeMethods.GetCurrentProcess(),
                    out IntPtr duplicated, 0, false, NativeMethods.DUPLICATE_CLOSE_SOURCE))
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            NativeMethods.CloseHandle(duplicated);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            NativeMethods.CloseHandle(processHandle);
        }
    }
}
