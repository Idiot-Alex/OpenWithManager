namespace OpenWithManager.App.Models;

public sealed record AssociationDiffItem(
    string Extension,
    string? CurrentProgId,
    string? ImportedProgId,
    string Status);
