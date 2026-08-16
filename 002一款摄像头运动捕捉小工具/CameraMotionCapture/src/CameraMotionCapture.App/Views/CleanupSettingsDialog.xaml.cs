using System.Windows;
using CameraMotionCapture.Core.Services.Interfaces;

namespace CameraMotionCapture.App.Views;

public partial class CleanupSettingsDialog : Window
{
    private readonly IStorageService _storageService;
    private readonly string _saveDir;

    public int RetentionDays { get; private set; }
    public DateTime NextCleanupDate { get; private set; }

    public CleanupSettingsDialog(int retentionDays, string saveDir, string? nextCleanupDate = null)
    {
        InitializeComponent();
        _saveDir = saveDir;
        _storageService = (IStorageService)App.ServiceProvider.GetService(typeof(IStorageService))!;

        RetentionDays = retentionDays;
        RetentionDaysInput.Text = retentionDays.ToString();

        if (!string.IsNullOrEmpty(nextCleanupDate) && DateTime.TryParse(nextCleanupDate, out var parsed))
            CleanupDatePicker.SelectedDate = parsed;
        else
            CleanupDatePicker.SelectedDate = DateTime.Now.AddDays(1);
    }

    private void CleanupNowButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (count, size) = _storageService.CleanupOldFiles(_saveDir, RetentionDays);
            var sizeMb = size / (1024.0 * 1024.0);
            MessageBox.Show(this,
                $"已清理 {count} 个文件，释放 {sizeMb:F2} MB 空间。",
                "清理完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"清理过程中出错: {ex.Message}",
                "清理失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(RetentionDaysInput.Text, out var days) && days > 0 && days <= 365)
        {
            RetentionDays = days;
            NextCleanupDate = CleanupDatePicker.SelectedDate ?? DateTime.Now.AddDays(1);
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show(this, "请输入有效的保留天数 (1-365)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}