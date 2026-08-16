namespace CameraMotionCapture.Shared;

public enum RecordMode
{
    Continuous,    // 全时录制
    MotionTrigger, // 运动录制
    Scheduled      // 定时录制
}

public enum SnapshotMode
{
    Disabled,   // 关闭
    Manual,     // 按键抓拍
    Motion,     // 运动触发
    Timed       // 定时抓拍
}

public enum CaptureBackend
{
    Auto,
    DShow,
    Msmf,
    Any
}