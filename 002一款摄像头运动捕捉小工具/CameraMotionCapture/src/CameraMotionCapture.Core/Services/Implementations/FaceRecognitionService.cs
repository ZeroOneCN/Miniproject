using System.Reflection;
using System.Net.Http;
using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>
/// 人脸检测服务 — 使用 Haar 级联分类器检测人脸并绘制框
/// 级联文件优先加载顺序：本地文件 → 嵌入式资源 → 网络下载
/// </summary>
public class FaceRecognitionService : IFaceRecognitionService
{
    private CascadeClassifier? _cascade;
    private bool _initialized;

    public bool IsReady => _initialized && _cascade != null;
    public bool IsEnabled { get; set; } = false;
    public double ConfidenceThreshold { get; set; } = 80.0;
    public string CascadeFilePath { get; set; } = "haarcascade_frontalface_default.xml";
    public string KnownFacesDir { get; set; } = "known_faces";

    public bool Initialize()
    {
        try
        {
            // 第1步：尝试从本地文件加载
            if (!File.Exists(CascadeFilePath))
            {
                // 第2步：尝试从嵌入式资源提取
                ExtractCascadeResource();
            }

            // 第3步：如果还没有，尝试网络下载
            if (!File.Exists(CascadeFilePath))
                DownloadCascadeFile();

            if (File.Exists(CascadeFilePath))
            {
                _cascade = new CascadeClassifier(CascadeFilePath);
                Log.Information("人脸检测级联分类器加载成功: {Path}", CascadeFilePath);
                _initialized = true;
                return true;
            }

            Log.Warning("人脸检测级联文件不存在: {Path}", CascadeFilePath);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "人脸识别初始化失败");
            return false;
        }
    }

    public void LoadKnownFaces()
    {
        if (!Directory.Exists(KnownFacesDir))
            Directory.CreateDirectory(KnownFacesDir);
    }

    public Rect[] DetectFaces(Mat frame)
    {
        if (!IsReady) return Array.Empty<Rect>();

        try
        {
            using var gray = new Mat();
            if (frame.Channels() == 3)
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            else
                frame.CopyTo(gray);

            var faces = _cascade!.DetectMultiScale(
                gray, 1.1, 3, HaarDetectionTypes.ScaleImage, new Size(30, 30));

            return faces;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "人脸检测异常");
            return Array.Empty<Rect>();
        }
    }

    public int DrawFaces(Mat frame)
    {
        if (!IsReady || !IsEnabled) return 0;

        var faces = DetectFaces(frame);
        if (faces.Length == 0) return 0;

        foreach (var face in faces)
        {
            Cv2.Rectangle(frame, face, Scalar.LimeGreen, 2);
            Cv2.PutText(frame, "Face",
                new Point(face.X, face.Y - 5),
                HersheyFonts.HersheySimplex, 0.5, Scalar.LimeGreen, 1);
        }

        return faces.Length;
    }

    /// <summary>从嵌入式资源提取级联文件</summary>
    private void ExtractCascadeResource()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            // 尝试多个可能的资源名称
            string[] resourceNames = {
                "CameraMotionCapture.Core.Resources.haarcascade_frontalface_default.xml",
                "CameraMotionCapture.Core.Resources.haarcascade_frontalface_default.xml.gz",
                "CameraMotionCapture.Core.haarcascade_frontalface_default.xml"
            };

            foreach (var name in resourceNames)
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream != null)
                {
                    if (name.EndsWith(".gz"))
                    {
                        using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
                        using var fs = File.Create(CascadeFilePath);
                        gzip.CopyTo(fs);
                    }
                    else
                    {
                        using var fs = File.Create(CascadeFilePath);
                        stream.CopyTo(fs);
                    }
                    Log.Information("从嵌入式资源提取级联文件成功: {Name}", name);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "嵌入式资源提取失败");
        }
    }

    private void DownloadCascadeFile()
    {
        try
        {
            var url = "https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/haarcascade_frontalface_default.xml";
            Log.Information("正在下载人脸检测级联文件...");
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var data = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
            File.WriteAllBytes(CascadeFilePath, data);
            Log.Information("级联文件下载成功: {Path}", CascadeFilePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "级联文件下载失败，请手动下载放置到: {Path}", CascadeFilePath);
        }
    }

    public void Dispose()
    {
        _cascade?.Dispose();
    }
}