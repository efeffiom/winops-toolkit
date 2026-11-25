using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace WinOps.App.Converters;

public sealed class ListToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is IEnumerable items ? string.Join(", ", items.Cast<object>()) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
