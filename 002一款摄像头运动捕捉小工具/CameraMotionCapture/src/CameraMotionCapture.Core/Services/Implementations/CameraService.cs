using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

public class CameraService : ICameraService
{
    private VideoCapture? _cap;
    private int _cameraId;
    private string? _preferredBackend;
    private int _targetWidth;
    private int _targetHeight;
    private int _targetFps;
    private bool _hardwareAccel;
    private int _captureFailures;
    private const int MaxCaptureFailures = 3;

    public bool IsOpened => _cap?.IsOpened() ?? false;
    public int ActualWidth => IsOpened ? (int)_cap!.Get(VideoCaptureProperties.FrameWidth) : 0;
    public int ActualHeight => IsOpened ? (int)_cap!.Get(VideoCaptureProperties.FrameHeight) : 0;
    public int ActualFps => IsOpened ? (int)_cap!.Get(VideoCaptureProperties.Fps) : 0;

    public List<int> GetAvailableCameras()
    {
        var available = new List<int>();
        for (int i = 0; i < 10; i++)
        {
            var cap = TryOpenCamera(i, false);
            if (cap != null)
            {
                available.Add(i);
                cap.Release();
                cap.Dispose();
            }
        }
        Log.Information("检测到可用摄像头: {Cameras}", available);
        return available;
    }

    public bool OpenCamera(int cameraId, string? preferredBackend = null, int width = 1280, int height = 720, int fps = 20, bool hardwareAccel = false)
    {
        Close();
        _cameraId = cameraId;
        _preferredBackend = preferredBackend;
        _targetWidth = width;
        _targetHeight = height;
        _targetFps = fps;
        _hardwareAccel = hardwareAccel;
        _captureFailures = 0;

        _cap = TryOpenCamera(cameraId, true, preferredBackend, width, height, fps, hardwareAccel);
        if (_cap == null)
        {
            Log.Error("无法打开摄像头 {CameraId}", cameraId);
            return false;
        }

        Log.Information("摄像头已打开: ID={CameraId}, 分辨率={W}x{H}, FPS={Fps}",
            cameraId, ActualWidth, ActualHeight, ActualFps);
        return true;
    }

    public bool OpenCamera(string url, int? cameraId = null, int width = 1280, int height = 720, int fps = 20)
    {
        Close();
        _cameraId = cameraId ?? -1;
        _targetWidth = width;
        _targetHeight = height;
        _targetFps = fps;
        _captureFailures = 0;

        try
        {
            _cap = new VideoCapture(url);
            if (!_cap.IsOpened())
            {
                _cap.Release();
                _cap.Dispose();
                _cap = null;
                Log.Error("无法打开网络摄像头: {Url}", url);
                return false;
            }

            _cap.Set(VideoCaptureProperties.FrameWidth, width);
            _cap.Set(VideoCaptureProperties.FrameHeight, height);
            _cap.Set(VideoCaptureProperties.Fps, fps);

            // 尝试读取一帧验证
            using var testFrame = new Mat();
            if (_cap.Read(testFrame) && !testFrame.Empty())
            {
                Log.Information("网络摄像头已打开: URL={Url}, 分辨率={W}x{H}",
                    url, ActualWidth, ActualHeight);
                return true;
            }

            Log.Warning("网络摄像头无法读取帧: {Url}", url);
            _cap.Release();
            _cap.Dispose();
            _cap = null;
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "打开网络摄像头异常: {Url}", url);
            return false;
        }
    }

    public Mat? ReadFrame()
    {
        if (!IsOpened) return null;

        using var frame = new Mat();
        bool ret = _cap!.Read(frame);

        if (!ret || frame.Empty())
        {
            _captureFailures++;
            Log.Warning("读取摄像头帧失败 ({Count}/{Max})", _captureFailures, MaxCaptureFailures);
            return null;
        }

        _captureFailures = 0;
        return frame.Clone();
    }

    public void Close()
    {
        if (_cap != null)
        {
            if (_cap.IsOpened())
                _cap.Release();
            _cap.Dispose();
            _cap = null;
            Log.Information("摄像头已关闭");
        }
    }

    public bool RetryOpen()
    {
        if (_captureFailures < MaxCaptureFailures) return false;
        Log.Information("尝试重新打开摄像头...");
        return OpenCamera(_cameraId, _preferredBackend, _targetWidth, _targetHeight, _targetFps, _hardwareAccel);
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    private static VideoCapture? TryOpenCamera(int cameraId, bool setSettings, string? preferredBackend = null,
        int width = 1280, int height = 720, int fps = 20, bool hardwareAccel = false)
    {
        var backends = GetBackendList(preferredBackend);

        foreach (var backend in backends)
        {
            var cap = new VideoCapture(cameraId, backend);
            if (!cap.IsOpened())
            {
                cap.Release();
                cap.Dispose();
                continue;
            }

            if (TryReadFrame(cap))
                return cap;

            cap.Release();
            cap.Dispose();

            // 第二次尝试：设置MJPG格式
            cap = new VideoCapture(cameraId, backend);
            if (!cap.IsOpened())
            {
                cap.Release();
                cap.Dispose();
                continue;
            }

            cap.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
            if (setSettings)
            {
                cap.Set(VideoCaptureProperties.FrameWidth, width);
                cap.Set(VideoCaptureProperties.FrameHeight, height);
                cap.Set(VideoCaptureProperties.Fps, fps);
                if (hardwareAccel)
                    cap.Set(VideoCaptureProperties.HwAcceleration, 1);
            }

            if (TryReadFrame(cap))
                return cap;

            cap.Release();
            cap.Dispose();
        }

        return null;
    }

    private static bool TryReadFrame(VideoCapture cap, int attempts = 5, int delayMs = 50)
    {
        using var frame = new Mat();
        for (int i = 0; i < attempts; i++)
        {
            if (cap.Read(frame) && !frame.Empty())
                return true;
            System.Threading.Thread.Sleep(delayMs);
        }
        return false;
    }

    private static List<VideoCaptureAPIs> GetBackendList(string? preferredBackend)
    {
        var list = new List<VideoCaptureAPIs>();
        var preferredMap = new Dictionary<string, VideoCaptureAPIs>(StringComparer.OrdinalIgnoreCase)
        {
            ["DSHOW"] = VideoCaptureAPIs.DSHOW,
            ["MSMF"] = VideoCaptureAPIs.MSMF,
            ["ANY"] = VideoCaptureAPIs.ANY
        };

        if (preferredBackend != null && preferredMap.TryGetValue(preferredBackend, out var preferred))
        {
            if (!list.Contains(preferred))
                list.Add(preferred);
        }

        foreach (var api in new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF, VideoCaptureAPIs.ANY })
        {
            if (!list.Contains(api))
                list.Add(api);
        }

        return list;
    }
}