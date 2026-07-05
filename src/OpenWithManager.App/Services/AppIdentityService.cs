namespace OpenWithManager.App.Services;

public static class AppIdentityService
{
    public static string NormalizeAppName(string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return "";
        }

        var normalized = new string(appName.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return NormalizeKnownInboxAppName(normalized);
    }

    private static string NormalizeKnownInboxAppName(string normalized)
    {
        if (normalized.EndsWith("photos", StringComparison.Ordinal)
            || normalized.Contains("照片", StringComparison.Ordinal))
        {
            return "photos";
        }

        if (normalized is "paint" or "mspaint"
            || normalized.EndsWith("paint", StringComparison.Ordinal)
            || normalized.Contains("画图", StringComparison.Ordinal))
        {
            return "paint";
        }

        if (normalized.Contains("snippingtool", StringComparison.Ordinal)
            || normalized.Contains("snipandsketch", StringComparison.Ordinal)
            || normalized.Contains("screenclip", StringComparison.Ordinal)
            || normalized.Contains("截图工具", StringComparison.Ordinal)
            || normalized.Contains("截图", StringComparison.Ordinal))
        {
            return "snippingtool";
        }

        return normalized;
    }
}
