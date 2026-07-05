namespace OpenWithManager.App.Models;

public sealed record FormatAppCandidate(
    string AppName,
    string? ProgId,
    string Source,
    bool IsCurrent,
    string? SettingsParameterName = null,
    string? SettingsParameterValue = null,
    string? ShellHandlerId = null,
    bool CanMakeDefault = false);
