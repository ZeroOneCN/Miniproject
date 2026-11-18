namespace CameraMotionCapture.Shared;

/// <summary>录制模式 —— 只保留两种真正实现的模式</summary>
public enum RecordMode
{
    /// <summary>全时录制：监控期间持续分段写盘</summary>
    Continuous,

    /// <summary>事件录制：仅检测到运动后录制一段时间</summary>
    MotionTrigger
}

public enum CaptureBackend
{
    Auto,
    DShow,
    Msmf,
    Any
}

/// <summary>录像画质档位。决定分辨率上限、写入帧率与 JPEG 质量，直接影响磁盘占用。</summary>
public enum RecordingQuality
{
    /// <summary>高清：保持摄像头原始分辨率与帧率</summary>
    High,

    /// <summary>平衡：最长边 1280、15fps（默认）</summary>
    Balanced,

    /// <summary>省空间：最长边 800、8fps</summary>
    Saver
}

/// <summary>摄像头运行状态</summary>
public enum CameraState
{
    Idle,
    Connecting,
    Online,
    Retrying,
    Failed
}
