using OpenCvSharp;
using CameraMotionCapture.Core.Models;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>
/// 存储服务接口 - 文件管理、清理、空间控制
/// </summary>
public interface IStorageService
{
    /// <summary>获取保存目录(按天可选)</summary>
    string GetSaveDirectory(string baseDir, bool useDailyFolder);

    /// <summary>获取截图目录</summary>
    string GetSnapshotDirectory(string baseDir, bool useDailyFolder);

    /// <summary>清理过期文件</summary>
    (int FileCount, long SizeBytes) CleanupOldFiles(string baseDir, int retentionDays);

    /// <summary>按空间阈值清理</summary>
    void CleanupByStorageLimit(string baseDir, double maxStorageGb);

    /// <summary>保存截图</summary>
    string? SaveSnapshot(string baseDir, Mat frame, int quality, bool useDailyFolder, string prefix = "snapshot", OverlaySettings? overlay = null, int cameraId = 0);

    /// <summary>获取总存储空间使用量(bytes)</summary>
    long GetTotalStorageSize(string baseDir);
}