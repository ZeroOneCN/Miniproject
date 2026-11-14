using System.ComponentModel;
using System.Windows;
using ScreenOffManager.Services;
using ScreenOffManager.ViewModels;

namespace ScreenOffManager;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainViewModel ViewModel { get; }

    /// <summary>用户尝试关闭窗口（由 App 决定收进托盘还是退出）</summary>
    public event EventHandler? HideRequested;

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();

        ViewModel = new MainViewModel(settings);
        ViewModel.MessageRequested += OnMessageRequested;
        DataContext = ViewModel;
    }

    private void OnMessageRequested(string title, string message)
    {
        var isError = title.Contains("失败");

        Dispatcher.Invoke(() =>
        {
            // 窗口隐藏时不要弹无主对话框，避免用户找不到
            if (IsVisible)
            {
                System.Windows.MessageBox.Show(this, message, title,
                    System.Windows.MessageBoxButton.OK,
                    isError ? System.Windows.MessageBoxImage.Error : System.Windows.MessageBoxImage.Information);
            }
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // 关窗只隐藏，程序继续在托盘运行
            e.Cancel = true;
            HideRequested?.Invoke(this, EventArgs.Empty);
        }

        base.OnClosing(e);
    }

    /// <summary>真正的退出（由托盘「退出」调用）</summary>
    public void AllowClose()
    {
        _allowClose = true;
        Close();
    }
}
