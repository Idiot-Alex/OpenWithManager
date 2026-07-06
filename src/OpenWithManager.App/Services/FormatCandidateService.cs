using OpenWithManager.App.Models;
using Microsoft.Win32;

namespace OpenWithManager.App.Services;

public sealed class FormatCandidateService
{
    private readonly FileAssociationService _fileAssociations;
    private readonly ShellAssociationService _shellAssociations;

    public FormatCandidateService(FileAssociationService fileAssociations, ShellAssociationService shellAssociations)
    {
        _fileAssociations = fileAssociations;
        _shellAssociations = shellAssociations;
    }

    public FormatCandidateResult GetCandidates(string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        var currentItem = _fileAssociations.GetAssociation(normalizedExtension);

        var candidates = new List<FormatAppCandidate>();
        var currentAppName = FileAssociationService.ReadVerifiedAppName(currentItem);
        if (!string.IsNullOrWhiteSpace(currentItem.ProgId)
            && FileAssociationService.HasProgIdApplicationEvidence(currentItem.ProgId)
            && FileAssociationService.IsUsableAppName(currentAppName, currentItem.Description, currentItem.Description))
        {
            candidates.Add(new FormatAppCandidate(
                currentAppName!,
                currentItem.ProgId,
                currentItem.Icon,
                "Current",
                true));
        }

        candidates.AddRange(_shellAssociations.GetHandlers(normalizedExtension));
        candidates.AddRange(ReadOpenWithProgIds(normalizedExtension, currentItem.ProgId, currentItem.Description));
        candidates.AddRange(ReadOpenWithList(normalizedExtension));
        candidates.AddRange(ReadRegisteredApplications(normalizedExtension, currentItem.ProgId));

        var distinctCandidates = candidates
            .Where(candidate => FileAssociationService.IsUsableAppName(candidate.AppName, currentItem.Description, currentItem.Description))
            .GroupBy(CandidateKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var selected = group
                    .OrderByDescending(candidate => candidate.IsCurrent)
                    .ThenBy(SourcePriority)
                    .First();
                var settingsTarget = group.FirstOrDefault(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.SettingsParameterName)
                    && !string.IsNullOrWhiteSpace(candidate.SettingsParameterValue));
                var iconTarget = group
                    .Where(candidate => candidate.Icon is not null)
                    .OrderBy(IconSourcePriority)
                    .ThenBy(SourcePriority)
                    .FirstOrDefault();

                return selected with
                {
                    Icon = iconTarget?.Icon ?? selected.Icon,
                    SettingsParameterName = settingsTarget?.SettingsParameterName ?? selected.SettingsParameterName,
                    SettingsParameterValue = settingsTarget?.SettingsParameterValue ?? selected.SettingsParameterValue
                };
            })
            .OrderByDescending(candidate => candidate.IsCurrent)
            .ThenBy(SourcePriority)
            .ThenBy(candidate => candidate.AppName)
            .ToList();

        return new FormatCandidateResult(
            normalizedExtension,
            currentItem.Description,
            distinctCandidates.FirstOrDefault(candidate => candidate.IsCurrent),
            distinctCandidates);
    }

    private static IEnumerable<FormatAppCandidate> ReadOpenWithProgIds(string extension, string? currentProgId, string description)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\OpenWithProgids");

        if (key is null)
        {
            yield break;
        }

        foreach (var progId in key.GetValueNames().Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!string.Equals(progId, currentProgId, StringComparison.OrdinalIgnoreCase)
                && !FileAssociationService.HasProgIdRegisteredApplicationEvidence(progId))
            {
                continue;
            }

            var icon = FileAssociationService.ReadIconLocation(progId);
            var appName = FileAssociationService.ReadVerifiedAppName(
                progId,
                icon,
                FileAssociationService.ReadFriendlyName(progId),
                description);
            if (!FileAssociationService.IsUsableAppName(appName))
            {
                continue;
            }

            yield return new FormatAppCandidate(
                appName!,
                progId,
                icon,
                "OpenWithProgids",
                string.Equals(progId, currentProgId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static IEnumerable<FormatAppCandidate> ReadOpenWithList(string extension)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\OpenWithList");

        if (key is null)
        {
            yield break;
        }

        foreach (var valueName in key.GetValueNames().Where(name => !string.Equals(name, "MRUList", StringComparison.OrdinalIgnoreCase)))
        {
            var executableName = key.GetValue(valueName) as string;
            if (string.IsNullOrWhiteSpace(executableName))
            {
                continue;
            }

            if (!FileAssociationService.HasApplicationEvidence(executableName))
            {
                continue;
            }

            var appName = ReadApplicationName(executableName) ?? executableName;
            yield return new FormatAppCandidate(
                appName,
                null,
                FileAssociationService.ReadApplicationIconLocation(executableName),
                "OpenWithList",
                false);
        }
    }

    private static IEnumerable<FormatAppCandidate> ReadRegisteredApplications(string extension, string? currentProgId)
    {
        foreach (var candidate in ReadRegisteredApplications(Registry.CurrentUser, "registeredAppUser", extension, currentProgId))
        {
            yield return candidate;
        }

        foreach (var candidate in ReadRegisteredApplications(Registry.LocalMachine, "registeredAppMachine", extension, currentProgId))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<FormatAppCandidate> ReadRegisteredApplications(
        RegistryKey hive,
        string settingsParameterName,
        string extension,
        string? currentProgId)
    {
        using var registeredApplications = hive.OpenSubKey(@"Software\RegisteredApplications");
        if (registeredApplications is null)
        {
            yield break;
        }

        foreach (var valueName in registeredApplications.GetValueNames())
        {
            var capabilitiesPath = registeredApplications.GetValue(valueName) as string;
            if (string.IsNullOrWhiteSpace(capabilitiesPath))
            {
                continue;
            }

            using var capabilities = hive.OpenSubKey(capabilitiesPath);
            using var associations = hive.OpenSubKey($@"{capabilitiesPath}\FileAssociations");
            if (capabilities is null)
            {
                continue;
            }

            var progId = associations?.GetValue(extension) as string;
            if (string.IsNullOrWhiteSpace(progId))
            {
                continue;
            }

            if (!string.Equals(progId, currentProgId, StringComparison.OrdinalIgnoreCase)
                && !FileAssociationService.HasRegisteredApplicationEvidence(hive, capabilitiesPath, valueName, progId))
            {
                continue;
            }

            var appName = FileAssociationService.ResolveDisplayName(capabilities?.GetValue("ApplicationName") as string)
                ?? FileAssociationService.ReadFriendlyName(progId)
                ?? valueName;

            yield return new FormatAppCandidate(
                appName,
                progId,
                FileAssociationService.ReadRegisteredApplicationIconLocation(hive, capabilitiesPath, valueName, progId),
                "RegisteredApplication",
                string.Equals(progId, currentProgId, StringComparison.OrdinalIgnoreCase),
                settingsParameterName,
                valueName);
        }
    }

    private static string? ReadApplicationName(string executableName)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"Applications\{executableName}\Application");
        return FileAssociationService.ResolveDisplayName(key?.GetValue("ApplicationName") as string);
    }

    private static string CandidateKey(FormatAppCandidate candidate)
    {
        var iconIdentity = NormalizeIconIdentity(candidate.Icon);
        if (!string.IsNullOrWhiteSpace(iconIdentity))
        {
            return $"icon:{iconIdentity}";
        }

        var appIdentity = AppIdentityService.NormalizeAppName(candidate.AppName);
        if (!string.IsNullOrWhiteSpace(appIdentity))
        {
            return $"app:{appIdentity}";
        }

        return !string.IsNullOrWhiteSpace(candidate.ProgId) ? $"prog:{candidate.ProgId}" : "";
    }

    private static string NormalizeIconIdentity(AppIconLocation? icon)
    {
        if (icon is null || string.IsNullOrWhiteSpace(icon.Path))
        {
            return "";
        }

        var path = Environment.ExpandEnvironmentVariables(icon.Path.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(path) ? "" : $"{path.ToLowerInvariant()}#{icon.Index}";
    }

    private static int IconSourcePriority(FormatAppCandidate candidate)
    {
        return candidate.Source switch
        {
            "ShellRecommended" => 0,
            "RegisteredApplication" => 1,
            "Current" => 2,
            "OpenWithList" => 3,
            "OpenWithProgids" => 4,
            _ => 6
        };
    }

    private static int SourcePriority(FormatAppCandidate candidate)
    {
        return candidate.Source switch
        {
            "Current" => 0,
            "ShellRecommended" => 1,
            "RegisteredApplication" => 2,
            "OpenWithProgids" => 3,
            "OpenWithList" => 4,
            _ => 6
        };
    }

    private static string NormalizeExtension(string extension)
    {
        var value = extension.Trim();
        return value.StartsWith('.') ? value.ToLowerInvariant() : $".{value.ToLowerInvariant()}";
    }
}
