using OpenWithManager.App.Models;

namespace OpenWithManager.App.ViewModels;

public sealed record FileKindListItem(
    FileKindSummary Kind,
    string DisplayName,
    string SummaryText,
    IReadOnlyCollection<AppIconBadge> Badges);
