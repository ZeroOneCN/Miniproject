using System;
using System.Windows;
using System.Windows.Threading;

namespace PortMonitor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 兜底：未处理异常不再直接崩掉，给用户一个可读的提示
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowFatal(args.ExceptionObject as Exception);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatal(e.Exception);
        e.Handled = true;
    }

    private static void ShowFatal(Exception? ex)
    {
        var text = ex is null
            ? "发生未知错误。"
            : $"发生未处理异常：\n\n{ex.GetType().Name}: {ex.Message}";

        MessageBox.Show(text, "端口占用与文件句柄管理", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
