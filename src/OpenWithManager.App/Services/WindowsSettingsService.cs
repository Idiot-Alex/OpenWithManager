using System.Diagnostics;

namespace OpenWithManager.App.Services;

public sealed class WindowsSettingsService
{
    public void OpenDefaultApps(string? extension = null)
    {
        // Windows does not expose a stable public deep link for every extension picker.
        // Keep this conservative and open the supported default apps settings page.
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:defaultapps",
            UseShellExecute = true
        });
    }
}
