using OpenCvSharp;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>
/// 摄像头服务接口 - 管理摄像头设备、采集帧
/// </summary>
public interface ICameraService : IDisposable
{
    /// <summary>当前摄像头是否已打开</summary>
    bool IsOpened { get; }

    /// <summary>当前实际分辨率(宽)</summary>
    int ActualWidth { get; }

    /// <summary>当前实际分辨率(高)</summary>
    int ActualHeight { get; }

    /// <summary>当前实际帧率</summary>
    int ActualFps { get; }

    /// <summary>获取可用摄像头索引列表</summary>
    List<int> GetAvailableCameras();

    /// <summary>打开指定摄像头</summary>
    bool OpenCamera(int cameraId, string? preferredBackend = null, int width = 1280, int height = 720, int fps = 20, bool hardwareAccel = false);

    /// <summary>通过 URL 打开网络摄像头（RTSP/HTTP/MJPEG）</summary>
    bool OpenCamera(string url, int? cameraId = null, int width = 1280, int height = 720, int fps = 20);

    /// <summary>读取一帧画面</summary>
    Mat? ReadFrame();

    /// <summary>关闭摄像头</summary>
    void Close();

    /// <summary>重试打开摄像头</summary>
    bool RetryOpen();
}