using System.Windows;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.App.Views;

/// <summary>
/// 设置窗口。只暴露<b>真正生效</b>的选项 —— 旧版本里那些"存了但没人读"的开关已全部移除。
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly AppConfig _source;

    /// <summary>与 XAML 里帧率下拉项一一对应</summary>
    private static readonly int[] FpsOptions = { 60, 30, 25, 20, 15, 10 };

    public SettingsDialog(AppConfig config)
    {
        InitializeComponent();
        _source = config;
        LoadIntoUi(config);
    }

    /// <summary>用户点击保存后的完整配置；取消时为 null</summary>
    public AppConfig? Result { get; private set; }

    // ───────────────────────────── 载入 ─────────────────────────────

    private void LoadIntoUi(AppConfig c)
    {
        BackendCombo.SelectedIndex = (int)c.Camera.Backend;
        ResolutionCombo.SelectedIndex = c.Camera.Width switch
        {
            1280 => 1,
            640 => 2,
            320 => 3,
            _ => 0            // 默认 1920×1080
        };
        FpsCombo.SelectedIndex = NearestFpsIndex(c.Camera.Fps);
        HardwareAccelCheck.IsChecked = c.Camera.HardwareAccel;

        RecordEnabledCheck.IsChecked = c.Recording.Enabled;
        RecordModeCombo.SelectedIndex = (int)c.Recording.Mode;
        SaveDirInput.Text = c.Recording.SaveDir;
        QualityCombo.SelectedIndex = (int)c.Recording.Quality;
        SegmentInput.Text = c.Recording.SegmentMinutes.ToString();
        MotionRecordInput.Text = c.Recording.MotionRecordSeconds.ToString();
        PreRollInput.Text = c.Recording.PreRollSeconds.ToString();
        MaxStorageInput.Text = c.Recording.MaxStorageGb.ToString("0.#");
        RetentionInput.Text = c.Recording.RetentionDays.ToString();
        DailyFolderCheck.IsChecked = c.Recording.UseDailyFolder;

        MotionEnabledCheck.IsChecked = c.Motion.Enabled;
        SensitivitySlider.Value = c.Motion.Sensitivity;
        SensitivityLabel.Text = c.Motion.Sensitivity.ToString();
        CooldownInput.Text = c.Motion.CooldownSeconds.ToString();
        SnapshotOnMotionCheck.IsChecked = c.Motion.SnapshotOnMotion;

        FaceEnabledCheck.IsChecked = c.Face.Enabled;
        CascadeInput.Text = c.Face.CascadeFilePath;

        OverlayTimestampCheck.IsChecked = c.Overlay.ShowTimestamp;
        OverlayDeviceCheck.IsChecked = c.Overlay.ShowDeviceName;
        WatermarkInput.Text = c.Overlay.WatermarkText ?? "";

        WebhookInput.Text = c.Motion.WebhookUrl ?? "";

        SensitivitySlider.ValueChanged += (_, e) => SensitivityLabel.Text = $"{(int)e.NewValue}";
    }

    // ───────────────────────────── 保存 ─────────────────────────────

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var (width, height) = ResolutionCombo.SelectedIndex switch
        {
            1 => (1280, 720),
            2 => (640, 480),
            3 => (320, 240),
            _ => (1920, 1080)
        };

        var config = new AppConfig
        {
            Camera = new CameraSettings
            {
                Backend = (CaptureBackend)Math.Max(0, BackendCombo.SelectedIndex),
                Width = width,
                Height = height,
                Fps = FpsCombo.SelectedIndex >= 0 && FpsCombo.SelectedIndex < FpsOptions.Length
                    ? FpsOptions[FpsCombo.SelectedIndex]
                    : Constants.DefaultFps,
                HardwareAccel = HardwareAccelCheck.IsChecked == true,
                // 检测区域不在这个窗口里编辑，原样带回去，别被新对象吃掉
                Regions = _source.Camera.Regions,
                FillScreen = _source.Camera.FillScreen
            },
            Recording = new RecordingSettings
            {
                Enabled = RecordEnabledCheck.IsChecked == true,
                Mode = (RecordMode)Math.Max(0, RecordModeCombo.SelectedIndex),
                SaveDir = string.IsNullOrWhiteSpace(SaveDirInput.Text) ? "recordings" : SaveDirInput.Text.Trim(),
                Quality = (RecordingQuality)Math.Clamp(QualityCombo.SelectedIndex, 0, 2),
                SegmentMinutes = ParseInt(SegmentInput.Text, Constants.DefaultSegmentMinutes, 1, 240),
                MotionRecordSeconds = ParseInt(MotionRecordInput.Text, Constants.DefaultMotionRecordSeconds, 3, 600),
                PreRollSeconds = ParseInt(PreRollInput.Text, Constants.DefaultPreRollSeconds, 0, 60),
                MaxStorageGb = ParseDouble(MaxStorageInput.Text, Constants.DefaultMaxStorageGb),
                RetentionDays = ParseInt(RetentionInput.Text, Constants.DefaultRetentionDays, 1, 3650),
                UseDailyFolder = DailyFolderCheck.IsChecked == true,
                JpegQuality = _source.Recording.JpegQuality
            },
            Motion = new MotionDetectionSettings
            {
                Enabled = MotionEnabledCheck.IsChecked == true,
                Sensitivity = (int)SensitivitySlider.Value,
                CooldownSeconds = ParseInt(CooldownInput.Text, Constants.DefaultNotificationCooldownSeconds, 1, 3600),
                SnapshotOnMotion = SnapshotOnMotionCheck.IsChecked == true,
                WebhookUrl = string.IsNullOrWhiteSpace(WebhookInput.Text) ? null : WebhookInput.Text.Trim()
            },
            Overlay = new OverlaySettings
            {
                ShowTimestamp = OverlayTimestampCheck.IsChecked == true,
                ShowDeviceName = OverlayDeviceCheck.IsChecked == true,
                WatermarkText = string.IsNullOrWhiteSpace(WatermarkInput.Text) ? null : WatermarkInput.Text.Trim()
            },
            Face = new FaceDetectionSettings
            {
                Enabled = FaceEnabledCheck.IsChecked == true,
                CascadeFilePath = string.IsNullOrWhiteSpace(CascadeInput.Text)
                    ? Constants.DefaultCascadeFileName
                    : CascadeInput.Text.Trim()
            }
        };

        Result = config;
        DialogResult = true;
    }

    private void OnBrowseDirectory(object sender, RoutedEventArgs e)
    {
        var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "选择录像与截图的保存目录",
            SelectedPath = SaveDirInput.Text
        };

        if (dialog.ShowDialog() == true)
            SaveDirInput.Text = dialog.SelectedPath;
    }

    private async void OnTestWebhook(object sender, RoutedEventArgs e)
    {
        var url = WebhookInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this, "请先填写 Webhook 地址。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        TestButton.IsEnabled = false;
        TestButton.Content = "发送中…";

        try
        {
            var notifier = (INotificationService)App.ServiceProvider.GetService(typeof(INotificationService))!;
            notifier.Configure(url);

            var ok = await notifier.SendTestMessageAsync();

            MessageBox.Show(this, ok ? "测试消息已送达，请查看企业微信群。" : "发送失败，请检查 Webhook 地址是否正确。",
                "测试结果", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Webhook 测试异常");
            MessageBox.Show(this, $"测试异常：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestButton.IsEnabled = true;
            TestButton.Content = "发送测试消息";
        }
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private static int NearestFpsIndex(int fps)
    {
        var best = 1;   // 30fps
        var bestDiff = int.MaxValue;
        for (var i = 0; i < FpsOptions.Length; i++)
        {
            var diff = Math.Abs(FpsOptions[i] - fps);
            if (diff >= bestDiff) continue;
            bestDiff = diff;
            best = i;
        }
        return best;
    }

    private static int ParseInt(string text, int fallback, int min, int max)
    {
        return int.TryParse(text, out var value) ? Math.Clamp(value, min, max) : fallback;
    }

    private static double ParseDouble(string text, double fallback)
    {
        if (!double.TryParse(text, out var value)) return fallback;
        return value < 0 ? 0 : value;
    }
}
