using OpenCvSharp;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>文件存储：截图落盘、容量控制、过期清理</summary>
public interface IStorageService
{
    /// <summary>解析（并创建）目标目录，支持按天分文件夹</summary>
    string ResolveDirectory(string baseDir, bool useDailyFolder, string subFolder = "snapshots");

    /// <summary>把帧编码为 JPEG 写入磁盘，返回文件路径；失败返回 null</summary>
    string? SaveSnapshot(Mat frame, string baseDir, int quality, bool useDailyFolder,
                         string prefix = "snapshot", int cameraId = 0);

    /// <summary>删除超过保留天数的录像/截图，返回删除数量与释放字节数</summary>
    (int Count, long Bytes) CleanupExpired(string baseDir, int retentionDays);

    /// <summary>按容量上限清理（最旧优先），返回释放的字节数</summary>
    long EnforceStorageLimit(string baseDir, double maxGb, int keepNewestHours = 1);

    /// <summary>统计目录占用（带短时缓存，避免高频全盘枚举）</summary>
    long GetDirectorySize(string baseDir, bool bypassCache = false);
}
