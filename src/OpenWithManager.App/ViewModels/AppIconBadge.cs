using System.Windows.Media;

namespace OpenWithManager.App.ViewModels;

public sealed record AppIconBadge(
    string Label,
    string Initial,
    ImageSource? Icon,
    bool IsOverflow = false);
