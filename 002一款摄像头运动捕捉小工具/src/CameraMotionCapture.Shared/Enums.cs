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

/// <summary>摄像头运行状态</summary>
public enum CameraState
{
    Idle,
    Connecting,
    Online,
    Retrying,
    Failed
}
