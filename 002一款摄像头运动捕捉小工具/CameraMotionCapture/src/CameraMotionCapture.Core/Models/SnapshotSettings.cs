using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

public class SnapshotSettings
{
    public SnapshotMode Mode { get; set; } = SnapshotMode.Disabled;
    public int IntervalSeconds { get; set; } = Constants.DefaultSnapshotInterval;
    public int CooldownSeconds { get; set; } = Constants.DefaultSnapshotCooldown;
}