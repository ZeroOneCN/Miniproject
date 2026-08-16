using System.IO.Compression;
using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

public class VideoRecordingService : IVideoRecordingService
{
    private VideoWriter? _writer;
    private string? _currentVideoPath;
    private bool _isRecording;

    public bool IsRecording => _isRecording;
    public string? CurrentVideoPath => _currentVideoPath;

    public bool StartNewSegment(string saveDir, int width, int height, double fps, string codec, int quality, bool useDailyFolder)
    {
        Stop();

        var dir = useDailyFolder
            ? Path.Combine(saveDir, DateTime.Now.ToString("yyyy-MM-dd"))
            : saveDir;
        Directory.CreateDirectory(dir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var codecStr = codec == "自动" ? SelectBestCodec(dir, width, height, fps) : codec;
        var fourcc = VideoWriter.FourCC(codecStr[0], codecStr[1], codecStr[2], codecStr[3]);
        var filename = Path.Combine(dir, $"recording_{timestamp}.avi");

        _writer = new VideoWriter(filename, fourcc, fps, new Size(width, height));
        if (!_writer.IsOpened())
        {
            Log.Error("无法初始化视频写入器: {Filename}", filename);
            _writer.Dispose();
            _writer = null;
            return false;
        }

        _currentVideoPath = filename;
        _isRecording = true;
        Log.Information("开始新视频分段: {Filename}, 分辨率={W}x{H}, FPS={Fps}", filename, width, height, fps);
        return true;
    }

    public void WriteFrame(Mat frame)
    {
        if (_writer == null || !_isRecording) return;
        _writer.Write(frame);
    }

    public void Stop()
    {
        if (_writer != null)
        {
            _writer.Release();
            _writer.Dispose();
            _writer = null;
        }
        _isRecording = false;
        _currentVideoPath = null;
    }

    public void CompressVideo(string? videoPath)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath)) return;

        try
        {
            var videoFile = new FileInfo(videoPath);
            var zipPath = Path.ChangeExtension(videoPath, ".zip");
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(videoPath, videoFile.Name);
            File.Delete(videoPath);
            Log.Information("视频已压缩: {ZipPath}", zipPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "视频压缩失败: {VideoPath}", videoPath);
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private static string SelectBestCodec(string dir, int width, int height, double fps)
    {
        var candidates = new[] { "H264", "MP4V", "XVID", "MJPG" };
        foreach (var codec in candidates)
        {
            try
            {
                var fourcc = VideoWriter.FourCC(codec[0], codec[1], codec[2], codec[3]);
                var testPath = Path.Combine(dir, $"codec_test_{codec}.avi");
                var writer = new VideoWriter(testPath, fourcc, Math.Max(fps, 1), new Size(width, height));
                if (writer.IsOpened())
                {
                    writer.Release();
                    writer.Dispose();
                    File.Delete(testPath);
                    return codec;
                }
                writer.Release();
                writer.Dispose();
                File.Delete(testPath);
            }
            catch { continue; }
        }
        return "XVID";
    }
}