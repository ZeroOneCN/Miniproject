namespace CameraMotionCapture.Shared;

public static class Constants
{
    public const string AppName = "摄像头监控台";
    public const string AppOrganization = "CameraMonitor";

    // 采集（默认全高清 + 30fps；驱动不支持时会自动回落到设备能力范围内的格式）
    public const int DefaultWidth = 1920;
    public const int DefaultHeight = 1080;
    public const int DefaultFps = 30;

    /// <summary>UI 渲染目标帧率（与采集帧率解耦，避免画面抖动）</summary>
    public const int RenderFps = 30;

    // 运动检测（基于降采样后的帧计算，单位：像素面积）
    public const int MotionDetectWidth = 320;
    public const int MotionMaxArea = 12_000;
    public const int MotionMinArea = 120;
    public const int DefaultSensitivity = 55;

    // 存储
    public const int DefaultQuality = 85;
    public const string DefaultCodec = "MJPG";
    public const int DefaultSegmentMinutes = 10;
    public const int DefaultRetentionDays = 7;
    public const double DefaultMaxStorageGb = 20;
    public const int DefaultMotionRecordSeconds = 30;
    public const int DefaultNotificationCooldownSeconds = 60;

    // 后台任务
    public const int DiskGuardIntervalSeconds = 300;
    public const int CleanupHour = 3;

    // 文件
    public const string ConfigFileName = "appsettings.json";
    public const string LogFileName = "logs/app-.log";
    public const string DefaultCascadeFileName = "haarcascade_frontalface_default.xml";
    public const string MutexName = @"Global\CameraMonitor.SingleInstance";
}
