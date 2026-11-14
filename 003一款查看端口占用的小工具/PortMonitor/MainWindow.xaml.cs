using System;
using System.Windows;
using System.Windows.Input;
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

    /// <summary>
    /// 全端口总览双击一行 → 带着端口号跳到“端口占用”页。
    /// </summary>
    private void AllPortsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.AllPorts.OpenInPortCommand.CanExecute(null))
            vm.AllPorts.OpenInPortCommand.Execute(null);
    }
}
