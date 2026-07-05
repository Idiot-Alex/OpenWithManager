using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenWithManager.App.Models;

namespace OpenWithManager.App.Services;

public sealed class AppIconService
{
    private readonly Dictionary<AppIconLocation, ImageSource?> _cache = [];

    public ImageSource? GetIcon(AppIconLocation? location)
    {
        if (location is null)
        {
            return null;
        }

        if (!_cache.TryGetValue(location, out var icon))
        {
            icon = LoadIcon(location);
            _cache[location] = icon;
        }

        return icon;
    }

    private static ImageSource? LoadIcon(AppIconLocation location)
    {
        var path = Environment.ExpandEnvironmentVariables(location.Path.Trim());
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var largeIcons = new IntPtr[1];
        var count = ExtractIconEx(path, location.Index, largeIcons, null, 1);
        var iconHandle = count > 0 ? largeIcons[0] : IntPtr.Zero;
        if (iconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(24, 24));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string fileName,
        int iconIndex,
        IntPtr[]? largeIcons,
        IntPtr[]? smallIcons,
        uint icons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);
}
