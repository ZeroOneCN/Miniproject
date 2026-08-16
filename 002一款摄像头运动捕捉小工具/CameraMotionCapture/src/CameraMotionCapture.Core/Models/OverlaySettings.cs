namespace CameraMotionCapture.Core.Models;

public class OverlaySettings
{
    public bool ShowTimestamp { get; set; } = true;
    public bool ShowDeviceName { get; set; } = true;
    public string? WatermarkText { get; set; }
}