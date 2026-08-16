using System.Text.Json;
using System.Security.Cryptography;
using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Core.Models;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

public class WeChatNotificationService : INotificationService
{
    private string? _webhookUrl;
    private readonly HttpClient _httpClient;

    public bool IsConfigured => !string.IsNullOrEmpty(_webhookUrl);

    public WeChatNotificationService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public void Configure(string webhookUrl)
    {
        _webhookUrl = webhookUrl;
    }

    public async Task<bool> SendMotionNotificationAsync(Mat frame, string snapshotDir, int quality, bool useDailyFolder)
    {
        if (!IsConfigured || frame == null) return false;

        try
        {
            // 保存截图
            var dir = useDailyFolder
                ? Path.Combine(snapshotDir, DateTime.Now.ToString("yyyy-MM-dd"))
                : snapshotDir;
            Directory.CreateDirectory(dir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var snapshotPath = Path.Combine(dir, $"motion_{timestamp}.jpg");

            var params_ = new int[] { (int)ImwriteFlags.JpegQuality, quality };
            frame.ImWrite(snapshotPath, params_);

            // 读取并编码图片
            var imageBytes = await File.ReadAllBytesAsync(snapshotPath);
            var base64 = Convert.ToBase64String(imageBytes);
            var md5 = Convert.ToHexString(MD5.HashData(imageBytes)).ToLower();

            var message = new
            {
                msgtype = "image",
                image = new
                {
                    base64,
                    md5
                }
            };

            var json = JsonSerializer.Serialize(message);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_webhookUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseBody);
                var errcode = doc.RootElement.GetProperty("errcode").GetInt32();
                if (errcode == 0)
                {
                    Log.Information("企业微信通知已发送 - {Timestamp}", timestamp);
                    return true;
                }
                Log.Warning("企业微信通知发送失败: errcode={Errcode}, body={Body}", errcode, responseBody);
            }
            else
            {
                Log.Warning("企业微信通知HTTP失败: {StatusCode}, {Body}", response.StatusCode, responseBody);
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "发送企业微信通知异常");
            return false;
        }
    }

    public async Task<bool> SendTestMessageAsync()
    {
        if (!IsConfigured) return false;

        try
        {
            var message = new
            {
                msgtype = "text",
                text = new
                {
                    content = "这是一条来自摄像头监控系统的测试消息。"
                }
            };

            var json = JsonSerializer.Serialize(message);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_webhookUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseBody);
                var errcode = doc.RootElement.GetProperty("errcode").GetInt32();
                return errcode == 0;
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "测试企业微信连接异常");
            return false;
        }
    }
}