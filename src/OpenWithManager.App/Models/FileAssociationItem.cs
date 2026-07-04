namespace OpenWithManager.App.Models;

public sealed record FileAssociationItem(
    string Extension,
    string Category,
    string Description,
    string? ProgId,
    string? FriendlyName,
    string? IconDataUrl,
    string Source);
