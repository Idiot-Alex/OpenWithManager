using OpenWithManager.App.Models;

namespace OpenWithManager.App.Services;

public static class AppCandidateIdentityService
{
    public static string GetIdentityKey(FormatAppCandidate candidate)
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

    public static FormatAppCandidate MergeCandidates(
        IEnumerable<FormatAppCandidate> candidates,
        Func<FormatAppCandidate, int> sourcePriority,
        Func<FormatAppCandidate, int> iconPriority)
    {
        var group = candidates.ToList();
        var selected = group
            .OrderByDescending(candidate => candidate.IsCurrent)
            .ThenBy(sourcePriority)
            .ThenBy(candidate => IsTechnicalName(candidate.AppName))
            .First();

        var settingsTarget = group.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.SettingsParameterName)
            && !string.IsNullOrWhiteSpace(candidate.SettingsParameterValue));

        var iconTarget = group
            .Where(candidate => candidate.Icon is not null)
            .OrderBy(iconPriority)
            .ThenBy(sourcePriority)
            .FirstOrDefault();

        return selected with
        {
            Icon = iconTarget?.Icon ?? selected.Icon,
            SettingsParameterName = settingsTarget?.SettingsParameterName ?? selected.SettingsParameterName,
            SettingsParameterValue = settingsTarget?.SettingsParameterValue ?? selected.SettingsParameterValue
        };
    }

    public static string FormatDiagnostic(FormatAppCandidate candidate)
    {
        var parts = new List<string>
        {
            $"Source: {candidate.Source}",
            $"Identity: {GetIdentityKey(candidate)}"
        };

        if (!string.IsNullOrWhiteSpace(candidate.ProgId))
        {
            parts.Add($"ProgID: {candidate.ProgId}");
        }

        var iconIdentity = NormalizeIconIdentity(candidate.Icon);
        if (!string.IsNullOrWhiteSpace(iconIdentity))
        {
            parts.Add($"Icon: {iconIdentity}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    public static string NormalizeIconIdentity(AppIconLocation? icon)
    {
        if (icon is null || string.IsNullOrWhiteSpace(icon.Path))
        {
            return "";
        }

        var path = Environment.ExpandEnvironmentVariables(icon.Path.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(path) ? "" : $"{path.ToLowerInvariant()}#{icon.Index}";
    }

    private static bool IsTechnicalName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Contains('_', StringComparison.Ordinal)
            || value.Contains('.', StringComparison.Ordinal)
            || value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }
}
