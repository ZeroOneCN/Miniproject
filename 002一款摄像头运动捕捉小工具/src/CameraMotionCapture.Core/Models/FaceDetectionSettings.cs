using CameraMotionCapture.Shared;

namespace CameraMotionCapture.Core.Models;

public class FaceDetectionSettings
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Haar 级联文件。可填相对程序目录的文件名或绝对路径。
    /// exe 旁边放同名文件即可覆盖；都没有时使用内嵌在程序集里的模型
    /// （首次使用释放到 %LOCALAPPDATA%），提取失败则自动跳过人脸检测。
    /// </summary>
    public string CascadeFilePath { get; set; } = Constants.DefaultCascadeFileName;
}
