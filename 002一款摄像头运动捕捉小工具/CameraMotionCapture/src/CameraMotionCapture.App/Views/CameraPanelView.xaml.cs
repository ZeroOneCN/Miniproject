using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CameraMotionCapture.App.ViewModels;

namespace CameraMotionCapture.App.Views;

/// <summary>
/// 单个摄像头面板 — 支持鼠标滚轮缩放 + 拖拽平移
/// </summary>
public partial class CameraPanelView : UserControl
{
    private CameraPanelViewModel? _viewModel;
    private bool _isPanning;
    private Point _lastMousePos;

    public CameraPanelView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel = DataContext as CameraPanelViewModel;
        if (_viewModel != null)
        {
            _viewModel.ZoomChanged += OnZoomChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.ZoomChanged -= OnZoomChanged;
        }
    }

    private void OnZoomChanged(object? sender, double level)
    {
        Dispatcher.Invoke(() =>
        {
            ZoomTransform.ScaleX = level;
            ZoomTransform.ScaleY = level;
        });
    }

    // ========== 鼠标滚轮缩放 ==========
    private void PreviewImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel == null) return;

        var oldLevel = _viewModel.ZoomLevel;
        if (e.Delta > 0)
            _viewModel.ZoomLevel *= 1.15;
        else
            _viewModel.ZoomLevel *= 0.87;

        // 缩放锚点：鼠标位置
        var mousePos = e.GetPosition(PreviewScrollViewer);
        if (oldLevel > 0 && _viewModel.ZoomLevel > 0)
        {
            var ratio = _viewModel.ZoomLevel / oldLevel;
            var newHOffset = (mousePos.X + PreviewScrollViewer.HorizontalOffset) * ratio - mousePos.X;
            var newVOffset = (mousePos.Y + PreviewScrollViewer.VerticalOffset) * ratio - mousePos.Y;
            PreviewScrollViewer.ScrollToHorizontalOffset(newHOffset);
            PreviewScrollViewer.ScrollToVerticalOffset(newVOffset);
        }

        e.Handled = true;
    }

    // ========== 鼠标拖拽平移 + 双击重置 ==========
    private void PreviewImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel == null) return;

        // 双击重置缩放
        if (e.ClickCount == 2)
        {
            _viewModel.ZoomResetCommand.Execute(null);
            PreviewScrollViewer.ScrollToLeftEnd();
            PreviewScrollViewer.ScrollToTop();
            e.Handled = true;
            return;
        }

        // 单击开始拖拽（仅缩放>1时）
        if (_viewModel.ZoomLevel <= 1.01) return;

        _isPanning = true;
        _lastMousePos = e.GetPosition(PreviewScrollViewer);
        PreviewImage.Cursor = Cursors.SizeAll;
        PreviewImage.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        var pos = e.GetPosition(PreviewScrollViewer);
        var dx = pos.X - _lastMousePos.X;
        var dy = pos.Y - _lastMousePos.Y;

        PreviewScrollViewer.ScrollToHorizontalOffset(
            PreviewScrollViewer.HorizontalOffset - dx);
        PreviewScrollViewer.ScrollToVerticalOffset(
            PreviewScrollViewer.VerticalOffset - dy);

        _lastMousePos = pos;
        e.Handled = true;
    }

    private void PreviewImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        PreviewImage.Cursor = Cursors.Hand;
        PreviewImage.ReleaseMouseCapture();
        e.Handled = true;
    }
}