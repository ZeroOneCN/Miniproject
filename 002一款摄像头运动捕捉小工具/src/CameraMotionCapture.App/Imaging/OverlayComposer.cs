using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using CameraMotionCapture.Core.Models;
using OpenCvSharp;

namespace CameraMotionCapture.App.Imaging;

/// <summary>
/// 水印/时间戳合成器。
///
/// 关键优化：旧实现<b>每帧</b>都要走一遍 GDI+ 建 Font / Bitmap / Graphics，
/// 并把位图逐行 Marshal 回 Mat，720p 下单帧要好几毫秒；
/// 这里改为"文字内容不变就复用上一次渲染好的透明图层"，
/// 每帧只剩一次带掩码的 ROI 拷贝（约 0.2ms）。
/// </summary>
public sealed class OverlayComposer : IDisposable
{
    private static readonly Font TimestampFont = CreateFont(22f, FontStyle.Bold);
    private static readonly Font LabelFont = CreateFont(20f, FontStyle.Regular);
    private static readonly Font WatermarkFont = CreateFont(20f, FontStyle.Regular);

    private readonly object _sync = new();
    private Mat? _layerBgr;
    private Mat? _mask;
    private string _signature = string.Empty;

    public void Compose(Mat frame, string deviceName, OverlaySettings settings)
    {
        if (frame is null || frame.Empty() || settings is null) return;

        try
        {
            lock (_sync)
            {
                var now = DateTime.Now;
                var signature = BuildSignature(deviceName, settings, now);

                if (_layerBgr is null || _layerBgr.Size() != frame.Size() || signature != _signature)
                {
                    Rebuild(frame.Width, frame.Height, deviceName, settings, now);
                    _signature = signature;
                }

                if (_layerBgr is not null && _mask is not null && !_layerBgr.Empty() && !_mask.Empty())
                    _layerBgr.CopyTo(frame, _mask);
            }
        }
        catch
        {
            // 水印失败绝不能让整条采集链路挂掉
        }
    }

    public void Invalidate()
    {
        lock (_sync) _signature = string.Empty;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _layerBgr?.Dispose();
            _mask?.Dispose();
            _layerBgr = null;
            _mask = null;
        }
    }

    // ───────────────────────────── 内部实现 ─────────────────────────────

    private static Font CreateFont(float size, FontStyle style)
    {
        try { return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Pixel); }
        catch { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel); }
    }

    private static string BuildSignature(string deviceName, OverlaySettings s, DateTime now)
    {
        var ts = s.ShowTimestamp ? now.ToString("yyyy-MM-dd HH:mm:ss") : "-";
        return $"{ts}|{s.ShowDeviceName}|{deviceName}|{s.WatermarkText}";
    }

    private void Rebuild(int width, int height, string deviceName, OverlaySettings s, DateTime now)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var y = 14f;

            if (s.ShowTimestamp)
            {
                DrawWithShadow(g, now.ToString("yyyy-MM-dd HH:mm:ss"), TimestampFont, 14f, y);
                y += 32f;
            }

            if (s.ShowDeviceName && !string.IsNullOrWhiteSpace(deviceName))
            {
                DrawWithShadow(g, deviceName, LabelFont, 14f, y);
                y += 28f;
            }

            if (!string.IsNullOrWhiteSpace(s.WatermarkText))
            {
                var text = s.WatermarkText
                    .Replace("{name}", deviceName)
                    .Replace("{time}", now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .Replace("{date}", now.ToString("yyyy-MM-dd"));

                var size = g.MeasureString(text, WatermarkFont);
                var x = Math.Max(8f, (width - size.Width) / 2f);
                DrawWithShadow(g, text, WatermarkFont, x, height - size.Height - 14f);
            }
        }

        var bgra = BitmapToBgraMat(bitmap);

        var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);

        var alpha = new Mat();
        Cv2.ExtractChannel(bgra, alpha, 3);
        var mask = new Mat();
        Cv2.Threshold(alpha, mask, 8, 255, ThresholdTypes.Binary);

        bgra.Dispose();
        alpha.Dispose();

        lock (_sync)
        {
            _layerBgr?.Dispose();
            _mask?.Dispose();
            _layerBgr = bgr;
            _mask = mask;
        }
    }

    /// <summary>白色文字 + 黑色描边阴影，保证在亮色/暗色画面上都清晰</summary>
    private static void DrawWithShadow(Graphics g, string text, Font font, float x, float y)
    {
        using var shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
        using var fill = new SolidBrush(Color.White);

        g.DrawString(text, font, shadow, x + 1.5f, y + 1.5f);
        g.DrawString(text, font, fill, x, y);
    }

    private static Mat BitmapToBgraMat(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4);
            var matStep = (int)mat.Step();
            var rowBytes = bitmap.Width * 4;
            var stride = Math.Abs(data.Stride);

            var rowBuffer = new byte[rowBytes];

            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), rowBuffer, 0, rowBytes);
                Marshal.Copy(rowBuffer, 0, IntPtr.Add(mat.Data, y * matStep), rowBytes);
            }

            return mat;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
