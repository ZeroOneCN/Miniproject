using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

public class CameraSettings
{
    public CaptureBackend Backend { get; set; } = CaptureBackend.Auto;
    public int Width { get; set; } = Constants.DefaultWidth;
    public int Height { get; set; } = Constants.DefaultHeight;
    public int Fps { get; set; } = Constants.DefaultFps;
    public bool HardwareAccel { get; set; } = false;

    /// <summary>
    /// 画面填充方式：true = 填满面板（裁切溢出部分，不留黑边）；false = 等比适应（完整可见，可能留边）。
    /// 默认填满 —— 监控场景里一屏黑边比少量裁切更难受。
    /// </summary>
    public bool FillScreen { get; set; } = true;

    public string BackendName => Backend switch
    {
        CaptureBackend.DShow => "DSHOW",
        CaptureBackend.Msmf => "MSMF",
        CaptureBackend.Any => "ANY",
        _ => null!
    };
}
