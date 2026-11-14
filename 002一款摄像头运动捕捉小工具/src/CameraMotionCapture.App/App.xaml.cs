using System.IO;
using System.Windows;
using CameraMotionCapture.App.Imaging;
using CameraMotionCapture.App.ViewModels;
using CameraMotionCapture.App.Views;
using CameraMotionCapture.Core.Services.Implementations;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace CameraMotionCapture.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // ── 单实例：两个进程抢同一个 USB 摄像头只会双双失败 ──
        _singleInstanceMutex = new Mutex(true, Constants.MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show($"{Constants.AppName} 已在运行中。", Constants.AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ConfigureOpenCvRuntime();
        ConfigureLogging();
        RegisterGlobalExceptionHandlers();

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();

            Log.Information("{App} 启动", Constants.AppName);

            var window = ServiceProvider.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "程序启动失败");
            MessageBox.Show($"程序启动失败：{ex.Message}", "启动错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    private static void ConfigureServices(IServiceCollection services)
    {
        // 基础设施（全局单例）
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<INotificationService, WeChatNotificationService>();
        services.AddSingleton<ICameraDiscovery, CameraDiscovery>();

        // 每路摄像头需要各自的实例
        services.AddTransient<ICameraService, CameraService>();
        services.AddTransient<IMotionDetectionService, MotionDetectionService>();
        services.AddTransient<IFaceDetectionService, FaceDetectionService>();

        // 应用层
        services.AddSingleton<CameraPipelineFactory>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    /// <summary>
    /// 限制 FFmpeg 的 RTSP 读超时，避免网络摄像头掉线时 Read() 长时间挂死。
    /// 必须在任何 VideoCapture 创建之前设置。
    /// </summary>
    private static void ConfigureOpenCvRuntime()
    {
        try
        {
            Environment.SetEnvironmentVariable(
                "OPENCV_FFMPEG_CAPTURE_OPTIONS",
                "rtsp_transport;tcp|stimeout;5000000");

            // 探测不存在的设备索引时 OpenCV 会往控制台刷大量 ERROR，
            // 这类信息我们已用自己的探测结果覆盖，无需再输出
            Environment.SetEnvironmentVariable("OPENCV_LOG_LEVEL", "SILENT");

            OpenCvSharp.Cv2.SetNumThreads(Math.Clamp(Environment.ProcessorCount - 1, 1, 8));
        }
        catch { }
    }

    private static void ConfigureLogging()
    {
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(logDir, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 20 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "未处理的 AppDomain 异常");
        };

        DispatcherUnhandledException += (_, args) =>
        {
            // 记录后吞掉：单帧处理失败不应让整个监控中断
            Log.Error(args.Exception, "UI 线程未处理异常");
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Warning(args.Exception, "未观察的任务异常");
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            (ServiceProvider?.GetService(typeof(MainViewModel)) as MainViewModel)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "退出清理异常");
        }

        Log.Information("{App} 退出", Constants.AppName);
        Log.CloseAndFlush();

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
