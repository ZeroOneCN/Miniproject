using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using CameraMotionCapture.App.Helpers;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Implementations;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.App.ViewModels;

/// <summary>
/// 单摄像头面板 ViewModel — 独立管理摄像头、帧采集、运动检测、人脸识别、叠加信息
/// </summary>
public class CameraPanelViewModel : ViewModelBase, IDisposable
{
    private readonly CameraService _cameraService;
    private readonly MotionDetectionService _motionService;
    private IFaceRecognitionService? _faceService;
    private readonly DispatcherTimer _frameTimer;
    private AppConfig _config;

    private readonly int _cameraId;
    private string? _cameraUrl;
    private string _cameraName;
    private ImageSource? _cameraImage;
    private double _zoomLevel = 1.0;
    private bool _isRecording;
    private bool _isMonitoring;
    private bool _isPreviewing;
    private bool _motionDetected;
    private int _faceCount;
    private string _statusText = "就绪";
    private Mat? _lastFrame;
    private int _captureFailures;
    private bool _faceInitialized;
    private bool _isDefaultCamera;

    public event EventHandler<double>? ZoomChanged;
    public event EventHandler<ImageSource?>? FrameCaptured;

    public int CameraId => _cameraId;

    public string? CameraUrl => _cameraUrl;

    public bool IsIpCamera => !string.IsNullOrEmpty(_cameraUrl);

    public string CameraName
    {
        get => _cameraName;
        set => SetProperty(ref _cameraName, value);
    }

    public ImageSource? CameraImage
    {
        get => _cameraImage;
        private set
        {
            if (SetProperty(ref _cameraImage, value))
                FrameCaptured?.Invoke(this, value);
        }
    }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            var clamped = Math.Clamp(value, 0.1, 5.0);
            if (SetProperty(ref _zoomLevel, clamped))
                ZoomChanged?.Invoke(this, clamped);
        }
    }

    public bool IsRecording { get => _isRecording; set => SetProperty(ref _isRecording, value); }
    public bool IsMonitoring
    {
        get => _isMonitoring;
        private set
        {
            if (SetProperty(ref _isMonitoring, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool IsPreviewing { get => _isPreviewing; private set => SetProperty(ref _isPreviewing, value); }
    public bool MotionDetected { get => _motionDetected; private set => SetProperty(ref _motionDetected, value); }
    public int FaceCount { get => _faceCount; private set => SetProperty(ref _faceCount, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public bool IsDefaultCamera
    {
        get => _isDefaultCamera;
        set => SetProperty(ref _isDefaultCamera, value);
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SnapshotCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomResetCommand { get; }
    public ICommand MaximizeCommand { get; }

    public CameraPanelViewModel(int cameraId, AppConfig config)
    {
        _cameraId = cameraId;
        _cameraName = $"摄像头 {cameraId}";
        _config = config;

        _cameraService = new CameraService();
        _motionService = new MotionDetectionService();

        _frameTimer = new DispatcherTimer();
        _frameTimer.Tick += OnFrameTimerTick;

        StartCommand = new RelayCommand(_ => StartMonitoring(), _ => !IsMonitoring);
        StopCommand = new RelayCommand(_ => StopMonitoring(), _ => IsMonitoring);
        SnapshotCommand = new RelayCommand(_ => { }, _ => IsMonitoring || IsPreviewing);
        ZoomInCommand = new RelayCommand(_ => ZoomLevel *= 1.25);
        ZoomOutCommand = new RelayCommand(_ => ZoomLevel *= 0.8);
        ZoomResetCommand = new RelayCommand(_ => ZoomLevel = 1.0);
        MaximizeCommand = new RelayCommand(_ => { });

        StartPreview();
    }

    /// <summary>通过 URL 创建网络摄像头面板</summary>
    public CameraPanelViewModel(string url, AppConfig config, int? cameraId = null)
    {
        _cameraId = cameraId ?? -1;
        _cameraUrl = url;
        _cameraName = $"IP摄像头 {_cameraId}";
        _config = config;

        _cameraService = new CameraService();
        _motionService = new MotionDetectionService();

        _frameTimer = new DispatcherTimer();
        _frameTimer.Tick += OnFrameTimerTick;

        StartCommand = new RelayCommand(_ => StartMonitoring(), _ => !IsMonitoring);
        StopCommand = new RelayCommand(_ => StopMonitoring(), _ => IsMonitoring);
        SnapshotCommand = new RelayCommand(_ => { }, _ => IsMonitoring || IsPreviewing);
        ZoomInCommand = new RelayCommand(_ => ZoomLevel *= 1.25);
        ZoomOutCommand = new RelayCommand(_ => ZoomLevel *= 0.8);
        ZoomResetCommand = new RelayCommand(_ => ZoomLevel = 1.0);
        MaximizeCommand = new RelayCommand(_ => { });

        StartPreview();
    }

    private void EnsureFaceService()
    {
        if (_faceInitialized || !_config.FaceRecognition.Enabled) return;
        _faceInitialized = true;

        try
        {
            _faceService = new FaceRecognitionService
            {
                IsEnabled = _config.FaceRecognition.Enabled,
                ConfidenceThreshold = _config.FaceRecognition.ConfidenceThreshold,
                CascadeFilePath = _config.FaceRecognition.CascadeFilePath,
                KnownFacesDir = _config.FaceRecognition.KnownFacesDir
            };
            _faceService.Initialize();
            if (_faceService.IsReady)
                Log.Information("摄像头 {CamId} 人脸识别已启用", _cameraId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "摄像头 {CamId} 人脸识别初始化失败", _cameraId);
        }
    }

    public void ApplyConfig(AppConfig config)
    {
        _config = config;

        if (_faceService != null)
        {
            _faceService.Dispose();
            _faceService = null;
        }
        _faceInitialized = false;
        EnsureFaceService();
    }

    public void StartPreview()
    {
        if (IsMonitoring) return;
        IsPreviewing = true;

        if (!_cameraService.IsOpened)
        {
            bool opened;
            if (IsIpCamera)
                opened = _cameraService.OpenCamera(_cameraUrl!, _cameraId, _config.Camera.Width, _config.Camera.Height, _config.Camera.Fps);
            else
                opened = _cameraService.OpenCamera(_cameraId, null, _config.Camera.Width, _config.Camera.Height, _config.Camera.Fps);
            if (!opened)
            {
                StatusText = "摄像头打开失败";
                IsPreviewing = false;
                return;
            }
        }

        _captureFailures = 0;
        StatusText = "预览中";
        _frameTimer.Interval = TimeSpan.FromMilliseconds(100);
        _frameTimer.Start();
    }

    public void StopPreview()
    {
        IsPreviewing = false;
        if (!IsMonitoring)
        {
            _frameTimer.Stop();
            _cameraService.Close();
        }
    }

    public void StartMonitoring()
    {
        if (IsMonitoring) return;
        StopPreview();

        if (!_cameraService.IsOpened)
        {
            if (!_cameraService.OpenCamera(_cameraId))
            {
                StatusText = "监控启动失败";
                return;
            }
        }

        IsMonitoring = true;
        StatusText = "监控中";
        _frameTimer.Interval = TimeSpan.FromMilliseconds(50);
        _frameTimer.Start();
    }

    public void StopMonitoring()
    {
        if (!IsMonitoring) return;
        IsMonitoring = false;
        IsRecording = false;
        _frameTimer.Stop();
        _cameraService.Close();
        CameraImage = null;
        _lastFrame?.Dispose();
        _lastFrame = null;
        StartPreview();
    }

    public void Restart()
    {
        bool wasMonitoring = IsMonitoring;
        StopPreview();
        _cameraService.Close();
        if (wasMonitoring) StartMonitoring(); else StartPreview();
    }

    private void OnFrameTimerTick(object? sender, EventArgs e)
    {
        try
        {
            var frame = _cameraService.ReadFrame();
            if (frame == null)
            {
                _captureFailures++;
                if (_captureFailures >= 3)
                {
                    _cameraService.RetryOpen();
                    StatusText = "重连中...";
                }
                return;
            }
            _captureFailures = 0;

            // ===== 叠加信息绘制（在原始帧上） =====
            DrawOverlays(frame);

            // ===== 人脸检测 =====
            if (_config.FaceRecognition.Enabled)
            {
                EnsureFaceService();
                if (_faceService?.IsReady == true)
                {
                    var count = _faceService.DrawFaces(frame);
                    FaceCount = count;
                }
            }

            // ===== 转换为 WPF BitmapSource =====
            using var rgbMat = new Mat();
            Cv2.CvtColor(frame, rgbMat, ColorConversionCodes.BGR2RGB);
            var bitmap = BitmapSource.Create(
                rgbMat.Width, rgbMat.Height, 96, 96,
                PixelFormats.Rgb24, null,
                rgbMat.Data, (int)(rgbMat.Step() * rgbMat.Rows), (int)rgbMat.Step());
            bitmap.Freeze();
            CameraImage = bitmap;

            // ===== 运动检测 =====
            var motion = _motionService.DetectMotion(frame, _lastFrame);
            _lastFrame?.Dispose();
            _lastFrame = frame.Clone();
            MotionDetected = motion;

            frame.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CameraPanel {CamId} 帧处理异常", _cameraId);
        }
    }

    /// <summary>在帧上绘制叠加信息（时间戳、设备名、水印）</summary>
    private void DrawOverlays(Mat frame)
    {
        try
        {
            var overlay = _config.Overlay;
            if (overlay == null) return;

            int lineHeight = 24;
            int y = 28;

            // 时间戳（左上角，微软雅黑）
            if (overlay.ShowTimestamp)
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                ChineseTextRenderer.DrawText(frame, timestamp, 10, y, Scalar.White, "Microsoft YaHei", 13);
                y += lineHeight;
            }

            // 设备名（左上角，微软雅黑）
            if (overlay.ShowDeviceName)
            {
                ChineseTextRenderer.DrawText(frame, _cameraName, 10, y, Scalar.White, "Microsoft YaHei", 13);
                y += lineHeight;
            }

            // 水印文字（底部居中 — 组合设备名+时间戳）
            if (!string.IsNullOrWhiteSpace(overlay.WatermarkText))
            {
                var watermark = overlay.WatermarkText;
                // 替换占位符
                watermark = watermark.Replace("{name}", _cameraName)
                    .Replace("{time}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"));

                // 用 GDI+ 测量宽度
                var fontSize = 16f;
                using var font = new System.Drawing.Font("Microsoft YaHei", fontSize, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
                using var tempBmp = new System.Drawing.Bitmap(1, 1);
                using var tempG = System.Drawing.Graphics.FromImage(tempBmp);
                var textSize = tempG.MeasureString(watermark, font);

                int textX = (int)((frame.Width - textSize.Width) / 2);
                int textY = frame.Height - 30;

                ChineseTextRenderer.DrawText(frame, watermark, Math.Max(0, textX), textY,
                    Scalar.White, "Microsoft YaHei", fontSize);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "叠加信息绘制异常");
        }
    }

    public void Dispose()
    {
        _frameTimer.Stop();
        _cameraService.Dispose();
        _motionService.Reset();
        _faceService?.Dispose();
        _lastFrame?.Dispose();
    }
}