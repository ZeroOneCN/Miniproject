using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CameraMotionCapture.App.Converters;

/// <summary>
/// 将 null 转换为 Visible，非 null 转换为 Collapsed
/// 用于"等待摄像头连接..."文字的显示控制
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}