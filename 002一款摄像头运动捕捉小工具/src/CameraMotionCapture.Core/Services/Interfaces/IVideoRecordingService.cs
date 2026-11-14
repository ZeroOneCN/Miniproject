using OpenCvSharp;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>视频分段录制</summary>
public interface IVideoRecordingService : IDisposable
{
    bool IsRecording { get; }

    /// <summary>当前正在写入的文件完整路径</summary>
    string? CurrentFilePath { get; }

    /// <summary>本次会话已写入的字节数</summary>
    long BytesWritten { get; }

    /// <summary>开始（或续接）录制。返回 false 表示编码器初始化失败。</summary>
    bool Start(string saveDir, int width, int height, double fps, string codec,
               int quality, int segmentMinutes, bool useDailyFolder);

    /// <summary>写入一帧；到达分段时长会自动切片。</summary>
    void WriteFrame(Mat frame);

    /// <summary>结束录制并关闭文件</summary>
    void Stop();
}
