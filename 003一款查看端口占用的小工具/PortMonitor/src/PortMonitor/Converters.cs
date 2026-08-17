using System;
using System.Globalization;
using System.Windows.Data;

namespace PortMonitor;

/// <summary>
/// Converts null/empty string to placeholder text.
/// </summary>
[ValueConversion(typeof(string), typeof(string))]
public class EmptyStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value?.ToString()) ? "-" : value.ToString()!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}