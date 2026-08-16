using OpenCvSharp;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>
/// 通知服务接口 - 企业微信Webhook通知
/// </summary>
public interface INotificationService
{
    /// <summary>是否已配置通知</summary>
    bool IsConfigured { get; }

    /// <summary>设置Webhook URL</summary>
    void Configure(string webhookUrl);

    /// <summary>发送运动检测通知(含图片)</summary>
    Task<bool> SendMotionNotificationAsync(Mat frame, string snapshotDir, int quality, bool useDailyFolder);

    /// <summary>发送测试消息</summary>
    Task<bool> SendTestMessageAsync();
}