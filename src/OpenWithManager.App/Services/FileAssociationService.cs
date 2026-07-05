using System.Runtime.InteropServices;
using System.Text;
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
        var appName = ResolveDisplayName(applicationKey?.GetValue("ApplicationName") as string);
        if (!string.IsNullOrWhiteSpace(appName))
        {
            return appName;
        }

        using var progIdKey = Registry.ClassesRoot.OpenSubKey(progId);
        var defaultName = ResolveDisplayName(progIdKey?.GetValue(null) as string);
        return string.IsNullOrWhiteSpace(defaultName) ? progId : defaultName;
    }

    public static string? ResolveDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!value.TrimStart().StartsWith('@'))
        {
            return value;
        }

        var buffer = new StringBuilder(512);
        var result = SHLoadIndirectString(value, buffer, (uint)buffer.Capacity, IntPtr.Zero);
        return result >= 0 && buffer.Length > 0 ? buffer.ToString() : value;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(
        string source,
        StringBuilder output,
        uint outputLength,
        IntPtr reserved);

    private sealed record KnownExtension(string Extension, string Category, string Description);
}
