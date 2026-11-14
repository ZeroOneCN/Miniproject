namespace CameraMotionCapture.Core.Models;

public class OverlaySettings
{
    public bool ShowTimestamp { get; set; } = true;
    public bool ShowDeviceName { get; set; } = true;

    /// <summary>自定义水印，支持 {name} / {time} / {date} 占位符</summary>
    public string? WatermarkText { get; set; }
}
