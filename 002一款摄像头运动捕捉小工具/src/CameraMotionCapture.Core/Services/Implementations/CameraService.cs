using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>
/// 摄像头设备访问实现。
///
/// <para>USB 摄像头的"设置了 30fps 却只跑 10fps"几乎总是同一个原因：没协商到 MJPG。
/// 1080p 未压缩（YUY2）在 USB 2.0 上带宽只够 5~10fps，驱动会默默给你一个更低的帧率，
/// 而 <c>cap.Set(Fps)</c> 不会报错、也读不出"被拒绝"。所以这里按
/// 「MJPG + 分辨率 + 帧率」→「MJPG + 分辨率」→「只设分辨率」→「原样打开」的顺序逐个真读帧验证，
/// 再用驱动回报的 FourCC / 帧率把实际协商结果写进日志。</para>
/// </summary>
public sealed class CameraService : ICameraService
{
    private VideoCapture? _capture;
    private int _cameraId;
    private string? _url;
    private string _backendName = "—";
    private double _negotiatedFps;

    /// <summary>打开时实测帧率的采样时长：太短会被首帧抖动带偏，太长会拖慢启动</summary>

    private readonly object _sync = new();

    public bool IsOpened
    {
        get { lock (_sync) return _capture?.IsOpened() ?? false; }
    }

    public int FrameWidth => GetInt(VideoCaptureProperties.FrameWidth);
    public int FrameHeight => GetInt(VideoCaptureProperties.FrameHeight);
    /// <summary>打开时实测的帧率（不是驱动自报的那个数）</summary>
    public double NegotiatedFps { get { lock (_sync) return _negotiatedFps; } }
    public string BackendName { get { lock (_sync) return _backendName; } }

    public string FourCc
    {
        get
        {
            var code = GetInt(VideoCaptureProperties.FourCC);
            if (code <= 0) return "—";

            var chars = new[]
            {
                (char)(code & 0xFF),
                (char)((code >> 8) & 0xFF),
                (char)((code >> 16) & 0xFF),
                (char)((code >> 24) & 0xFF)
            };
            var text = new string(chars).Trim('\0', ' ');
            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }
    }

    public bool Open(int cameraId, string? backend = null, int width = 1280, int height = 720,
        int fps = 20, bool hardwareAccel = false)
    {
        Close();
        lock (_sync)
        {
            _cameraId = cameraId;
            _url = null;
            _backendName = "—";

            var backends = ResolveBackends(backend);

            for (var i = 0; i < backends.Length && _capture is null; i++)
            {
                // 首选后端把"降级组合"全试一遍；其余后端只试最可能成功的两档，
                // 否则一次失败要为同一台设备反复重开，启动时间反而变长
                var profiles = i == 0 ? OpenProfile.All : OpenProfile.Fallback;

                foreach (var profile in profiles)
                {
                    var cap = TryOpenIndex(cameraId, backends[i], profile, width, height, fps, hardwareAccel);
                    if (cap is null) continue;

                    _capture = cap;
                    _backendName = backends[i].ToString();
                    break;
                }
            }

            if (_capture is null)
            {
                Log.Error("无法打开摄像头 #{Id}（已尝试全部采集后端与格式组合）", cameraId);
                return false;
            }

            _negotiatedFps = _capture.Get(VideoCaptureProperties.Fps);
        }

        Log.Information("摄像头 #{Id} 已打开：{W}×{H}，驱动报 {Fps:F0}fps（编码 {FourCc}，后端 {Backend}）",
            cameraId, FrameWidth, FrameHeight, NegotiatedFps, FourCc, BackendName);
        return true;
    }

    public bool Open(string url, int width = 1280, int height = 720, int fps = 20)
    {
        Close();
        lock (_sync)
        {
            _cameraId = -1;
            _url = url;
            _backendName = "URL";

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

                if (!CanRead(cap, timeoutMs: 6000))
                {
                    cap.Dispose();
                    Log.Error("网络摄像头已连接但读不到画面：{Url}", UrlSanitizer.Mask(url));
                    return false;
                }

                _capture = cap;
                _negotiatedFps = fps;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "打开网络摄像头异常：{Url}", UrlSanitizer.Mask(url));
                return false;
            }
        }

        Log.Information("网络摄像头已连接：{Url}（{W}×{H}，编码 {FourCc}）",
            UrlSanitizer.Mask(url), FrameWidth, FrameHeight, FourCc);
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
            finally
            {
                _capture = null;
                _backendName = "—";
            }
        }
    }

    public void Dispose() => Close();

    // ───────────────────────────── 内部实现 ─────────────────────────────

    private int GetInt(VideoCaptureProperties prop) => (int)GetDouble(prop);

    private double GetDouble(VideoCaptureProperties prop)
    {
        lock (_sync)
        {
            return _capture?.IsOpened() == true ? _capture.Get(prop) : 0;
        }
    }

    /// <summary>一次打开尝试要设置哪些属性 —— 降级顺序由 <see cref="OpenProfile.All"/> 决定。</summary>
    private readonly record struct OpenProfile(bool UseMjpg, bool UseSize, bool UseFps)
    {
        /// <summary>首选后端用：尽量拿到 MJPG + 目标分辨率 + 目标帧率</summary>
        public static readonly OpenProfile[] All =
        {
            new(UseMjpg: true,  UseSize: true,  UseFps: true),
            new(UseMjpg: true,  UseSize: true,  UseFps: false),
            new(UseMjpg: false, UseSize: true,  UseFps: true),
            new(UseMjpg: false, UseSize: false, UseFps: false)
        };

        /// <summary>备用后端用：只试"最理想"和"原样"，避免拖长启动</summary>
        public static readonly OpenProfile[] Fallback =
        {
            All[0],
            All[3]
        };
    }

    private static VideoCapture? TryOpenIndex(int cameraId, VideoCaptureAPIs api, OpenProfile profile,
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

            if (profile.UseSize)
            {
                cap.Set(VideoCaptureProperties.FrameWidth, width);
                cap.Set(VideoCaptureProperties.FrameHeight, height);
            }

            if (profile.UseFps)
            {
                cap.Set(VideoCaptureProperties.Fps, fps);

                // 部分 DSHOW 驱动第一次 Set(Fps) 会被忽略，再设一次才生效
                if (cap.Get(VideoCaptureProperties.Fps) <= 1)
                    cap.Set(VideoCaptureProperties.Fps, fps);
            }

            if (profile.UseMjpg)
            {
                // MJPG 必须**最后**设，这一条是被实测逼出来的（本机 USB 摄像头）：
                //   先 MJPG 再尺寸 → 驱动退回 YUY2，1080p 只剩 5fps；
                //   尺寸 → 帧率 → MJPG → 稳定拿到 MJPG，同分辨率下翻三倍以上。
                cap.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
            }

            if (hardwareAccel)
                cap.Set(VideoCaptureProperties.HwAcceleration, 1);

            // MSMF 每次重新协商格式后首帧要 1.5~4 秒，等太短会把能用的组合误判成失败
            var timeoutMs = api == VideoCaptureAPIs.MSMF ? 6000 : 1600;
            if (CanRead(cap, timeoutMs)) return cap;

            cap.Dispose();
            return null;
        }
        catch
        {
            cap?.Dispose();
            return null;
        }
    }

    /// <summary>在超时时间内反复尝试读一帧，成功即认为设备可用。</summary>
    private static bool CanRead(VideoCapture cap, int timeoutMs)
    {
        using var frame = new Mat();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                if (cap.Read(frame) && !frame.Empty()) return true;
            }
            catch { return false; }

            Thread.Sleep(60);
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
