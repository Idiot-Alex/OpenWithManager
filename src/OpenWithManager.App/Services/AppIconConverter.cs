using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OpenWithManager.App.Models;

namespace OpenWithManager.App.Services;

public sealed class AppIconConverter : IValueConverter
{
    private readonly AppIconService _icons = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is AppIconLocation location ? _icons.GetIcon(location) : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
