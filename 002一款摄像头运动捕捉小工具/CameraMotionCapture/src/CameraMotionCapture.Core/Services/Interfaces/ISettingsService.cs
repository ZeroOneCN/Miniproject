using CameraMotionCapture.Core.Models;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>
/// 配置服务接口 - 读写JSON配置文件
/// </summary>
public interface ISettingsService
{
    /// <summary>加载完整配置</summary>
    AppConfig LoadConfig();

    /// <summary>保存完整配置</summary>
    void SaveConfig(AppConfig config);

    /// <summary>加载Webhook URL</summary>
    string? LoadWebhookUrl();

    /// <summary>保存Webhook URL</summary>
    void SaveWebhookUrl(string url);
}