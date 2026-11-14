using OpenCvSharp;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>
/// 运动检测 —— 内部自行维护"上一帧"状态，调用方只需逐帧喂入。
/// </summary>
public interface IMotionDetectionService : IDisposable
{
    /// <summary>灵敏度 1~100，越大越敏感</summary>
    int Sensitivity { get; set; }

    /// <summary>当前生效的检测面积阈值（由灵敏度换算）</summary>
    int AreaThreshold { get; }

    /// <summary>输入一帧，返回该帧是否判定为有运动</summary>
    bool Detect(Mat frame);

    /// <summary>清除历史帧（摄像头重连后调用，避免误判）</summary>
    void Reset();
}
