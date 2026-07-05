using System.IO;
using System.Text.Json;
using OpenWithManager.App.Models;

namespace OpenWithManager.App.Services;

public sealed class ExportImportService
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public void Export(string path, IReadOnlyCollection<FileAssociationItem> associations)
    {
        var snapshot = new AssociationSnapshot(
            DateTimeOffset.Now,
            Environment.MachineName,
            associations.Select(item => item with { Icon = null }).ToList());

        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, _jsonOptions));
    }

    public List<FileAssociationItem> Import(string path)
    {
        var text = File.ReadAllText(path);
        var snapshot = JsonSerializer.Deserialize<AssociationSnapshot>(text, _jsonOptions)
            ?? throw new InvalidOperationException("The selected file is not a valid association snapshot.");

        return snapshot.Associations.ToList();
    }

    public List<AssociationDiffItem> Compare(
        IReadOnlyCollection<FileAssociationItem> current,
        IReadOnlyCollection<FileAssociationItem> imported)
    {
        var currentByExtension = current.ToDictionary(item => item.Extension, StringComparer.OrdinalIgnoreCase);

        return imported
            .Select(item =>
            {
                currentByExtension.TryGetValue(item.Extension, out var currentItem);
                var status = currentItem is null
                    ? "Missing locally"
                    : string.Equals(currentItem.ProgId, item.ProgId, StringComparison.OrdinalIgnoreCase)
                        ? "Same"
                        : "Different";

                return new AssociationDiffItem(item.Extension, currentItem?.ProgId, item.ProgId, status);
            })
            .OrderBy(item => item.Status)
            .ThenBy(item => item.Extension)
            .ToList();
    }

    private sealed record AssociationSnapshot(
        DateTimeOffset ExportedAt,
        string MachineName,
        IReadOnlyCollection<FileAssociationItem> Associations);
}
