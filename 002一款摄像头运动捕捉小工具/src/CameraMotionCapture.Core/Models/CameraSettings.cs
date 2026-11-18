using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

/// <summary>
/// 检测区域（相对坐标，0~1）。
/// 用相对坐标存，换分辨率、换窗口大小都不需要重新框。
/// </summary>
public class RegionRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;

    public bool IsFullFrame => X <= 0.001 && Y <= 0.001 && Width >= 0.999 && Height >= 0.999;

    public RegionRect Clone() => new() { X = X, Y = Y, Width = Width, Height = Height };

    /// <summary>规范到 0~1 之间，并保证宽高有最小值（防止误点画出一个零面积区域）</summary>
    public RegionRect Normalize()
    {
        var x = Math.Clamp(X, 0, 1);
        var y = Math.Clamp(Y, 0, 1);
        var w = Math.Clamp(Width, 0.02, 1 - x);
        var h = Math.Clamp(Height, 0.02, 1 - y);
        return new RegionRect { X = x, Y = y, Width = w, Height = h };
    }
}

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

    /// <summary>
    /// 各通道的检测区域（key 形如 "USB #0" / 掩码后的网络地址）。
    /// 只在框选出来的范围内判定运动，能挡掉马路、树影、窗帘这类常年误报源。
    /// </summary>
    public Dictionary<string, RegionRect> Regions { get; set; } = new();

    public string BackendName => Backend switch
    {
        CaptureBackend.DShow => "DSHOW",
        CaptureBackend.Msmf => "MSMF",
        CaptureBackend.Any => "ANY",
        _ => null!
    };
}
