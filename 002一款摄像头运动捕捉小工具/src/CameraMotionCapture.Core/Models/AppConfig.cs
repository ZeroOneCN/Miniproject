using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

/// <summary>
/// 应用全部可持久化配置。
/// 原则：<b>任何出现在这里的字段都必须有真实的消费方</b>，不再保留"仅界面可见"的装饰性开关。
/// </summary>
public class AppConfig
{
    public CameraSettings Camera { get; set; } = new();
    public RecordingSettings Recording { get; set; } = new();
    public MotionDetectionSettings Motion { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public FaceDetectionSettings Face { get; set; } = new();

    /// <summary>补齐反序列化时可能为 null 的段落，避免旧配置文件导致空引用</summary>
    public AppConfig Normalize()
    {
        Camera ??= new CameraSettings();
        Recording ??= new RecordingSettings();
        Motion ??= new MotionDetectionSettings();
        Overlay ??= new OverlaySettings();
        Face ??= new FaceDetectionSettings();
        return this;
    }
}
