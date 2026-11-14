using System.Windows;

namespace PortMonitor.Services;

/// <summary>
/// 对话框集中封装，避免 ViewModel 直接依赖 MessageBox。
/// </summary>
public static class Dialogs
{
    public static bool Confirm(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
           == MessageBoxResult.Yes;

    public static void Info(string message, string title = "提示")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(string message, string title = "错误")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
