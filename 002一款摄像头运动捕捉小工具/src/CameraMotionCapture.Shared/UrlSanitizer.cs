namespace CameraMotionCapture.Shared;

/// <summary>URL 脱敏工具 —— 避免 RTSP 地址里的账号密码进入日志与界面</summary>
public static class UrlSanitizer
{
    /// <summary>
    /// 把 <c>rtsp://admin:123456@192.168.1.10:554/stream</c>
    /// 变成 <c>rtsp://***@192.168.1.10:554/stream</c>。
    /// 不含凭证的地址原样返回。
    /// </summary>
    public static string Mask(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return url;

        var at = url.IndexOf('@', schemeEnd + 3);
        if (at < 0) return url;

        return string.Concat(url.AsSpan(0, schemeEnd + 3), "***@", url.AsSpan(at + 1));
    }
}
