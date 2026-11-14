using System.Text.Json;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>配置读写：原子写入 + 自动迁移旧版 webhook_config.json</summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _baseDir;
    private readonly string _legacyWebhookPath;

    public SettingsService()
    {
        _baseDir = AppContext.BaseDirectory;
        ConfigPath = Path.Combine(_baseDir, Constants.ConfigFileName);
        _legacyWebhookPath = Path.Combine(_baseDir, "webhook_config.json");
    }

    public string ConfigPath { get; }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config != null)
                {
                    var normalized = config.Normalize();
                    MigrateLegacyWebhook(normalized);
                    return normalized;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "读取配置失败，使用默认配置");
            TryBackupBrokenConfig();
        }

        var fresh = new AppConfig();
        MigrateLegacyWebhook(fresh);
        return fresh;
    }

    public void Save(AppConfig config)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 先写临时文件再替换，避免写入过程中断电导致配置文件损坏
            var temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, JsonOptions));

            if (File.Exists(ConfigPath)) File.Replace(temp, ConfigPath, null);
            else File.Move(temp, ConfigPath);

            Log.Information("配置已保存：{Path}", ConfigPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存配置失败");
        }
    }

    /// <summary>把旧版本的独立 webhook 文件并入主配置，避免用户重新填写</summary>
    private void MigrateLegacyWebhook(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Motion.WebhookUrl)) return;
        if (!File.Exists(_legacyWebhookPath)) return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_legacyWebhookPath));
            if (doc.RootElement.TryGetProperty("webhook_url", out var url))
            {
                var value = url.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    config.Motion.WebhookUrl = value.Trim();
                    Log.Information("已从旧版 webhook_config.json 迁移 Webhook 配置");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "旧版 webhook 配置迁移失败");
        }
    }

    private void TryBackupBrokenConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var backup = ConfigPath + $".broken-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(ConfigPath, backup);
            Log.Warning("配置文件无法解析，已备份为 {Backup}", backup);
        }
        catch { }
    }
}
