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
            SettingsParameterValue = settingsTarget?.SettingsParameterValue ?? selected.SettingsParameterValue,
            Diagnostic = FormatMergeDiagnostic(group, selected, iconTarget, settingsTarget)
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

        if (!string.IsNullOrWhiteSpace(candidate.Diagnostic))
        {
            parts.Add(candidate.Diagnostic);
        }

        return string.Join(Environment.NewLine, parts);
    }

    public static string FormatCompactDiagnostic(FormatAppCandidate candidate)
    {
        var values = new List<string> { candidate.Source, GetIdentityKey(candidate) };
        if (!string.IsNullOrWhiteSpace(candidate.ProgId))
        {
            values.Add(candidate.ProgId);
        }

        return string.Join("  |  ", values);
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

    private static string FormatMergeDiagnostic(
        IReadOnlyCollection<FormatAppCandidate> group,
        FormatAppCandidate selected,
        FormatAppCandidate? iconTarget,
        FormatAppCandidate? settingsTarget)
    {
        var parts = new List<string>
        {
            $"Merged: {group.Count}",
            $"Chosen name source: {selected.Source}"
        };

        var sources = group
            .Select(candidate => candidate.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source)
            .ToList();
        if (sources.Count > 0)
        {
            parts.Add($"Sources: {string.Join(", ", sources)}");
        }

        if (iconTarget is not null)
        {
            parts.Add($"Icon source: {iconTarget.Source}");
        }

        if (settingsTarget is not null)
        {
            parts.Add($"Settings source: {settingsTarget.Source}");
        }

        return string.Join(Environment.NewLine, parts);
    }
}
