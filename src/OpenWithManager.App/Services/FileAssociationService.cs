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
                var shellProgId = ReadAssociationString(extension.Extension, AssocString.ProgId);
                var fallback = ReadClassDefault(extension.Extension);
                var progId = userChoice ?? shellProgId ?? fallback;
                var appName = ReadAssociationString(extension.Extension, AssocString.FriendlyAppName)
                    ?? ReadFriendlyName(progId);

                return new FileAssociationItem(
                    extension.Extension,
                    extension.Category,
                    extension.Description,
                    progId,
                    appName,
                    userChoice is not null ? "UserChoice" : shellProgId is not null ? "Shell" : fallback is not null ? "Registry" : "Unknown");
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

    private static string? ReadAssociationString(string extension, AssocString value)
    {
        var length = 1024u;
        var buffer = new StringBuilder((int)length);
        var result = AssocQueryString(AssocFlags.None, value, extension, null, buffer, ref length);
        return result >= 0 && buffer.Length > 0 ? ResolveDisplayName(buffer.ToString()) : null;
    }

    public static AppIconLocation? ReadIconLocation(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            return null;
        }

        return ReadRegistryIconLocation($@"{progId}\DefaultIcon")
            ?? ReadRegistryOpenCommandIconLocation($@"{progId}\shell\open\command");
    }

    public static AppIconLocation? ReadApplicationIconLocation(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        return ReadRegistryIconLocation($@"Applications\{executableName}\DefaultIcon")
            ?? ReadRegistryOpenCommandIconLocation($@"Applications\{executableName}\shell\open\command")
            ?? ReadAppPathIconLocation(Registry.CurrentUser, executableName)
            ?? ReadAppPathIconLocation(Registry.LocalMachine, executableName);
    }

    private static AppIconLocation? ReadRegistryIconLocation(string keyPath)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(keyPath);
        return ParseIconLocation(key?.GetValue(null) as string);
    }

    private static AppIconLocation? ReadRegistryOpenCommandIconLocation(string keyPath)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(keyPath);
        return ParseCommandLocation(key?.GetValue(null) as string);
    }

    private static AppIconLocation? ReadAppPathIconLocation(RegistryKey hive, string executableName)
    {
        using var key = hive.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
        return ParseCommandLocation(key?.GetValue(null) as string);
    }

    private static AppIconLocation? ParseIconLocation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = Environment.ExpandEnvironmentVariables(value.Trim());
        var path = text;
        var index = 0;
        if (text.StartsWith('"'))
        {
            var endQuote = text.IndexOf('"', 1);
            if (endQuote > 1)
            {
                path = text[1..endQuote];
                index = ParseIconIndex(text[(endQuote + 1)..]);
            }
        }
        else
        {
            var commaIndex = text.LastIndexOf(',');
            if (commaIndex > 0 && int.TryParse(text[(commaIndex + 1)..].Trim(), out var parsedIndex))
            {
                path = text[..commaIndex];
                index = parsedIndex;
            }
        }

        path = path.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(path) ? null : new AppIconLocation(path, index);
    }

    private static AppIconLocation? ParseCommandLocation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = Environment.ExpandEnvironmentVariables(value.Trim());
        string path;
        if (text.StartsWith('"'))
        {
            var endQuote = text.IndexOf('"', 1);
            path = endQuote > 1 ? text[1..endQuote] : text.Trim('"');
        }
        else
        {
            var exeIndex = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = exeIndex > 0 ? text[..(exeIndex + 4)] : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        }

        path = path.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(path) ? null : new AppIconLocation(path);
    }

    private static int ParseIconIndex(string value)
    {
        var commaIndex = value.LastIndexOf(',');
        return commaIndex >= 0 && int.TryParse(value[(commaIndex + 1)..].Trim(), out var index) ? index : 0;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(
        string source,
        StringBuilder output,
        uint outputLength,
        IntPtr reserved);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(
        AssocFlags flags,
        AssocString value,
        string association,
        string? extra,
        StringBuilder output,
        ref uint outputLength);

    private enum AssocFlags
    {
        None = 0
    }

    private enum AssocString
    {
        FriendlyAppName = 4,
        ProgId = 20
    }

    private sealed record KnownExtension(string Extension, string Category, string Description);
}
