namespace OpenWithManager.App.Models;

public sealed record FileKindSummary(
    string Id,
    string DisplayName,
    string ShortName,
    string Description,
    IReadOnlyCollection<string> Extensions,
    string? PrimaryAppName,
    string? PrimaryProgId,
    int MatchingFormats,
    int TotalFormats,
    string Status,
    IReadOnlyCollection<FileKindOutlier> Outliers,
    IReadOnlyCollection<FileAssociationItem> Items);
