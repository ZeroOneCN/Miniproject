using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using Serilog;

namespace CameraMotionCapture.App.Views;

/// <summary>录像库：列出录好的片段，并在窗口内直接播放。</summary>
public partial class RecordingsWindow : System.Windows.Window
{
    private readonly string _root;
    private readonly ObservableCollection<RecordingClip> _clips = new();
    private readonly DispatcherTimer _timer;

    private VideoCapture? _capture;
    private WriteableBitmap? _bitmap;
    private RecordingClip? _current;

    private double _fps = 15;
    private int _frameCount;
    private int _frameIndex;
    private bool _playing;
    private bool _seeking;
    private DateTime _lastFrameAt = DateTime.MinValue;

    public RecordingsWindow(string root)
    {
        InitializeComponent();

        _root = root;
        ClipList.ItemsSource = _clips;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick += OnTick;
        _timer.Start();

        Loaded += (_, _) => LoadClips();
        Closed += (_, _) => Release();

        PreviewKeyDown += OnPreviewKeyDown;
    }

    // ───────────────────────────── 列表 ─────────────────────────────

    private void LoadClips()
    {
        var selected = _current?.Path;

        _clips.Clear();

        try
        {
            if (!Directory.Exists(_root))
            {
                ClipSummary.Text = "还没有录像文件";
                return;
            }

            var files = Directory.GetFiles(_root, "*.avi", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(_root, "*.mp4", SearchOption.AllDirectories))
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(500);

            foreach (var file in files) _clips.Add(RecordingClip.From(file));

            var totalMb = _clips.Sum(c => c.Bytes) / 1024.0 / 1024.0;
            ClipSummary.Text = _clips.Count == 0
                ? "还没有录像文件"
                : $"{_clips.Count} 段 · 共 {FormatSize(totalMb)}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "读取录像目录失败");
            ClipSummary.Text = "读取失败";
        }

        if (selected is not null)
        {
            var again = _clips.FirstOrDefault(c => c.Path == selected);
            if (again is not null) ClipList.SelectedItem = again;
        }
        else if (_clips.Count > 0)
        {
            ClipList.SelectedIndex = 0;
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => LoadClips();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = _current is null ? _root : Path.GetDirectoryName(_current.Path) ?? _root;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "打开录像目录失败");
        }
    }

    // ───────────────────────────── 打开与播放 ─────────────────────────────

    private void OnClipSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ClipList.SelectedItem is not RecordingClip clip) return;

        Open(clip);
    }

    private void Open(RecordingClip clip)
    {
        Release();

        _current = clip;
        PlaybackError.Visibility = Visibility.Collapsed;

        try
        {
            _capture = new VideoCapture(clip.Path);
            if (!_capture.IsOpened())
            {
                ShowError("这段视频打不开，可能文件损坏或编码不受支持。");
                return;
            }

            _fps = _capture.Fps > 0 && _capture.Fps < 121 ? _capture.Fps : 15;
            _frameCount = (int)Math.Max(0, _capture.FrameCount);
            _frameIndex = 0;

            // 有些文件头没写总帧数，用"时长 × 帧率"兜一下
            if (_frameCount <= 0)
                _frameCount = (int)Math.Max(0, _capture.Get(VideoCaptureProperties.FrameCount));

            Progress.Maximum = Math.Max(1, _frameCount);
            Progress.Value = 0;

            EmptyHint.Visibility = Visibility.Collapsed;
            PlayButton.Content = "▶ 播放";
            _playing = false;

            ReadFrame();

            UpdateTimeText();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "打开录像失败：{Path}", clip.Path);
            ShowError("打开失败：" + ex.Message);
        }
    }

    private void ShowError(string message)
    {
        PlaybackError.Text = message;
        PlaybackError.Visibility = Visibility.Visible;
    }

    private void OnPlayClick(object sender, RoutedEventArgs e) => TogglePlay();

    private void TogglePlay()
    {
        if (_capture is null) return;

        _playing = !_playing;
        PlayButton.Content = _playing ? "⏸ 暂停" : "▶ 播放";

        if (_playing)
        {
            // 播到结尾再点播放：从头开始
            if (_frameIndex >= _frameCount - 1)
            {
                _frameIndex = 0;
                _capture.Set(VideoCaptureProperties.PosFrames, 0);
            }

            _lastFrameAt = DateTime.MinValue;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_playing || _capture is null) return;

        var interval = 1000.0 / Math.Clamp(_fps, 1, 60);
        if (_lastFrameAt > DateTime.MinValue && (DateTime.Now - _lastFrameAt).TotalMilliseconds < interval)
            return;

        _lastFrameAt = DateTime.Now;

        if (!ReadFrame())
        {
            _playing = false;
            PlayButton.Content = "▶ 播放";
            return;
        }

        Progress.Value = _frameIndex;
        UpdateTimeText();
    }

    /// <summary>读下一帧并刷到画面上；读到结尾返回 false。</summary>
    private bool ReadFrame()
    {
        if (_capture is null) return false;

        using var frame = new Mat();
        if (!_capture.Read(frame) || frame.Empty()) return false;

        _frameIndex++;

        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgr24, null);
            PlayerImage.Source = _bitmap;
        }

        _bitmap.Lock();
        try
        {
            unsafe
            {
                // OpenCV 的行长度可能带填充，逐行拷贝更稳
                var source = (byte*)frame.Data;
                var dest = (byte*)_bitmap.BackBuffer;
                var sourceStride = (int)frame.Step();
                var destStride = _bitmap.BackBufferStride;
                var rows = frame.Height;      // 缓存：Mat.Height 每次访问都是一次 P/Invoke

                for (var y = 0; y < rows; y++)
                    Buffer.MemoryCopy(source + y * sourceStride, dest + y * destStride, destStride, Math.Min(sourceStride, destStride));
            }

            _bitmap.AddDirtyRect(new Int32Rect(0, 0, frame.Width, frame.Height));
        }
        finally
        {
            _bitmap.Unlock();
        }

        return true;
    }

    private void UpdateTimeText()
    {
        var total = _frameCount / Math.Max(1, _fps);
        var current = _frameIndex / Math.Max(1, _fps);
        TimeText.Text = $"{FormatTime(current)} / {FormatTime(total)}";
    }

    private static string FormatTime(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    // ───────────────────────────── 进度条 ─────────────────────────────

    private void OnSeekStart(object sender, DragStartedEventArgs e) => _seeking = true;

    private void OnSeekEnd(object sender, DragCompletedEventArgs e)
    {
        _seeking = false;
        SeekTo((int)Math.Clamp(Progress.Value, 0, Math.Max(0, _frameCount - 1)));
    }

    private void OnSeek(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_seeking || _capture is null) return;

        // 拖拽过程中实时跟随，但不要每像素都解码一次真实画面
        if ((int)e.NewValue % 5 != 0) return;
        SeekTo((int)e.NewValue);
    }

    private void SeekTo(int frame)
    {
        if (_capture is null) return;

        frame = Math.Clamp(frame, 0, Math.Max(0, _frameCount - 1));

        try
        {
            _capture.Set(VideoCaptureProperties.PosFrames, frame);
            _frameIndex = frame;
            ReadFrame();
            _frameIndex = frame;
            UpdateTimeText();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "定位失败");
        }
    }

    // ───────────────────────────── 上一段 / 下一段 ─────────────────────────────

    private void OnPrevClick(object sender, RoutedEventArgs e) => Step(-1);

    private void OnNextClick(object sender, RoutedEventArgs e) => Step(1);

    private void Step(int delta)
    {
        if (_clips.Count == 0) return;

        var index = _current is null ? 0 : _clips.IndexOf(_current);
        var next = Math.Clamp(index + delta, 0, _clips.Count - 1);
        if (next == index && _current is not null) return;

        ClipList.SelectedIndex = next;
        ClipList.ScrollIntoView(_clips[next]);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                TogglePlay();
                e.Handled = true;
                break;

            case Key.Left:
                SeekTo(_frameIndex - (int)Math.Round(_fps * 5));
                Progress.Value = _frameIndex;
                e.Handled = true;
                break;

            case Key.Right:
                SeekTo(_frameIndex + (int)Math.Round(_fps * 5));
                Progress.Value = _frameIndex;
                e.Handled = true;
                break;

            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void Release()
    {
        _playing = false;
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
        _bitmap = null;
    }

    private static string FormatSize(double megabytes) => megabytes >= 1024
        ? $"{megabytes / 1024:F2} GB"
        : $"{megabytes:F0} MB";
}

/// <summary>列表里的一行：一个录像文件</summary>
public sealed class RecordingClip
{
    public string Path { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string SizeText { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public long Bytes { get; init; }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    public static RecordingClip From(FileInfo file)
    {
        // 文件名形如 rec_cam0_20261118_143355.avi / motion_cam1_...
        var parts = System.IO.Path.GetFileNameWithoutExtension(file.Name).Split('_');
        var channel = parts.Length > 1 ? parts[1] : "cam?";
        var kind = parts.Length > 0 ? parts[0] : "rec";

        var title = kind switch
        {
            "motion" => $"运动事件 · {channel}",
            "rec" => $"连续录制 · {channel}",
            _ => $"{kind} · {channel}"
        };

        var mb = file.Length / 1024.0 / 1024.0;

        return new RecordingClip
        {
            Path = file.FullName,
            Title = title,
            Bytes = file.Length,
            SizeText = mb >= 1024 ? $"{mb / 1024:F2} GB" : $"{mb:F1} MB",
            TimeText = file.LastWriteTime.ToString("MM-dd HH:mm:ss")
        };
    }
}
