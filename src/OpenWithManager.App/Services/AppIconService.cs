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
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

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
        var path = ResolveIconPath(location.Path);
        if (path is null)
        {
            return null;
        }

        var bitmap = LoadBitmapIcon(path);
        if (bitmap is not null)
        {
            return bitmap;
        }

        var iconHandle = ExtractIconHandle(path, location.Index);
        if (iconHandle == IntPtr.Zero)
        {
            iconHandle = GetShellIconHandle(path);
        }

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

    private static string? ResolveIconPath(string value)
    {
        var path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (path.StartsWith('@') && !path.StartsWith("@{", StringComparison.Ordinal))
        {
            path = path[1..].Trim().Trim('"');
        }

        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("@{", StringComparison.Ordinal))
        {
            return null;
        }

        if (File.Exists(path))
        {
            return path;
        }

        if (Path.IsPathRooted(path))
        {
            return null;
        }

        var systemPath = Path.Combine(Environment.SystemDirectory, path);
        if (File.Exists(systemPath))
        {
            return systemPath;
        }

        var windowsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
        return File.Exists(windowsPath) ? windowsPath : null;
    }

    private static ImageSource? LoadBitmapIcon(string path)
    {
        var extension = Path.GetExtension(path);
        if (!IsBitmapIconExtension(extension))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 24;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBitmapIconExtension(string extension)
    {
        return extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static IntPtr ExtractIconHandle(string path, int index)
    {
        var primary = ExtractIconHandleAtIndex(path, index);
        return primary != IntPtr.Zero || index == 0 ? primary : ExtractIconHandleAtIndex(path, 0);
    }

    private static IntPtr ExtractIconHandleAtIndex(string path, int index)
    {
        var largeIcons = new IntPtr[1];
        var smallIcons = new IntPtr[1];
        var count = ExtractIconEx(path, index, largeIcons, smallIcons, 1);
        if (count == 0)
        {
            return IntPtr.Zero;
        }

        var selected = largeIcons[0] != IntPtr.Zero ? largeIcons[0] : smallIcons[0];
        DestroyUnusedIcon(largeIcons[0], selected);
        DestroyUnusedIcon(smallIcons[0], selected);
        return selected;
    }

    private static void DestroyUnusedIcon(IntPtr iconHandle, IntPtr selectedHandle)
    {
        if (iconHandle != IntPtr.Zero && iconHandle != selectedHandle)
        {
            DestroyIcon(iconHandle);
        }
    }

    private static IntPtr GetShellIconHandle(string path)
    {
        var info = new ShellFileInfo();
        var result = SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon);

        return result == IntPtr.Zero ? IntPtr.Zero : info.IconHandle;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }
}
