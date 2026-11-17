using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>
/// 分段视频录制。
/// 旧版本这类代码写完后从未被调用；本实现已接入采集管线，可在运动触发或全时模式下真正产出文件。
/// </summary>
public sealed class VideoRecordingService : IVideoRecordingService
{
    private readonly int _cameraId;
    private readonly object _sync = new();

    private VideoWriter? _writer;
    private string? _currentPath;
    private DateTime _segmentStart;
    private long _bytes;
    private string _fileNamePrefix = "rec";

    // 录制参数（Start 时确定，供切片时复用）
    private string _saveDir = string.Empty;
    private int _width;
    private int _height;
    private double _fps = 20;
    private string _codec = "MJPG";
    private int _quality = 85;
    private int _segmentMinutes = 10;
    private bool _useDailyFolder;

    public VideoRecordingService(int cameraId = 0) => _cameraId = cameraId;

    public bool IsRecording { get { lock (_sync) return _writer != null; } }
    public string? CurrentFilePath { get { lock (_sync) return _currentPath; } }
    public long BytesWritten { get { lock (_sync) return _bytes; } }

    /// <summary>由采集管线设置文件名前缀（区分 motion / continuous）</summary>
    public string FileNamePrefix
    {
        set => _fileNamePrefix = value;
    }

    public bool Start(string saveDir, int width, int height, double fps, string codec,
        int quality, int segmentMinutes, bool useDailyFolder)
    {
        lock (_sync)
        {
            // 参数变化时重新开一段
            StopCore();

            _saveDir = saveDir;
            _width = width;
            _height = height;
            _fps = fps <= 0 ? 20 : fps;
            _codec = string.IsNullOrWhiteSpace(codec) ? "MJPG" : codec;
            _quality = quality;
            _segmentMinutes = Math.Max(1, segmentMinutes);
            _useDailyFolder = useDailyFolder;
            _bytes = 0;

            return OpenSegmentCore();
        }
    }

    public void WriteFrame(Mat frame)
    {
        if (frame is null || frame.Empty()) return;

        lock (_sync)
        {
            if (_writer is null) return;

            try
            {
                if (ShouldRotate())
                {
                    StopCore();
                    if (!OpenSegmentCore()) return;
                }

                // 分辨率必须与编码器初始化时一致。设备重协商或网络摄像头切流时会变，
                // 此时**按新尺寸重开一段**——绝不能 Cv2.Resize 到旧尺寸，
                // 那会把画面按非等比拉伸，录下来的文件永久变形。
                if (frame.Width != _width || frame.Height != _height)
                {
                    Log.Information("录制分辨率变为 {W}×{H}（原 {OW}×{OH}），按新尺寸另起一段以保持成像比例",
                        frame.Width, frame.Height, _width, _height);

                    StopCore();
                    _width = frame.Width;
                    _height = frame.Height;
                    if (!OpenSegmentCore()) return;
                }

                _writer.Write(frame);

                if (!string.IsNullOrEmpty(_currentPath) && File.Exists(_currentPath))
                    _bytes = new FileInfo(_currentPath).Length;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "写入视频帧失败，停止当前分段");
                StopCore();
            }
        }
    }

    public void Stop()
    {
        lock (_sync) StopCore();
    }

    public void Dispose() => Stop();

    // ───────────────────────────── 内部实现 ─────────────────────────────

    private bool ShouldRotate() =>
        _segmentMinutes > 0 && DateTime.Now - _segmentStart >= TimeSpan.FromMinutes(_segmentMinutes);

    private bool OpenSegmentCore()
    {
        try
        {
            var dir = _useDailyFolder
                ? Path.Combine(_saveDir, DateTime.Now.ToString("yyyy-MM-dd"))
                : _saveDir;
            Directory.CreateDirectory(dir);

            var ext = _codec.ToUpperInvariant() is "MP4V" or "H264" or "AVC1" ? ".mp4" : ".avi";
            var name = $"{_fileNamePrefix}_cam{_cameraId}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
            var path = Path.Combine(dir, name);

            var codec = _codec;
            var writer = CreateWriter(path, codec);
            if (writer is null && codec != "MJPG")
            {
                // 回退到兼容性最好的 MJPG
                codec = "MJPG";
                path = Path.ChangeExtension(path, ".avi");
                writer = CreateWriter(path, codec);
            }

            if (writer is null)
            {
                Log.Error("无法创建视频编码器（codec={Codec}），录制未启动", _codec);
                return false;
            }

            _writer = writer;
            _currentPath = path;
            _segmentStart = DateTime.Now;

            Log.Information("开始录制分段：{Path}（{W}×{H} · {Fps:F0}fps · {Codec}）",
                path, _width, _height, _fps, codec);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建录制分段失败");
            return false;
        }
    }

    private VideoWriter? CreateWriter(string path, string codec)
    {
        VideoWriter? writer = null;
        try
        {
            var fourcc = ToFourCC(codec);
            writer = new VideoWriter(path, fourcc, _fps, new Size(_width, _height));

            if (!writer.IsOpened())
            {
                writer.Dispose();
                return null;
            }

            try { writer.Set(VideoWriterProperties.Quality, _quality); }
            catch { /* 部分编码后端不支持质量参数，忽略 */ }

            return writer;
        }
        catch
        {
            writer?.Dispose();
            return null;
        }
    }

    private void StopCore()
    {
        if (_writer is not null)
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentPath) && File.Exists(_currentPath))
                    _bytes = new FileInfo(_currentPath).Length;
                _writer.Release();
            }
            catch { }
            finally
            {
                _writer.Dispose();
                _writer = null;
                if (!string.IsNullOrEmpty(_currentPath))
                    Log.Information("录制分段结束：{Path}（{Size:F1} MB）",
                        _currentPath, _bytes / 1024.0 / 1024.0);
            }
        }
    }

    private static int ToFourCC(string codec)
    {
        var c = (codec ?? "MJPG").ToUpperInvariant().PadRight(4)[..4];
        return VideoWriter.FourCC(c[0], c[1], c[2], c[3]);
    }
}
