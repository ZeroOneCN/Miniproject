using System.Windows;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;

namespace CameraMotionCapture.App.Views;

public partial class SettingsDialog : Window
{
    private readonly AppConfig _config;

    // 输出属性
    public CameraSettings CameraSettings { get; private set; } = new();
    public RecordingSettings RecordingSettings { get; private set; } = new();
    public MotionDetectionSettings MotionSettings { get; private set; } = new();
    public SnapshotSettings SnapshotSettings { get; private set; } = new();
    public OverlaySettings OverlaySettings { get; private set; } = new();
    public FaceRecognitionSettings FaceSettings { get; private set; } = new();
    public int RetentionDays { get; private set; } = 7;

    public SettingsDialog(AppConfig config)
    {
        InitializeComponent();
        _config = config;

        // ====== 摄像头 ======
        BackendCombo.SelectedIndex = (int)config.Camera.Backend;
        var resIndex = config.Camera.Width switch
        {
            1920 => 1,
            640 => 2,
            320 => 3,
            _ => 0
        };
        ResolutionCombo.SelectedIndex = resIndex;
        FpsInput.Text = config.Camera.Fps.ToString();
        PreviewFpsInput.Text = config.Camera.PreviewFps.ToString();
        LowLoadCheck.IsChecked = config.Camera.LowLoadPreview;
        HardwareAccelCheck.IsChecked = config.Camera.HardwareAccel;

        // ====== 录制 ======
        SaveDirInput.Text = config.Recording.SaveDir;
        CodecCombo.SelectedIndex = config.Recording.Codec switch
        {
            "XVID" => 1,
            "MJPG" => 2,
            "H264" => 3,
            "MP4V" => 4,
            _ => 0
        };
        RecordModeCombo.SelectedIndex = (int)config.Recording.Mode;
        ScheduleStartInput.Text = config.Recording.ScheduleStart;
        ScheduleEndInput.Text = config.Recording.ScheduleEnd;
        SegmentHoursInput.Text = (config.Recording.SegmentDurationSeconds / 3600).ToString();
        QualitySlider.Value = config.Recording.Quality;
        QualityLabel.Text = $"{config.Recording.Quality}%";
        MaxStorageInput.Text = config.Recording.MaxStorageGb.ToString("F1");
        DailyFolderCheck.IsChecked = config.Recording.UseDailyFolder;
        AutoCompressCheck.IsChecked = config.Recording.AutoCompress;

        // ====== 运动检测 ======
        MotionEnabledCheck.IsChecked = config.MotionDetection.Enabled;
        ThresholdSlider.Value = config.MotionDetection.Threshold / 50.0; // 归一化
        ThresholdLabel.Text = config.MotionDetection.Threshold.ToString();
        NotifCooldownInput.Text = (config.MotionDetection.NotificationCooldownSeconds / 60).ToString();
        SnapshotModeCombo.SelectedIndex = (int)config.Snapshot.Mode;
        SnapshotIntervalInput.Text = config.Snapshot.IntervalSeconds.ToString();
        SnapshotCooldownInput.Text = config.Snapshot.CooldownSeconds.ToString();

        // ====== 叠加 ======
        OverlayTimestampCheck.IsChecked = config.Overlay.ShowTimestamp;
        OverlayDeviceCheck.IsChecked = config.Overlay.ShowDeviceName;
        WatermarkInput.Text = config.Overlay.WatermarkText ?? "";

        // ====== 人脸识别 ======
        FaceEnabledCheck.IsChecked = config.FaceRecognition.Enabled;
        FaceConfidenceSlider.Value = config.FaceRecognition.ConfidenceThreshold;
        FaceConfidenceLabel.Text = config.FaceRecognition.ConfidenceThreshold.ToString("F0");
        CascadePathInput.Text = config.FaceRecognition.CascadeFilePath;
        KnownFacesInput.Text = config.FaceRecognition.KnownFacesDir;

        // ====== 通知 ======
        WebhookInput.Text = config.MotionDetection.WebhookUrl ?? "";

        RetentionDays = config.RetentionDays;

        // 订阅事件
        QualitySlider.ValueChanged += (_, e) => QualityLabel.Text = $"{(int)e.NewValue}%";
        ThresholdSlider.ValueChanged += (_, e) => ThresholdLabel.Text = $"{(int)(e.NewValue * 50)}";
        FaceConfidenceSlider.ValueChanged += (_, e) => FaceConfidenceLabel.Text = $"{(int)e.NewValue}";
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "选择保存目录",
            SelectedPath = SaveDirInput.Text
        };
        if (dlg.ShowDialog() == true)
            SaveDirInput.Text = dlg.SelectedPath;
    }

    private void BrowseFacesButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "选择已知人脸目录",
            SelectedPath = KnownFacesInput.Text
        };
        if (dlg.ShowDialog() == true)
            KnownFacesInput.Text = dlg.SelectedPath;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // 收集摄像头设置
        var (width, height) = ResolutionCombo.SelectedIndex switch
        {
            1 => (1920, 1080),
            2 => (640, 480),
            3 => (320, 240),
            _ => (1280, 720)
        };

        int.TryParse(FpsInput.Text, out var fps);
        if (fps <= 0) fps = Constants.DefaultFps;
        int.TryParse(PreviewFpsInput.Text, out var previewFps);
        if (previewFps <= 0) previewFps = Constants.DefaultPreviewFps;

        CameraSettings = new CameraSettings
        {
            Backend = (CaptureBackend)BackendCombo.SelectedIndex,
            Width = width, Height = height,
            Fps = fps,
            PreviewFps = previewFps,
            LowLoadPreview = LowLoadCheck.IsChecked ?? true,
            HardwareAccel = HardwareAccelCheck.IsChecked ?? false
        };

        // 收集录制设置
        double.TryParse(SegmentHoursInput.Text, out var hours);
        if (hours <= 0) hours = 1;
        double.TryParse(MaxStorageInput.Text, out var maxStorage);
        if (maxStorage <= 0) maxStorage = Constants.DefaultMaxStorageGb;

        RecordingSettings = new RecordingSettings
        {
            SaveDir = SaveDirInput.Text.Trim(),
            Codec = CodecCombo.SelectedIndex switch
            {
                1 => "XVID", 2 => "MJPG", 3 => "H264", 4 => "MP4V", _ => "auto"
            },
            Mode = (RecordMode)RecordModeCombo.SelectedIndex,
            ScheduleStart = ScheduleStartInput.Text.Trim(),
            ScheduleEnd = ScheduleEndInput.Text.Trim(),
            SegmentDurationSeconds = (int)(hours * 3600),
            Quality = (int)QualitySlider.Value,
            MaxStorageGb = maxStorage,
            UseDailyFolder = DailyFolderCheck.IsChecked ?? true,
            AutoCompress = AutoCompressCheck.IsChecked ?? false
        };

        // 收集运动检测设置
        int.TryParse(NotifCooldownInput.Text, out var cooldownMin);
        int.TryParse(SnapshotIntervalInput.Text, out var snapInterval);
        int.TryParse(SnapshotCooldownInput.Text, out var snapCooldown);

        MotionSettings = new MotionDetectionSettings
        {
            Enabled = MotionEnabledCheck.IsChecked ?? true,
            Threshold = (int)(ThresholdSlider.Value * 50),
            NotificationCooldownSeconds = cooldownMin * 60,
            WebhookUrl = WebhookInput.Text.Trim()
        };

        SnapshotSettings = new SnapshotSettings
        {
            Mode = (SnapshotMode)SnapshotModeCombo.SelectedIndex,
            IntervalSeconds = snapInterval > 0 ? snapInterval : Constants.DefaultSnapshotInterval,
            CooldownSeconds = snapCooldown > 0 ? snapCooldown : Constants.DefaultSnapshotCooldown
        };

        // 收集叠加设置
        OverlaySettings = new OverlaySettings
        {
            ShowTimestamp = OverlayTimestampCheck.IsChecked ?? true,
            ShowDeviceName = OverlayDeviceCheck.IsChecked ?? true,
            WatermarkText = WatermarkInput.Text.Trim()
        };

        // 收集人脸识别设置
        FaceSettings = new FaceRecognitionSettings
        {
            Enabled = FaceEnabledCheck.IsChecked ?? false,
            ConfidenceThreshold = FaceConfidenceSlider.Value,
            CascadeFilePath = CascadePathInput.Text.Trim(),
            KnownFacesDir = KnownFacesInput.Text.Trim()
        };

        // 异步保存配置到文件
        var settingsService = (ISettingsService)App.ServiceProvider.GetService(typeof(ISettingsService))!;
        var saveConfig = new AppConfig
        {
            Camera = CameraSettings,
            Recording = RecordingSettings,
            MotionDetection = MotionSettings,
            Snapshot = SnapshotSettings,
            Overlay = OverlaySettings,
            FaceRecognition = FaceSettings,
            RetentionDays = RetentionDays
        };
        // 更新 Webhook
        saveConfig.MotionDetection.WebhookUrl = WebhookInput.Text.Trim();
        await Task.Run(() => settingsService.SaveConfig(saveConfig));

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void TestWebhookButton_Click(object sender, RoutedEventArgs e)
    {
        var url = WebhookInput.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show(this, "请先输入 Webhook URL", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            TestWebhookButton.IsEnabled = false;
            TestWebhookButton.Content = "测试中...";

            var notificationService = (INotificationService)App.ServiceProvider.GetService(typeof(INotificationService))!;
            notificationService.Configure(url);
            var result = await Task.Run(() => notificationService.SendTestMessageAsync());

            MessageBox.Show(this, result ? "Webhook 连接测试成功！" : "Webhook 测试失败，请检查 URL 是否正确",
                "测试结果", MessageBoxButton.OK, result ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"测试异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestWebhookButton.IsEnabled = true;
            TestWebhookButton.Content = "测试连接";
        }
    }
}