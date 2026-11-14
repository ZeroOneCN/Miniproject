using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

/// <summary>摄像头枚举选项</summary>
public sealed class CameraDiscoveryOptions
{
    /// <summary>采集后端。默认 <c>Auto</c>：DirectShow 枚举（带设备名）。</summary>
    public CaptureBackend Backend { get; init; } = CaptureBackend.Auto;

    /// <summary>探测的最大设备序号</summary>
    public int MaxIndex { get; init; } = 8;
}
