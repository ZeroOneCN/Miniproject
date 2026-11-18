using OpenCvSharp;
using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>录像画质档位对应的输出规格。</summary>
public readonly record struct RecordingProfile(int MaxWidth, int MaxFps, int JpegQuality)
{
    /// <summary>0 表示"跟采集一致"，不做任何缩放与降帧。</summary>
    public bool IsNative => MaxWidth <= 0;
}

/// <summary>一次录像会话的规格（由配置转换而来，管线只需传这个）。</summary>
public sealed class RecordingOptions
{
    public string SaveDir { get; set; } = "recordings";
    public RecordingQuality Quality { get; set; } = RecordingQuality.Balanced;

    public int SegmentMinutes { get; set; } = 60;

    /// <summary>运动触发录制时，把触发之前的这么多秒也写进去（环形缓冲）。</summary>
    public int PreRollSeconds { get; set; } = 5;

    public bool UseDailyFolder { get; set; } = true;

    /// <summary>文件名前缀，用来区分常规录制与事件录制。</summary>
    public string FileNamePrefix { get; set; } = "rec";

    public RecordingProfile Profile => Quality switch
    {
        // 高清：原始分辨率 + 原始帧率，只做轻度压缩
        RecordingQuality.High => new RecordingProfile(0, 0, 88),

        // 平衡：960 宽、12fps、质量 68 —— 看清"谁、在做什么"，每小时几百 MB
        RecordingQuality.Balanced => new RecordingProfile(960, 12, 68),

        // 省空间：640 宽、8fps、质量 58 —— 只看"有没有人、什么时候"足够
        _ => new RecordingProfile(640, 8, 58)
    };
}

/// <summary>视频分段录制（MJPEG AVI，画质可控，支持预录环形缓冲）。</summary>
public interface IVideoRecordingService : IDisposable
{
    bool IsRecording { get; }

    /// <summary>当前正在写入的文件完整路径</summary>
    string? CurrentFilePath { get; }

    /// <summary>当前分段的字节数</summary>
    long BytesWritten { get; }

    /// <summary>预录缓冲里已有的帧数</summary>
    int PreRollFrames { get; }

    /// <summary>更新录制规格（画质、分段、预录秒数等）。不改动正在写的文件。</summary>
    void Configure(RecordingOptions options);

    /// <summary>
    /// 每帧调用一次。<paramref name="recordNow"/> 为 true 表示这一帧该写盘；
    /// <paramref name="keepPreRoll"/> 为 true 时把这一帧压进预录环形缓冲（为运动触发回溯做准备）。
    /// </summary>
    void Feed(Mat frame, bool recordNow, bool keepPreRoll = false);

    /// <summary>结束录制并关闭文件</summary>
    void Stop();
}
