using System.Collections.ObjectModel;
using System.Windows.Input;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Implementations;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.App.ViewModels;

public enum LayoutMode
{
    Single,
    Grid2x2,
    Grid3x3,
    Grid1Plus2
}

public class MainViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IStorageService _storageService;
    private readonly INotificationService _notificationService;
    private AppConfig _config;

    private LayoutMode _layoutMode = LayoutMode.Grid2x2;
    private int _gridRows = 2;
    private int _gridColumns = 2;
    private int _maxCameras = 4;
    private int _fullScreenCameraIndex = -1; // -1 = 正常网格模式

    public event EventHandler? BrowseDirectoryRequested;
    public event EventHandler? ShowCleanupDialogRequested;
    public event EventHandler? ShowSettingsDialogRequested;
    /// <summary>请求显示 IP 摄像头输入对话框</summary>
    public event EventHandler? AddIpCameraRequested;
    /// <summary>请求全屏显示某个摄像头（-1 = 恢复网格）</summary>
    public event EventHandler<int>? FullScreenRequested;

    public MainViewModel()
    {
        _settingsService = (ISettingsService)App.ServiceProvider.GetService(typeof(ISettingsService))!;
        _storageService = (IStorageService)App.ServiceProvider.GetService(typeof(IStorageService))!;
        _notificationService = (INotificationService)App.ServiceProvider.GetService(typeof(INotificationService))!;

        _config = _settingsService.LoadConfig();

        // 加载 Webhook
        var webhookUrl = _settingsService.LoadWebhookUrl();
        if (!string.IsNullOrEmpty(webhookUrl))
        {
            _config.MotionDetection.WebhookUrl = webhookUrl;
            _notificationService.Configure(webhookUrl);
        }

        // 命令
        StartAllCommand = new RelayCommand(_ => StartAll(), _ => Cameras.Any(c => !c.IsMonitoring));
        StopAllCommand = new RelayCommand(_ => StopAll(), _ => Cameras.Any(c => c.IsMonitoring));
        AddCameraCommand = new RelayCommand(_ => AddCamera());
        RemoveCameraCommand = new RelayCommand(_ => RemoveCamera(), _ => Cameras.Count > 1);
        ApplyLayoutCommand = new RelayCommand(_ => { });
        RefreshCamerasCommand = new RelayCommand(_ => RefreshCameras());
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        ShowCleanupDialogCommand = new RelayCommand(_ => ShowCleanupDialogRequested?.Invoke(this, EventArgs.Empty));
        BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectoryRequested?.Invoke(this, EventArgs.Empty));
        ShowSettingsCommand = new RelayCommand(_ => ShowSettingsDialogRequested?.Invoke(this, EventArgs.Empty));
        AddIpCameraCommand = new RelayCommand(_ => AddIpCameraRequested?.Invoke(this, EventArgs.Empty));
        ToggleFullScreenCommand = new RelayCommand(index =>
        {
            if (index is int i) ToggleFullScreen(i);
        });

        RefreshCameras();
    }

    public AppConfig Config => _config;

    public ObservableCollection<CameraPanelViewModel> Cameras { get; } = new();

    public bool IsFullScreenMode => _fullScreenCameraIndex >= 0;

    public LayoutMode CurrentLayoutMode
    {
        get => _layoutMode;
        set
        {
            if (SetProperty(ref _layoutMode, value))
                ApplyLayout(value);
        }
    }

    public int GridRows
    {
        get => _gridRows;
        set => SetProperty(ref _gridRows, value);
    }

    public int GridColumns
    {
        get => _gridColumns;
        set => SetProperty(ref _gridColumns, value);
    }

    public int MaxCameras
    {
        get => _maxCameras;
        set => SetProperty(ref _maxCameras, value);
    }

    // 兼容旧属性
    public string SaveDir { get => _config.Recording.SaveDir; set => _config.Recording.SaveDir = value; }
    public int Quality { get => _config.Recording.Quality; set => _config.Recording.Quality = value; }
    public bool UseDailyFolder { get => _config.Recording.UseDailyFolder; set => _config.Recording.UseDailyFolder = value; }
    public int RetentionDays { get => _config.RetentionDays; set => _config.RetentionDays = value; }
    public string NextCleanupDate { get => _config.NextCleanupDate ?? ""; set => _config.NextCleanupDate = value; }
    public string WebhookUrl { get => _config.MotionDetection.WebhookUrl ?? ""; set => _config.MotionDetection.WebhookUrl = value; }
    public int RetentionDaysInternal { get => _config.RetentionDays; set => _config.RetentionDays = value; }

    // 默认摄像头索引
    public int DefaultCameraIndex
    {
        get => _config.Camera.DefaultCameraIndex;
        set
        {
            _config.Camera.DefaultCameraIndex = value;
            UpdateDefaultCameraIndicator();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefaultCameraName));
        }
    }

    // 单画面模式下选中的摄像头索引
    private int _selectedCameraIndex;
    public int SelectedCameraIndex
    {
        get => _selectedCameraIndex;
        set
        {
            if (value < 0) value = Cameras.Count - 1;
            if (value >= Cameras.Count) value = 0;
            if (SetProperty(ref _selectedCameraIndex, value))
            {
                OnPropertyChanged(nameof(SelectedCameraDisplayName));
            }
        }
    }

    public string SelectedCameraDisplayName
    {
        get
        {
            if (_selectedCameraIndex >= 0 && _selectedCameraIndex < Cameras.Count)
                return Cameras[_selectedCameraIndex].CameraName;
            return "未选择";
        }
    }

    private void UpdateDefaultCameraIndicator()
    {
        for (int i = 0; i < Cameras.Count; i++)
            Cameras[i].IsDefaultCamera = (i == _config.Camera.DefaultCameraIndex);
    }

    public string DefaultCameraName
    {
        get
        {
            if (DefaultCameraIndex >= 0 && DefaultCameraIndex < Cameras.Count)
                return Cameras[DefaultCameraIndex].CameraName;
            return "无";
        }
    }

    private string _statusText = "就绪";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private int _activeCameraCount;
    public int ActiveCameraCount
    {
        get => _activeCameraCount;
        set => SetProperty(ref _activeCameraCount, value);
    }

    public ICommand StartAllCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand AddCameraCommand { get; }
    public ICommand RemoveCameraCommand { get; }
    public ICommand ApplyLayoutCommand { get; }
    public ICommand RefreshCamerasCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ShowCleanupDialogCommand { get; }
    public ICommand BrowseDirectoryCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand AddIpCameraCommand { get; }
    public ICommand ToggleFullScreenCommand { get; }

    private async void RefreshCameras()
    {
        StatusText = "正在检测摄像头...";
        var available = await Task.Run(() =>
        {
            using var tempCam = new CameraService();
            return tempCam.GetAvailableCameras();
        });
        Log.Information("检测到可用摄像头: {Count}", available.Count);

        if (Cameras.Count == 0 && available.Count > 0)
        {
            int count = Math.Min(available.Count, 9);
            for (int i = 0; i < count; i++)
            {
                var panel = new CameraPanelViewModel(available[i], _config, _notificationService);
                Cameras.Add(panel);
            }

            if (count <= 1) CurrentLayoutMode = LayoutMode.Single;
            else if (count <= 4) CurrentLayoutMode = LayoutMode.Grid2x2;
            else CurrentLayoutMode = LayoutMode.Grid3x3;
        }
        else if (Cameras.Count == 0)
        {
            var panel = new CameraPanelViewModel(0, _config, _notificationService);
            Cameras.Add(panel);
            CurrentLayoutMode = LayoutMode.Single;
        }

        ActiveCameraCount = Cameras.Count;
        UpdateDefaultCameraIndicator();
        StatusText = $"已连接 {Cameras.Count} 个摄像头";
    }

    public void AddCamera()
    {
        int nextId = 0;
        var existingIds = Cameras.Select(c => c.CameraId).ToHashSet();
        for (int i = 0; i < 10; i++)
        {
            if (!existingIds.Contains(i))
            {
                nextId = i;
                break;
            }
        }

        var panel = new CameraPanelViewModel(nextId, _config, _notificationService);
        Cameras.Add(panel);
        UpdateDefaultCameraIndicator();
        AdjustLayoutForCount(Cameras.Count);
        ActiveCameraCount = Cameras.Count;
        StatusText = $"已添加摄像头 {nextId}";
    }

    public void RemoveCamera()
    {
        if (Cameras.Count <= 1) return;
        var last = Cameras.Last();
        last.Dispose();
        Cameras.Remove(last);
        AdjustLayoutForCount(Cameras.Count);
        ActiveCameraCount = Cameras.Count;
        StatusText = "已移除摄像头";
    }

    /// <summary>添加 IP 网络摄像头</summary>
    public void AddIpCamera(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        // 生成唯一 ID
        int nextId = -1;
        var existingIds = Cameras.Select(c => c.CameraId).ToHashSet();
        for (int i = -1; i > -100; i--)
        {
            if (!existingIds.Contains(i))
            {
                nextId = i;
                break;
            }
        }

        var panel = new CameraPanelViewModel(url, _config, nextId, _notificationService);
        Cameras.Add(panel);
        UpdateDefaultCameraIndicator();
        AdjustLayoutForCount(Cameras.Count);
        ActiveCameraCount = Cameras.Count;
        StatusText = $"已添加 IP 摄像头: {url}";
    }

    private void StartAll()
    {
        foreach (var cam in Cameras)
        {
            if (!cam.IsMonitoring)
                cam.StartMonitoring();
        }
        StatusText = $"全部 {Cameras.Count} 个摄像头已启动监控";
    }

    private void StopAll()
    {
        foreach (var cam in Cameras)
        {
            if (cam.IsMonitoring)
                cam.StopMonitoring();
        }
        StatusText = "监控已全部停止";
    }

    private void ApplyLayout(LayoutMode mode)
    {
        switch (mode)
        {
            case LayoutMode.Single: GridRows = 1; GridColumns = 1; MaxCameras = 9; break;
            case LayoutMode.Grid2x2: GridRows = 2; GridColumns = 2; MaxCameras = 4; break;
            case LayoutMode.Grid3x3: GridRows = 3; GridColumns = 3; MaxCameras = 9; break;
            case LayoutMode.Grid1Plus2: GridRows = 2; GridColumns = 2; MaxCameras = 3; break;
        }

        // 如果处于全屏模式，退出全屏
        if (_fullScreenCameraIndex >= 0)
        {
            _fullScreenCameraIndex = -1;
            OnPropertyChanged(nameof(IsFullScreenMode));
        }
    }

    private void AdjustLayoutForCount(int count)
    {
        if (count <= 1) CurrentLayoutMode = LayoutMode.Single;
        else if (count <= 4) CurrentLayoutMode = LayoutMode.Grid2x2;
        else CurrentLayoutMode = LayoutMode.Grid3x3;
    }

    /// <summary>切换全屏模式（双击某个摄像头面板时调用）</summary>
    public void ToggleFullScreen(int cameraIndex)
    {
        if (cameraIndex < 0 || cameraIndex >= Cameras.Count) return;

        if (_fullScreenCameraIndex == cameraIndex)
        {
            // 恢复网格
            _fullScreenCameraIndex = -1;
        }
        else
        {
            // 进入全屏
            _fullScreenCameraIndex = cameraIndex;
        }

        OnPropertyChanged(nameof(IsFullScreenMode));
        FullScreenRequested?.Invoke(this, _fullScreenCameraIndex);
    }

    public void SaveSettings()
    {
        _settingsService.SaveConfig(_config);
        Log.Information("配置已保存");
    }

    public void ApplyConfig(AppConfig newConfig)
    {
        _config = newConfig;
        SaveSettings();

        // 重新配置通知服务
        if (!string.IsNullOrEmpty(_config.MotionDetection.WebhookUrl))
        {
            _notificationService.Configure(_config.MotionDetection.WebhookUrl);
        }

        // 更新所有摄像头面板的配置
        foreach (var cam in Cameras)
        {
            cam.ApplyConfig(_config);
        }

        StatusText = "设置已应用";
    }
}