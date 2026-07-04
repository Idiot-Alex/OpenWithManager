namespace OpenWithManager.App.Models;

public sealed record FormatCandidateResult(
    string Extension,
    string Description,
    FormatAppCandidate? Current,
    IReadOnlyCollection<FormatAppCandidate> Candidates);
