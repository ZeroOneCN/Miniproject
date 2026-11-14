using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>
/// 企业微信机器人推送。
/// 相比旧实现：图片在内存中直接编码为 JPEG，不再"写盘 → 读回 → 转 Base64"绕一圈磁盘。
/// </summary>
public sealed class WeChatNotificationService : INotificationService, IDisposable
{
    private readonly HttpClient _httpClient;
    private string? _webhookUrl;

    public WeChatNotificationService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_webhookUrl);

    public void Configure(string? webhookUrl)
    {
        _webhookUrl = string.IsNullOrWhiteSpace(webhookUrl) ? null : webhookUrl.Trim();
    }

    public async Task<bool> SendMotionAlertAsync(Mat frame, int cameraId, string cameraName, string? note = null)
    {
        if (!IsConfigured || frame is null || frame.Empty()) return false;

        try
        {
            // 有附注时先发一条文字，让通知在群列表里可读（纯图片看不出是哪一路）
            if (!string.IsNullOrWhiteSpace(note))
                await PostAsync(new { msgtype = "text", text = new { content = note } }).ConfigureAwait(false);

            var (base64, md5) = EncodeJpeg(frame, quality: 80);
            var ok = await PostAsync(new { msgtype = "image", image = new { base64, md5 } }).ConfigureAwait(false);

            if (ok) Log.Information("运动告警已推送到企业微信（摄像头 {Id}）", cameraId);
            return ok;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "推送企业微信告警异常");
            return false;
        }
    }

    public async Task<bool> SendTestMessageAsync()
    {
        if (!IsConfigured) return false;

        return await PostAsync(new
        {
            msgtype = "text",
            text = new { content = $"{Constants.AppName} · Webhook 连接测试成功。" }
        }).ConfigureAwait(false);
    }

    public void Dispose() => _httpClient.Dispose();

    // ───────────────────────────── 内部实现 ─────────────────────────────

    private async Task<bool> PostAsync(object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(_webhookUrl, content).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("企业微信接口 HTTP 失败：{Status}", (int)response.StatusCode);
                return false;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errcode", out var code) && code.GetInt32() == 0)
                return true;

            Log.Warning("企业微信接口返回错误：{Body}", body);
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "企业微信接口调用异常");
            return false;
        }
    }

    private static (string Base64, string Md5) EncodeJpeg(Mat frame, int quality)
    {
        var parameters = new[] { (int)ImwriteFlags.JpegQuality, Math.Clamp(quality, 1, 100) };
        Cv2.ImEncode(".jpg", frame, out var buffer, parameters);

        var base64 = Convert.ToBase64String(buffer);
        var md5 = Convert.ToHexString(MD5.HashData(buffer)).ToLowerInvariant();
        return (base64, md5);
    }
}
