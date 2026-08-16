using OpenCvSharp;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>
/// 运动检测服务接口 - 帧差法运动检测
/// </summary>
public interface IMotionDetectionService
{
    /// <summary>运动检测阈值 (面积)</summary>
    int Threshold { get; set; }

    /// <summary>检测当前帧是否有运动</summary>
    bool DetectMotion(Mat currentFrame, Mat? lastFrame);

    /// <summary>重置检测状态</summary>
    void Reset();
}