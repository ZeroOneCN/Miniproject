using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PortMonitor.Models;
using PortMonitor.ViewModels;

namespace PortMonitor;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // Bind Enter key to check port
        PortTextBox.KeyDown += OnPortTextBoxKeyDown;
    }

    private void OnPortTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.CheckPortCommand.CanExecute(null))
        {
            _viewModel.CheckPortCommand.Execute(null);
        }
    }

    private void ConnectionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConnectionsGrid.SelectedItem is ConnectionInfo info)
        {
            _viewModel.OnSelectionChanged(info);
        }
        else
        {
            _viewModel.OnSelectionChanged(null);
        }
    }
}