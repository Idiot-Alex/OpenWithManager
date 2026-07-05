using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OpenWithManager.App.Models;
using Microsoft.Win32;

namespace OpenWithManager.App.Services;

public sealed class FileAssociationService
{
    private static readonly KnownExtension[] SeedKnownExtensions =
    [
        new(".pdf", "PDF document", "application/pdf", "document"),
        new(".txt", "Plain text", "text/plain", "text"),
        new(".md", "Markdown", "text/markdown", "text"),
        new(".docx", "Word document", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "document"),
        new(".xlsx", "Excel workbook", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "document"),
        new(".pptx", "PowerPoint presentation", "application/vnd.openxmlformats-officedocument.presentationml.presentation", "document"),
        new(".jpg", "JPEG image", "image/jpeg", "image"),
        new(".jpeg", "JPEG image", "image/jpeg", "image"),
        new(".png", "PNG image", "image/png", "image"),
        new(".gif", "GIF image", "image/gif", "image"),
        new(".webp", "WebP image", "image/webp", "image"),
        new(".svg", "SVG image", "image/svg+xml", "image"),
        new(".mp3", "MP3 audio", "audio/mpeg", "audio"),
        new(".wav", "WAV audio", "audio/wav", "audio"),
        new(".mp4", "MP4 video", "video/mp4", "video"),
        new(".mov", "QuickTime video", "video/quicktime", "video"),
        new(".mkv", "Matroska video", "video/x-matroska", "video"),
        new(".ts", "MPEG transport stream video", "video/mp2t", "video"),
        new(".zip", "ZIP archive", "application/zip", "compressed"),
        new(".rar", "RAR archive", "application/vnd.rar", "compressed"),
        new(".7z", "7-Zip archive", "application/x-7z-compressed", "compressed"),
        new(".html", "HTML document", "text/html", "text"),
        new(".htm", "HTML document", "text/html", "text"),
        new(".json", "JSON file", "application/json", "text"),
        new(".js", "JavaScript file", "text/javascript", "text"),
        new(".cs", "C# source file", "text/plain", "text"),
        new(".py", "Python source file", "text/x-python", "text")
    ];

    public List<FileAssociationItem> GetKnownAssociations()
    {
        return DiscoverExtensions()
            .Select(ReadAssociation)
            .OrderBy(item => item.Category)
            .ThenBy(item => item.Extension)
            .ToList();
    }

    public FileAssociationItem GetAssociation(string extension)
    {
        var normalized = NormalizeExtension(extension);
        var extensions = new SortedDictionary<string, ExtensionMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in SeedKnownExtensions.Where(seed => string.Equals(seed.Extension, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            AddOrUpdateExtension(
                extensions,
                seed.Extension,
                seed.Description,
                seed.ContentType,
                seed.PerceivedType);
        }

        using var key = SafeOpenSubKey(Registry.ClassesRoot, normalized);
        AddOrUpdateExtension(
            extensions,
            normalized,
            key?.GetValue(null) as string,
            key?.GetValue("Content Type") as string,
            key?.GetValue("PerceivedType") as string);

        return ReadAssociation(extensions[normalized]);
    }

    private static FileAssociationItem ReadAssociation(ExtensionMetadata extension)
    {
        var userChoice = ReadUserChoice(extension.Extension);
        var shellProgId = ReadAssociationString(extension.Extension, AssocString.ProgId);
        var fallback = ReadClassDefault(extension.Extension);
        var progId = userChoice ?? shellProgId ?? fallback;
        var description = ReadDescription(extension, progId);
        var appName = progId is null
            ? null
            : ReadDisplayAppName(extension.Extension, description, progId);
        var source = userChoice is not null ? "UserChoice" : shellProgId is not null ? "Shell" : fallback is not null ? "Registry" : "Unknown";

        var item = new FileAssociationItem(
            extension.Extension,
            FileFormatClassifier.OtherId,
            description,
            progId,
            appName,
            ReadCurrentAppIconLocation(extension.Extension, progId),
            source,
            extension.ContentType,
            extension.PerceivedType);

        return item with { Category = FileFormatClassifier.Classify(item) };
    }

    private static IReadOnlyCollection<ExtensionMetadata> DiscoverExtensions()
    {
        var extensions = new SortedDictionary<string, ExtensionMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in SeedKnownExtensions)
        {
            AddOrUpdateExtension(
                extensions,
                extension.Extension,
                extension.Description,
                extension.ContentType,
                extension.PerceivedType);
        }

        AddClassesRootExtensions(extensions);
        AddCurrentUserExtensions(extensions);
        return extensions.Values.ToList();
    }

    private static void AddClassesRootExtensions(IDictionary<string, ExtensionMetadata> extensions)
    {
        foreach (var extension in SafeGetSubKeyNames(Registry.ClassesRoot))
        {
            if (!IsExtensionName(extension))
            {
                continue;
            }

            using var key = SafeOpenSubKey(Registry.ClassesRoot, extension);
            AddOrUpdateExtension(
                extensions,
                extension,
                key?.GetValue(null) as string,
                key?.GetValue("Content Type") as string,
                key?.GetValue("PerceivedType") as string);
        }
    }

    private static void AddCurrentUserExtensions(IDictionary<string, ExtensionMetadata> extensions)
    {
        using var fileExts = SafeOpenSubKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts");
        if (fileExts is null)
        {
            return;
        }

        foreach (var extension in SafeGetSubKeyNames(fileExts))
        {
            if (IsExtensionName(extension))
            {
                AddOrUpdateExtension(extensions, extension, null, null, null);
            }
        }
    }

    private static void AddOrUpdateExtension(
        IDictionary<string, ExtensionMetadata> extensions,
        string extension,
        string? description,
        string? contentType,
        string? perceivedType)
    {
        var normalized = NormalizeExtension(extension);
        if (!extensions.TryGetValue(normalized, out var metadata))
        {
            metadata = new ExtensionMetadata(normalized);
            extensions[normalized] = metadata;
        }

        metadata.Description = FirstNonEmpty(metadata.Description, ResolveDisplayName(description));
        metadata.ContentType = FirstNonEmpty(metadata.ContentType, contentType);
        metadata.PerceivedType = FirstNonEmpty(metadata.PerceivedType, perceivedType);
    }

    private static string ReadDescription(ExtensionMetadata extension, string? progId)
    {
        return FirstNonEmpty(
            ReadAssociationString(extension.Extension, AssocString.FriendlyDocName),
            ReadFriendlyName(progId),
            extension.Description,
            $"{extension.Extension.TrimStart('.').ToUpperInvariant()} file")!;
    }

    private static string[] SafeGetSubKeyNames(RegistryKey key)
    {
        try
        {
            return key.GetSubKeyNames();
        }
        catch
        {
            return [];
        }
    }

    private static RegistryKey? SafeOpenSubKey(RegistryKey key, string subKeyName)
    {
        try
        {
            return key.OpenSubKey(subKeyName);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsExtensionName(string value)
    {
        return value.Length is > 1 and <= 40
            && value[0] == '.'
            && value.Skip(1).All(character => char.IsLetterOrDigit(character)
                || character is '_' or '-' or '+' or '#');
    }

    private static string NormalizeExtension(string extension)
    {
        var value = extension.Trim();
        return value.StartsWith('.') ? value.ToLowerInvariant() : $".{value.ToLowerInvariant()}";
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
        var text = ReadRawAssociationString(extension, value);
        return string.IsNullOrWhiteSpace(text) ? null : ResolveDisplayName(text);
    }

    private static string? ReadRawAssociationString(string extension, AssocString value)
    {
        var length = 1024u;
        var buffer = new StringBuilder((int)length);
        var result = AssocQueryString(AssocFlags.None, value, extension, null, buffer, ref length);
        return result >= 0 && buffer.Length > 0 ? buffer.ToString() : null;
    }

    private static string? ReadDisplayAppName(string extension, string description, string progId)
    {
        var documentName = ReadAssociationString(extension, AssocString.FriendlyDocName);
        return FirstAppLikeName(ReadAssociationString(extension, AssocString.FriendlyAppName), documentName, description)
            ?? ReadExecutableDisplayName(ReadAssociationString(extension, AssocString.Executable))
            ?? FirstAppLikeName(ReadFriendlyName(progId), documentName, description);
    }

    private static string? FirstAppLikeName(string? value, string? documentName, string description)
    {
        if (string.IsNullOrWhiteSpace(value) || IsDocumentName(value, documentName, description) || IsPlaceholderAppName(value))
        {
            return null;
        }

        return value;
    }

    private static bool IsDocumentName(string value, string? documentName, string description)
    {
        var candidate = NormalizeName(value);
        return candidate == NormalizeName(documentName)
            || candidate == NormalizeName(description);
    }

    private static bool IsPlaceholderAppName(string value)
    {
        var normalized = NormalizeName(value);
        return normalized is "chooseanapp" or "selectanapp" or "chooseapp" or "选取应用" or "选择应用";
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized.EndsWith('s') ? normalized[..^1] : normalized;
    }

    private static string? ReadExecutableDisplayName(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return FirstNonEmpty(info.FileDescription, info.ProductName, Path.GetFileNameWithoutExtension(executablePath));
        }
        catch
        {
            return Path.GetFileNameWithoutExtension(executablePath);
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    public static AppIconLocation? ReadIconLocation(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            return null;
        }

        return ReadRegistryShellApplicationIconLocation($@"{progId}\Application")
            ?? ReadRegistryApplicationIconLocation($@"{progId}\Application")
            ?? ReadRegistryOpenCommandIconLocation($@"{progId}\shell\open\command")
            ?? ReadRegistryIconLocation($@"{progId}\DefaultIcon");
    }

    public static AppIconLocation? ReadCurrentAppIconLocation(string extension, string? progId)
    {
        return ReadRegistryShellApplicationIconLocation(progId is null ? null : $@"{progId}\Application")
            ?? ParseIconLocation(ReadRawAssociationString(extension, AssocString.AppIconReference))
            ?? ParseCommandLocation(ReadRawAssociationString(extension, AssocString.Executable))
            ?? ReadIconLocation(progId)
            ?? ParseIconLocation(ReadRawAssociationString(extension, AssocString.DefaultIcon));
    }

    public static AppIconLocation? ReadApplicationIconLocation(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        return ReadRegistryApplicationIconLocation($@"Applications\{executableName}\Application")
            ?? ReadRegistryOpenCommandIconLocation($@"Applications\{executableName}\shell\open\command")
            ?? ReadAppPathIconLocation(Registry.CurrentUser, executableName)
            ?? ReadAppPathIconLocation(Registry.LocalMachine, executableName)
            ?? ReadRegistryIconLocation($@"Applications\{executableName}\DefaultIcon");
    }

    public static AppIconLocation? ReadRegisteredApplicationIconLocation(
        RegistryKey hive,
        string capabilitiesPath,
        string registeredApplicationName,
        string? progId)
    {
        using var capabilities = hive.OpenSubKey(capabilitiesPath);
        return ReadShellApplicationIconLocation(registeredApplicationName)
            ?? ParseIconLocation(capabilities?.GetValue("ApplicationIcon") as string)
            ?? ReadIconLocation(progId);
    }

    public static AppIconLocation? ReadShellApplicationIconLocation(string? appUserModelId)
    {
        return !string.IsNullOrWhiteSpace(appUserModelId) && appUserModelId.Contains('!')
            ? new AppIconLocation($@"shell:AppsFolder\{appUserModelId}")
            : null;
    }

    private static AppIconLocation? ReadRegistryIconLocation(string keyPath)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(keyPath);
        return ParseIconLocation(key?.GetValue(null) as string);
    }

    private static AppIconLocation? ReadRegistryApplicationIconLocation(string keyPath)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(keyPath);
        return ParseIconLocation(key?.GetValue("ApplicationIcon") as string);
    }

    private static AppIconLocation? ReadRegistryShellApplicationIconLocation(string? keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return null;
        }

        using var key = Registry.ClassesRoot.OpenSubKey(keyPath);
        return ReadShellApplicationIconLocation(
            key?.GetValue("AppUserModelID") as string
            ?? key?.GetValue("AppUserModelId") as string);
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

        var text = NormalizeIconLocationText(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

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

    private static string NormalizeIconLocationText(string value)
    {
        var text = Environment.ExpandEnvironmentVariables(value.Trim());
        return text.StartsWith('@') && !text.StartsWith("@{", StringComparison.Ordinal)
            ? text[1..].Trim()
            : text;
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
        Executable = 2,
        FriendlyDocName = 3,
        FriendlyAppName = 4,
        DefaultIcon = 15,
        ProgId = 20,
        AppIconReference = 23
    }

    private sealed record KnownExtension(
        string Extension,
        string Description,
        string? ContentType = null,
        string? PerceivedType = null);

    private sealed class ExtensionMetadata
    {
        public ExtensionMetadata(string extension)
        {
            Extension = extension;
        }

        public string Extension { get; }

        public string? Description { get; set; }

        public string? ContentType { get; set; }

        public string? PerceivedType { get; set; }
    }
}
