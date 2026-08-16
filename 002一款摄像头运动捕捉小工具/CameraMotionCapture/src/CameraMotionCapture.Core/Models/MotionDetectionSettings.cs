using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

public class MotionDetectionSettings
{
    public bool Enabled { get; set; } = true;
    public int Threshold { get; set; } = Constants.DefaultMotionThreshold;
    public int NotificationCooldownSeconds { get; set; } = 300;
    public string? WebhookUrl { get; set; }
}