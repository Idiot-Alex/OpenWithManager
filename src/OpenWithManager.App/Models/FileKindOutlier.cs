namespace OpenWithManager.App.Models;

public sealed record FileKindOutlier(
    string Extension,
    string Description,
    string? AppName,
    string? ProgId,
    string Source);
