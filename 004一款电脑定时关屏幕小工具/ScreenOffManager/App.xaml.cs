using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ScreenOffManager.Services;

namespace ScreenOffManager;

/// <summary>
/// 应用入口：单实例与全局异常兜底。
/// 关闭主窗口即退出进程，不常驻后台、不驻留托盘。
/// </summary>
public partial class App
{
    private Mutex? _mutex;
    private AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例：第二次启动直接提示并退出，避免多开互相抢电源设置
        _mutex = new Mutex(true, @"Local\ScreenOffManager.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show("熄屏管家已经在运行了，请查看任务栏。", "熄屏管家",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        RegisterExceptionHandlers();
        base.OnStartup(e);

        _settings = AppSettings.Load();
        Log.Info($"启动：管理员={PowerSettingsService.IsAdmin()}");

        MainWindow = new MainWindow(_settings);
        MainWindow.Show();
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("未处理异常（AppDomain）", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("UI 线程未处理异常", e.Exception);

        System.Windows.MessageBox.Show(
            $"程序遇到未预期的问题：\n{e.Exception.Message}\n\n日志目录：\n{Log.LogDirectory}",
            "熄屏管家", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

        // 已记录并提示，不再让进程直接崩溃
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _settings.Save(); } catch { /* 退出阶段忽略 */ }
        try { _mutex?.Dispose(); } catch { /* 忽略 */ }

        base.OnExit(e);
    }
}
