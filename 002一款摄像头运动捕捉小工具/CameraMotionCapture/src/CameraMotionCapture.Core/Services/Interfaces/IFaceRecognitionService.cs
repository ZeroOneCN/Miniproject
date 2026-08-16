using OpenCvSharp;

namespace CameraMotionCapture.Core.Services.Interfaces;

public interface IFaceRecognitionService : IDisposable
{
    bool IsReady { get; }
    bool IsEnabled { get; set; }
    double ConfidenceThreshold { get; set; }
    string CascadeFilePath { get; set; }
    string KnownFacesDir { get; set; }

    /// <summary>初始化级联分类器和已知人脸库</summary>
    bool Initialize();

    /// <summary>检测帧中的人脸，返回人脸区域列表</summary>
    Rect[] DetectFaces(Mat frame);

    /// <summary>检测并绘制人脸框，返回检测到的人脸数</summary>
    int DrawFaces(Mat frame);

    /// <summary>加载已知人脸</summary>
    void LoadKnownFaces();
}