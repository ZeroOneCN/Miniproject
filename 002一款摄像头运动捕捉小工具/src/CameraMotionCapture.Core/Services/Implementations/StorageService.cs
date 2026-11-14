using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>
/// 文件存储与磁盘治理。
/// 相比旧实现修复了三个实际问题：
/// 1. 截图不再依赖"另画一遍水印"，帧在采集管线里已经叠加完毕，直接编码落盘；
/// 2. 容量上限真正生效（旧代码传入 0 直接 early-return，等于没调用）；
/// 3. 目录体积统计带缓存，避免每帧都全盘枚举。
/// </summary>
public sealed class StorageService : IStorageService
{
    private static readonly string[] MediaExtensions = { ".avi", ".mp4", ".mkv", ".jpg", ".jpeg", ".png" };

    private readonly Dictionary<string, (long Size, DateTime At)> _sizeCache = new();
    private readonly object _cacheLock = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public string ResolveDirectory(string baseDir, bool useDailyFolder, string subFolder = "snapshots")
    {
        var root = string.IsNullOrWhiteSpace(baseDir) ? "recordings" : baseDir;
        if (!Path.IsPathRooted(root))
            root = Path.Combine(AppContext.BaseDirectory, root);

        var dir = useDailyFolder
            ? Path.Combine(root, subFolder, DateTime.Now.ToString("yyyy-MM-dd"))
            : Path.Combine(root, subFolder);

        Directory.CreateDirectory(dir);
        return dir;
    }

    public string? SaveSnapshot(Mat frame, string baseDir, int quality, bool useDailyFolder,
        string prefix = "snapshot", int cameraId = 0)
    {
        if (frame is null || frame.Empty()) return null;

        try
        {
            var dir = ResolveDirectory(baseDir, useDailyFolder);
            var path = Path.Combine(dir, $"{prefix}_cam{cameraId}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");

            var parameters = new[] { (int)ImwriteFlags.JpegQuality, Math.Clamp(quality, 1, 100) };
            Cv2.ImEncode(".jpg", frame, out var buffer, parameters);
            File.WriteAllBytes(path, buffer);

            Log.Debug("截图已保存：{Path}", path);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "保存截图失败");
            return null;
        }
    }

    public (int Count, long Bytes) CleanupExpired(string baseDir, int retentionDays)
    {
        if (retentionDays <= 0) return (0, 0);

        var root = ResolveRoot(baseDir);
        if (!Directory.Exists(root)) return (0, 0);

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        var count = 0;
        long bytes = 0;

        try
        {
            foreach (var file in EnumerateMediaFiles(root))
            {
                try
                {
                    if (file.LastWriteTime >= cutoff) continue;
                    var size = file.Length;
                    file.Delete();
                    count++;
                    bytes += size;
                }
                catch (IOException) { /* 文件正被录制占用，跳过 */ }
            }

            if (count > 0)
            {
                InvalidateCache(root);
                Log.Information("清理过期文件：删除 {Count} 个，释放 {Size:F1} MB",
                    count, bytes / 1024.0 / 1024.0);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "清理过期文件异常");
        }

        return (count, bytes);
    }

    public long EnforceStorageLimit(string baseDir, double maxGb, int keepNewestHours = 1)
    {
        if (maxGb <= 0) return 0;

        var root = ResolveRoot(baseDir);
        if (!Directory.Exists(root)) return 0;

        var limitBytes = (long)(maxGb * 1024 * 1024 * 1024);
        var protectAfter = DateTime.Now.AddHours(-keepNewestHours);

        long freed = 0;
        try
        {
            var files = EnumerateMediaFiles(root).OrderBy(f => f.LastWriteTime).ToList();
            var total = files.Sum(f => f.Length);
            if (total <= limitBytes) return 0;

            foreach (var file in files)
            {
                if (total <= limitBytes) break;
                if (file.LastWriteTime > protectAfter) continue;   // 保护正在录制的最新文件

                try
                {
                    var size = file.Length;
                    file.Delete();
                    total -= size;
                    freed += size;
                }
                catch (IOException) { }
            }

            if (freed > 0)
            {
                InvalidateCache(root);
                Log.Warning("磁盘配额触发：删除最旧文件释放 {Size:F1} MB（上限 {Limit:F1} GB）",
                    freed / 1024.0 / 1024.0, maxGb);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "容量清理异常");
        }

        return freed;
    }

    public long GetDirectorySize(string baseDir, bool bypassCache = false)
    {
        var root = ResolveRoot(baseDir);
        if (!Directory.Exists(root)) return 0;

        lock (_cacheLock)
        {
            if (!bypassCache && _sizeCache.TryGetValue(root, out var cached)
                && DateTime.UtcNow - cached.At < CacheTtl)
            {
                return cached.Size;
            }
        }

        long size = 0;
        try
        {
            size = EnumerateMediaFiles(root).Sum(f => f.Length);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "统计目录体积失败：{Root}", root);
        }

        lock (_cacheLock) _sizeCache[root] = (size, DateTime.UtcNow);
        return size;
    }

    // ───────────────────────────── 内部实现 ─────────────────────────────

    private static string ResolveRoot(string baseDir)
    {
        var root = string.IsNullOrWhiteSpace(baseDir) ? "recordings" : baseDir;
        return Path.IsPathRooted(root) ? root : Path.Combine(AppContext.BaseDirectory, root);
    }

    private static IEnumerable<FileInfo> EnumerateMediaFiles(string root)
    {
        var info = new DirectoryInfo(root);
        if (!info.Exists) yield break;

        foreach (var file in info.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (MediaExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                yield return file;
        }
    }

    private void InvalidateCache(string root)
    {
        lock (_cacheLock) _sizeCache.Remove(root);
    }
}
