using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using OpenWithManager.App.Models;
using Microsoft.Win32;

namespace OpenWithManager.App.Services;

public sealed class FileAssociationService
{
    private static readonly KnownExtension[] KnownExtensions =
    [
        new(".pdf", "Document", "PDF document"),
        new(".txt", "Document", "Plain text"),
        new(".md", "Document", "Markdown"),
        new(".docx", "Document", "Word document"),
        new(".xlsx", "Document", "Excel workbook"),
        new(".pptx", "Document", "PowerPoint presentation"),
        new(".jpg", "Image", "JPEG image"),
        new(".jpeg", "Image", "JPEG image"),
        new(".png", "Image", "PNG image"),
        new(".gif", "Image", "GIF image"),
        new(".webp", "Image", "WebP image"),
        new(".svg", "Image", "SVG image"),
        new(".mp3", "Audio", "MP3 audio"),
        new(".wav", "Audio", "WAV audio"),
        new(".mp4", "Video", "MP4 video"),
        new(".mov", "Video", "QuickTime video"),
        new(".mkv", "Video", "Matroska video"),
        new(".zip", "Archive", "ZIP archive"),
        new(".rar", "Archive", "RAR archive"),
        new(".7z", "Archive", "7-Zip archive"),
        new(".html", "Web", "HTML document"),
        new(".htm", "Web", "HTML document"),
        new(".json", "Code", "JSON file"),
        new(".js", "Code", "JavaScript file"),
        new(".ts", "Code", "TypeScript file"),
        new(".cs", "Code", "C# source file"),
        new(".py", "Code", "Python source file")
    ];

    public List<FileAssociationItem> GetKnownAssociations()
    {
        return KnownExtensions
            .Select(extension =>
            {
                var userChoice = ReadUserChoice(extension.Extension);
                var fallback = ReadClassDefault(extension.Extension);
                var progId = userChoice ?? fallback;

                return new FileAssociationItem(
                    extension.Extension,
                    extension.Category,
                    extension.Description,
                    progId,
                    ReadFriendlyName(progId),
                    ReadIconDataUrl(progId),
                    userChoice is not null ? "UserChoice" : fallback is not null ? "Registry" : "Unknown");
            })
            .OrderBy(item => item.Category)
            .ThenBy(item => item.Extension)
            .ToList();
    }

    private static string? ReadUserChoice(string extension)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice");

        return key?.GetValue("ProgId") as string;
    }

    private static string? ReadClassDefault(string extension)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(extension);
        return key?.GetValue(null) as string;
    }

    public static string? ReadFriendlyName(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            return null;
        }

        using var applicationKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\Application");
        var appName = applicationKey?.GetValue("ApplicationName") as string;
        if (!string.IsNullOrWhiteSpace(appName))
        {
            return appName;
        }

        using var progIdKey = Registry.ClassesRoot.OpenSubKey(progId);
        var defaultName = progIdKey?.GetValue(null) as string;
        return string.IsNullOrWhiteSpace(defaultName) ? progId : defaultName;
    }

    public static string? ReadIconDataUrl(string? progId)
    {
        var iconPath = ReadOpenCommandPath(progId) ?? ReadIconPath(progId);
        return ReadFileIconDataUrl(iconPath);
    }

    public static string? ReadFileIconDataUrl(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        try
        {
            var filePath = NormalizeResourcePath(iconPath);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            var iconHandle = GetLargeIconHandle(filePath);
            if (iconHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
            }
            finally
            {
                DestroyIcon(iconHandle);
            }
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadIconPath(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            return null;
        }

        using var key = Registry.ClassesRoot.OpenSubKey($@"{progId}\DefaultIcon");
        return key?.GetValue(null) as string;
    }

    private static string? ReadOpenCommandPath(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            return null;
        }

        using var key = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        return key?.GetValue(null) as string;
    }

    private static string? NormalizeResourcePath(string value)
    {
        var path = Environment.ExpandEnvironmentVariables(value.Trim());
        if (path.StartsWith('"'))
        {
            var endQuote = path.IndexOf('"', 1);
            if (endQuote > 1)
            {
                path = path[1..endQuote];
            }
        }
        else
        {
            var commaIndex = path.IndexOf(',');
            if (commaIndex > 0)
            {
                path = path[..commaIndex];
            }

            var exeIndex = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex > 0)
            {
                path = path[..(exeIndex + 4)];
            }
        }

        return path.Trim().Trim('"');
    }

    private static IntPtr GetLargeIconHandle(string filePath)
    {
        var info = new ShFileInfo();
        var result = SHGetFileInfo(
            filePath,
            0,
            ref info,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            ShGetFileInfoFlags.Icon | ShGetFileInfoFlags.LargeIcon);

        return result == IntPtr.Zero ? IntPtr.Zero : info.IconHandle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShFileInfo fileInfo,
        uint fileInfoSize,
        ShGetFileInfoFlags flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [Flags]
    private enum ShGetFileInfoFlags : uint
    {
        Icon = 0x000000100,
        LargeIcon = 0x000000000
    }

    private sealed record KnownExtension(string Extension, string Category, string Description);
}
