using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CameraMotionCapture.App.ViewModels;

namespace CameraMotionCapture.App.Converters;

public class LayoutToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LayoutMode mode)
            return mode == LayoutMode.Single ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}