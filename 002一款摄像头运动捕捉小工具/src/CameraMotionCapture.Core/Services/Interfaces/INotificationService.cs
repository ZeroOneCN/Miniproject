using OpenCvSharp;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>告警推送（企业微信机器人 Webhook）</summary>
public interface INotificationService
{
    bool IsConfigured { get; }

    /// <summary>设置/更新 Webhook 地址；空字符串表示关闭推送</summary>
    void Configure(string? webhookUrl);

    /// <summary>推送运动告警：图片 + 文字说明，图片在内存中直接编码，不落盘</summary>
    Task<bool> SendMotionAlertAsync(Mat frame, int cameraId, string cameraName, string? note = null);

    /// <summary>发送测试消息</summary>
    Task<bool> SendTestMessageAsync();
}
