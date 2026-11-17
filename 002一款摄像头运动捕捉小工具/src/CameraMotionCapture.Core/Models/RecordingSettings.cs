using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

public class RecordingSettings
{
    /// <summary>
    /// 是否允许录像。只管"「录像」按钮能不能点"，程序启动与开始监控都不会自动录制——
    /// 录像必须由人点一次「录像」才开始，这是刻意的：没人看着的时候不该无声无息写满硬盘。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>点「录像」之后的写入方式：持续录 / 只在运动时录</summary>
    public RecordMode Mode { get; set; } = RecordMode.Continuous;

    public string SaveDir { get; set; } = "recordings";

    /// <summary>单个视频文件的最大时长（分钟），到达后自动切片</summary>
    public int SegmentMinutes { get; set; } = Constants.DefaultSegmentMinutes;

    /// <summary>FourCC 编码，默认 MJPG（兼容性最好，无需外部 DLL）</summary>
    public string Codec { get; set; } = Constants.DefaultCodec;

    public int JpegQuality { get; set; } = Constants.DefaultQuality;

    /// <summary>事件录制模式下，一次运动触发后持续录制的秒数</summary>
    public int MotionRecordSeconds { get; set; } = Constants.DefaultMotionRecordSeconds;

    /// <summary>录像目录容量上限（GB），超出后按最旧优先删除。0 = 不限制。</summary>
    public double MaxStorageGb { get; set; } = Constants.DefaultMaxStorageGb;

    /// <summary>自动清理超过该天数的录像/截图</summary>
    public int RetentionDays { get; set; } = Constants.DefaultRetentionDays;

    public bool UseDailyFolder { get; set; } = true;
}
