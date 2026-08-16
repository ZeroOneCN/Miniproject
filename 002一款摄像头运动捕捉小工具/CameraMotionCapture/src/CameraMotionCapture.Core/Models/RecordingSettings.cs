using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

public class RecordingSettings
{
    public string SaveDir { get; set; } = "recordings";
    public string Codec { get; set; } = Constants.DefaultCodec;
    public int Quality { get; set; } = Constants.DefaultQuality;
    public int SegmentDurationSeconds { get; set; } = 3600;
    public RecordMode Mode { get; set; } = RecordMode.Continuous;
    public string ScheduleStart { get; set; } = "08:00";
    public string ScheduleEnd { get; set; } = "20:00";
    public bool UseDailyFolder { get; set; } = true;
    public bool AutoCompress { get; set; } = false;
    public double MaxStorageGb { get; set; } = Constants.DefaultMaxStorageGb;
}