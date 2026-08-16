using System.Text.Json;
using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Core.Models;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

public class StorageService : IStorageService
{
    public string GetSaveDirectory(string baseDir, bool useDailyFolder)
    {
        var dir = useDailyFolder
            ? Path.Combine(baseDir, DateTime.Now.ToString("yyyy-MM-dd"))
            : baseDir;
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string GetSnapshotDirectory(string baseDir, bool useDailyFolder)
    {
        return GetSaveDirectory(baseDir, useDailyFolder);
    }

    public (int FileCount, long SizeBytes) CleanupOldFiles(string baseDir, int retentionDays)
    {
        if (retentionDays <= 0 || !Directory.Exists(baseDir))
            return (0, 0);

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        int count = 0;
        long totalSize = 0;

        try
        {
            var files = Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTime < cutoff)
                {
                    totalSize += fileInfo.Length;
                    fileInfo.Delete();
                    count++;
                }
            }
            Log.Information("清理过期文件: 删除 {Count} 个文件, 释放 {SizeMB:F2} MB",
                count, totalSize / (1024.0 * 1024.0));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "清理过期文件异常");
        }

        return (count, totalSize);
    }

    public void CleanupByStorageLimit(string baseDir, double maxStorageGb)
    {
        if (maxStorageGb <= 0 || !Directory.Exists(baseDir))
            return;

        var maxBytes = (long)(maxStorageGb * 1024 * 1024 * 1024);
        var files = new DirectoryInfo(baseDir).GetFiles("*", SearchOption.AllDirectories)
            .OrderBy(f => f.LastWriteTime)
            .ToList();

        long totalSize = files.Sum(f => f.Length);
        if (totalSize <= maxBytes) return;

        foreach (var file in files)
        {
            if (totalSize <= maxBytes) break;
            totalSize -= file.Length;
            try { file.Delete(); } catch { }
        }
    }

    public string? SaveSnapshot(string baseDir, Mat frame, int quality, bool useDailyFolder,
        string prefix = "snapshot", OverlaySettings? overlay = null, int cameraId = 0)
    {
        if (frame == null) return null;

        try
        {
            var dir = GetSnapshotDirectory(baseDir, useDailyFolder);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(dir, $"{prefix}_{timestamp}.jpg");

            var outputFrame = ApplyOverlay(frame, overlay, cameraId);
            var params_ = new int[] { (int)ImwriteFlags.JpegQuality, quality };
            outputFrame.ImWrite(path, params_);

            CleanupByStorageLimit(baseDir, 0); // 仅在使用maxStorageGb时触发
            return path;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "保存截图失败");
            return null;
        }
    }

    public long GetTotalStorageSize(string baseDir)
    {
        if (!Directory.Exists(baseDir)) return 0;
        return new DirectoryInfo(baseDir).GetFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }

    private static Mat ApplyOverlay(Mat frame, OverlaySettings? overlay, int cameraId)
    {
        if (overlay == null) return frame.Clone();

        var output = frame.Clone();
        var texts = new List<string>();

        if (overlay.ShowTimestamp)
            texts.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        if (overlay.ShowDeviceName)
            texts.Add($"Camera {cameraId}");
        if (!string.IsNullOrEmpty(overlay.WatermarkText))
            texts.Add(overlay.WatermarkText);

        if (texts.Count == 0) return output;

        int y = 30;
        foreach (var text in texts)
        {
            Cv2.PutText(output, text, new Point(10, y), HersheyFonts.HersheySimplex, 0.7,
                new Scalar(255, 255, 255), 2);
            y += 30;
        }

        return output;
    }
}