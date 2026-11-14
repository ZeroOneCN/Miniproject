using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Implementations;
using CameraMotionCapture.Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CameraMotionCapture.App.Imaging;

/// <summary>
/// 采集管线工厂。集中处理"每路摄像头一套独立服务实例"的组装，
/// 让 ViewModel 不再出现 <c>new CameraService()</c> 这类硬编码依赖。
/// </summary>
public sealed class CameraPipelineFactory
{
    private readonly IServiceProvider _provider;

    public CameraPipelineFactory(IServiceProvider provider) => _provider = provider;

    public CameraPipeline Create(int cameraId, string? url, string name, AppConfig config)
    {
        return new CameraPipeline(
            cameraId,
            url,
            name,
            config,
            _provider.GetRequiredService<ICameraService>(),
            _provider.GetRequiredService<IMotionDetectionService>(),
            _provider.GetRequiredService<IFaceDetectionService>(),
            new VideoRecordingService(cameraId),          // 每路独立的编码器状态
            _provider.GetRequiredService<IStorageService>(),
            _provider.GetRequiredService<INotificationService>());
    }
}
