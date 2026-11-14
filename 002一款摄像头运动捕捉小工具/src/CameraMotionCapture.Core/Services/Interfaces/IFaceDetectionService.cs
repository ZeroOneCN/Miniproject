using OpenCvSharp;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>人脸检测（Haar 级联，只做检测与标注，不做身份识别）</summary>
public interface IFaceDetectionService : IDisposable
{
    bool IsReady { get; }

    /// <summary>加载级联分类器；失败返回 false 并记录原因</summary>
    bool Initialize(string? cascadePath = null);

    /// <summary>检测并在帧上绘制人脸框，返回检测到的人脸数</summary>
    int DetectAndDraw(Mat frame);
}
