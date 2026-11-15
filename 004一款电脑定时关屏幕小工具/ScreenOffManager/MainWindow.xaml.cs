using System.Windows;
using ScreenOffManager.Services;
using ScreenOffManager.ViewModels;

namespace ScreenOffManager;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();

        ViewModel = new MainViewModel(settings);
        ViewModel.MessageRequested += OnMessageRequested;
        DataContext = ViewModel;
    }

    private void OnMessageRequested(string title, string message)
    {
        var isError = title.Contains("失败");

        Dispatcher.Invoke(() =>
        {
            System.Windows.MessageBox.Show(this, message, title,
                System.Windows.MessageBoxButton.OK,
                isError ? System.Windows.MessageBoxImage.Error : System.Windows.MessageBoxImage.Information);
        });
    }
}
