using OpenCvSharp;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>
/// Haar 级联人脸检测 —— 只做"检测 + 画框"，不做身份识别。
/// （旧版本命名为 Recognition 并保留了置信度、已知人脸库等从未生效的字段，已一并清理。）
/// </summary>
public sealed class FaceDetectionService : IFaceDetectionService
{
    private CascadeClassifier? _cascade;
    private bool _initialized;

    public bool IsReady => _initialized && _cascade is not null;

    public bool Initialize(string? cascadePath = null)
    {
        try
        {
            var path = ResolveCascadePath(cascadePath);

            if (!File.Exists(path))
            {
                Log.Warning("人脸级联文件不存在：{Path}，人脸检测已跳过", path);
                return false;
            }

            var cascade = new CascadeClassifier(path);
            if (cascade.Empty())
            {
                cascade.Dispose();
                Log.Warning("人脸级联文件加载失败（内容无效）：{Path}", path);
                return false;
            }

            _cascade = cascade;
            _initialized = true;
            Log.Information("人脸检测已就绪：{Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "人脸检测初始化失败");
            return false;
        }
    }

    public int DetectAndDraw(Mat frame)
    {
        if (!IsReady || frame is null || frame.Empty()) return 0;

        try
        {
            using var gray = new Mat();
            if (frame.Channels() == 3)
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            else
                frame.CopyTo(gray);

            var faces = _cascade!.DetectMultiScale(
                gray, scaleFactor: 1.15, minNeighbors: 4,
                flags: HaarDetectionTypes.ScaleImage,
                minSize: new Size(36, 36));

            foreach (var face in faces)
            {
                Cv2.Rectangle(frame, face, Scalar.LimeGreen, 2);
                Cv2.PutText(frame, "Face", new Point(face.X, Math.Max(12, face.Y - 6)),
                    HersheyFonts.HersheySimplex, 0.45, Scalar.LimeGreen, 1);
            }

            return faces.Length;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "人脸检测异常");
            return 0;
        }
    }

    public void Dispose()
    {
        _cascade?.Dispose();
        _cascade = null;
        _initialized = false;
    }

    // ───────────────────────────── 内部实现 ─────────────────────────────

    private static string ResolveCascadePath(string? cascadePath)
    {
        var name = string.IsNullOrWhiteSpace(cascadePath)
            ? Constants.DefaultCascadeFileName
            : cascadePath.Trim();

        if (Path.IsPathRooted(name)) return name;

        // 相对路径统一相对程序目录解析，避免受"当前工作目录"影响；
        // 放在 exe 旁边可以覆盖内嵌模型（想换模型直接扔文件即可）
        var beside = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(beside)) return beside;

        return TryExtractEmbeddedCascade(name) ?? beside;
    }

    /// <summary>
    /// 模型以嵌入资源的形式编译进程序集，首次使用时释放到用户目录。
    /// 这样单文件发布出来的 exe 也能直接用人脸检测，不必额外附带 xml。
    /// </summary>
    private static string? TryExtractEmbeddedCascade(string name)
    {
        try
        {
            var target = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Constants.AppName, "models", name);

            if (File.Exists(target) && new FileInfo(target).Length > 0) return target;

            using var stream = typeof(FaceDetectionService).Assembly.GetManifestResourceStream(name);
            if (stream is null) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var tmp = $"{target}.{Guid.NewGuid():N}.tmp";
            using (var file = File.Create(tmp))
            {
                stream.CopyTo(file);
            }
            File.Move(tmp, target, overwrite: true);

            Log.Information("已释放内嵌人脸模型：{Path}", target);
            return target;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "释放内嵌人脸模型失败");
            return null;
        }
    }
}
