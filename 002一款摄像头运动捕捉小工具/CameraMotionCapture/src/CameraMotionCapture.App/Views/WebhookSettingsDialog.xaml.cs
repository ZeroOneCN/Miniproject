using System.Windows;
using CameraMotionCapture.Core.Services.Implementations;
using CameraMotionCapture.Core.Services.Interfaces;

namespace CameraMotionCapture.App.Views;

public partial class WebhookSettingsDialog : Window
{
    private readonly INotificationService _notificationService;

    public string WebhookUrl { get; private set; }

    public WebhookSettingsDialog(string currentUrl = "")
    {
        InitializeComponent();
        WebhookUrl = currentUrl;
        WebhookInput.Text = currentUrl;
        _notificationService = (INotificationService)App.ServiceProvider.GetService(typeof(INotificationService))!;
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var url = WebhookInput.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show(this, "请输入有效的Webhook URL", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TestButton.IsEnabled = false;
        _notificationService.Configure(url);

        try
        {
            var success = await _notificationService.SendTestMessageAsync();
            if (success)
                MessageBox.Show(this, "Webhook连接测试成功！", "测试成功", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(this, "连接测试失败，请检查URL是否正确。", "测试失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            MessageBox.Show(this, "连接测试异常，请检查网络连接。", "测试异常", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        WebhookUrl = WebhookInput.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}