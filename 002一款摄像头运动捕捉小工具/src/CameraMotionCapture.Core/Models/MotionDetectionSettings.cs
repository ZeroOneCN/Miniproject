using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

public class MotionDetectionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 灵敏度 1~100，数值越大越敏感。
    /// 内部会被换算成实际检测面积阈值（见 <c>MotionDetectionService.AreaThreshold</c>）。
    /// </summary>
    public int Sensitivity { get; set; } = Constants.DefaultSensitivity;

    /// <summary>两次通知之间的最小间隔（秒），避免刷屏</summary>
    public int CooldownSeconds { get; set; } = Constants.DefaultNotificationCooldownSeconds;

    /// <summary>运动触发时自动保存一张截图</summary>
    public bool SnapshotOnMotion { get; set; } = true;

    /// <summary>企业微信机器人 Webhook（为空则不推送）</summary>
    public string? WebhookUrl { get; set; }
}
