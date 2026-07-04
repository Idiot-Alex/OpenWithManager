using OpenWithManager.App.Models;

namespace OpenWithManager.App.Services;

public sealed class FileKindService
{
    private readonly FileAssociationService _fileAssociations;

    private static readonly FileKindProfile[] Profiles =
    [
        new(
            "images",
            "Photos and images",
            "Images",
            "Pictures, screenshots, and image assets.",
            [".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"]),
        new(
            "videos",
            "Videos",
            "Videos",
            "Movies, clips, and screen recordings.",
            [".mp4", ".mov", ".mkv"]),
        new(
            "music",
            "Music and audio",
            "Audio",
            "Songs, recordings, and sound files.",
            [".mp3", ".wav"]),
        new(
            "pdf",
            "PDF documents",
            "PDF",
            "Portable documents and forms.",
            [".pdf"]),
        new(
            "word",
            "Word documents",
            "Word",
            "Microsoft Word documents.",
            [".docx"]),
        new(
            "spreadsheets",
            "Spreadsheets",
            "Sheet",
            "Excel workbooks and spreadsheet files.",
            [".xlsx"]),
        new(
            "presentations",
            "Presentations",
            "Deck",
            "PowerPoint presentation files.",
            [".pptx"]),
        new(
            "notes",
            "Text and notes",
            "Text",
            "Plain text and Markdown notes.",
            [".txt", ".md"]),
        new(
            "archives",
            "Compressed files",
            "Archives",
            "Zip, 7-Zip, and other packaged files.",
            [".zip", ".rar", ".7z"]),
        new(
            "code",
            "Code files",
            "Code",
            "Developer files that usually open in an editor.",
            [".json", ".js", ".ts", ".cs", ".py"]),
        new(
            "web",
            "Web pages",
            "Web",
            "HTML files and pages saved from the web.",
            [".html", ".htm"])
    ];

    public FileKindService(FileAssociationService fileAssociations)
    {
        _fileAssociations = fileAssociations;
    }

    public List<FileKindSummary> GetFileKinds()
    {
        var associations = _fileAssociations.GetKnownAssociations();
        var byExtension = associations.ToDictionary(item => item.Extension, StringComparer.OrdinalIgnoreCase);

        return Profiles
            .Select(profile => BuildSummary(profile, byExtension))
            .OrderBy(StatusPriority)
            .ThenBy(summary => summary.DisplayName)
            .ToList();
    }

    private static FileKindSummary BuildSummary(
        FileKindProfile profile,
        IReadOnlyDictionary<string, FileAssociationItem> associations)
    {
        var items = profile.Extensions
            .Select(extension => associations.TryGetValue(extension, out var item) ? item : null)
            .OfType<FileAssociationItem>()
            .ToList();

        var appGroups = items
            .GroupBy(item => DisplayAppName(item), StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                App = group.Key,
                Items = group.ToList()
            })
            .OrderByDescending(group => group.Items.Count)
            .ThenBy(group => group.App)
            .ToList();

        var primary = appGroups.FirstOrDefault();
        var primaryItem = primary?.Items.FirstOrDefault();
        var totalFormats = items.Count;
        var matchingFormats = primary?.Items.Count ?? 0;
        var missingFormats = items.Count(item => string.IsNullOrWhiteSpace(item.ProgId));
        var status = totalFormats == 0 || missingFormats == totalFormats
            ? "Missing"
            : missingFormats > 0 || appGroups.Count > 1
                ? "Mixed"
                : "Consistent";

        var outliers = items
            .Where(item => primary is null || !string.Equals(DisplayAppName(item), primary.App, StringComparison.OrdinalIgnoreCase))
            .Select(item => new FileKindOutlier(
                item.Extension,
                item.Description,
                DisplayAppName(item),
                item.ProgId,
                item.Source))
            .ToList();

        return new FileKindSummary(
            profile.Id,
            profile.DisplayName,
            profile.ShortName,
            profile.Description,
            profile.Extensions,
            primaryItem is null ? null : DisplayAppName(primaryItem),
            primaryItem?.ProgId,
            primaryItem?.IconDataUrl,
            matchingFormats,
            totalFormats,
            status,
            outliers,
            items);
    }

    private static string DisplayAppName(FileAssociationItem item)
    {
        return item.FriendlyName ?? item.ProgId ?? "No default app";
    }

    private static int StatusPriority(FileKindSummary summary)
    {
        return summary.Status switch
        {
            "Mixed" => 0,
            "Missing" => 1,
            _ => 2
        };
    }

    private sealed record FileKindProfile(
        string Id,
        string DisplayName,
        string ShortName,
        string Description,
        IReadOnlyCollection<string> Extensions);
}
