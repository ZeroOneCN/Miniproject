using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ScreenOffManager.Services;
using ScreenOffManager.ViewModels;

namespace ScreenOffManager;

/// <summary>
/// 应用入口：单实例、全局异常兜底、托盘生命周期。
/// 关闭窗口只是隐藏，退出必须走托盘菜单（或再次点击托盘「退出」）。
/// </summary>
public partial class App
{
    private Mutex? _mutex;
    private TrayService? _tray;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private AppSettings _settings = new();
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例：第二次启动直接提示并退出，避免多开互相抢电源设置
        _mutex = new Mutex(true, @"Local\ScreenOffManager.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show("熄屏管家已经在运行了，请查看系统托盘。", "熄屏管家",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        RegisterExceptionHandlers();
        base.OnStartup(e);

        _settings = AppSettings.Load();
        Log.Info($"启动：管理员={PowerSettingsService.IsAdmin()}");

        _window = new MainWindow(_settings);
        _viewModel = _window.ViewModel;

        _tray = new TrayService(StartupManager.IsEnabled);
        _tray.OpenRequested += ShowMainWindow;
        _tray.PresetRequested += minutes =>
            Dispatcher.InvokeAsync(() => _ = _viewModel.ApplyPresetAsync(minutes, showDialog: false));
        _tray.AutoStartToggled += enabled =>
            Dispatcher.InvokeAsync(() => _viewModel.IsAutoStart = enabled);
        _tray.ExitRequested += ExitApplication;

        _viewModel.BalloonRequested += (title, text) =>
            _tray.ShowTip(title, text, warning: title.Contains("失败"));

        // 主界面勾选变化 → 同步托盘菜单的勾选状态
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsAutoStart))
                _tray.SyncAutoStart(_viewModel.IsAutoStart);
        };

        // 关窗 = 收进托盘
        _window.HideRequested += (_, _) =>
        {
            _window.Hide();

            if (!_settings.TrayTipShown)
            {
                _tray.ShowTip("熄屏管家仍在运行", "关闭窗口不会退出程序。右键托盘图标可以快速切换，菜单里可以退出。");
                _settings.TrayTipShown = true;
                _settings.Save();
            }
        };

        var startMinimized = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        if (startMinimized)
        {
            _tray.ShowTip("熄屏管家已启动", "程序在托盘后台运行，双击图标打开主界面。");
        }
        else
        {
            ShowMainWindow();
        }
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

    private void ShowMainWindow()
    {
        if (_window is null) return;

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;

        _settings.Save();
        _tray?.Dispose();
        _window?.AllowClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();

        try { _mutex?.Dispose(); } catch { /* 忽略 */ }

        base.OnExit(e);
    }
}
