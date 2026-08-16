using OpenCvSharp;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>
/// 视频录制服务接口 - 视频编码、分段录制、文件管理
/// </summary>
public interface IVideoRecordingService : IDisposable
{
    /// <summary>是否正在录制</summary>
    bool IsRecording { get; }

    /// <summary>开始新的视频分段</summary>
    bool StartNewSegment(string saveDir, int width, int height, double fps, string codec, int quality, bool useDailyFolder);

    /// <summary>写入一帧到视频</summary>
    void WriteFrame(Mat frame);

    /// <summary>停止录制并释放资源</summary>
    void Stop();

    /// <summary>压缩视频文件(转换为zip)</summary>
    void CompressVideo(string? videoPath);

    /// <summary>获取当前视频文件路径</summary>
    string? CurrentVideoPath { get; }
}