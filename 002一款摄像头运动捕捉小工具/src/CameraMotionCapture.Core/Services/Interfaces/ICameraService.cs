using OpenCvSharp;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Implementations;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>摄像头设备访问 —— 只负责"把帧读出来"，不含任何业务逻辑。</summary>
public interface ICameraService : IDisposable
{
    bool IsOpened { get; }
    int FrameWidth { get; }
    int FrameHeight { get; }

    /// <summary>驱动回报的帧率（可能高于实测值，仅供参考）</summary>
    double NegotiatedFps { get; }

    /// <summary>协商到的像素格式，如 MJPG / YUY2；取不到时返回 "—"</summary>
    string FourCc { get; }

    /// <summary>实际打开成功的采集后端，如 DSHOW / MSMF</summary>
    string BackendName { get; }

    /// <summary>打开本地/USB 摄像头</summary>
    bool Open(int cameraId, string? backend = null, int width = 1280, int height = 720, int fps = 20, bool hardwareAccel = false);

    /// <summary>通过 RTSP/HTTP 地址打开网络摄像头</summary>
    bool Open(string url, int width = 1280, int height = 720, int fps = 20);

    /// <summary>读取一帧（调用方负责 Dispose）。失败返回 null。</summary>
    Mat? ReadFrame();

    void Close();
}

/// <summary>摄像头枚举（与具体设备实例无关，可安全并发调用）</summary>
public interface ICameraDiscovery
{
    /// <summary>
    /// 枚举可用的本地摄像头（含设备名）。
    /// 默认跳过虚拟摄像头（手机投屏 / 直播软件注册的设备），见 <see cref="CameraDiscoveryOptions"/>。
    /// </summary>
    Task<IReadOnlyList<VideoDevice>> DiscoverAsync(CameraDiscoveryOptions options,
        CancellationToken ct = default);
}
