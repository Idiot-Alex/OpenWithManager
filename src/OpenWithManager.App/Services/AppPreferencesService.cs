using System.IO;
using System.Text.Json;
using OpenWithManager.App.ViewModels;

namespace OpenWithManager.App.Services;

public static class AppPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppPreferences Load()
    {
        try
        {
            var path = PreferencesPath();
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path)) ?? new AppPreferences()
                : new AppPreferences();
        }
        catch
        {
            return new AppPreferences();
        }
    }

    public static void Save(AppPreferences preferences)
    {
        try
        {
            var path = PreferencesPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(preferences, JsonOptions));
        }
        catch
        {
            // Preferences should never block the main default-app workflow.
        }
    }

    private static string PreferencesPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenWithManager",
            "preferences.json");
    }
}
