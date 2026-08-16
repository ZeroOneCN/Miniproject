using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CameraMotionCapture.App.ViewModels;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Interfaces;
using Serilog;

namespace CameraMotionCapture.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<int, CameraPanelView> _panelViews = new();
    private int _currentFullScreenIndex = -1;

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            Log.Information("MainWindow InitializeComponent 完成");

            // 设置程序图标
            try { Icon = CreateAppIcon(); } catch { }

            _viewModel = new MainViewModel();
            _settingsService = (ISettingsService)App.ServiceProvider.GetService(typeof(ISettingsService))!;
            Log.Information("MainViewModel 创建完成, 摄像头数: {Count}", _viewModel.Cameras.Count);

            _viewModel.BrowseDirectoryRequested += OnBrowseDirectoryRequested;
            _viewModel.ShowCleanupDialogRequested += OnShowCleanupDialogRequested;
            _viewModel.ShowSettingsDialogRequested += OnShowSettingsDialogRequested;
            _viewModel.AddIpCameraRequested += OnAddIpCameraRequested;
            _viewModel.FullScreenRequested += OnFullScreenRequested;
            _viewModel.Cameras.CollectionChanged += OnCamerasCollectionChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            DataContext = _viewModel;

            SyncPanelsWithViewModels();
            ArrangeGrid();
            Log.Information("MainWindow 构造完成, CameraGrid 子元素数: {Count}", CameraGrid.Children.Count);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "MainWindow 构造失败");
            MessageBox.Show($"MainWindow 构造失败: {ex.Message}\n\n{ex.StackTrace}",
                "构造错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.GridRows) ||
            e.PropertyName == nameof(MainViewModel.GridColumns))
        {
            ArrangeGrid();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedCameraIndex))
        {
            // 延迟执行，避免 ComboBox 选择过程中重入导致 UI 卡死
            Dispatcher.BeginInvoke(() => ArrangeGrid());
        }
    }

    private void OnCamerasCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (CameraPanelViewModel vm in e.NewItems)
            {
                if (!_panelViews.ContainsKey(vm.CameraId))
                {
                    var view = new CameraPanelView();
                    var captured = vm;
                    view.PreviewMouseLeftButtonDown += (s, me) =>
                    {
                        if (me.ClickCount == 2)
                            _viewModel.ToggleFullScreen(_viewModel.Cameras.IndexOf(captured));
                    };
                    _panelViews[vm.CameraId] = view;
                }
            }
        }

        if (e.OldItems != null)
        {
            foreach (CameraPanelViewModel vm in e.OldItems)
            {
                if (_panelViews.TryGetValue(vm.CameraId, out var view))
                {
                    CameraGrid.Children.Remove(view);
                    _panelViews.Remove(vm.CameraId);
                }
                vm.Dispose();
            }
        }

        ArrangeGrid();
    }

    private void SyncPanelsWithViewModels()
    {
        foreach (var vm in _viewModel.Cameras)
        {
            if (!_panelViews.ContainsKey(vm.CameraId))
            {
                var view = new CameraPanelView();
                var captured = vm;
                view.PreviewMouseLeftButtonDown += (s, me) =>
                {
                    if (me.ClickCount == 2)
                        _viewModel.ToggleFullScreen(_viewModel.Cameras.IndexOf(captured));
                };
                _panelViews[vm.CameraId] = view;
            }
        }
    }

    private void ArrangeGrid()
    {
        try
        {
            CameraGrid.Children.Clear();
            CameraGrid.RowDefinitions.Clear();
            CameraGrid.ColumnDefinitions.Clear();

            if (_currentFullScreenIndex >= 0)
            {
                CameraGrid.RowDefinitions.Add(new RowDefinition());
                CameraGrid.ColumnDefinitions.Add(new ColumnDefinition());

                if (_currentFullScreenIndex < _viewModel.Cameras.Count)
                {
                    var vm = _viewModel.Cameras[_currentFullScreenIndex];
                    if (_panelViews.TryGetValue(vm.CameraId, out var view))
                    {
                        view.DataContext = vm;
                        Grid.SetRow(view, 0);
                        Grid.SetColumn(view, 0);
                        CameraGrid.Children.Add(view);
                    }
                }
                return;
            }

            if (_viewModel.Cameras.Count == 0)
            {
                CameraGrid.Children.Add(new TextBlock
                {
                    Text = "未检测到摄像头，请点击「刷新」重新检测",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                return;
            }

            // 单画面模式：只显示选中的摄像头
            if (_viewModel.CurrentLayoutMode == LayoutMode.Single)
            {
                CameraGrid.RowDefinitions.Add(new RowDefinition());
                CameraGrid.ColumnDefinitions.Add(new ColumnDefinition());

                int idx = _viewModel.SelectedCameraIndex;
                if (idx >= 0 && idx < _viewModel.Cameras.Count)
                {
                    var vm = _viewModel.Cameras[idx];
                    if (_panelViews.TryGetValue(vm.CameraId, out var view))
                    {
                        view.DataContext = vm;
                        Grid.SetRow(view, 0);
                        Grid.SetColumn(view, 0);
                        CameraGrid.Children.Add(view);
                    }
                }
                return;
            }

            int rows = _viewModel.GridRows;
            int cols = _viewModel.GridColumns;

            for (int r = 0; r < rows; r++)
                CameraGrid.RowDefinitions.Add(new RowDefinition());
            for (int c = 0; c < cols; c++)
                CameraGrid.ColumnDefinitions.Add(new ColumnDefinition());

            for (int i = 0; i < _viewModel.Cameras.Count; i++)
            {
                var vm = _viewModel.Cameras[i];
                if (!_panelViews.TryGetValue(vm.CameraId, out var view))
                    continue;

                view.DataContext = vm;

                int row = i / cols;
                int col = i % cols;

                if (_viewModel.CurrentLayoutMode == LayoutMode.Grid1Plus2)
                {
                    if (i == 0)
                    {
                        Grid.SetColumnSpan(view, 2);
                        if (CameraGrid.RowDefinitions.Count < 2)
                            CameraGrid.RowDefinitions.Add(new RowDefinition());
                    }
                    else if (i == 1)
                    {
                        row = 1; col = 0;
                    }
                    else
                    {
                        row = 1; col = 1;
                    }
                }

                Grid.SetRow(view, row);
                Grid.SetColumn(view, col);
                CameraGrid.Children.Add(view);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ArrangeGrid 异常");
        }
    }

    private void OnFullScreenRequested(object? sender, int cameraIndex)
    {
        _currentFullScreenIndex = cameraIndex;
        ArrangeGrid();
    }

    private void OnBrowseDirectoryRequested(object? sender, EventArgs e)
    {
        try
        {
            var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
            {
                Description = "选择保存目录",
                SelectedPath = _viewModel.SaveDir
            };
            if (dialog.ShowDialog() == true)
                _viewModel.SaveDir = dialog.SelectedPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BrowseDirectory 异常");
        }
    }

    private void OnShowCleanupDialogRequested(object? sender, EventArgs e)
    {
        try
        {
            var dialog = new CleanupSettingsDialog(
                _viewModel.RetentionDaysInternal,
                _viewModel.SaveDir,
                _viewModel.NextCleanupDate)
            { Owner = this };

            if (dialog.ShowDialog() == true)
            {
                _viewModel.RetentionDaysInternal = dialog.RetentionDays;
                _viewModel.NextCleanupDate = dialog.NextCleanupDate.ToString("yyyy-MM-dd");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ShowCleanupDialog 异常");
        }
    }

    private void OnShowSettingsDialogRequested(object? sender, EventArgs e)
    {
        try
        {
            var dialog = new SettingsDialog(_viewModel.Config)
            { Owner = this };

            if (dialog.ShowDialog() == true)
            {
                _viewModel.ApplyConfig(new AppConfig
                {
                    Camera = dialog.CameraSettings,
                    Recording = dialog.RecordingSettings,
                    MotionDetection = dialog.MotionSettings,
                    Snapshot = dialog.SnapshotSettings,
                    Overlay = dialog.OverlaySettings,
                    FaceRecognition = dialog.FaceSettings,
                    RetentionDays = dialog.RetentionDays,
                    NextCleanupDate = _viewModel.Config.NextCleanupDate
                });

                // 保存 Webhook 并通知服务
                var webhookUrl = dialog.MotionSettings.WebhookUrl ?? "";
                var notificationService = (INotificationService)App.ServiceProvider.GetService(typeof(INotificationService))!;
                notificationService.Configure(webhookUrl);
                _settingsService.SaveWebhookUrl(webhookUrl);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"打开设置对话框时出错: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnAddIpCameraRequested(object? sender, EventArgs e)
    {
        try
        {
            var inputDialog = new InputDialog("添加IP摄像头",
                "请输入摄像头 URL（支持 RTSP/HTTP/MJPEG 协议）:\n例如: rtsp://192.168.1.100:554/stream",
                "rtsp://")
            { Owner = this };

            if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.InputText))
            {
                _viewModel.AddIpCamera(inputDialog.InputText.Trim());
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "添加IP摄像头异常");
            MessageBox.Show(this, $"添加IP摄像头时出错: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static ImageSource CreateAppIcon()
    {
        var canvas = new System.Windows.Controls.Canvas { Width = 32, Height = 32 };

        // 蓝色圆形背景
        var bg = new System.Windows.Shapes.Ellipse
        {
            Width = 32, Height = 32,
            Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x88, 0xE5))
        };
        canvas.Children.Add(bg);

        // 摄像头机身
        var body = new System.Windows.Shapes.Rectangle
        {
            Width = 22, Height = 14, RadiusX = 3, RadiusY = 3,
            Fill = System.Windows.Media.Brushes.White, Opacity = 0.95
        };
        System.Windows.Controls.Canvas.SetLeft(body, 5);
        System.Windows.Controls.Canvas.SetTop(body, 9);
        canvas.Children.Add(body);

        // 镜头外圈
        var lensOuter = new System.Windows.Shapes.Ellipse
        {
            Width = 10, Height = 10,
            Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x88, 0xE5))
        };
        System.Windows.Controls.Canvas.SetLeft(lensOuter, 11);
        System.Windows.Controls.Canvas.SetTop(lensOuter, 11);
        canvas.Children.Add(lensOuter);

        // 镜头内圈
        var lensInner = new System.Windows.Shapes.Ellipse
        {
            Width = 6, Height = 6,
            Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x15, 0x65, 0xC0))
        };
        System.Windows.Controls.Canvas.SetLeft(lensInner, 13);
        System.Windows.Controls.Canvas.SetTop(lensInner, 13);
        canvas.Children.Add(lensInner);

        // 指示灯
        var led = new System.Windows.Shapes.Ellipse
        {
            Width = 3, Height = 3,
            Fill = System.Windows.Media.Brushes.LimeGreen
        };
        System.Windows.Controls.Canvas.SetLeft(led, 7);
        System.Windows.Controls.Canvas.SetTop(led, 10);
        canvas.Children.Add(led);

        canvas.Measure(new System.Windows.Size(32, 32));
        canvas.Arrange(new System.Windows.Rect(0, 0, 32, 32));

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(32, 32, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(canvas);
        return rtb;
    }

    public class InputDialog : Window
    {
        public string InputText { get; private set; } = "";

        public InputDialog(string title, string prompt, string defaultText = "")
        {
            Title = title;
            Width = 480;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var promptBlock = new TextBlock
            {
                Text = prompt,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 13
            };
            Grid.SetRow(promptBlock, 0);
            grid.Children.Add(promptBlock);

            var textBox = new TextBox
            {
                Text = defaultText,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 14,
                Padding = new Thickness(4)
            };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var btnPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var okBtn = new Button
            {
                Content = "确定",
                Width = 80, Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            okBtn.Click += (_, _) =>
            {
                InputText = textBox.Text;
                DialogResult = true;
            };
            var cancelBtn = new Button
            {
                Content = "取消",
                Width = 80, Height = 30,
                IsCancel = true,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            Grid.SetRow(btnPanel, 2);
            grid.Children.Add(btnPanel);

            Content = grid;
            textBox.Focus();
            textBox.SelectAll();
        }
    }
}