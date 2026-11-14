using System.Windows;

namespace CameraMotionCapture.App.Views;

public partial class TextInputDialog : Window
{
    public TextInputDialog(string title, string hint, string defaultValue = "")
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        HintText.Text = hint;
        Input.Text = defaultValue;

        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    public string Value => Input.Text.Trim();

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Input.Text)) return;
        DialogResult = true;
    }
}
