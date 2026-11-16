using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FRMS.PRN212.WpfConnection.Theming;

/// <summary>Highlights the sidebar route whose command parameter matches the active page.</summary>
public sealed class RouteMatchConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 &&
        values[0] is string current &&
        values[1] is string route &&
        string.Equals(current, route, StringComparison.OrdinalIgnoreCase);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("Navigation selection is read-only.");
}
