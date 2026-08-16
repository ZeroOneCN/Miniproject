using System.Text.Json;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

public class SettingsService : ISettingsService
{
    private readonly string _configPath;
    private readonly string _webhookConfigPath;

    public SettingsService()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _configPath = Path.Combine(baseDir, Constants.ConfigFileName);
        _webhookConfigPath = Path.Combine(baseDir, Constants.WebhookConfigFileName);
    }

    public AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null) return config;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载配置文件失败，使用默认配置");
        }

        return new AppConfig();
    }

    public void SaveConfig(AppConfig config)
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_configPath, json);
            Log.Information("配置已保存: {ConfigPath}", _configPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存配置文件失败");
        }
    }

    public string? LoadWebhookUrl()
    {
        try
        {
            if (File.Exists(_webhookConfigPath))
            {
                var json = File.ReadAllText(_webhookConfigPath);
                using var doc = JsonDocument.Parse(json);
                var url = doc.RootElement.GetProperty("webhook_url").GetString();
                return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载Webhook配置失败");
        }
        return null;
    }

    public void SaveWebhookUrl(string url)
    {
        try
        {
            var dir = Path.GetDirectoryName(_webhookConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(new { webhook_url = url }, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_webhookConfigPath, json);
            Log.Information("Webhook配置已保存");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存Webhook配置失败");
        }
    }
}