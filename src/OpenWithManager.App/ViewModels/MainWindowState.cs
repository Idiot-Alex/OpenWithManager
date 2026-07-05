using OpenWithManager.App.Models;

namespace OpenWithManager.App.ViewModels;

public sealed class MainWindowState
{
    public List<FileKindSummary> AllKinds { get; set; } = [];
    public string Status { get; set; } = "All";
    public string Query { get; set; } = "";
    public FileKindSummary? SelectedKind { get; set; }
    public FormatCandidateResult? SelectedFormat { get; set; }
    public FormatAppCandidate? SelectedCandidate { get; set; }
    public bool IsLoading { get; set; }
    public string? LoadError { get; set; }
}
