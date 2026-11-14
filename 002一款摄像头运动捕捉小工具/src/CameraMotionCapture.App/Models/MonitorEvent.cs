namespace CameraMotionCapture.App.Models;

/// <summary>一条监控事件（运动触发、摄像头掉线等），用于主界面事件列表。</summary>
public sealed class MonitorEvent
{
    public required DateTime Time { get; init; }
    public required string CameraName { get; init; }
    public required string Message { get; init; }
    public string? SnapshotPath { get; init; }

    public string TimeText => Time.ToString("HH:mm:ss");
    public string FullTimeText => Time.ToString("yyyy-MM-dd HH:mm:ss");
    public bool HasSnapshot => !string.IsNullOrEmpty(SnapshotPath);
}
