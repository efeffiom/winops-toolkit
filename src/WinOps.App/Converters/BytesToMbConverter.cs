using System.Globalization;
using System.Windows.Data;

namespace WinOps.App.Converters;

public sealed class BytesToMbConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is long bytes ? (bytes / 1024.0 / 1024.0).ToString("N0", culture) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
