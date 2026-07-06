using OpenWithManager.App.Models;

namespace OpenWithManager.App.Services;

public sealed class FileKindService
{
    private static readonly IReadOnlyDictionary<string, int> DefinitionOrder =
        FileFormatClassifier.KindDefinitions
            .Select((definition, index) => new { definition.Id, Index = index })
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.OrdinalIgnoreCase);

    private readonly FileAssociationService _fileAssociations;

    public FileKindService(FileAssociationService fileAssociations)
    {
        _fileAssociations = fileAssociations;
    }

    public List<FileKindSummary> GetFileKinds(IProgress<IReadOnlyList<FileKindSummary>>? progress = null)
    {
        var associations = new List<FileAssociationItem>();
        var count = 0;
        foreach (var association in _fileAssociations.EnumerateKnownAssociations())
        {
            associations.Add(association);
            count++;

            if (progress is not null && (count == 8 || count % 24 == 0))
            {
                progress.Report(BuildFileKinds(associations));
            }
        }

        var fileKinds = BuildFileKinds(associations);
        progress?.Report(fileKinds);
        return fileKinds;
    }

    private static List<FileKindSummary> BuildFileKinds(IReadOnlyCollection<FileAssociationItem> associations)
    {
        var byCategory = associations
            .GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.Extension, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        return FileFormatClassifier.KindDefinitions
            .Select(definition => BuildSummary(definition, GetCategoryItems(byCategory, definition.Id)))
            .Where(summary => summary.TotalFormats > 0)
            .OrderBy(summary => DefinitionOrder.TryGetValue(summary.Id, out var order) ? order : int.MaxValue)
            .ThenBy(summary => summary.DisplayName)
            .ToList();
    }

    private static IReadOnlyCollection<FileAssociationItem> GetCategoryItems(
        IReadOnlyDictionary<string, List<FileAssociationItem>> byCategory,
        string categoryId)
    {
        return byCategory.TryGetValue(categoryId, out var items)
            ? items
            : Array.Empty<FileAssociationItem>();
    }

    private static FileKindSummary BuildSummary(
        FileKindDefinition definition,
        IReadOnlyCollection<FileAssociationItem> associations)
    {
        var items = associations
            .OrderBy(item => item.Extension, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var extensions = items
            .Select(item => item.Extension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
            ? FileKindStatus.Missing
            : missingFormats > 0 || appGroups.Count > 1
                ? FileKindStatus.Mixed
                : FileKindStatus.Consistent;

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
            definition.Id,
            definition.DisplayName,
            definition.ShortName,
            definition.Description,
            extensions,
            primaryItem is null ? null : DisplayAppName(primaryItem),
            primaryItem?.ProgId,
            matchingFormats,
            totalFormats,
            status,
            outliers,
            items);
    }

    private static string DisplayAppName(FileAssociationItem item)
    {
        return FileAssociationService.ReadVerifiedAppName(item) ?? "No default app";
    }
}
