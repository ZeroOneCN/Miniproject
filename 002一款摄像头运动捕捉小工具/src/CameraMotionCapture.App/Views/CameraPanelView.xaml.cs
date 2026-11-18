using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CameraMotionCapture.App.ViewModels;
using CameraMotionCapture.Core.Models;

namespace CameraMotionCapture.App.Views;

/// <summary>
/// 单个摄像头面板：滚轮缩放、拖拽平移、双击复位。
///
/// <para>画面尺寸由这里算，而不是交给 <c>Stretch</c>：<c>Uniform</c> 会让控件保持整格大小、
/// 内容在里面居中留黑边，于是 <c>RenderTransform</c> 缩放的是"整格"而不是"画面"，
/// 平移边界也就全错了（放大后能拖出黑边）。现在的做法是——
/// 按「适应 / 填满」算出画面应有的宽高设置给 Image，宽高严格等于"源像素 × 同一个比例"，
/// 所以永远不会出现横向和纵向比例不一致的拉伸；<c>Stretch=Uniform</c> 只是把这层等比
/// 关系再兜一遍底（万一取整有误差，表现为边缘多出不到 1 像素的空隙，而不是把画面压扁）。</para>
/// </summary>
public partial class CameraPanelView : UserControl
{
    private const double MinZoom = 1.0;
    private const double MaxZoom = 8.0;

    private CameraPanelViewModel? _viewModel;
    private bool _isPanning;
    private Point _lastMousePosition;
    private double _zoom = 1.0;

    // 框选状态
    private bool _regionDragging;
    private Point _regionStart;

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

            case nameof(CameraPanelViewModel.Region):
            case nameof(CameraPanelViewModel.HasRegion):
                UpdateRegionBox();
                break;

            case nameof(CameraPanelViewModel.IsSelectingRegion):
                if (_viewModel is null) break;

                if (_viewModel.IsSelectingRegion)
                {
                    PreviewHost.Cursor = Cursors.Cross;
                }
                else
                {
                    RegionDraft.Visibility = Visibility.Collapsed;
                    PreviewHost.Cursor = Cursors.Arrow;
                }
                break;
        }
    }

    // ══════ 画面尺寸（适应 / 填满） ══════

    /// <summary>
    /// 位图真正落到 Image 上之后再算一次尺寸。
    ///
    /// <para>只靠 ViewModel 的 PropertyChanged 会漏一拍：面板控件可能是首帧之后才创建出来的，
    /// 那时 DataContext 刚设上、绑定还没把 Source 推过来，读到的还是 null，于是停在上一次
    /// 的"整格等比"，看起来就是上下两条黑边——而且只有窗口尺寸变化时才会被纠正。
    /// <c>TargetUpdated</c> 是绑定值真正写进目标的那一刻，这里补算最可靠。</para>
    /// </summary>
    private void PreviewImage_TargetUpdated(object? sender, System.Windows.Data.DataTransferEventArgs e)
    {
        UpdateContentSize();
        ApplyZoom(_zoom, null);
    }

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

        // 宽高共用一个比例，且不取整：这是"不拉伸"的根本保证。
        // 以前用 Math.Ceiling 会让宽高各自进位，长宽比就和源差了零点几个百分点。
        ImageLayer.Width = srcW * scale;
        ImageLayer.Height = srcH * scale;

        UpdateRegionBox();
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
        var elemW = ImageLayer.Width > 0 && !double.IsNaN(ImageLayer.Width) ? ImageLayer.Width : hostW;
        var elemH = ImageLayer.Height > 0 && !double.IsNaN(ImageLayer.Height) ? ImageLayer.Height : hostH;

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
        var elemW = ImageLayer.Width;
        var elemH = ImageLayer.Height;
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
        UpdateRegionBox();
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

    // ══════ 双击复位 / 拖拽平移 / 框选检测区域 ══════

    private void PreviewImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null) return;

        // 框选模式优先：左键拖拽就是画检测框，此时不平移
        if (_viewModel.IsSelectingRegion)
        {
            if (ImageLayer.Width <= 0 || ImageLayer.Height <= 0) return;

            _regionStart = ClampToLayer(e.GetPosition(ImageLayer));
            _regionDragging = true;

            RegionDraft.Visibility = Visibility.Visible;
            PlaceRect(RegionDraft, _regionStart, _regionStart);

            PreviewHost.CaptureMouse();
            PreviewHost.Cursor = Cursors.Cross;
            e.Handled = true;
            return;
        }

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
        PreviewHost.Cursor = Cursors.SizeAll;
        PreviewHost.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_regionDragging)
        {
            PlaceRect(RegionDraft, _regionStart, ClampToLayer(e.GetPosition(ImageLayer)));
            e.Handled = true;
            return;
        }

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
        if (_regionDragging)
        {
            _regionDragging = false;
            RegionDraft.Visibility = Visibility.Collapsed;
            PreviewHost.ReleaseMouseCapture();
            PreviewHost.Cursor = Cursors.Arrow;

            CommitRegion(_regionStart, ClampToLayer(e.GetPosition(ImageLayer)));
            e.Handled = true;
            return;
        }

        if (!_isPanning) return;

        _isPanning = false;
        PreviewHost.Cursor = Cursors.Arrow;
        PreviewHost.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PreviewImage_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        _isPanning = false;
        PreviewHost.Cursor = Cursors.Arrow;
        PreviewHost.ReleaseMouseCapture();
    }

    // ══════ 检测区域 ══════

    /// <summary>鼠标坐标夹到画面范围内（画布之外的拖拽不算）。</summary>
    private Point ClampToLayer(Point point) => new(
        Math.Clamp(point.X, 0, Math.Max(0, ImageLayer.Width)),
        Math.Clamp(point.Y, 0, Math.Max(0, ImageLayer.Height)));

    private static void PlaceRect(FrameworkElement element, Point a, Point b)
    {
        Canvas.SetLeft(element, Math.Min(a.X, b.X));
        Canvas.SetTop(element, Math.Min(a.Y, b.Y));
        element.Width = Math.Abs(a.X - b.X);
        element.Height = Math.Abs(a.Y - b.Y);
    }

    /// <summary>把拖出来的矩形换算成相对坐标存进配置。</summary>
    private void CommitRegion(Point a, Point b)
    {
        if (_viewModel is null || ImageLayer.Width <= 0 || ImageLayer.Height <= 0) return;

        var width = Math.Abs(a.X - b.X) / ImageLayer.Width;
        var height = Math.Abs(a.Y - b.Y) / ImageLayer.Height;

        // 手抖画出来的小框没有意义，直接当成"取消"
        if (width < 0.03 || height < 0.03)
        {
            _viewModel.IsSelectingRegion = false;
            return;
        }

        _viewModel.SetRegion(new RegionRect
        {
            X = Math.Min(a.X, b.X) / ImageLayer.Width,
            Y = Math.Min(a.Y, b.Y) / ImageLayer.Height,
            Width = width,
            Height = height
        });
    }

    /// <summary>把已保存的区域画到画面上（跟着画面一起缩放/平移）。</summary>
    private void UpdateRegionBox()
    {
        var region = _viewModel?.Region;

        if (region is null || ImageLayer.Width <= 0 || ImageLayer.Height <= 0)
        {
            RegionBox.Visibility = Visibility.Collapsed;
            return;
        }

        RegionBox.Visibility = Visibility.Visible;
        Canvas.SetLeft(RegionBox, region.X * ImageLayer.Width);
        Canvas.SetTop(RegionBox, region.Y * ImageLayer.Height);
        RegionBox.Width = Math.Max(0, region.Width * ImageLayer.Width);
        RegionBox.Height = Math.Max(0, region.Height * ImageLayer.Height);
    }
}
