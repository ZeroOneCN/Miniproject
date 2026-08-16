namespace CameraMotionCapture.Core.Models;

public class AppConfig
{
    public CameraSettings Camera { get; set; } = new();
    public RecordingSettings Recording { get; set; } = new();
    public MotionDetectionSettings MotionDetection { get; set; } = new();
    public SnapshotSettings Snapshot { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public FaceRecognitionSettings FaceRecognition { get; set; } = new();
    public int RetentionDays { get; set; } = 7;
    public string? NextCleanupDate { get; set; }
}