using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OpenWithManager.App.Models;
using Microsoft.Win32;

namespace OpenWithManager.App.Services;

public sealed class FileAssociationService
{
    private static readonly Guid IidIUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly ConcurrentDictionary<string, bool> ApplicationEvidenceCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> ProgIdEvidenceCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> ShellApplicationEvidenceCache = new(StringComparer.OrdinalIgnoreCase);
    private const string AppsFolderPrefix = @"shell:AppsFolder\";
    private const string AppsFolderGuidPrefix = @"shell:::{4234d49b-0245-4df3-b780-3893943456e1}\";

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
        return EnumerateKnownAssociations()
            .OrderBy(item => item.Category)
            .ThenBy(item => item.Extension)
            .ToList();
    }

    public IEnumerable<FileAssociationItem> EnumerateKnownAssociations()
    {
        foreach (var extension in DiscoverExtensions().OrderBy(item => item.Extension, StringComparer.OrdinalIgnoreCase))
        {
            yield return ReadAssociation(extension);
        }
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

        AddKnownClassesRootExtensions(extensions);
        AddCurrentUserExtensions(extensions);
        return extensions.Values.ToList();
    }

    private static void AddKnownClassesRootExtensions(IDictionary<string, ExtensionMetadata> extensions)
    {
        var knownExtensions = SeedKnownExtensions
            .Select(extension => extension.Extension)
            .Concat(FileFormatClassifier.KnownExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var extension in knownExtensions)
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
                using var key = SafeOpenSubKey(Registry.ClassesRoot, extension);
                AddOrUpdateExtension(
                    extensions,
                    extension,
                    key?.GetValue(null) as string,
                    key?.GetValue("Content Type") as string,
                    key?.GetValue("PerceivedType") as string);
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
        if (!IsUsableAppName(value, documentName, description))
        {
            return null;
        }

        return value;
    }

    public static bool IsUsableAppName(string? value, string? documentName = null, string? description = null)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !IsPlaceholderAppName(value)
            && !IsDocumentName(value, documentName, description);
    }

    private static bool IsDocumentName(string value, string? documentName, string? description)
    {
        var candidate = NormalizeName(value);
        return IsSameOrNestedDocumentName(candidate, NormalizeName(documentName))
            || IsSameOrNestedDocumentName(candidate, NormalizeName(description));
    }

    private static bool IsSameOrNestedDocumentName(string candidate, string documentName)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(documentName))
        {
            return false;
        }

        return candidate == documentName
            || candidate.Length >= 8 && documentName.Contains(candidate, StringComparison.Ordinal)
            || documentName.Length >= 8 && candidate.Contains(documentName, StringComparison.Ordinal);
    }

    private static bool IsPlaceholderAppName(string value)
    {
        var normalized = NormalizeName(value);
        if (normalized is "pickanapp")
        {
            return true;
        }

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

        return ReadRegistryApplicationIconLocation($@"{progId}\Application")
            ?? ReadRegistryOpenCommandIconLocation($@"{progId}\shell\open\command")
            ?? ReadRegistryIconLocation($@"{progId}\DefaultIcon")
            ?? ReadRegistryShellApplicationIconLocation($@"{progId}\Application");
    }

    public static AppIconLocation? ReadCurrentAppIconLocation(string extension, string? progId)
    {
        return ParseIconLocation(ReadRawAssociationString(extension, AssocString.AppIconReference))
            ?? ReadProgIdApplicationIconLocation(progId)
            ?? ParseCommandLocation(ReadRawAssociationString(extension, AssocString.Executable))
            ?? ReadIconLocation(progId)
            ?? ReadRegistryShellApplicationIconLocation(progId is null ? null : $@"{progId}\Application")
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
        return ParseIconLocation(capabilities?.GetValue("ApplicationIcon") as string)
            ?? ReadIconLocation(progId)
            ?? ReadShellApplicationIconLocation(registeredApplicationName);
    }

    public static bool HasApplicationEvidence(string executableName)
    {
        return !string.IsNullOrWhiteSpace(executableName)
            && ApplicationEvidenceCache.GetOrAdd(executableName, name => ResolveApplicationExecutablePath(name) is not null);
    }

    public static bool HasProgIdApplicationEvidence(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            return false;
        }

        return ProgIdEvidenceCache.GetOrAdd(progId, HasProgIdApplicationEvidenceCore);
    }

    public static bool HasProgIdRegisteredApplicationEvidence(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            return false;
        }

        if (progId.StartsWith(@"Applications\", StringComparison.OrdinalIgnoreCase))
        {
            return HasApplicationEvidence(progId[@"Applications\".Length..]);
        }

        using var applicationKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\Application");
        var appName = ResolveDisplayName(applicationKey?.GetValue("ApplicationName") as string);
        var appUserModelId = applicationKey?.GetValue("AppUserModelID") as string
            ?? applicationKey?.GetValue("AppUserModelId") as string;

        return !string.IsNullOrWhiteSpace(appName)
            || IsInstalledShellApplication(appUserModelId);
    }

    public static string? ReadInstalledProgIdApplicationName(string? progId)
    {
        if (!HasProgIdRegisteredApplicationEvidence(progId))
        {
            return null;
        }

        if (progId!.StartsWith(@"Applications\", StringComparison.OrdinalIgnoreCase))
        {
            var executableName = progId[@"Applications\".Length..];
            return ReadExecutableDisplayName(ResolveApplicationExecutablePath(executableName))
                ?? Path.GetFileNameWithoutExtension(executableName);
        }

        using var applicationKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\Application");
        return ResolveDisplayName(applicationKey?.GetValue("ApplicationName") as string)
            ?? ReadExecutableDisplayName(ResolveProgIdExecutablePath(progId))
            ?? progId;
    }

    public static string? ReadVerifiedAppName(FileAssociationItem item)
    {
        return ReadVerifiedAppName(item.ProgId, item.Icon, item.FriendlyName, item.Description);
    }

    public static string? ReadVerifiedAppName(
        string? progId,
        AppIconLocation? icon,
        string? fallbackName,
        string? description)
    {
        var installedAppName = ReadInstalledProgIdApplicationName(progId);
        if (IsUsableAppName(installedAppName, description, description))
        {
            return installedAppName;
        }

        if (!string.IsNullOrWhiteSpace(progId)
            && (HasProgIdApplicationEvidence(progId) || IsResolvableIconLocation(icon))
            && IsUsableAppName(fallbackName, description, description))
        {
            return fallbackName;
        }

        return null;
    }

    private static bool HasProgIdApplicationEvidenceCore(string progId)
    {
        if (progId.StartsWith(@"Applications\", StringComparison.OrdinalIgnoreCase))
        {
            return HasApplicationEvidence(progId[@"Applications\".Length..]);
        }

        return ReadRegistryShellApplicationIconLocation($@"{progId}\Application") is not null
            || ResolveProgIdExecutablePath(progId) is not null;
    }

    public static bool HasRegisteredApplicationEvidence(
        RegistryKey hive,
        string capabilitiesPath,
        string registeredApplicationName,
        string? progId)
    {
        using var capabilities = hive.OpenSubKey(capabilitiesPath);
        if (capabilities is null)
        {
            return false;
        }

        return ReadShellApplicationIconLocation(registeredApplicationName) is not null
            || HasProgIdApplicationEvidence(progId)
            || IsResolvableIconLocation(ParseIconLocation(capabilities.GetValue("ApplicationIcon") as string));
    }

    public static AppIconLocation? ReadShellApplicationIconLocation(string? appUserModelId)
    {
        return IsInstalledShellApplication(appUserModelId)
            ? new AppIconLocation($@"shell:AppsFolder\{appUserModelId}")
            : null;
    }

    public static bool IsInstalledShellApplication(string? appUserModelId)
    {
        return !string.IsNullOrWhiteSpace(appUserModelId)
            && appUserModelId.Contains('!')
            && ShellApplicationEvidenceCache.GetOrAdd(appUserModelId, CanCreateAppsFolderItem);
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

    private static AppIconLocation? ReadProgIdApplicationIconLocation(string? progId)
    {
        return string.IsNullOrWhiteSpace(progId)
            ? null
            : ReadRegistryApplicationIconLocation($@"{progId}\Application");
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

    private static string? ResolveApplicationExecutablePath(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        return ReadAppPathExecutablePath(Registry.CurrentUser, executableName)
            ?? ReadAppPathExecutablePath(Registry.LocalMachine, executableName)
            ?? ResolveApplicationsExecutablePath(executableName)
            ?? ResolveExecutablePath(executableName);
    }

    private static string? ResolveProgIdExecutablePath(string progId)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        return ResolveExecutablePath(ParseCommandLocation(key?.GetValue(null) as string)?.Path ?? "");
    }

    private static string? ResolveApplicationsExecutablePath(string executableName)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"Applications\{executableName}\shell\open\command");
        return ResolveExecutablePath(ParseCommandLocation(key?.GetValue(null) as string)?.Path ?? "");
    }

    private static string? ReadAppPathExecutablePath(RegistryKey hive, string executableName)
    {
        using var key = hive.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
        return ResolveExecutablePath(ParseCommandLocation(key?.GetValue(null) as string)?.Path ?? "");
    }

    private static string? ResolveExecutablePath(string value)
    {
        var path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Path.IsPathRooted(path))
        {
            return File.Exists(path) ? path : null;
        }

        var systemPath = Path.Combine(Environment.SystemDirectory, path);
        if (File.Exists(systemPath))
        {
            return systemPath;
        }

        var windowsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
        if (File.Exists(windowsPath))
        {
            return windowsPath;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(directory.Trim(), path);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    public static bool IsResolvableIconLocation(AppIconLocation? location)
    {
        if (location is null)
        {
            return false;
        }

        var path = Environment.ExpandEnvironmentVariables(location.Path.Trim().Trim('"'));
        if (path.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return IsInstalledShellApplication(path[AppsFolderPrefix.Length..]);
        }

        if (path.StartsWith(AppsFolderGuidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return IsInstalledShellApplication(path[AppsFolderGuidPrefix.Length..]);
        }

        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.StartsWith("@{", StringComparison.Ordinal))
        {
            return IsInstalledPackagedResource(path);
        }

        if (path.StartsWith('@') && !path.StartsWith("@{", StringComparison.Ordinal))
        {
            path = path[1..].Trim().Trim('"');
        }

        if (Path.IsPathRooted(path))
        {
            return File.Exists(path);
        }

        return File.Exists(Path.Combine(Environment.SystemDirectory, path))
            || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path));
    }

    private static bool IsInstalledPackagedResource(string reference)
    {
        var content = reference.Trim();
        if (!content.StartsWith("@{", StringComparison.Ordinal) || !content.EndsWith('}'))
        {
            return false;
        }

        content = content[2..^1];
        var queryIndex = content.IndexOf('?');
        if (queryIndex <= 0)
        {
            return false;
        }

        return FindPackageRoot(content[..queryIndex]) is not null;
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
        return Directory.Exists(exactPath) ? exactPath : null;
    }

    private static IEnumerable<string?> ReadPackageRootCandidates(string packageFullName)
    {
        using var packageKey = Registry.ClassesRoot.OpenSubKey(
            $@"Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages\{packageFullName}");

        yield return packageKey?.GetValue("Path") as string;
        yield return packageKey?.GetValue("PackageRootFolder") as string;
        yield return packageKey?.GetValue("InstallLocation") as string;
    }

    private static bool CanCreateAppsFolderItem(string appUserModelId)
    {
        return CanCreateShellItem($"{AppsFolderPrefix}{appUserModelId}")
            || CanCreateShellItem($"{AppsFolderGuidPrefix}{appUserModelId}");
    }

    private static bool CanCreateShellItem(string parsingName)
    {
        var iid = IidIUnknown;
        var result = SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var shellItem);
        if (result < 0 || shellItem is null)
        {
            return false;
        }

        Marshal.ReleaseComObject(shellItem);
        return true;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object? shellItem);

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
