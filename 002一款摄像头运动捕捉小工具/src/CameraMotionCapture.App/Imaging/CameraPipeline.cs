using System.Runtime.InteropServices;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using OpenCvSharp;
using Serilog;

namespace CameraMotionCapture.App.Imaging;

/// <summary>
/// 单路摄像头采集管线。
///
/// <para>这是本次重构的核心。旧实现用 <c>DispatcherTimer</c> 在 UI 线程上跑
/// <c>VideoCapture.Read()</c>（同步阻塞）+ 人脸检测 + 运动检测 + 位图创建，
/// 多路时必然把界面拖死。现在：</para>
///
/// <list type="bullet">
/// <item>采集、检测、录像全部在独立后台线程完成；</item>
/// <item>UI 线程只按固定帧率把共享缓冲区里的最新帧拷进 <c>WriteableBitmap</c>；</item>
/// <item>处理不过来时自动丢帧（只保留最新），延迟不会累积；</item>
/// <item>掉线自动指数退避重连。</item>
/// </list>
/// </summary>
public sealed class CameraPipeline : IDisposable
{
    private readonly ICameraService _camera;
    private readonly IMotionDetectionService _motion;
    private readonly IFaceDetectionService _face;
    private readonly IVideoRecordingService _recorder;
    private readonly IStorageService _storage;
    private readonly INotificationService _notifier;
    private readonly OverlayComposer _overlay = new();

    private readonly object _configLock = new();
    private AppConfig _config;

    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _monitoring;
    private volatile bool _restartRequested;

    /// <summary>启动/停止的生命周期锁。避免"刚停就启"时两代线程同时持有同一个设备。</summary>
    private readonly object _lifecycleLock = new();
    private int _runId;

    // ── 输出帧（后台写 / UI 读） ──
    private readonly object _frameLock = new();
    private byte[]? _frameBuffer;
    private int _frameWidth;
    private int _frameHeight;
    private long _frameVersion;

    // ── 运行状态 ──
    private volatile int _stateValue = (int)CameraState.Idle;
    private volatile bool _motionActive;
    private volatile bool _recording;
    private int _faceCount;
    private double _fps;
    private string _statusText = "未启动";
    private long _motionEventSeq;
    private DateTime _lastMotionAt = DateTime.MinValue;
    private string? _lastSnapshotPath;

    // ── 内部计时 ──
    private DateTime _lastFaceCheck = DateTime.MinValue;
    private DateTime _lastNotifyAt = DateTime.MinValue;
    private DateTime _recordUntil = DateTime.MinValue;
    private DateTime _fpsWindowStart = DateTime.Now;
    private int _fpsFrames;
    private int _readFailures;
    private int _reconnectDelaySec = 1;

    public CameraPipeline(
        int cameraId,
        string? url,
        string name,
        AppConfig config,
        ICameraService camera,
        IMotionDetectionService motion,
        IFaceDetectionService face,
        IVideoRecordingService recorder,
        IStorageService storage,
        INotificationService notifier)
    {
        CameraId = cameraId;
        Url = url;
        Name = name;
        _config = config;
        _camera = camera;
        _motion = motion;
        _face = face;
        _recorder = recorder;
        _storage = storage;
        _notifier = notifier;
    }

    public int CameraId { get; }
    public string? Url { get; }
    public string Name { get; private set; }
    public bool IsIpCamera => !string.IsNullOrEmpty(Url);
    public string DisplayTarget => IsIpCamera ? UrlSanitizer.Mask(Url) : $"USB #{CameraId}";

    // ── 供 UI 轮询的只读状态 ──
    public CameraState State => (CameraState)_stateValue;
    public bool StateIsOnline => State == CameraState.Online;
    public bool IsMonitoring => _monitoring;
    public bool MotionActive => _motionActive;
    public bool IsRecording => _recording;
    public int FaceCount => Volatile.Read(ref _faceCount);
    public double Fps => Volatile.Read(ref _fps);
    public string StatusText => _statusText;
    public long FrameVersion => Volatile.Read(ref _frameVersion);
    public long MotionEventSeq => Volatile.Read(ref _motionEventSeq);
    public DateTime LastMotionAt => _lastMotionAt;
    public string? LastSnapshotPath => _lastSnapshotPath;
    public int FrameWidth { get { lock (_frameLock) return _frameWidth; } }
    public int FrameHeight { get { lock (_frameLock) return _frameHeight; } }

    public void Rename(string name) => Name = name;

    // ───────────────────────────── 生命周期 ─────────────────────────────

    public void Start(bool startMonitoring = true)
    {
        Thread? stale;

        lock (_lifecycleLock)
        {
            if (_running)
            {
                _monitoring = startMonitoring;
                return;
            }

            _running = true;
            _monitoring = startMonitoring;
            _stateValue = (int)CameraState.Connecting;
            _statusText = "连接中…";
            _readFailures = 0;
            _reconnectDelaySec = 1;
            Interlocked.Increment(ref _runId);
            stale = _thread;
        }

        var runId = Volatile.Read(ref _runId);

        _thread = new Thread(() =>
        {
            // 上一轮线程可能还卡在 Read()/Release() 上，等它彻底放手再接管设备
            if (stale is { IsAlive: true })
            {
                try { stale.Join(4000); } catch { }

                if (stale.IsAlive)
                    Log.Warning("摄像头 {Id} 上一条采集线程未在 4 秒内退出，仍继续启动", CameraId);
            }

            if (!_running) return;

            // 上一代如果是被"停掉又立刻启动"，设备可能还挂在打开状态。
            // 这种半开状态在 DSHOW 上会让新线程的 Read() 一直阻塞（不报错、不出帧），
            // 所以这里强制先关干净，再让采集循环重新 Open。
            CloseSafely();

            WorkerLoop(runId);
        })
        {
            IsBackground = true,
            Name = $"pipeline-cam{CameraId}"
        };

        _thread.Start();
        Log.Information("采集管线启动：摄像头 {Id}（{Target}）", CameraId, DisplayTarget);

        // 启动时按配置加载级联模型。以前只有从「设置」点确定才会走 UpdateConfig，
        // 结果是配置文件里开着人脸检测、启动后却没有生效。
        if (_config.Face.Enabled && !_face.IsReady)
            _face.Initialize(_config.Face.CascadeFilePath);
    }

    public void SetMonitoring(bool monitoring)
    {
        _monitoring = monitoring;
        if (!monitoring)
        {
            _motionActive = false;
            _recordUntil = DateTime.MinValue;
            _motion.Reset();
        }
    }

    /// <summary>
    /// 停止采集。默认立即返回（关闭设备与线程回收交给后台），
    /// 避免在 UI 线程等待可能阻塞的 <c>VideoCapture.Release()</c>。
    /// </summary>
    public void Stop(bool waitForExit = false)
    {
        Thread? thread;
        int stopRunId;

        lock (_lifecycleLock)
        {
            if (!_running) return;
            _running = false;
            _monitoring = false;
            thread = _thread;

            // 必须在锁内取：出了锁再取，可能已经读到"新一轮启动"的 id，
            // 那样关设备的收尾逻辑就会把新线程刚打开的设备给关掉
            stopRunId = _runId;
        }

        _motionActive = false;
        _recording = false;
        _recordUntil = DateTime.MinValue;
        _stateValue = (int)CameraState.Idle;
        _statusText = "已停止";

        if (waitForExit)
        {
            CloseSafely();
            TryJoin(3000);
            return;
        }

        _ = Task.Run(() =>
        {
            // 先让采集线程自己从 Read() 里出来，再关设备；顺序反了容易踩到已释放的句柄
            try { thread?.Join(1500); } catch { }

            // 这期间用户可能已经点了"开始"：runId 一变，设备就属于新一代线程了，不能在这里关
            if (!_running && Volatile.Read(ref _runId) == stopRunId)
            {
                try { CloseSafely(); } catch { }
            }

            TryJoin(1500);
        });
    }

    public void UpdateConfig(AppConfig config)
    {
        bool needsRestart;
        lock (_configLock)
        {
            needsRestart = _config.Camera.Width != config.Camera.Width
                        || _config.Camera.Height != config.Camera.Height
                        || _config.Camera.Fps != config.Camera.Fps
                        || _config.Camera.Backend != config.Camera.Backend
                        || _config.Camera.HardwareAccel != config.Camera.HardwareAccel;

            _config = config;
        }

        _overlay.Invalidate();
        _notifier.Configure(config.Motion.WebhookUrl);

        // 人脸开关切换需要重新加载级联
        if (config.Face.Enabled && !_face.IsReady)
            _face.Initialize(config.Face.CascadeFilePath);

        if (needsRestart && _running) _restartRequested = true;
    }

    public void Dispose()
    {
        Stop(waitForExit: true);
        _overlay.Dispose();
        _recorder.Dispose();
        _face.Dispose();
        _motion.Dispose();
        _camera.Dispose();
    }

    // ───────────────────────────── 采集主循环 ─────────────────────────────

    private void WorkerLoop(int runId)
    {
        Log.Information("采集循环开始：摄像头 {Id}（run={Run}，已打开={Opened}）", CameraId, runId, _camera.IsOpened);
        while (_running && runId == Volatile.Read(ref _runId))
        {
            if (_restartRequested)
            {
                _restartRequested = false;
                SetState(CameraState.Connecting, "应用新参数…");
                CloseSafely();
                Thread.Sleep(200);
            }

            if (!_camera.IsOpened)
            {
                if (!TryOpenDevice()) continue;
                _motion.Reset();
                _readFailures = 0;
                _reconnectDelaySec = 1;
                SetState(CameraState.Online, _monitoring ? "监控中" : "预览中");
            }

            var frame = _camera.ReadFrame();
            if (frame is null)
            {
                if (HandleReadFailure()) continue;
                continue;
            }

            try
            {
                ProcessFrame(frame);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "摄像头 {Id} 帧处理异常", CameraId);
            }
            finally
            {
                frame.Dispose();
            }
        }

        // 只有当前这一代线程负责收尾：被新一代取代时，设备已交给新线程，不能在这里关
        if (runId == Volatile.Read(ref _runId))
        {
            CloseSafely();
            SetState(CameraState.Idle, "已停止");
            Log.Information("采集管线结束：摄像头 {Id}", CameraId);
        }
    }

    private void ProcessFrame(Mat frame)
    {
        var cfg = SnapshotConfig();
        var now = DateTime.Now;

        // ① 运动检测必须在叠加水印之前 —— 时间戳每秒变化，否则会把自己判成运动
        var motionNow = false;
        if (_monitoring && cfg.Motion.Enabled)
            motionNow = _motion.Detect(frame);

        // ② 人脸检测限频到 5fps，避免每帧都跑级联
        if (cfg.Face.Enabled && _face.IsReady && (now - _lastFaceCheck).TotalMilliseconds >= 200)
        {
            Volatile.Write(ref _faceCount, _face.DetectAndDraw(frame));
            _lastFaceCheck = now;
        }

        // ③ 叠加时间戳/设备名/水印（内容不变时复用已渲染图层）
        _overlay.Compose(frame, Name, cfg.Overlay);

        // ④ 录像
        UpdateRecording(frame, motionNow, cfg, now);

        // ⑤ 运动事件（截图 + 推送）
        if (motionNow)
        {
            _motionActive = true;
            _lastMotionAt = now;
            HandleMotionEvent(frame, cfg, now);
        }
        else
        {
            _motionActive = false;
        }

        // ⑥ 输出给 UI
        PublishFrame(frame);

        // ⑦ 帧率统计
        _fpsFrames++;
        var elapsed = (now - _fpsWindowStart).TotalSeconds;
        if (elapsed >= 1.0)
        {
            Volatile.Write(ref _fps, _fpsFrames / elapsed);
            _fpsFrames = 0;
            _fpsWindowStart = now;
        }
    }

    private void HandleMotionEvent(Mat frame, AppConfig cfg, DateTime now)
    {
        var cooldown = TimeSpan.FromSeconds(Math.Max(1, cfg.Motion.CooldownSeconds));
        if (now - _lastNotifyAt < cooldown) return;

        _lastNotifyAt = now;
        Interlocked.Increment(ref _motionEventSeq);

        if (cfg.Motion.SnapshotOnMotion)
        {
            var path = _storage.SaveSnapshot(frame, cfg.Recording.SaveDir, cfg.Recording.JpegQuality,
                cfg.Recording.UseDailyFolder, "motion", CameraId);
            if (path is not null) _lastSnapshotPath = path;
        }

        if (_notifier.IsConfigured)
        {
            var note = $"[{Constants.AppName}] {Name} 检测到运动\n时间：{now:yyyy-MM-dd HH:mm:ss}";
            var copy = frame.Clone();
            _ = Task.Run(async () =>
            {
                using (copy)
                {
                    try { await _notifier.SendMotionAlertAsync(copy, CameraId, Name, note); }
                    catch (Exception ex) { Log.Debug(ex, "运动告警推送异常"); }
                }
            });
        }
    }

    private void UpdateRecording(Mat frame, bool motionNow, AppConfig cfg, DateTime now)
    {
        var rec = cfg.Recording;

        if (!rec.Enabled || !_monitoring)
        {
            if (_recorder.IsRecording) { _recorder.Stop(); _recording = false; }
            return;
        }

        bool shouldWrite;
        if (rec.Mode == RecordMode.Continuous)
        {
            shouldWrite = true;
        }
        else
        {
            if (motionNow) _recordUntil = now.AddSeconds(Math.Max(3, rec.MotionRecordSeconds));
            shouldWrite = now < _recordUntil;
        }

        if (!shouldWrite)
        {
            if (_recorder.IsRecording) { _recorder.Stop(); _recording = false; }
            return;
        }

        if (!_recorder.IsRecording)
        {
            var ok = _recorder.Start(rec.SaveDir, frame.Width, frame.Height,
                Math.Max(1, cfg.Camera.Fps), rec.Codec, rec.JpegQuality,
                rec.SegmentMinutes, rec.UseDailyFolder);

            _recording = ok;
            if (ok) _statusText = "监控中 · 录制";
            else _statusText = "监控中（录制启动失败）";

            if (!ok) return;
        }

        _recorder.WriteFrame(frame);
    }

    private void PublishFrame(Mat bgr)
    {
        lock (_frameLock)
        {
            var need = bgr.Width * bgr.Height * 4;
            if (_frameBuffer is null || _frameBuffer.Length != need)
            {
                _frameBuffer = new byte[need];
                _frameWidth = bgr.Width;
                _frameHeight = bgr.Height;
            }

            using var dest = Mat.FromPixelData(_frameHeight, _frameWidth, MatType.CV_8UC4, _frameBuffer);
            Cv2.CvtColor(bgr, dest, ColorConversionCodes.BGR2BGRA);
            _frameVersion++;
        }
    }

    /// <summary>把最新一帧拷贝到目标缓冲区（通常是 WriteableBitmap.BackBuffer）。</summary>
    public bool TryCopyFrame(IntPtr destination, int destinationStride, out int width, out int height)
    {
        lock (_frameLock)
        {
            width = _frameWidth;
            height = _frameHeight;

            if (_frameBuffer is null || _frameWidth == 0 || _frameHeight == 0) return false;

            var sourceStride = _frameWidth * 4;

            if (destinationStride == sourceStride)
            {
                Marshal.Copy(_frameBuffer, 0, destination, _frameBuffer.Length);
                return true;
            }

            for (var y = 0; y < _frameHeight; y++)
            {
                Marshal.Copy(_frameBuffer, y * sourceStride,
                    IntPtr.Add(destination, y * destinationStride), sourceStride);
            }
            return true;
        }
    }

    /// <summary>
    /// 把当前最新画面（已叠加水印）另存为截图，返回文件路径。
    /// 直接从共享 BGRA 缓冲区还原，无需再让采集线程额外保留一份 BGR 副本。
    /// </summary>
    public string? RequestSnapshot()
    {
        var cfg = SnapshotConfig();

        try
        {
            lock (_frameLock)
            {
                if (_frameBuffer is null || _frameWidth == 0) return null;

                using var bgra = Mat.FromPixelData(_frameHeight, _frameWidth, MatType.CV_8UC4, _frameBuffer);
                using var bgr = new Mat();
                Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);

                return _storage.SaveSnapshot(bgr, cfg.Recording.SaveDir, cfg.Recording.JpegQuality,
                    cfg.Recording.UseDailyFolder, "manual", CameraId);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "手动截图失败：摄像头 {Id}", CameraId);
            return null;
        }
    }

    // ───────────────────────────── 连接与容错 ─────────────────────────────

    private bool TryOpenDevice()
    {
        SetState(CameraState.Connecting, _readFailures > 0 ? "重连中…" : "连接中…");

        var cfg = SnapshotConfig();
        bool opened;

        if (IsIpCamera)
            opened = _camera.Open(Url!, cfg.Camera.Width, cfg.Camera.Height, cfg.Camera.Fps);
        else
            opened = _camera.Open(CameraId, cfg.Camera.BackendName, cfg.Camera.Width,
                cfg.Camera.Height, cfg.Camera.Fps, cfg.Camera.HardwareAccel);

        if (opened)
        {
            Log.Information("摄像头 {Id} 已连接", CameraId);
            return true;
        }

        _readFailures = Math.Max(_readFailures, 1);
        SetState(CameraState.Retrying, $"{_reconnectDelaySec}s 后重试");

        SleepInterruptible(TimeSpan.FromSeconds(_reconnectDelaySec));
        _reconnectDelaySec = Math.Min(_reconnectDelaySec * 2, 15);   // 上限 15 秒：等太久用户会以为坏了
        return false;
    }

    /// <summary>返回 true 表示已进入重连流程，本轮跳过后续处理。</summary>
    private bool HandleReadFailure()
    {
        _readFailures++;
        if (_readFailures < 5) return false;

        Log.Warning("摄像头 {Id} 连续 {Count} 次读取失败，进入重连", CameraId, _readFailures);
        SetState(CameraState.Retrying, "信号中断，重连中…");
        CloseSafely();
        _motion.Reset();
        _readFailures = 0;
        _reconnectDelaySec = 1;
        return true;
    }

    private void SleepInterruptible(TimeSpan duration)
    {
        var until = DateTime.Now + duration;
        while (DateTime.Now < until && _running)
            Thread.Sleep(100);
    }

    private void CloseSafely()
    {
        try
        {
            if (_recorder.IsRecording) _recorder.Stop();
            _recording = false;
            _camera.Close();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "关闭摄像头 {Id} 时出现异常", CameraId);
        }
    }

    private void TryJoin(int timeoutMs)
    {
        try
        {
            if (_thread is { IsAlive: true }) _thread.Join(timeoutMs);
        }
        catch { }
    }

    private void SetState(CameraState state, string text)
    {
        _stateValue = (int)state;
        _statusText = text;
    }

    private AppConfig SnapshotConfig()
    {
        lock (_configLock) return _config;
    }
}
