using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenWithManager.App.Models;

namespace OpenWithManager.App.Services;

public sealed class AppIconService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const string AppsFolderPrefix = @"shell:AppsFolder\";
    private const string AppsFolderGuidPrefix = @"shell:::{4234d49b-0245-4df3-b780-3893943456e1}\";

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
        if (location.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return LoadShellItemIcon(location.Path);
        }

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
        if (path.StartsWith("@{", StringComparison.Ordinal))
        {
            return ResolvePackagedIconPath(path);
        }

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

    private static string? ResolvePackagedIconPath(string reference)
    {
        var indirectPath = ResolveIndirectStringPath(reference);
        if (!string.IsNullOrWhiteSpace(indirectPath))
        {
            return indirectPath;
        }

        var content = reference.Trim();
        if (!content.StartsWith("@{", StringComparison.Ordinal) || !content.EndsWith('}'))
        {
            return null;
        }

        content = content[2..^1];
        var queryIndex = content.IndexOf('?');
        if (queryIndex <= 0)
        {
            return null;
        }

        var packageFullName = content[..queryIndex];
        var relativePath = ParsePackagedResourcePath(content[(queryIndex + 1)..]);
        var packageRoot = FindPackageRoot(packageFullName);
        return packageRoot is null || relativePath is null ? null : ResolvePackageAssetPath(packageRoot, relativePath);
    }

    private static string? ResolveIndirectStringPath(string reference)
    {
        var buffer = new StringBuilder(1024);
        var result = SHLoadIndirectString(reference, buffer, (uint)buffer.Capacity, IntPtr.Zero);
        if (result < 0 || buffer.Length == 0)
        {
            return null;
        }

        var resolved = buffer.ToString();
        return string.Equals(resolved, reference, StringComparison.Ordinal) ? null : ResolveIconPath(resolved);
    }

    private static string? ParsePackagedResourcePath(string resource)
    {
        var text = Uri.UnescapeDataString(resource).Replace('\\', '/');
        var filesIndex = text.IndexOf("/Files/", StringComparison.OrdinalIgnoreCase);
        if (filesIndex >= 0)
        {
            return text[(filesIndex + "/Files/".Length)..].Replace('/', Path.DirectorySeparatorChar);
        }

        if (text.StartsWith("Files/", StringComparison.OrdinalIgnoreCase))
        {
            return text["Files/".Length..].Replace('/', Path.DirectorySeparatorChar);
        }

        var assetsIndex = text.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
        return assetsIndex >= 0 ? text[assetsIndex..].Replace('/', Path.DirectorySeparatorChar) : null;
    }

    private static string? FindPackageRoot(string packageFullName)
    {
        foreach (var root in ReadPackageRootCandidates(packageFullName))
        {
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                return root;
            }
        }

        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");
        var exactPath = Path.Combine(windowsApps, packageFullName);
        if (Directory.Exists(exactPath))
        {
            return exactPath;
        }

        return null;
    }

    private static IEnumerable<string?> ReadPackageRootCandidates(string packageFullName)
    {
        using var packageKey = Registry.ClassesRoot.OpenSubKey(
            $@"Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages\{packageFullName}");

        yield return packageKey?.GetValue("Path") as string;
        yield return packageKey?.GetValue("PackageRootFolder") as string;
        yield return packageKey?.GetValue("InstallLocation") as string;
    }

    private static string? ResolvePackageAssetPath(string packageRoot, string relativePath)
    {
        var exactPath = Path.Combine(packageRoot, relativePath);
        if (File.Exists(exactPath))
        {
            return exactPath;
        }

        var directory = Path.GetDirectoryName(exactPath);
        var extension = Path.GetExtension(exactPath);
        var baseName = Path.GetFileNameWithoutExtension(exactPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(extension) || !Directory.Exists(directory))
        {
            return null;
        }

        var preferredSuffixes = new[]
        {
            ".targetsize-48_altform-unplated",
            ".targetsize-48",
            ".targetsize-44_altform-unplated",
            ".targetsize-44",
            ".scale-200",
            ".scale-150",
            ".scale-100"
        };

        foreach (var suffix in preferredSuffixes)
        {
            var candidate = Path.Combine(directory, $"{baseName}{suffix}{extension}");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        try
        {
            return Directory
                .EnumerateFiles(directory, $"{baseName}*{extension}")
                .OrderByDescending(path => path.Contains("targetsize-48", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(path => path.Contains("scale-200", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
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

    private static ImageSource? LoadShellItemIcon(string parsingName)
    {
        var icon = LoadShellItemIconCore(parsingName);
        if (icon is not null || !parsingName.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return icon;
        }

        var appUserModelId = parsingName[AppsFolderPrefix.Length..];
        return LoadShellItemIconCore($"{AppsFolderGuidPrefix}{appUserModelId}");
    }

    private static ImageSource? LoadShellItemIconCore(string parsingName)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        var result = SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory);
        if (result < 0 || factory is null)
        {
            return null;
        }

        try
        {
            result = factory.GetImage(
                new NativeSize(32, 32),
                ShellItemImageFactoryFlags.IconOnly | ShellItemImageFactoryFlags.BiggerSizeOk,
                out var bitmapHandle);
            if (result < 0 || bitmapHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapHandle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(24, 24));
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(bitmapHandle);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
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

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(
        string source,
        StringBuilder output,
        uint outputLength,
        IntPtr reserved);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? shellItem);

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

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize
    {
        public NativeSize(int width, int height)
        {
            Cx = width;
            Cy = height;
        }

        public readonly int Cx;

        public readonly int Cy;
    }

    [Flags]
    private enum ShellItemImageFactoryFlags
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        IconOnly = 0x04
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(
            NativeSize size,
            ShellItemImageFactoryFlags flags,
            out IntPtr bitmapHandle);
    }
}
