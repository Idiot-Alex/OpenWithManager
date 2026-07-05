using OpenWithManager.App.Models;
using OpenWithManager.App.ViewModels;

namespace OpenWithManager.App.Services;

public sealed class FileKindDisplayService
{
    private readonly LocalizationService _text;
    private readonly AppIconService _icons;

    public FileKindDisplayService(LocalizationService text, AppIconService icons)
    {
        _text = text;
        _icons = icons;
    }

    public FileKindListItem CreateListItem(FileKindSummary kind)
    {
        return new FileKindListItem(
            kind,
            kind.DisplayName,
            FormatKindSummary(kind),
            BuildAppBadges(kind));
    }

    public string FormatKindSummary(FileKindSummary kind)
    {
        var formatCount = kind.Items.Count;
        var formatText = T(formatCount == 1 ? "oneFormat" : "formatCount", ("count", formatCount.ToString()));
        var hasMissing = kind.Items.Any(item => string.IsNullOrWhiteSpace(item.ProgId));
        if (hasMissing)
        {
            return $"{formatText} · {T("hasUnsetFormats")}";
        }

        var appCount = CountDistinctApps(kind);
        var appText = appCount <= 1
            ? DisplaySummaryAppName(kind.PrimaryAppName, kind)
            : T("appCount", ("count", appCount.ToString()));

        return $"{formatText} · {appText}";
    }

    public string DisplaySummaryAppName(string? name, FileKindSummary kind)
    {
        var displayName = DisplayAppName(name);
        return IsFileKindName(displayName, kind) ? T("defaultAppSet") : displayName;
    }

    public IReadOnlyCollection<AppIconBadge> BuildAppBadges(FileKindSummary kind)
    {
        var appGroups = kind.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.ProgId))
            .GroupBy(item => DisplayAppKey(item.FriendlyName ?? item.ProgId), StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                AppName = DisplaySummaryAppName(group.First().FriendlyName ?? group.First().ProgId, kind),
                Count = group.Count(),
                Icon = group.Select(item => item.Icon).FirstOrDefault(icon => icon is not null)
            })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.AppName)
            .ToList();

        var badges = appGroups
            .Take(3)
            .Select(group => new AppIconBadge(group.AppName, "", _icons.GetIcon(group.Icon)))
            .ToList();

        var remaining = appGroups.Count - badges.Count;
        if (remaining > 0)
        {
            badges.Add(new AppIconBadge(T("moreApps", ("count", remaining.ToString())), $"+{remaining}", null, true));
        }

        return badges;
    }

    private int CountDistinctApps(FileKindSummary kind)
    {
        return kind.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.ProgId))
            .Select(item => DisplayAppKey(item.FriendlyName ?? item.ProgId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private string DisplayAppName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) || name == "No default app" ? T("noDefaultApp") : name;
    }

    private static bool IsFileKindName(string name, FileKindSummary kind)
    {
        var normalizedName = NormalizeLabel(name);
        return normalizedName == NormalizeLabel(kind.DisplayName)
            || normalizedName == NormalizeLabel(kind.Description)
            || normalizedName == NormalizeLabel(kind.ShortName);
    }

    private static string NormalizeLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized.EndsWith('s') ? normalized[..^1] : normalized;
    }

    private static string DisplayAppKey(string? name)
    {
        return AppIdentityService.NormalizeAppName(name);
    }

    private string T(string key, params (string Key, string Value)[] values)
    {
        return _text.T(key, values);
    }
}
