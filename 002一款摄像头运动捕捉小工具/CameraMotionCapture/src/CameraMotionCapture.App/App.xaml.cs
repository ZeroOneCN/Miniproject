using System.Windows;
using CameraMotionCapture.Core.Services.Implementations;
using CameraMotionCapture.Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CameraMotionCapture.App;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局异常捕获
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Log.Fatal(ex, "未处理的AppDomain异常");
            MessageBox.Show($"程序发生未处理异常: {ex?.Message}\n\n请查看日志文件获取详细信息。",
                "致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "UI线程未处理异常");
            MessageBox.Show($"UI线程异常: {args.Exception.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // 先初始化日志和DI容器
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/camera-monitor-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();

            Log.Information("智能摄像头监控系统启动");

            // 手动创建主窗口（确保ServiceProvider已就绪）
            var mainWindow = new Views.MainWindow();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "程序启动失败");
            MessageBox.Show($"程序启动失败: {ex.Message}\n\n{ex.StackTrace}",
                "启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }

        base.OnStartup(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<INotificationService, WeChatNotificationService>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}