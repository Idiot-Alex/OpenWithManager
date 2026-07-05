using System.Diagnostics;

namespace OpenWithManager.App.Services;

public sealed class WindowsSettingsService
{
    private static readonly HashSet<string> SupportedDefaultAppParameters = new(StringComparer.Ordinal)
    {
        "registeredAppMachine",
        "registeredAppUser",
        "registeredAUMID"
    };

    public void OpenDefaultApps(string? parameterName = null, string? parameterValue = null)
    {
        var target = "ms-settings:defaultapps";
        if (parameterName is not null
            && parameterValue is not null
            && SupportedDefaultAppParameters.Contains(parameterName)
            && !string.IsNullOrWhiteSpace(parameterValue))
        {
            target = $"{target}?{parameterName}={Uri.EscapeDataString(parameterValue)}";
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }
}
