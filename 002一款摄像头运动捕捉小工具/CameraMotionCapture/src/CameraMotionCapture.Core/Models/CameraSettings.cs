using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

public class CameraSettings
{
    public int CameraId { get; set; } = 0;
    public int ManualCameraId { get; set; } = 0;
    public int DefaultCameraIndex { get; set; } = 0;
    public CaptureBackend Backend { get; set; } = CaptureBackend.Auto;
    public int Width { get; set; } = Constants.DefaultResolution.Width;
    public int Height { get; set; } = Constants.DefaultResolution.Height;
    public int Fps { get; set; } = Constants.DefaultFps;
    public int PreviewFps { get; set; } = Constants.DefaultPreviewFps;
    public bool LowLoadPreview { get; set; } = true;
    public bool HardwareAccel { get; set; } = false;
}