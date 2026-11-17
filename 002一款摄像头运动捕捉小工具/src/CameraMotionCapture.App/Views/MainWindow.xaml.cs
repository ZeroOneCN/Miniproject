using System.Windows;
using System.Windows.Input;
using CameraMotionCapture.App.Models;
using CameraMotionCapture.App.ViewModels;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        FitToScreen();

        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.SettingsRequested += OnSettingsRequested;
        _viewModel.AddIpCameraRequested += OnAddIpCameraRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        StateChanged += (_, _) => ApplyContentMargin();
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += (_, _) => _viewModel.Dispose();

        ApplyContentMargin();

        Log.Information("主窗口已就绪");
    }


    // ══════════════════ 窗口尺寸 ══════════════════

    /// <summary>
    /// 按屏幕可用区域收敛窗口尺寸。
    ///
    /// <para>XAML 里写死的 1440×900 是"设计稿尺寸"，直接当实际尺寸用会出事：
    /// 屏幕 1920×1080 + 系统缩放 125% 时，可用区域只有约 1536×832（DIP），
    /// 900 高的窗口居中后标题栏会被顶到屏幕外，关闭按钮直接看不到。
    /// 这里改成"不超过可用区域的 92%"，小屏笔记本同样适用。</para>
    /// </summary>
    private void FitToScreen()
    {
        var work = SystemParameters.WorkArea;

        var width = Math.Min(Width, work.Width * 0.92);
        var height = Math.Min(Height, work.Height * 0.92);

        // 最小尺寸也不能超过屏幕，否则窗口无法缩到屏幕内
        MinWidth = Math.Min(MinWidth, work.Width * 0.9);
        MinHeight = Math.Min(MinHeight, work.Height * 0.9);

        Width = width;
        Height = height;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = work.Left + (work.Width - width) / 2;
        Top = work.Top + (work.Height - height) / 2;
    }

    /// <summary>
    /// 最大化时把内容边距收成 0。
    /// <para>普通窗口需要一圈留白才好看，但最大化状态下还留 12px，四周就会露出一条浅色/深色边，
    /// 看着就像"窗口没铺满"。</para>
    /// </summary>
    private void ApplyContentMargin()
    {
        ContentRoot.Margin = WindowState == WindowState.Normal
            ? new Thickness(12, 10, 12, 10)
            : new Thickness(0);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsFullscreen)) ApplyFullscreenChrome();
    }

    /// <summary>
    /// 单路放大 = 去掉标题栏 + 最大化，像正经监控软件那样把画面铺满整块屏。
    /// 用 <c>WindowStyle</c> 而不是 <c>WindowState.Maximized</c> 单独实现，
    /// 是为了让 F11 和面板上的 ⛶ 行为一致。
    /// </summary>
    private void ApplyFullscreenChrome()
    {
        try
        {
            if (_viewModel.IsFullscreen)
            {
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Normal;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "切换全屏外观失败");
        }

        ApplyContentMargin();
    }

    // ══════════════════ 快捷键 ══════════════════

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F11:
                _viewModel.ToggleFullscreen(_viewModel.SelectedCamera ?? _viewModel.Cameras.FirstOrDefault());
                e.Handled = true;
                break;

            case Key.Escape when _viewModel.IsFullscreen:
                _viewModel.ToggleFullscreen(_viewModel.FullscreenCamera);
                e.Handled = true;
                break;

            case Key.F5:
                _viewModel.RefreshCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.O when Keyboard.Modifiers == ModifierKeys.Control:
                _viewModel.OpenSaveDirCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ══════════════════ 对话框 ══════════════════

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
            var dialog = new SettingsDialog(_viewModel.Config) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is not null)
                _viewModel.ApplyConfig(dialog.Result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "打开设置窗口失败");
            MessageBox.Show(this, $"打开设置失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnAddIpCameraRequested(object? sender, EventArgs e)
    {
        var dialog = new TextInputDialog(
            "添加网络摄像头",
            $"支持 RTSP / HTTP / MJPEG 地址，例如 rtsp://192.168.1.100:554/stream\n" +
            $"含账号密码时格式：rtsp://用户名:密码@地址:端口/路径（{Constants.AppName}会隐藏显示）",
            "rtsp://")
        { Owner = this };

        if (dialog.ShowDialog() == true)
            _viewModel.AddIpCamera(dialog.Value);
    }

    // ══════════════════ 事件列表 ══════════════════

    private void EventList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EventList.SelectedItem is not MonitorEvent item || !item.HasSnapshot) return;

        _viewModel.OpenSnapshotCommand.Execute(item.SnapshotPath);
    }
}
