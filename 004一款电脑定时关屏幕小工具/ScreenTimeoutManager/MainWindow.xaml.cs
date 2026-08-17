using System.Windows;
using ScreenTimeoutManager.ViewModels;

namespace ScreenTimeoutManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        _viewModel.MessageRequested += OnMessageRequested;
        DataContext = _viewModel;
    }

    private void OnMessageRequested(string title, string message)
    {
        var isError = title == "错误";
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                isError ? MessageBoxImage.Error : MessageBoxImage.Information);
        });
    }
}