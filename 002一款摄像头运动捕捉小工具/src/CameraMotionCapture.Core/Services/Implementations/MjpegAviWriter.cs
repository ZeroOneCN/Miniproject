using OpenCvSharp;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>
/// 自己写 MJPEG AVI 容器。
///
/// <para>为什么要自己写：OpenCV 的 <c>VideoWriter</c> 写 MJPG 时<b>不认 JPEG 质量参数</b>
/// （实测 q55/85 出来的文件一字不差），一律用接近最高质量编码，导致 1080p 每小时能吃掉好几个 G。
/// 体积又只能靠"改分辨率 / 改帧率"来压，没有别的旋钮。</para>
///
/// <para>自己写还有个白送的好处：每帧就是一段标准 JPEG，预录环形缓冲可以直接存编码后的字节，
/// 触发时原样倒进容器即可，不用再解码-重编码一遍。</para>
///
/// <para>文件结构就是最朴素的 RIFF/AVI：hdrl（avih + strl：strh/strf）+ movi（一堆 00dc）+ idx1。
/// 结尾回填各处长度，所以必须能随机读写（用 FileStream）。</para>
/// </summary>
internal sealed class MjpegAviWriter : IDisposable
{
    private const int AvifHasIndex = 0x10;
    private const int AviifKeyFrame = 0x10;

    /// <summary>AVI 的 RIFF 长度是 32 位，超过约 2GB 就会写坏，调用方必须提前切片。</summary>
    public const long MaxSegmentBytes = 1_800_000_000;

    private readonly FileStream _fs;
    private readonly BinaryWriter _bw;
    private readonly int _width;
    private readonly int _height;
    private int _fps;
    private readonly List<(int Offset, int Size)> _index = new();

    private long _riffSizePos;
    private long _moviSizePos;
    private long _moviDataStart;
    private long _avihMicroSecPos;
    private long _avihMaxBytesPos;
    private long _avihTotalFramesPos;
    private long _avihSuggestedPos;
    private long _strhRatePos;
    private long _strhLengthPos;
    private long _strhSuggestedPos;
    private int _maxChunkBytes;
    private bool _disposed;

    public MjpegAviWriter(string path, int width, int height, int fps)
    {
        _width = Math.Max(2, width);
        _height = Math.Max(2, height);
        _fps = Math.Clamp(fps, 1, 120);

        _fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read,
            1 << 16, FileOptions.None);
        _bw = new BinaryWriter(_fs);

        WriteHeader();
    }

    public int FrameCount => _index.Count;

    public long BytesWritten => _disposed ? 0 : _fs.Length;

    /// <summary>追加一帧已经编码好的 JPEG（就是 00dc 里那段字节）。</summary>
    public void AddJpeg(byte[] jpeg, int length)
    {
        if (_disposed || length <= 0) return;

        var chunkStart = _fs.Position;

        WriteFourCc("00dc");
        _bw.Write(length);
        _bw.Write(jpeg, 0, length);
        if ((length & 1) == 1) _bw.Write((byte)0);   // RIFF 要求偶数字节对齐

        _index.Add(((int)(chunkStart - _moviDataStart), length));
        if (length > _maxChunkBytes) _maxChunkBytes = length;
    }

    /// <summary>把一帧原图按给定质量编码成 JPEG 后追加。</summary>
    public void AddFrame(Mat frame, int jpegQuality)
    {
        Cv2.ImEncode(".jpg", frame, out var buffer,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, Math.Clamp(jpegQuality, 1, 100)));

        AddJpeg(buffer, buffer.Length);
    }

    /// <summary>
    /// 修正文件里声明的帧率。
    ///
    /// <para>写盘是"按目标帧率节流"的：如果编码跟不上（1080p 高画质时很常见），
    /// 实际每秒只有十几帧。这时若还按目标帧率写头，播放器会把 17 帧的内容当 30 帧放，
    /// 画面看起来像快进。收尾前按"帧数 ÷ 实际时长"回填真实帧率，播放速度才对。</para>
    /// </summary>
    public void SetPlaybackFps(int fps)
    {
        if (_disposed) return;

        _fps = Math.Clamp(fps, 1, 120);
        Patch32(_avihMicroSecPos, 1_000_000 / _fps);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Close();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "收尾视频文件失败");
        }
        finally
        {
            _bw.Dispose();
            _fs.Dispose();
        }
    }

    // ───────────────────────────── 容器结构 ─────────────────────────────

    private void Close()
    {
        var idxStart = _fs.Position;

        // idx1：每帧 16 字节，告诉播放器第 N 帧在哪
        WriteFourCc("idx1");
        _bw.Write(_index.Count * 16);
        foreach (var (offset, size) in _index)
        {
            WriteFourCc("00dc");
            _bw.Write(AviifKeyFrame);
            _bw.Write(offset);
            _bw.Write(size);
        }

        var fileEnd = _fs.Position;

        Patch32(_moviSizePos, (int)(idxStart - _moviSizePos - 4));
        Patch32(_riffSizePos, (int)(fileEnd - 8));

        Patch32(_avihTotalFramesPos, _index.Count);
        Patch32(_avihMaxBytesPos, _maxChunkBytes * _fps);
        Patch32(_avihSuggestedPos, _maxChunkBytes);
        Patch32(_strhRatePos, _fps);
        Patch32(_strhLengthPos, _index.Count);
        Patch32(_strhSuggestedPos, _maxChunkBytes);

        _bw.Flush();
        _fs.Flush();
    }

    private void WriteHeader()
    {
        WriteFourCc("RIFF");
        _riffSizePos = _fs.Position;
        _bw.Write(0);
        WriteFourCc("AVI ");

        // ── LIST hdrl ──
        WriteFourCc("LIST");
        var hdrlSizePos = _fs.Position;
        _bw.Write(0);
        var hdrlStart = _fs.Position;
        WriteFourCc("hdrl");

        // avih：主头，56 字节
        WriteFourCc("avih");
        _bw.Write(56);
        _avihMicroSecPos = _fs.Position;
        _bw.Write(1_000_000 / _fps);                  // dwMicroSecPerFrame（回填真实帧率）
        _avihMaxBytesPos = _fs.Position;
        _bw.Write(0);                                 // dwMaxBytesPerSec（回填）
        _bw.Write(0);                                 // dwPaddingGranularity
        _bw.Write(AvifHasIndex);                      // dwFlags
        _avihTotalFramesPos = _fs.Position;
        _bw.Write(0);                                 // dwTotalFrames（回填）
        _bw.Write(0);                                 // dwInitialFrames
        _bw.Write(1);                                 // dwStreams
        _avihSuggestedPos = _fs.Position;
        _bw.Write(0);                                 // dwSuggestedBufferSize（回填）
        _bw.Write(_width);
        _bw.Write(_height);
        _bw.Write(0); _bw.Write(0); _bw.Write(0); _bw.Write(0);   // dwReserved[4]

        // LIST strl
        WriteFourCc("LIST");
        var strlSizePos = _fs.Position;
        _bw.Write(0);
        var strlStart = _fs.Position;
        WriteFourCc("strl");

        // strh：流头，56 字节
        WriteFourCc("strh");
        _bw.Write(56);
        WriteFourCc("vids");
        WriteFourCc("MJPG");
        _bw.Write(0);                                 // dwFlags
        _bw.Write((short)0);                          // wPriority
        _bw.Write((short)0);                          // wLanguage
        _bw.Write(0);                                 // dwInitialFrames
        _bw.Write(1);                                 // dwScale
        _strhRatePos = _fs.Position;
        _bw.Write(_fps);                              // dwRate  → 帧率 = rate / scale（回填）
        _bw.Write(0);                                 // dwStart
        _strhLengthPos = _fs.Position;
        _bw.Write(0);                                 // dwLength（回填）
        _strhSuggestedPos = _fs.Position;
        _bw.Write(0);                                 // dwSuggestedBufferSize（回填）
        _bw.Write(-1);                                // dwQuality
        _bw.Write(0);                                 // dwSampleSize
        _bw.Write((short)0); _bw.Write((short)0);      // rcFrame.left / top
        _bw.Write((short)_width); _bw.Write((short)_height);

        // strf：BITMAPINFOHEADER，40 字节
        WriteFourCc("strf");
        _bw.Write(40);
        _bw.Write(40);                                // biSize
        _bw.Write(_width);
        _bw.Write(_height);
        _bw.Write((short)1);                          // biPlanes
        _bw.Write((short)24);                         // biBitCount
        WriteFourCc("MJPG");                          // biCompression
        _bw.Write(_width * _height * 3);              // biSizeImage
        _bw.Write(0);                                 // biXPelsPerMeter
        _bw.Write(0);                                 // biYPelsPerMeter
        _bw.Write(0);                                 // biClrUsed
        _bw.Write(0);                                 // biClrImportant

        Patch32(strlSizePos, (int)(_fs.Position - strlSizePos - 4));
        Patch32(hdrlSizePos, (int)(_fs.Position - hdrlSizePos - 4));

        // ── LIST movi ──
        WriteFourCc("LIST");
        _moviSizePos = _fs.Position;
        _bw.Write(0);
        WriteFourCc("movi");
        _moviDataStart = _fs.Position;
    }

    private void WriteFourCc(string tag)
    {
        foreach (var ch in tag) _bw.Write((byte)ch);
    }

    /// <summary>回填一个 32 位长度字段，然后回到原来的写入位置继续。</summary>
    private void Patch32(long position, int value)
    {
        var here = _fs.Position;
        _fs.Position = position;
        _bw.Write(value);
        _fs.Position = Math.Max(here, position + 4);
    }
}
