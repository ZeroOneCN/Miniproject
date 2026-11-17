using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CameraMotionCapture.App.ViewModels;

namespace CameraMotionCapture.App.Views;

/// <summary>
/// 单个摄像头面板：滚轮缩放、拖拽平移、双击复位。
///
/// <para>画面尺寸由这里算，而不是交给 <c>Stretch</c>：<c>Uniform</c> 会让控件保持整格大小、
/// 内容在里面居中留黑边，于是 <c>RenderTransform</c> 缩放的是"整格"而不是"画面"，
/// 平移边界也就全错了（放大后能拖出黑边）。现在的做法是——
/// 按「适应 / 填满」算出画面应有的宽高设置给 Image，<c>Stretch=Fill</c> 只是把
/// 位图铺满这个已等比算好的尺寸，变换与边界都基于真实内容尺寸。</para>
/// </summary>
public partial class CameraPanelView : UserControl
{
    private const double MinZoom = 1.0;
    private const double MaxZoom = 8.0;

    private CameraPanelViewModel? _viewModel;
    private bool _isPanning;
    private Point _lastMousePosition;
    private double _zoom = 1.0;

    /// <summary>true = 填满（裁切掉超出部分，不留黑边）；false = 适应（等比缩放到完整可见）</summary>
    public static readonly DependencyProperty FillModeProperty = DependencyProperty.Register(
        nameof(FillMode), typeof(bool), typeof(CameraPanelView),
        new PropertyMetadata(true, OnFillModeChanged));

    public bool FillMode
    {
        get => (bool)GetValue(FillModeProperty);
        set => SetValue(FillModeProperty, value);
    }

    private static void OnFillModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CameraPanelView view)
        {
            view.UpdateContentSize();
            view.ApplyZoom(view._zoom, null);
        }
    }

    public CameraPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
        Loaded += (_, _) => UpdateContentSize();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = e.NewValue as CameraPanelViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateContentSize();
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
        switch (e.PropertyName)
        {
            // 分辨率/首帧到达时画面尺寸会变，重新算一次等比尺寸
            case nameof(CameraPanelViewModel.CameraImage):
                UpdateContentSize();
                ApplyZoom(_zoom, null);
                break;

            case nameof(CameraPanelViewModel.ZoomLevel) when _viewModel is not null:
                // 滚轮已经在事件里按鼠标位置缩放过了，这里只处理工具栏按钮等程序化改动
                if (Math.Abs(_viewModel.ZoomLevel - _zoom) < 0.001) return;
                ApplyZoom(_viewModel.ZoomLevel, null);
                break;
        }
    }

    // ══════ 画面尺寸（适应 / 填满） ══════

    /// <summary>按当前模式把画面缩放到宿主区域，并写回 Image 的实际宽高。</summary>
    private void UpdateContentSize()
    {
        var hostW = PreviewHost.ActualWidth;
        var hostH = PreviewHost.ActualHeight;
        if (hostW <= 0 || hostH <= 0) return;

        var source = PreviewImage.Source as BitmapSource;
        double srcW = source?.PixelWidth ?? 0;
        double srcH = source?.PixelHeight ?? 0;
        if (srcW <= 0 || srcH <= 0) return;

        var scale = FillMode
            ? Math.Max(hostW / srcW, hostH / srcH)   // 填满：取较大比例，溢出部分被裁切
            : Math.Min(hostW / srcW, hostH / srcH);  // 适应：取较小比例，整体可见

        PreviewImage.Width = Math.Ceiling(srcW * scale);
        PreviewImage.Height = Math.Ceiling(srcH * scale);
    }

    // ══════ 缩放与平移 ══════

    /// <summary>
    /// 应用缩放。<paramref name="anchor"/> 为宿主区域坐标（通常是鼠标位置），
    /// 传 null 表示以画面中心为锚点。
    /// </summary>
    private void ApplyZoom(double level, Point? anchor)
    {
        level = Math.Clamp(level, MinZoom, MaxZoom);

        var hostW = PreviewHost.ActualWidth;
        var hostH = PreviewHost.ActualHeight;
        var elemW = PreviewImage.Width > 0 ? PreviewImage.Width : hostW;
        var elemH = PreviewImage.Height > 0 ? PreviewImage.Height : hostH;

        // Image 用居中对齐，未缩放时在宿主中的左上角偏移
        var offsetX = (hostW - elemW) / 2;
        var offsetY = (hostH - elemH) / 2;

        var pivot = anchor ?? new Point(hostW / 2, hostH / 2);

        // 保持锚点下的内容不动：先反算该点在未缩放内容中的位置，再按新比例重新摆正
        var contentX = (pivot.X - offsetX - PanTransform.X) / _zoom;
        var contentY = (pivot.Y - offsetY - PanTransform.Y) / _zoom;

        _zoom = level;
        ZoomTransform.ScaleX = level;
        ZoomTransform.ScaleY = level;

        PanTransform.X = pivot.X - offsetX - contentX * level;
        PanTransform.Y = pivot.Y - offsetY - contentY * level;

        ClampPan();
    }

    /// <summary>把平移量夹在"画面边缘不离开视口"的范围内；画面比视口小时居中。</summary>
    private void ClampPan()
    {
        var hostW = PreviewHost.ActualWidth;
        var hostH = PreviewHost.ActualHeight;
        var elemW = PreviewImage.Width;
        var elemH = PreviewImage.Height;
        if (hostW <= 0 || hostH <= 0 || elemW <= 0 || elemH <= 0) return;

        var scaledW = elemW * _zoom;
        var scaledH = elemH * _zoom;
        var offsetX = (hostW - elemW) / 2;
        var offsetY = (hostH - elemH) / 2;

        // 小于视口：居中（此时不该有黑边偏移，居中即最合理）
        // 大于视口：允许在溢出范围内拖动，边缘不允许进入视口内部
        PanTransform.X = scaledW <= hostW
            ? (hostW - scaledW) / 2 - offsetX
            : Math.Clamp(PanTransform.X, hostW - offsetX - scaledW, -offsetX);

        PanTransform.Y = scaledH <= hostH
            ? (hostH - scaledH) / 2 - offsetY
            : Math.Clamp(PanTransform.Y, hostH - offsetY - scaledH, -offsetY);
    }

    private void ResetView()
    {
        _zoom = 1.0;
        ZoomTransform.ScaleX = 1;
        ZoomTransform.ScaleY = 1;
        PanTransform.X = 0;
        PanTransform.Y = 0;
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateContentSize();
        ClampPan();
    }

    // ══════ 滚轮缩放 ══════

    private void PreviewImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel is null) return;

        var level = Math.Clamp(e.Delta > 0 ? _zoom * 1.15 : _zoom * 0.87, MinZoom, MaxZoom);

        // 先把本地值同步过去，PropertyChanged 回调里才不会再以画面中心为锚点缩放一次
        _zoom = level;
        _viewModel.ZoomLevel = level;
        ApplyZoom(level, e.GetPosition(PreviewHost));

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
            ClampPan();
            e.Handled = true;
            return;
        }

        // 填满模式下即使 1x 也可能有被裁掉的区域，允许直接拖动
        if (_zoom <= MinZoom + 0.01 && !FillMode) return;

        _isPanning = true;
        _lastMousePosition = e.GetPosition(PreviewHost);
        PreviewImage.Cursor = Cursors.SizeAll;
        PreviewImage.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        var position = e.GetPosition(PreviewHost);
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
