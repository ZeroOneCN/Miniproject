using CameraMotionCapture.Core.Models;

namespace CameraMotionCapture.Core.Services.Interfaces;

/// <summary>配置读写</summary>
public interface ISettingsService
{
    AppConfig Load();

    /// <summary>原子写入（先写临时文件再替换），避免断电写坏配置</summary>
    void Save(AppConfig config);

    string ConfigPath { get; }
}
