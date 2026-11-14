using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>
/// 摄像头设备访问实现。
/// 设计要点：
/// 1. 打开设备时优先尝试设置 MJPG，减少 USB 带宽占用；
/// 2. 每次尝试都会真实读帧验证，避免"打开了但永远读不到画面"；
/// 3. 日志中的地址一律脱敏。
/// </summary>
public sealed class CameraService : ICameraService
{
    private VideoCapture? _capture;
    private int _cameraId;
    private string? _url;
    private readonly object _sync = new();

    public bool IsOpened
    {
        get { lock (_sync) return _capture?.IsOpened() ?? false; }
    }

    public int FrameWidth => GetProp(VideoCaptureProperties.FrameWidth);
    public int FrameHeight => GetProp(VideoCaptureProperties.FrameHeight);

    public bool Open(int cameraId, string? backend = null, int width = 1280, int height = 720,
        int fps = 20, bool hardwareAccel = false)
    {
        Close();
        lock (_sync)
        {
            _cameraId = cameraId;
            _url = null;

            var backends = ResolveBackends(backend);

            for (var i = 0; i < backends.Length; i++)
            {
                // 首选后端尝试"带参数"（MJPG + 目标分辨率），失败再原样打开；
                // 其余后端直接原样打开，避免为同一设备反复重开拖慢启动
                if (i == 0)
                {
                    var withSettings = TryOpenIndex(cameraId, backends[i], withSettings: true,
                        width, height, fps, hardwareAccel);
                    if (withSettings != null) { _capture = withSettings; break; }
                }

                var plain = TryOpenIndex(cameraId, backends[i], withSettings: false,
                    width, height, fps, hardwareAccel);
                if (plain != null) { _capture = plain; break; }
            }

            if (_capture == null)
            {
                Log.Error("无法打开摄像头 #{Id}（已尝试全部采集后端）", cameraId);
                return false;
            }
        }

        Log.Information("摄像头 #{Id} 已打开，实际分辨率 {W}×{H}，帧率 {Fps}",
            cameraId, FrameWidth, FrameHeight, fps);
        return true;
    }

    public bool Open(string url, int width = 1280, int height = 720, int fps = 20)
    {
        Close();
        lock (_sync)
        {
            _cameraId = -1;
            _url = url;

            try
            {
                var cap = new VideoCapture(url);
                if (!cap.IsOpened())
                {
                    cap.Dispose();
                    Log.Error("无法打开网络摄像头 {Url}", UrlSanitizer.Mask(url));
                    return false;
                }

                cap.Set(VideoCaptureProperties.FrameWidth, width);
                cap.Set(VideoCaptureProperties.FrameHeight, height);
                cap.Set(VideoCaptureProperties.Fps, fps);

                if (!CanRead(cap, attempts: 5, delayMs: 100))
                {
                    cap.Dispose();
                    Log.Error("网络摄像头已连接但读不到画面：{Url}", UrlSanitizer.Mask(url));
                    return false;
                }

                _capture = cap;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "打开网络摄像头异常：{Url}", UrlSanitizer.Mask(url));
                return false;
            }
        }

        Log.Information("网络摄像头已连接：{Url}（{W}×{H}）",
            UrlSanitizer.Mask(url), FrameWidth, FrameHeight);
        return true;
    }

    public Mat? ReadFrame()
    {
        lock (_sync)
        {
            if (_capture is null || !_capture.IsOpened()) return null;

            var frame = new Mat();
            try
            {
                if (_capture.Read(frame) && !frame.Empty())
                    return frame;   // 所有权移交给调用方

                frame.Dispose();
                return null;
            }
            catch
            {
                frame.Dispose();
                return null;
            }
        }
    }

    public void Close()
    {
        lock (_sync)
        {
            if (_capture is null) return;
            try
            {
                if (_capture.IsOpened()) _capture.Release();
                _capture.Dispose();
            }
            catch { /* 关闭阶段的异常无需上报 */ }
            finally { _capture = null; }
        }
    }

    public void Dispose() => Close();

    // ───────────────────────────── 内部实现 ─────────────────────────────

    private int GetProp(VideoCaptureProperties prop)
    {
        lock (_sync)
        {
            return _capture?.IsOpened() == true ? (int)_capture.Get(prop) : 0;
        }
    }

    private static VideoCapture? TryOpenIndex(int cameraId, VideoCaptureAPIs api, bool withSettings,
        int width, int height, int fps, bool hardwareAccel)
    {
        VideoCapture? cap = null;
        try
        {
            cap = new VideoCapture(cameraId, api);
            if (!cap.IsOpened())
            {
                cap.Dispose();
                return null;
            }

            if (withSettings)
            {
                cap.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
                cap.Set(VideoCaptureProperties.FrameWidth, width);
                cap.Set(VideoCaptureProperties.FrameHeight, height);
                cap.Set(VideoCaptureProperties.Fps, fps);
                if (hardwareAccel)
                    cap.Set(VideoCaptureProperties.HwAcceleration, 1);
            }

            if (CanRead(cap, attempts: 2, delayMs: 40)) return cap;

            cap.Dispose();
            return null;
        }
        catch
        {
            cap?.Dispose();
            return null;
        }
    }

    private static bool CanRead(VideoCapture cap, int attempts, int delayMs)
    {
        using var frame = new Mat();
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                if (cap.Read(frame) && !frame.Empty()) return true;
            }
            catch { return false; }

            if (i < attempts - 1) Thread.Sleep(delayMs);
        }
        return false;
    }

    private static VideoCaptureAPIs[] ResolveBackends(string? preferred)
    {
        // DirectShow 在前：同一台设备 DSHOW 打开约 0.2~0.7s，MSMF 要 2~4s
        //（MSMF 每次 Set 都要重新协商流格式，实测单次 1.5~3.7s）。
        // 索引空间由 CameraDiscovery 按 DSHOW 顺序给出，因此这里必须优先 DSHOW，
        // 否则会开到别的设备上。
        var all = new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF, VideoCaptureAPIs.ANY };

        var mapped = preferred?.ToUpperInvariant() switch
        {
            "DSHOW" => VideoCaptureAPIs.DSHOW,
            "MSMF" => VideoCaptureAPIs.MSMF,
            "ANY" => VideoCaptureAPIs.ANY,
            _ => (VideoCaptureAPIs?)null
        };

        if (mapped is null) return all;
        return new[] { mapped.Value }.Concat(all.Where(a => a != mapped.Value)).ToArray();
    }
}
