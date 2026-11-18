using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using CameraMotionCapture.App.Imaging;
using CameraMotionCapture.App.Models;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.App.ViewModels;

public enum LayoutMode
{
    /// <summary>按实际路数自动铺满</summary>
    Auto,
    Single,
    Grid2x2,
    Grid3x3
}

/// <summary>
/// 主视图模型。
/// 相比旧版本：构造函数注入全部依赖（不再通过静态 ServiceProvider 取服务），
/// 「刷新」真正能发现新插入的摄像头，并新增了事件列表与磁盘守护。
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private const int MaxEvents = 200;

    private readonly ISettingsService _settings;
    private readonly IStorageService _storage;
    private readonly INotificationService _notifier;
    private readonly ICameraDiscovery _discovery;
    private readonly CameraPipelineFactory _factory;
    private readonly DispatcherTimer _diskGuardTimer;
    private readonly DispatcherTimer _statTimer;
    private readonly Process _self = Process.GetCurrentProcess();

    private AppConfig _config;
    private LayoutMode _layout = LayoutMode.Auto;
    private bool _layoutChosenByUser;
    private CameraPanelViewModel? _selectedCamera;
    private CameraPanelViewModel? _fullscreenCamera;
    private string _statusText = "就绪";
    private bool _isBusy;
    private DateTime _lastCleanupDate = DateTime.MinValue;
    private string _memoryText = "内存 —";
    private string _diskUsageText = "录像 —";
    private int _statTick;

    public event EventHandler? SettingsRequested;
    public event EventHandler? AddIpCameraRequested;

    /// <summary>请求打开录像库（回放）</summary>
    public event EventHandler? LibraryRequested;

    public MainViewModel(
        ISettingsService settings,
        IStorageService storage,
        INotificationService notifier,
        ICameraDiscovery discovery,
        CameraPipelineFactory factory)
    {
        _settings = settings;
        _storage = storage;
        _notifier = notifier;
        _discovery = discovery;
        _factory = factory;

        _config = _settings.Load();
        _notifier.Configure(_config.Motion.WebhookUrl);

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        AddIpCameraCommand = new RelayCommand(_ => AddIpCameraRequested?.Invoke(this, EventArgs.Empty));
        RemoveCameraCommand = new RelayCommand(p => RemoveCamera(p as CameraPanelViewModel));
        StartAllCommand = new RelayCommand(_ => SetAllMonitoring(true), _ => HasCameras && !AllMonitoring);
        StopAllCommand = new RelayCommand(_ => SetAllMonitoring(false), _ => HasMonitoring);
        RecordAllCommand = new RelayCommand(_ => SetAllRecording(!AnyRecordingRequested),
            _ => HasMonitoring);
        SettingsCommand = new RelayCommand(_ => SettingsRequested?.Invoke(this, EventArgs.Empty));
        LibraryCommand = new RelayCommand(_ => LibraryRequested?.Invoke(this, EventArgs.Empty),
            _ => Directory.Exists(ResolveSaveDir()));
        OpenSaveDirCommand = new RelayCommand(_ => OpenSaveDirectory());
        ClearEventsCommand = new RelayCommand(_ =>
        {
            Events.Clear();
            OnPropertyChanged(nameof(HasEvents));
        });
        OpenSnapshotCommand = new RelayCommand(p => OpenPath(p as string));
        ToggleFullscreenCommand = new RelayCommand(p => ToggleFullscreen(p as CameraPanelViewModel));
        ExitFullscreenCommand = new RelayCommand(_ => ExitFullscreen(), _ => IsFullscreen);
        ToggleFillCommand = new RelayCommand(_ => FillScreen = !FillScreen);

        _diskGuardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Constants.DiskGuardIntervalSeconds) };
        _diskGuardTimer.Tick += (_, _) => RunDiskGuard();
        _diskGuardTimer.Start();

        // 状态栏里的内存/磁盘数字必须一直在动，否则用户只会看到启动那一眼的 0
        _statTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statTimer.Tick += (_, _) => UpdateRuntimeStats();
        _statTimer.Start();

        UpdateRuntimeStats();
        UpdateDiskUsage();
        _ = RefreshAsync();
    }

    // ───────────────────────────── 数据 ─────────────────────────────

    public ObservableCollection<CameraPanelViewModel> Cameras { get; } = new();

    public ObservableCollection<CameraPanelViewModel> VisibleCameras { get; } = new();

    public ObservableCollection<MonitorEvent> Events { get; } = new();

    public AppConfig Config => _config;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool HasCameras => Cameras.Count > 0;

    /// <summary>程序自身内存占用（工作集），状态栏实时刷新</summary>
    public string MemoryText
    {
        get => _memoryText;
        private set => SetProperty(ref _memoryText, value);
    }

    /// <summary>录像 / 截图占用的磁盘空间</summary>
    public string DiskUsageText
    {
        get => _diskUsageText;
        private set => SetProperty(ref _diskUsageText, value);
    }

    /// <summary>布局下拉框索引（0 自动铺满 / 1 单画面 / 2 四画面 / 3 九画面）</summary>
    public int LayoutIndex
    {
        get => (int)_layout;
        set
        {
            var mode = (LayoutMode)Math.Clamp(value, 0, 3);
            if (_layout == mode) return;

            _layout = mode;
            _layoutChosenByUser = mode != LayoutMode.Auto;   // 用户手动选了就不再自动改
            OnPropertyChanged();
            OnPropertyChanged(nameof(GridRows));
            OnPropertyChanged(nameof(GridColumns));
            OnPropertyChanged(nameof(IsSingleLayout));
            ExitFullscreen();
            RefreshVisibleCameras();
        }
    }

    /// <summary>
    /// 网格行数。自动模式下按实际路数算，避免"两路画面占着 2×2 的一半、下面半屏全空"。
    /// 全屏（单路放大）时固定 1×1，否则被选中的那路只会占左侧一格。
    /// </summary>
    public int GridRows => IsFullscreen ? 1 : _layout switch
    {
        LayoutMode.Single => 1,
        LayoutMode.Grid2x2 => 2,
        LayoutMode.Grid3x3 => 3,
        _ => AutoGrid.Rows
    };

    public int GridColumns => IsFullscreen ? 1 : _layout switch
    {
        LayoutMode.Single => 1,
        LayoutMode.Grid2x2 => 2,
        LayoutMode.Grid3x3 => 3,
        _ => AutoGrid.Columns
    };

    /// <summary>自动布局：按路数挑一组能把画面铺满的行列</summary>
    private (int Rows, int Columns) AutoGrid => Cameras.Count switch
    {
        <= 1 => (1, 1),
        2 => (1, 2),
        3 => (1, 3),
        4 => (2, 2),
        5 or 6 => (2, 3),
        _ => (3, 3)
    };

    public CameraPanelViewModel? SelectedCamera
    {
        get => _selectedCamera;
        set
        {
            if (SetProperty(ref _selectedCamera, value) && _layout == LayoutMode.Single)
                RefreshVisibleCameras();
        }
    }

    public CameraPanelViewModel? FullscreenCamera
    {
        get => _fullscreenCamera;
        private set
        {
            if (SetProperty(ref _fullscreenCamera, value))
            {
                OnPropertyChanged(nameof(IsFullscreen));
                OnPropertyChanged(nameof(ShowEventBar));
                OnPropertyChanged(nameof(ShowWindowChrome));
                // 全屏时网格必须变成 1×1，否则画面只占左上角一格
                OnPropertyChanged(nameof(GridRows));
                OnPropertyChanged(nameof(GridColumns));
                RefreshVisibleCameras();
            }
        }
    }

    public bool IsFullscreen => _fullscreenCamera is not null;

    private bool _eventBarVisible = true;

    /// <summary>右上角「事件」开关的状态</summary>
    public bool IsEventBarVisible
    {
        get => _eventBarVisible;
        set
        {
            if (SetProperty(ref _eventBarVisible, value)) OnPropertyChanged(nameof(ShowEventBar));
        }
    }

    /// <summary>事件栏是否占位：单路放大时让出整屏，退出后恢复原来的开关状态</summary>
    public bool ShowEventBar => _eventBarVisible && !IsFullscreen;

    /// <summary>顶栏与状态栏是否显示：全屏时收起来，让画面真正占满整屏</summary>
    public bool ShowWindowChrome => !IsFullscreen;

    public bool IsSingleLayout => _layout == LayoutMode.Single;

    /// <summary>画面填充方式：填满面板（裁切）或等比适应（留边）</summary>
    public bool FillScreen
    {
        get => _config.Camera.FillScreen;
        set
        {
            if (_config.Camera.FillScreen == value) return;
            _config.Camera.FillScreen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FillModeText));
            PersistConfig();
        }
    }

    public string FillModeText => _config.Camera.FillScreen ? "填满" : "适应";

    // ── 实时状态汇总（顶栏胶囊 / 状态栏 / 按钮可用性都吃这几个值） ──

    public int MonitoringCount => Cameras.Count(c => c.IsMonitoring);
    public int RecordingCount => Cameras.Count(c => c.IsRecording);

    /// <summary>已下达录像指令的路数（含"布防中"）</summary>
    public int RecordingRequestedCount => Cameras.Count(c => c.RecordRequested);

    public bool HasMonitoring => MonitoringCount > 0;
    public bool AnyRecording => RecordingCount > 0;
    public bool AnyRecordingRequested => RecordingRequestedCount > 0;

    /// <summary>是否已全部在监控（全部开始按钮据此置灰）</summary>
    public bool AllMonitoring => Cameras.Count > 0 && MonitoringCount == Cameras.Count;

    public string MonitoringChipText => $"监控中 {MonitoringCount} 路";
    public string RecordingChipText => RecordingCount > 0
        ? $"录制中 {RecordingCount} 路"
        : "未在录制";

    /// <summary>顶栏「全部录像 / 停止全部录像」按钮文案</summary>
    public string RecordAllText => AnyRecordingRequested ? "■ 停止全部录像" : "● 全部录像";

    public bool IsAnyRecordingRequested => AnyRecordingRequested;

    /// <summary>录着的时候按钮变红，没录时是中性灰</summary>
    public System.Windows.Media.Brush RecordAllBrush => AnyRecordingRequested
        ? System.Windows.Media.Brushes.Firebrick
        : System.Windows.Media.Brushes.SlateGray;

    /// <summary>状态栏左侧那句话：优先反映"在录什么"，而不是"设置已应用"这种一次性提示</summary>
    public string LiveSummary
    {
        get
        {
            if (Cameras.Count == 0) return "未检测到摄像头";
            if (!HasMonitoring) return $"{Cameras.Count} 路在线 · 未开始监控";
            if (AnyRecording) return $"监控 {MonitoringCount} 路 · 录制 {RecordingCount} 路";
            return AnyRecordingRequested
                ? $"监控 {MonitoringCount} 路 · 已布防，等待运动触发"
                : $"监控 {MonitoringCount} 路 · 未录像（点「录像」才会开始）";
        }
    }

    public bool HasEvents => Events.Count > 0;

    // ───────────────────────────── 命令 ─────────────────────────────

    public ICommand RefreshCommand { get; }
    public ICommand AddIpCameraCommand { get; }
    public ICommand RemoveCameraCommand { get; }
    public ICommand StartAllCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand RecordAllCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand LibraryCommand { get; }
    public ICommand OpenSaveDirCommand { get; }
    public ICommand ClearEventsCommand { get; }
    public ICommand OpenSnapshotCommand { get; }
    public ICommand ToggleFullscreenCommand { get; }
    public ICommand ExitFullscreenCommand { get; }
    public ICommand ToggleFillCommand { get; }

    // ───────────────────────────── 实时状态 ─────────────────────────────

    /// <summary>
    /// 把"在监控几路 / 在录几路"同步到顶栏胶囊、状态栏与按钮可用性。
    /// 由面板的属性变更和 2 秒定时器共同驱动，保证点了开始就能立刻看到反馈。
    /// </summary>
    private void RefreshLiveState()
    {
        OnPropertyChanged(nameof(MonitoringCount));
        OnPropertyChanged(nameof(RecordingCount));
        OnPropertyChanged(nameof(RecordingRequestedCount));
        OnPropertyChanged(nameof(HasMonitoring));
        OnPropertyChanged(nameof(AnyRecording));
        OnPropertyChanged(nameof(AnyRecordingRequested));
        OnPropertyChanged(nameof(IsAnyRecordingRequested));
        OnPropertyChanged(nameof(AllMonitoring));
        OnPropertyChanged(nameof(MonitoringChipText));
        OnPropertyChanged(nameof(RecordingChipText));
        OnPropertyChanged(nameof(RecordAllText));
        OnPropertyChanged(nameof(RecordAllBrush));
        OnPropertyChanged(nameof(LiveSummary));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>工具栏上的小操作（例如切换填充方式）也要落盘，下次启动保持</summary>
    private void PersistConfig()
    {
        try { _settings.Save(_config); }
        catch (Exception ex) { Log.Debug(ex, "保存配置失败"); }
    }

    // ───────────────────────────── 摄像头管理 ─────────────────────────────

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "正在检测摄像头…";

        try
        {
            var found = await _discovery.DiscoverAsync(new CameraDiscoveryOptions
            {
                Backend = _config.Camera.Backend
            });

            // 只补充新设备，不动已有面板：已连接设备短时无响应不应该被当成"拔出"
            var added = 0;
            foreach (var device in found)
            {
                if (Cameras.Any(c => !c.IsIpCamera && c.CameraId == device.Index)) continue;

                // 同名设备（两块一样的 USB 摄像头）用序号区分
                var duplicated = found.Count(d => d.Name == device.Name) > 1;
                var title = duplicated ? $"{device.Name} #{device.Index}" : device.Name;

                AddPanel(_factory.Create(device.Index, null, title, _config));
                added++;
            }

            if (Cameras.Count == 0)
            {
                StatusText = "未检测到摄像头，请确认设备已连接后点击刷新";
            }
            else
            {
                StatusText = added > 0
                    ? $"发现 {added} 个新摄像头，当前共 {Cameras.Count} 路"
                    : $"已连接 {Cameras.Count} 路摄像头";
            }

            AutoSelectLayout();
            RefreshVisibleCameras();
            OnPropertyChanged(nameof(HasCameras));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "刷新摄像头失败");
            StatusText = "刷新失败，请查看日志";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void AddIpCamera(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (Cameras.Any(c => string.Equals(c.DisplayTarget, UrlSanitizer.Mask(url), StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = "该网络摄像头已添加";
            return;
        }

        var nextId = -1;
        while (Cameras.Any(c => c.CameraId == nextId)) nextId--;

        AddPanel(_factory.Create(nextId, url.Trim(), $"网络 {Math.Abs(nextId)}", _config));

        StatusText = $"已添加网络摄像头（{Cameras.Count} 路）";
        AutoSelectLayout();
        RefreshVisibleCameras();
        OnPropertyChanged(nameof(HasCameras));
    }

    private void AddPanel(CameraPipeline pipeline)
    {
        var panel = new CameraPanelViewModel(pipeline, _config);
        panel.EventRaised += OnPanelEvent;
        panel.FullscreenToggleRequested += (s, _) => ToggleFullscreen(s as CameraPanelViewModel);
        panel.RegionChanged += (_, _) => PersistRegion(panel);

        // 监控/录制状态一变就刷新顶栏与状态栏，不用等定时器那一拍
        panel.PropertyChanged += OnPanelPropertyChanged;

        Cameras.Add(panel);
        _selectedCamera ??= panel;
        RefreshLiveState();
    }

    /// <summary>把某一路的检测区域写回配置并落盘</summary>
    private void PersistRegion(CameraPanelViewModel panel)
    {
        try
        {
            if (panel.Region is null) _config.Camera.Regions.Remove(panel.ConfigKey);
            else _config.Camera.Regions[panel.ConfigKey] = panel.Region.Clone();

            _settings.Save(_config);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "保存检测区域失败");
        }
    }

    private void OnPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CameraPanelViewModel.IsMonitoring)
            or nameof(CameraPanelViewModel.IsRecording)
            or nameof(CameraPanelViewModel.RecordRequested))
        {
            RefreshLiveState();
        }
    }

    private void RemoveCamera(CameraPanelViewModel? panel)
    {
        if (panel is null) return;

        panel.EventRaised -= OnPanelEvent;
        panel.PropertyChanged -= OnPanelPropertyChanged;
        Cameras.Remove(panel);
        panel.Dispose();

        if (ReferenceEquals(_selectedCamera, panel)) _selectedCamera = Cameras.FirstOrDefault();
        if (ReferenceEquals(_fullscreenCamera, panel)) FullscreenCamera = null;

        OnPropertyChanged(nameof(SelectedCamera));
        OnPropertyChanged(nameof(HasCameras));
        AutoSelectLayout();
        RefreshVisibleCameras();
        StatusText = $"已移除摄像头，剩余 {Cameras.Count} 路";
    }

    private void SetAllMonitoring(bool monitoring)
    {
        if (Cameras.Count == 0)
        {
            StatusText = "还没有可用的摄像头";
            return;
        }

        foreach (var cam in Cameras) cam.SetMonitoring(monitoring);

        StatusText = monitoring
            ? $"已启动全部 {Cameras.Count} 路监控（录像需手动点击）"
            : $"已停止全部 {Cameras.Count} 路监控（摄像头已释放）";

        RefreshLiveState();
    }

    /// <summary>
    /// 全部录像 / 停止全部录像。只对已在监控且录像功能开启的那几路生效——
    /// 没有监控时本来就没有画面可录。
    /// </summary>
    private void SetAllRecording(bool on)
    {
        var targets = Cameras.Where(c => c.IsMonitoring).ToList();
        if (targets.Count == 0)
        {
            StatusText = "先开始监控，再录像";
            return;
        }

        foreach (var cam in targets) cam.SetRecording(on);

        var effective = targets.Count(c => c.RecordRequested == on);
        StatusText = on
            ? $"已对 {effective} 路下达录像指令"
            : $"已停止 {effective} 路录像";

        RefreshLiveState();
    }

    public void ToggleFullscreen(CameraPanelViewModel? panel)
    {
        if (panel is null) return;
        FullscreenCamera = ReferenceEquals(_fullscreenCamera, panel) ? null : panel;
    }

    private void ExitFullscreen()
    {
        if (_fullscreenCamera is not null) FullscreenCamera = null;
    }

    private void AutoSelectLayout()
    {
        // 用户手动挑过布局就不再插手
        if (_layoutChosenByUser) return;

        if (_layout == LayoutMode.Auto)
        {
            // 自动模式下路数变了，行列要重算
            OnPropertyChanged(nameof(GridRows));
            OnPropertyChanged(nameof(GridColumns));
            return;
        }

        _layout = LayoutMode.Auto;
        OnPropertyChanged(nameof(LayoutIndex));
        OnPropertyChanged(nameof(GridRows));
        OnPropertyChanged(nameof(GridColumns));
        OnPropertyChanged(nameof(IsSingleLayout));
    }

    private void RefreshVisibleCameras()
    {
        VisibleCameras.Clear();

        if (_fullscreenCamera is not null)
        {
            VisibleCameras.Add(_fullscreenCamera);
            return;
        }

        if (_layout == LayoutMode.Single)
        {
            var only = _selectedCamera ?? Cameras.FirstOrDefault();
            if (only is not null) VisibleCameras.Add(only);
            return;
        }

        var capacity = GridRows * GridColumns;
        foreach (var cam in Cameras.Take(capacity))
            VisibleCameras.Add(cam);
    }

    // ───────────────────────────── 事件与设置 ─────────────────────────────

    private void OnPanelEvent(object? sender, MonitorEvent e)
    {
        Events.Insert(0, e);
        while (Events.Count > MaxEvents) Events.RemoveAt(Events.Count - 1);
        OnPropertyChanged(nameof(HasEvents));
    }

    public void ApplyConfig(AppConfig config)
    {
        _config = config;
        _settings.Save(config);
        _notifier.Configure(config.Motion.WebhookUrl);

        foreach (var cam in Cameras) cam.ApplyConfig(config);

        UpdateDiskUsage();
        StatusText = "设置已应用";
    }

    private void OpenSaveDirectory()
    {
        try
        {
            var root = ResolveSaveDir();

            Directory.CreateDirectory(root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "打开保存目录失败");
            StatusText = "无法打开保存目录";
        }
    }

    /// <summary>录像/截图的绝对目录（配置里可能是相对路径）</summary>
    private string ResolveSaveDir()
    {
        var dir = _config.Recording.SaveDir;
        return Path.IsPathRooted(dir) ? dir : Path.Combine(AppContext.BaseDirectory, dir);
    }

    private void OpenPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "打开文件失败：{Path}", path);
        }
    }

    // ───────────────────────────── 磁盘守护 ─────────────────────────────

    private void RunDiskGuard()
    {
        try
        {
            var dir = _config.Recording.SaveDir;

            // 容量上限
            _storage.EnforceStorageLimit(dir, _config.Recording.MaxStorageGb);

            // 每天凌晨清理一次过期文件
            if (DateTime.Now.Hour >= Constants.CleanupHour && _lastCleanupDate.Date != DateTime.Today)
            {
                _lastCleanupDate = DateTime.Today;
                _storage.CleanupExpired(dir, _config.Recording.RetentionDays);
            }

            UpdateDiskUsage();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "磁盘守护任务异常");
        }
    }

    /// <summary>状态栏实时统计：内存每 2 秒一次，磁盘体积每 10 秒一次（扫描目录有开销）</summary>
    private void UpdateRuntimeStats()
    {
        try
        {
            _self.Refresh();
            var ws = _self.WorkingSet64 / 1024.0 / 1024.0;
            var priv = _self.PrivateMemorySize64 / 1024.0 / 1024.0;

            MemoryText = ws >= 1024
                ? $"内存 {ws / 1024:F2} GB"
                : $"内存 {ws:F0} MB";

            // 私有内存异常膨胀通常意味着采集缓冲没释放，值得让用户看得见
            if (priv > ws * 3 && priv > 1024)
                MemoryText += $"（私有 {priv / 1024:F1} GB）";
        }
        catch { }

        _statTick++;
        RefreshLiveState();
        if (_statTick % 5 == 0) UpdateDiskUsage();
    }

    private void UpdateDiskUsage()
    {
        try
        {
            var bytes = _storage.GetDirectorySize(_config.Recording.SaveDir, bypassCache: true);
            var used = bytes / 1024.0 / 1024.0;
            var limit = _config.Recording.MaxStorageGb;

            DiskUsageText = limit > 0
                ? $"录像 {FormatSize(used)} / {(limit < 1 ? $"{limit:F1}" : $"{limit:F0}")} GB"
                : $"录像 {FormatSize(used)}";
        }
        catch
        {
            DiskUsageText = "录像 —";
        }
    }

    private static string FormatSize(double megabytes) => megabytes >= 1024
        ? $"{megabytes / 1024:F2} GB"
        : $"{megabytes:F0} MB";

    public void Dispose()
    {
        _diskGuardTimer.Stop();
        _statTimer.Stop();
        foreach (var cam in Cameras.ToList())
        {
            cam.EventRaised -= OnPanelEvent;
            cam.PropertyChanged -= OnPanelPropertyChanged;
            cam.Dispose();
        }
        Cameras.Clear();
        VisibleCameras.Clear();
        _self.Dispose();
    }
}
