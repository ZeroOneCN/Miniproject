using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CameraMotionCapture.App.Imaging;
using CameraMotionCapture.App.Models;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.App.ViewModels;

/// <summary>
/// 单路摄像头面板视图模型。
/// 不再自己开定时器读帧，只负责：把管线产出的最新帧刷进 <see cref="WriteableBitmap"/>，
/// 以及把管线状态同步成可绑定的属性。
/// </summary>
public sealed class CameraPanelViewModel : ViewModelBase, IDisposable
{
    private readonly CameraPipeline _pipeline;
    private readonly DispatcherTimer _renderTimer;

    private WriteableBitmap? _bitmap;
    private long _lastRenderedVersion = -1;
    private long _lastMotionSeq;
    private DateTime _lastSeenMotion = DateTime.MinValue;

    private AppConfig _config;

    private string _cameraName;
    private string _statusText = "未启动";
    private double _fps;
    private int _faceCount;
    private bool _motionActive;
    private bool _isRecording;
    private bool _isOnline;
    private bool _isMonitoring;
    private bool _lastStopped;
    private double _zoomLevel = 1.0;
    private ImageSource? _cameraImage;
    private string _recordText = string.Empty;
    private string _captureInfo = "—";
    private bool _lowFps;

    /// <summary>产生一条事件（运动、掉线等），由主视图模型收集</summary>
    public event EventHandler<MonitorEvent>? EventRaised;

    /// <summary>请求切换全屏</summary>
    public event EventHandler? FullscreenToggleRequested;

    public CameraPanelViewModel(CameraPipeline pipeline, AppConfig config)
    {
        _pipeline = pipeline;
        _config = config;
        _cameraName = pipeline.Name;

        StartCommand = new RelayCommand(_ => SetMonitoring(true), _ => !IsMonitoring);
        StopCommand = new RelayCommand(_ => SetMonitoring(false), _ => IsMonitoring);
        SnapshotCommand = new RelayCommand(_ => TakeSnapshot(), _ => IsOnline);
        ZoomInCommand = new RelayCommand(_ => ZoomLevel *= 1.25);
        ZoomOutCommand = new RelayCommand(_ => ZoomLevel *= 0.8);
        ZoomResetCommand = new RelayCommand(_ => ZoomLevel = 1.0);
        FullscreenCommand = new RelayCommand(_ => FullscreenToggleRequested?.Invoke(this, EventArgs.Empty));

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / Constants.RenderFps)
        };
        _renderTimer.Tick += OnRenderTick;

        _pipeline.Start();
        _renderTimer.Start();
    }

    public CameraPipeline Pipeline => _pipeline;

    public int CameraId => _pipeline.CameraId;
    public bool IsIpCamera => _pipeline.IsIpCamera;
    public string DisplayTarget => _pipeline.DisplayTarget;

    public string CameraName
    {
        get => _cameraName;
        private set
        {
            if (SetProperty(ref _cameraName, value)) _pipeline.Rename(value);
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ImageSource? CameraImage
    {
        get => _cameraImage;
        private set => SetProperty(ref _cameraImage, value);
    }

    public double Fps
    {
        get => _fps;
        private set => SetProperty(ref _fps, value);
    }

    public int FaceCount
    {
        get => _faceCount;
        private set
        {
            if (SetProperty(ref _faceCount, value)) OnPropertyChanged(nameof(ShowFaceCount));
        }
    }

    public bool ShowFaceCount => _faceCount > 0 && _config.Face.Enabled;

    public bool MotionActive
    {
        get => _motionActive;
        private set => SetProperty(ref _motionActive, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set => SetProperty(ref _isRecording, value);
    }

    /// <summary>
    /// 一眼看懂当前到底在干什么：录制中带计时，监控但没录像时明确写出来，
    /// 免得用户点了「开始监控」后盯着画面猜"这算在录吗"。
    /// </summary>
    public string RecordText
    {
        get => _recordText;
        private set => SetProperty(ref _recordText, value);
    }

    /// <summary>状态胶囊的前景色 / 底色由它派生</summary>
    public Brush RecordBrush => _isRecording
        ? DangerBrush
        : _isMonitoring ? Brushes.LimeGreen : Brushes.Gray;

    public bool ShowRecordChip => _isMonitoring || _isRecording;

    /// <summary>协商到的采集参数（分辨率 / 编码 / 后端），放在帧率旁边做 ToolTip</summary>
    public string CaptureInfo
    {
        get => _captureInfo;
        private set => SetProperty(ref _captureInfo, value);
    }

    public string FpsToolTip =>
        $"实测 {_fps:F1} fps\n设定 {_pipeline.RequestedFps} fps\n{CaptureInfo}\n" +
        "低于设定值时常见原因：暗光下自动曝光拉长、USB 带宽不足（未协商到 MJPG）、设备本身不支持该分辨率下的高帧率";

    /// <summary>实测帧率明显低于设定值时变琥珀色，提示"你的相机没跑到设定帧率"</summary>
    public Brush FpsBrush
    {
        get
        {
            var requested = _pipeline.RequestedFps;
            if (_fps <= 0 || requested <= 0) return NeutralFpsBrush;
            return _fps < requested * 0.8 ? WarningFpsBrush : NeutralFpsBrush;
        }
    }

    private static readonly Brush DangerBrush = Frozen("#E0524B");
    private static readonly Brush NeutralFpsBrush = Frozen("#7C838F");
    private static readonly Brush WarningFpsBrush = Frozen("#E8A33D");

    private static Brush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (SetProperty(ref _isOnline, value)) OnPropertyChanged(nameof(StatusBrush));
        }
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        private set
        {
            if (SetProperty(ref _isMonitoring, value))
            {
                OnPropertyChanged(nameof(MonitorText));
                OnPropertyChanged(nameof(StatusBrush));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string MonitorText => _isMonitoring ? "停止" : "监控";

    /// <summary>已停止采集（设备已释放）——面板上盖一层提示，避免"画面冻住但没人知道为什么"</summary>
    public bool IsStopped => !_isMonitoring && _pipeline.State == CameraState.Idle;

    /// <summary>状态指示灯颜色</summary>
    public Brush StatusBrush => !_isMonitoring
        ? Brushes.DimGray
        : _isOnline ? Brushes.LimeGreen : Brushes.OrangeRed;

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            // 下限 1x：画面永远铺满面板，不缩到比面板还小（留一圈黑边毫无意义）
            var clamped = Math.Clamp(value, 1.0, 8.0);
            if (SetProperty(ref _zoomLevel, clamped)) OnPropertyChanged(nameof(ZoomText));
        }
    }

    public string ZoomText => $"{_zoomLevel:F1}x";

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SnapshotCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomResetCommand { get; }
    public ICommand FullscreenCommand { get; }

    // ───────────────────────────── 操作 ─────────────────────────────

    public void SetMonitoring(bool monitoring)
    {
        if (monitoring)
        {
            // 停止时设备已经释放，这里要重新打开（Start 内部会等上一代线程退出）
            _pipeline.Start(true);
            StatusText = "监控中";
        }
        else
        {
            // 真正停掉：关设备、停采集线程、关录像，而不是只把录制标志位翻一下
            _pipeline.Stop();
            StatusText = "已停止";
        }

        IsMonitoring = monitoring;
        if (!monitoring)
        {
            IsRecording = false;
            MotionActive = false;
            Fps = 0;
        }

        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(StatusBrush));
        CommandManager.InvalidateRequerySuggested();
    }

    public void ApplyConfig(AppConfig config)
    {
        _config = config;
        _pipeline.UpdateConfig(config);
        OnPropertyChanged(nameof(ShowFaceCount));
    }

    private void TakeSnapshot()
    {
        var path = _pipeline.RequestSnapshot();
        StatusText = path is null ? "截图失败" : "已截图";

        if (path is not null)
        {
            EventRaised?.Invoke(this, new MonitorEvent
            {
                Time = DateTime.Now,
                CameraName = CameraName,
                Message = "手动截图",
                SnapshotPath = path
            });
        }
    }

    // ───────────────────────────── 渲染循环 ─────────────────────────────

    private void OnRenderTick(object? sender, EventArgs e)
    {
        try
        {
            SyncStatus();
            RenderFrame();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "面板 {Cam} 渲染异常", CameraId);
        }
    }

    private void RenderFrame()
    {
        var version = _pipeline.FrameVersion;
        if (version == _lastRenderedVersion) return;
        _lastRenderedVersion = version;

        var width = _pipeline.FrameWidth;
        var height = _pipeline.FrameHeight;
        if (width <= 0 || height <= 0) return;

        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            CameraImage = _bitmap;
        }

        _bitmap.Lock();
        try
        {
            if (_pipeline.TryCopyFrame(_bitmap.BackBuffer, _bitmap.BackBufferStride, out _, out _))
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            _bitmap.Unlock();
        }
    }

    private void SyncStatus()
    {
        var wasRecording = IsRecording;
        var wasMonitoring = IsMonitoring;
        var wasLowFps = _lowFps;

        IsOnline = _pipeline.StateIsOnline;
        IsMonitoring = _pipeline.IsMonitoring;
        IsRecording = _pipeline.IsRecording;
        MotionActive = _pipeline.MotionActive;
        Fps = Math.Round(_pipeline.Fps, 1);

        var stopped = IsStopped;
        if (stopped != _lastStopped)
        {
            _lastStopped = stopped;
            OnPropertyChanged(nameof(IsStopped));
        }

        if (FaceCount != _pipeline.FaceCount) FaceCount = _pipeline.FaceCount;

        var status = _pipeline.StatusText;
        if (status != StatusText) StatusText = status;

        var capture = _pipeline.CaptureInfo;
        if (capture != _captureInfo) CaptureInfo = capture;

        // 录制状态文案：录制中带计时，未录像时明确写清楚为什么没有 REC
        var record = BuildRecordText();
        if (record != _recordText) RecordText = record;

        _lowFps = Fps > 0 && _pipeline.RequestedFps > 0 && Fps < _pipeline.RequestedFps * 0.8;

        if (wasRecording != IsRecording || wasMonitoring != IsMonitoring)
            OnPropertyChanged(nameof(RecordBrush));

        if (wasLowFps != _lowFps)
            OnPropertyChanged(nameof(FpsBrush));

        OnPropertyChanged(nameof(ShowRecordChip));

        // 运动事件：管线用自增序号通知，避免跨线程回调
        var seq = _pipeline.MotionEventSeq;
        if (seq != _lastMotionSeq)
        {
            _lastMotionSeq = seq;

            // 同一秒内的连续检测合并成一条事件，避免刷屏
            if ((DateTime.Now - _lastSeenMotion).TotalSeconds >= 3)
            {
                _lastSeenMotion = DateTime.Now;
                EventRaised?.Invoke(this, new MonitorEvent
                {
                    Time = _pipeline.LastMotionAt,
                    CameraName = CameraName,
                    Message = "检测到运动",
                    SnapshotPath = _pipeline.LastSnapshotPath
                });
            }
        }
    }

    private string BuildRecordText()
    {
        if (_pipeline.IsRecording)
        {
            var elapsed = _pipeline.RecordingElapsed;
            return $"REC {(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }

        if (!IsMonitoring) return "已停止";

        if (!_config.Recording.Enabled) return "监控中 · 录像未开启";

        return _config.Recording.Mode == RecordMode.MotionTrigger
            ? "监控中 · 运动触发"
            : "监控中";
    }

    public void Dispose()
    {
        _renderTimer.Stop();
        _renderTimer.Tick -= OnRenderTick;
        _pipeline.Dispose();
        _bitmap = null;
    }
}
