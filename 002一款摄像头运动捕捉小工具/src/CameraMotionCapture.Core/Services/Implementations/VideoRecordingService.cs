using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>
/// 分段视频录制（MJPEG AVI）。
///
/// <para>相比上一版（交给 OpenCV <c>VideoWriter</c>）的差别：</para>
/// <list type="number">
/// <item><b>画质真的能控</b>：自己编码 JPEG 并写 AVI 容器，质量参数说了算。
/// OpenCV 的 MJPG 写出品一律接近最高质量、参数被忽略，这也是"录 12 小时吃掉 8G"的根源之一；</item>
/// <item><b>按档位降分辨率/降帧率</b>：等比缩放（绝不拉伸），每帧都过一遍节流；</item>
/// <item><b>预录环形缓冲</b>：没在录的时候也只保留最近 N 秒的编码帧，运动触发时先把这段倒出去，
/// 于是录像里有"事情发生前"的那几秒。</item>
/// </list>
/// </summary>
public sealed class VideoRecordingService : IVideoRecordingService
{
    private readonly int _cameraId;
    private readonly object _sync = new();

    private RecordingOptions _options = new();

    private MjpegAviWriter? _writer;
    private string? _currentPath;
    private DateTime _segmentStart;

    // 输出规格（每次开始录制时按当前帧尺寸确定，会话内固定）
    private int _outWidth;
    private int _outHeight;
    private int _outFps;
    private int _quality;

    private DateTime _lastWriteAt = DateTime.MinValue;
    private DateTime _lastPreRollAt = DateTime.MinValue;

    /// <summary>本段文件开头补了多少秒预录画面，收尾算真实帧率时要算进去</summary>
    private double _preRollSecondsInSegment;

    // 预录环形缓冲：只存编码后的 JPEG 字节，5 秒 15fps 大约 3 MB
    private readonly Queue<byte[]> _preRoll = new();
    private int _preRollLimit;

    private Mat? _scaled;

    public VideoRecordingService(int cameraId = 0) => _cameraId = cameraId;

    public bool IsRecording { get { lock (_sync) return _writer is not null; } }
    public string? CurrentFilePath { get { lock (_sync) return _currentPath; } }

    public long BytesWritten
    {
        get
        {
            lock (_sync)
            {
                try
                {
                    if (_currentPath is not null && File.Exists(_currentPath))
                        return new FileInfo(_currentPath).Length;
                }
                catch { }
                return 0;
            }
        }
    }

    public int PreRollFrames { get { lock (_sync) return _preRoll.Count; } }

    public void Configure(RecordingOptions options)
    {
        lock (_sync)
        {
            _options = options ?? new RecordingOptions();

            var profile = _options.Profile;
            var preRoll = Math.Clamp(_options.PreRollSeconds, 0, 60);
            var fps = profile.MaxFps > 0 ? profile.MaxFps : 15;

            _preRollLimit = preRoll == 0 ? 0 : Math.Max(1, preRoll * fps);
            while (_preRoll.Count > _preRollLimit) _preRoll.Dequeue();
        }
    }

    public void Feed(Mat frame, bool recordNow, bool keepPreRoll = false)
    {
        if (frame is null || frame.Empty()) return;

        lock (_sync)
        {
            try
            {
                if (recordNow)
                {
                    if (_writer is null && !StartSegment(frame)) return;
                    if (!Due(ref _lastWriteAt, _outFps)) return;
                    WriteCurrent(frame);
                }
                else
                {
                    if (_writer is not null) StopSegment();
                    if (keepPreRoll) BufferPreRoll(frame);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "录制帧处理失败，当前分段已收尾");
                StopSegment();
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopSegment();
            _preRoll.Clear();
        }
    }

    public void Dispose()
    {
        Stop();
        _scaled?.Dispose();
        _scaled = null;
    }

    // ───────────────────────────── 预录 ─────────────────────────────

    /// <summary>没在录的时候维护最近 N 秒的编码帧，供运动触发回溯。</summary>
    private void BufferPreRoll(Mat frame)
    {
        if (_preRollLimit <= 0) return;

        var profile = _options.Profile;
        var fps = profile.MaxFps > 0 ? profile.MaxFps : 15;
        if (!Due(ref _lastPreRollAt, fps)) return;

        var jpeg = Encode(frame, profile, out _, out _);
        if (jpeg is null) return;

        _preRoll.Enqueue(jpeg);
        while (_preRoll.Count > _preRollLimit) _preRoll.Dequeue();
    }

    // ───────────────────────────── 分段 ─────────────────────────────

    private bool StartSegment(Mat frame)
    {
        var profile = _options.Profile;
        var (width, height) = OutputSize(frame, profile);

        _outWidth = width;
        _outHeight = height;
        _quality = profile.JpegQuality;
        _outFps = profile.MaxFps > 0 ? Math.Min(profile.MaxFps, 60) : 30;

        try
        {
            var dir = _options.UseDailyFolder
                ? Path.Combine(_options.SaveDir, DateTime.Now.ToString("yyyy-MM-dd"))
                : _options.SaveDir;
            Directory.CreateDirectory(dir);

            var name = $"{_options.FileNamePrefix}_cam{_cameraId}_{DateTime.Now:yyyyMMdd_HHmmss}.avi";
            var path = Path.Combine(dir, name);

            _writer = new MjpegAviWriter(path, _outWidth, _outHeight, _outFps);
            _currentPath = path;
            _segmentStart = DateTime.Now;
            _lastWriteAt = DateTime.MinValue;

            // 先把预录缓冲倒进去：这样事件录像开头就有"事情发生前"的那几秒
            var preRollCount = _preRoll.Count;
            while (_preRoll.Count > 0)
            {
                var jpeg = _preRoll.Dequeue();
                _writer.AddJpeg(jpeg, jpeg.Length);
            }

            if (preRollCount > 0)
            {
                _preRollSecondsInSegment = preRollCount / (double)Math.Max(1, _outFps);
                Log.Information("摄像头 {Id} 补入 {Seconds:F1} 秒预录画面（{Frames} 帧）",
                    _cameraId, _preRollSecondsInSegment, preRollCount);
            }

            Log.Information("开始录制：{Path}（{W}×{H} · {Fps}fps · JPEG q{Quality}）",
                path, _outWidth, _outHeight, _outFps, _quality);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建录像文件失败");
            _writer?.Dispose();
            _writer = null;
            _currentPath = null;
            return false;
        }
    }

    private void WriteCurrent(Mat frame)
    {
        if (_writer is null) return;

        var profile = _options.Profile;
        var jpeg = Encode(frame, profile, out var width, out var height);
        if (jpeg is null) return;

        // 分辨率变了（设备重协商）：按新尺寸另起一段，绝不拉伸到旧尺寸
        if (width != _outWidth || height != _outHeight)
        {
            Log.Information("录像分辨率变为 {W}×{H}（原 {OW}×{OH}），另起一段以保持成像比例",
                width, height, _outWidth, _outHeight);
            StopSegment();
            if (!StartSegment(frame)) return;

            jpeg = Encode(frame, profile, out _, out _);
            if (jpeg is null) return;
        }

        _writer.AddJpeg(jpeg, jpeg.Length);

        var elapsed = DateTime.Now - _segmentStart;
        if (elapsed >= TimeSpan.FromMinutes(Math.Max(1, _options.SegmentMinutes))
            || _writer.BytesWritten >= MjpegAviWriter.MaxSegmentBytes)
        {
            Log.Information("分段到点（{Minutes:F0} 分钟 / {Size:F0} MB），开始新一段",
                elapsed.TotalMinutes, _writer.BytesWritten / 1024.0 / 1024.0);
            StopSegment();
        }
    }

    private void StopSegment()
    {
        if (_writer is null) return;

        try
        {
            var frames = _writer.FrameCount;
            var bytes = _writer.BytesWritten;

            // 编码跟不上时实际帧率会低于目标值，按真实值回填，播放速度才正常
            var seconds = (DateTime.Now - _segmentStart).TotalSeconds + _preRollSecondsInSegment;
            if (frames > 1 && seconds >= 0.5)
            {
                var actualFps = (int)Math.Round(frames / seconds);
                if (Math.Abs(actualFps - _outFps) > 1)
                {
                    _writer.SetPlaybackFps(actualFps);
                    Log.Information("实际写入 {Actual}fps（目标 {Target}fps），已按真实帧率写头",
                        actualFps, _outFps);
                }
            }

            _writer.Dispose();

            Log.Information("录制分段结束：{Path}（{Frames} 帧 · {Size:F1} MB）",
                _currentPath, frames, bytes / 1024.0 / 1024.0);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "关闭录像文件失败");
        }
        finally
        {
            _writer = null;
            _currentPath = null;
            _lastWriteAt = DateTime.MinValue;
            _preRollSecondsInSegment = 0;
        }
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    /// <summary>按档位等比缩放到输出尺寸并编码成 JPEG。</summary>
    private byte[]? Encode(Mat frame, RecordingProfile profile, out int width, out int height)
    {
        var target = frame;
        (width, height) = OutputSize(frame, profile);

        if (width != frame.Width || height != frame.Height)
        {
            _scaled ??= new Mat();
            Cv2.Resize(frame, _scaled, new Size(width, height), interpolation: InterpolationFlags.Area);
            target = _scaled;
        }

        _quality = profile.JpegQuality;

        try
        {
            Cv2.ImEncode(".jpg", target, out var buffer,
                new ImageEncodingParam(ImwriteFlags.JpegQuality, Math.Clamp(_quality, 1, 100)));
            return buffer;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "JPEG 编码失败");
            return null;
        }
    }

    /// <summary>算出输出尺寸：宽高乘同一个系数（等比，绝不拉伸），宽不超过档位上限。</summary>
    private static (int Width, int Height) OutputSize(Mat frame, RecordingProfile profile)
    {
        if (profile.IsNative || frame.Width <= profile.MaxWidth)
            return (frame.Width, frame.Height);

        var scale = profile.MaxWidth / (double)frame.Width;
        return (profile.MaxWidth, Math.Max(2, (int)Math.Round(frame.Height * scale)));
    }

    /// <summary>按目标帧率节流：采集 30fps、录像 15fps 时自动隔帧写。</summary>
    private static bool Due(ref DateTime last, int fps)
    {
        var now = DateTime.Now;
        var intervalMs = 1000.0 / Math.Clamp(fps, 1, 120);

        if (last > DateTime.MinValue && (now - last).TotalMilliseconds < intervalMs) return false;

        last = now;
        return true;
    }
}
