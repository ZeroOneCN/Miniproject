using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CameraMotionCapture.App.ViewModels;

namespace CameraMotionCapture.App.Views;

/// <summary>
/// 单个摄像头面板：滚轮缩放、拖拽平移、双击复位。
///
/// <para>旧实现把缩放交给 <c>RenderTransform</c>，却把平移交给 <c>ScrollViewer</c> 的滚动偏移。
/// 两者不联动——<c>RenderTransform</c> 不改变布局尺寸，ScrollViewer 的滚动范围始终是 0，
/// 所以放大后既拖不动、也看不到被裁掉的部分。现在统一用
/// <c>ScaleTransform + TranslateTransform</c>，平移量按缩放倍数夹紧，不会把画面拖出视野。</para>
/// </summary>
public partial class CameraPanelView : UserControl
{
    private const double MinZoom = 1.0;
    private const double MaxZoom = 8.0;

    private CameraPanelViewModel? _viewModel;
    private bool _isPanning;
    private Point _lastMousePosition;
    private double _zoom = 1.0;

    public CameraPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = e.NewValue as CameraPanelViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyZoom(_viewModel.ZoomLevel, null);
        }
        else
        {
            ResetView();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CameraPanelViewModel.ZoomLevel) || _viewModel is null) return;

        // 滚轮已经在事件里按鼠标位置缩放过了，这里只处理工具栏按钮等程序化改动
        if (Math.Abs(_viewModel.ZoomLevel - _zoom) < 0.001) return;

        ApplyZoom(_viewModel.ZoomLevel, null);
    }

    // ══════ 缩放与平移 ══════

    /// <summary>
    /// 应用缩放。<paramref name="anchor"/> 为缩放锚点（控件坐标，通常是鼠标位置），
    /// 传 null 表示以画面中心为锚点。
    /// </summary>
    private void ApplyZoom(double level, Point? anchor)
    {
        level = Math.Clamp(level, MinZoom, MaxZoom);

        var width = PreviewImage.ActualWidth;
        var height = PreviewImage.ActualHeight;
        var pivot = anchor ?? new Point(width / 2, height / 2);

        // 保持锚点下的内容不动：先反算该点在未缩放内容中的位置，再按新比例重新摆正
        var contentX = (pivot.X - PanTransform.X) / _zoom;
        var contentY = (pivot.Y - PanTransform.Y) / _zoom;

        _zoom = level;
        ZoomTransform.ScaleX = level;
        ZoomTransform.ScaleY = level;

        if (level <= MinZoom + 0.001)
        {
            PanTransform.X = 0;
            PanTransform.Y = 0;
        }
        else
        {
            PanTransform.X = pivot.X - contentX * level;
            PanTransform.Y = pivot.Y - contentY * level;
        }

        ClampPan();
    }

    /// <summary>把平移量夹在"画面边缘不进入视口内部"的范围内。</summary>
    private void ClampPan()
    {
        var width = PreviewImage.ActualWidth;
        var height = PreviewImage.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var scaledWidth = width * _zoom;
        var scaledHeight = height * _zoom;

        PanTransform.X = scaledWidth <= width
            ? 0
            : Math.Clamp(PanTransform.X, width - scaledWidth, 0);

        PanTransform.Y = scaledHeight <= height
            ? 0
            : Math.Clamp(PanTransform.Y, height - scaledHeight, 0);
    }

    private void ResetView()
    {
        _zoom = 1.0;
        ZoomTransform.ScaleX = 1;
        ZoomTransform.ScaleY = 1;
        PanTransform.X = 0;
        PanTransform.Y = 0;
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => ClampPan();

    /// <summary>找不到坐标计算依据时用的兜底：控件坐标 → 本控件坐标。</summary>
    private Point GetAnchor(MouseEventArgs e) => e.GetPosition(PreviewImage);

    // ══════ 滚轮缩放 ══════
    private void PreviewImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel is null) return;

        var level = Math.Clamp(e.Delta > 0 ? _zoom * 1.15 : _zoom * 0.87, MinZoom, MaxZoom);

        // 先把本地值同步过去，PropertyChanged 回调里才不会再以画面中心为锚点缩放一次
        _zoom = level;
        _viewModel.ZoomLevel = level;
        ApplyZoom(level, GetAnchor(e));

        e.Handled = true;
    }

    // ══════ 双击复位 / 拖拽平移 ══════
    private void PreviewImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null) return;

        if (e.ClickCount == 2)
        {
            _viewModel.ZoomResetCommand.Execute(null);
            ResetView();
            e.Handled = true;
            return;
        }

        if (_zoom <= MinZoom + 0.01) return;

        _isPanning = true;
        _lastMousePosition = e.GetPosition(PreviewImage);
        PreviewImage.Cursor = Cursors.SizeAll;
        PreviewImage.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        var position = e.GetPosition(PreviewImage);
        PanTransform.X += position.X - _lastMousePosition.X;
        PanTransform.Y += position.Y - _lastMousePosition.Y;
        ClampPan();

        // 夹紧后位置会被修正，记录值也要跟着改，否则继续拖动会"打滑"
        _lastMousePosition = position;
        e.Handled = true;
    }

    private void PreviewImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;

        _isPanning = false;
        PreviewImage.Cursor = Cursors.Arrow;
        PreviewImage.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PreviewImage_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        _isPanning = false;
        PreviewImage.Cursor = Cursors.Arrow;
        PreviewImage.ReleaseMouseCapture();
    }
}
