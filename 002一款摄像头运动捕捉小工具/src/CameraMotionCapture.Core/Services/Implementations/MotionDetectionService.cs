using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>
/// 帧差法运动检测。
///
/// 相比旧实现的关键改动：
/// 1. <b>降采样</b>：先把帧缩到 320 宽再算，720p 下开销从一个数量级降到 1ms 级；
/// 2. <b>状态内聚</b>：上一帧灰度图由本服务缓存，调用方不再需要额外 Clone 一整帧；
/// 3. <b>不做轮廓检索</b>：直接统计前景像素数，比 FindContours + ContourArea 快得多；
/// 4. <b>灵敏度真正生效</b>：1~100 映射到实际面积阈值。
/// </summary>
public sealed class MotionDetectionService : IMotionDetectionService
{
    private Mat? _prevGray;
    private int _sensitivity = Constants.DefaultSensitivity;

    /// <summary>连续命中计数，用于抑制单帧噪声</summary>
    private int _hitStreak;
    private const int HitStreakRequired = 2;

    public int Sensitivity
    {
        get => _sensitivity;
        set => _sensitivity = Math.Clamp(value, 1, 100);
    }

    /// <summary>
    /// 灵敏度 → 面积阈值（降采样后像素数）。
    /// 灵敏度 100 → 120 px（极敏感）；灵敏度 1 → 12000 px（几乎只认大动作）。
    /// </summary>
    public int AreaThreshold
    {
        get
        {
            var ratio = (_sensitivity - 1) / 99.0;              // 0..1
            var span = Constants.MotionMaxArea - Constants.MotionMinArea;
            return (int)(Constants.MotionMaxArea - span * ratio);
        }
    }

    public bool Detect(Mat frame, Rect? region = null)
    {
        if (frame is null || frame.Empty()) return false;

        try
        {
            // 有检测区域时只裁出这块：马路、树影、窗帘在区域外怎么动都不算
            using var cropped = CropToRegion(frame, region);
            using var small = Resize(cropped);
            using var gray = ToBlurredGray(small);

            if (_prevGray is null || _prevGray.Size() != gray.Size())
            {
                gray.CopyTo(_prevGray = new Mat());
                _hitStreak = 0;
                return false;   // 第一帧只作为基准
            }

            using var diff = new Mat();
            Cv2.Absdiff(gray, _prevGray, diff);
            gray.CopyTo(_prevGray);

            using var mask = new Mat();
            Cv2.Threshold(diff, mask, 25, 255, ThresholdTypes.Binary);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.Dilate(mask, mask, kernel, iterations: 1);

            var foreground = Cv2.CountNonZero(mask);

            if (foreground >= AreaThreshold)
            {
                _hitStreak++;
            }
            else
            {
                _hitStreak = 0;
            }

            return _hitStreak >= HitStreakRequired;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "运动检测异常");
            return false;
        }
    }

    public void Reset()
    {
        _prevGray?.Dispose();
        _prevGray = null;
        _hitStreak = 0;
    }

    public void Dispose() => Reset();

    // ───────────────────────────── 内部实现 ─────────────────────────────

    /// <summary>按像素矩形裁出检测区域；没设区域就整帧参与。</summary>
    private static Mat CropToRegion(Mat frame, Rect? region)
    {
        if (region is null) return frame.Clone();

        var rect = region.Value;
        rect.X = Math.Clamp(rect.X, 0, Math.Max(0, frame.Width - 2));
        rect.Y = Math.Clamp(rect.Y, 0, Math.Max(0, frame.Height - 2));
        rect.Width = Math.Clamp(rect.Width, 2, Math.Max(2, frame.Width - rect.X));
        rect.Height = Math.Clamp(rect.Height, 2, Math.Max(2, frame.Height - rect.Y));

        using var view = new Mat(frame, rect);
        return view.Clone();
    }

    private static Mat Resize(Mat frame)    {
        var targetWidth = Math.Min(Constants.MotionDetectWidth, frame.Width);
        if (frame.Width <= targetWidth) return frame.Clone();

        var scale = targetWidth / (double)frame.Width;
        var size = new Size(targetWidth, Math.Max(1, (int)(frame.Height * scale)));

        var resized = new Mat();
        Cv2.Resize(frame, resized, size, interpolation: InterpolationFlags.Area);
        return resized;
    }

    private static Mat ToBlurredGray(Mat frame)
    {
        var gray = new Mat();
        if (frame.Channels() == 3)
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        else
            frame.CopyTo(gray);

        // 9×9 足够抹掉传感器噪点，在 320 宽的图上开销可忽略
        Cv2.GaussianBlur(gray, gray, new Size(9, 9), 0);
        return gray;
    }
}
