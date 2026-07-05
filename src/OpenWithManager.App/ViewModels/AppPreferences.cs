namespace OpenWithManager.App.ViewModels;

public sealed class AppPreferences
{
    public bool IsChinese { get; set; } = true;
    public bool ShowTechnicalDetails { get; set; }
    public bool AutoRefreshAfterSettings { get; set; } = true;
    public bool ShowCandidateSources { get; set; }
}
