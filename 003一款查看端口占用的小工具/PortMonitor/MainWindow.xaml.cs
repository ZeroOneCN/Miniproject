using System;
using System.Windows;
using System.Windows.Media.Imaging;
using PortMonitor.ViewModels;

namespace PortMonitor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 从嵌入资源载入图标，单文件发布下同样可用
        Icon = new BitmapImage(new Uri("pack://application:,,,/PortMonitor;component/Resources/app_icon.png"));

        DataContext = new MainViewModel();
    }
}
