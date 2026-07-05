namespace OpenWithManager.App.Models;

public sealed record FileKindSummary(
    string Id,
    string DisplayName,
    string ShortName,
    string Description,
    IReadOnlyCollection<string> Extensions,
    string? PrimaryAppName,
    string? PrimaryProgId,
    AppIconLocation? PrimaryIcon,
    int MatchingFormats,
    int TotalFormats,
    FileKindStatus Status,
    string StatusText,
    IReadOnlyCollection<FileKindOutlier> Outliers,
    IReadOnlyCollection<FileAssociationItem> Items);
