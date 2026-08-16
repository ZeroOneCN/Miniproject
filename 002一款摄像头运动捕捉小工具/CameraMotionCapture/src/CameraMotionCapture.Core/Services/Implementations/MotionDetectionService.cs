using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

public class MotionDetectionService : IMotionDetectionService
{
    public int Threshold { get; set; } = 1500;

    public bool DetectMotion(Mat currentFrame, Mat? lastFrame)
    {
        if (lastFrame == null || currentFrame == null)
            return false;

        try
        {
            // 转灰度 + 高斯模糊
            using var grayCurrent = new Mat();
            using var grayLast = new Mat();
            Cv2.CvtColor(currentFrame, grayCurrent, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(grayCurrent, grayCurrent, new Size(21, 21), 0);
            Cv2.CvtColor(lastFrame, grayLast, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(grayLast, grayLast, new Size(21, 21), 0);

            // 帧差
            using var frameDiff = new Mat();
            Cv2.Absdiff(grayCurrent, grayLast, frameDiff);

            using var thresh = new Mat();
            Cv2.Threshold(frameDiff, thresh, 30, 255, ThresholdTypes.Binary);

            // 膨胀
            Cv2.Dilate(thresh, thresh, null, iterations: 2);

            // 查找轮廓
            Cv2.FindContours(thresh, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours == null) return false;

            foreach (var contour in contours)
            {
                if (Cv2.ContourArea(contour) > Threshold)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "运动检测异常");
            return false;
        }
    }

    public void Reset()
    {
        // 无状态需要重置
    }
}