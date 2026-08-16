using System.Globalization;
using System.Windows.Data;

namespace CameraMotionCapture.App.Converters;

/// <summary>
/// 将 LayoutMode 枚举值转换为 ComboBox 的 SelectedIndex
/// LayoutMode: Single=0, Grid2x2=1, Grid3x3=2, Grid1Plus2=3
/// </summary>
public class EnumToIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Enum e ? (int)(object)e : 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && targetType.IsEnum)
        {
            var values = Enum.GetValues(targetType);
            if (index >= 0 && index < values.Length)
                return values.GetValue(index)!;
        }
        return 0;
    }
}