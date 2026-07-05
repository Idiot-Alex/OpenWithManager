namespace OpenWithManager.App.Models;

public sealed record FileAssociationItem(
    string Extension,
    string Category,
    string Description,
    string? ProgId,
    string? FriendlyName,
    AppIconLocation? Icon,
    string Source,
    string? ContentType = null,
    string? PerceivedType = null);
