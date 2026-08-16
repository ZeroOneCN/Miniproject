namespace CameraMotionCapture.Shared;

public static class Constants
{
    public const string AppName = "智能摄像头监控系统";
    public const string AppOrganization = "SecurityMonitor";
    public const string AppVersion = "2.0.0";

    // 默认分辨率
    public static readonly (int Width, int Height) DefaultResolution = (1280, 720);
    public const int DefaultFps = 20;
    public const int DefaultPreviewFps = 10;
    public const string DefaultCodec = "XVID";
    public const int DefaultQuality = 85;
    public const int DefaultMotionThreshold = 1500;
    public const int DefaultRetentionDays = 7;
    public const int DefaultSnapshotInterval = 30;
    public const int DefaultSnapshotCooldown = 15;
    public const double DefaultMaxStorageGb = 0.0;
    public const int DefaultNotificationCooldownMinutes = 5;
    public const int SegmentDurationHours = 1;
    public const int MaxCameraIndex = 10;

    // 配置文件路径
    public const string ConfigFileName = "appsettings.json";
    public const string WebhookConfigFileName = "webhook_config.json";
    public const string LogFileName = "logs/camera-monitor-.log";

    // 人脸识别
    public const double DefaultFaceConfidenceThreshold = 80.0;
    public const string DefaultCascadeFileName = "haarcascade_frontalface_default.xml";
    public const string DefaultKnownFacesDir = "known_faces";
}