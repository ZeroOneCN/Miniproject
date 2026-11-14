using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

public class CameraSettings
{
    public CaptureBackend Backend { get; set; } = CaptureBackend.Auto;
    public int Width { get; set; } = Constants.DefaultWidth;
    public int Height { get; set; } = Constants.DefaultHeight;
    public int Fps { get; set; } = Constants.DefaultFps;
    public bool HardwareAccel { get; set; } = false;

    public string BackendName => Backend switch
    {
        CaptureBackend.DShow => "DSHOW",
        CaptureBackend.Msmf => "MSMF",
        CaptureBackend.Any => "ANY",
        _ => null!
    };
}
