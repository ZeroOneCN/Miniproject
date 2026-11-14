using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

public class RecordingSettings
{
    /// <summary>是否启用录像。默认关闭，避免开箱即写满硬盘。</summary>
    public bool Enabled { get; set; } = false;

    public RecordMode Mode { get; set; } = RecordMode.MotionTrigger;

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
