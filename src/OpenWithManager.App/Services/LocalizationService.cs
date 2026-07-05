namespace OpenWithManager.App.Services;

public sealed class LocalizationService
{
    private static readonly Dictionary<string, string> En = new()
    {
        ["appTagline"] = "Choose apps for file kinds.",
        ["searchPlaceholder"] = "Search files or apps",
        ["refresh"] = "Refresh",
        ["fileKinds"] = "File kinds",
        ["emptyNoMatch"] = "No matching file kinds.",
        ["all"] = "All",
        ["needsReview"] = "Needs review",
        ["noFileKindSelected"] = "No file kind selected",
        ["pickFileKind"] = "Pick a file kind to see its current app.",
        ["fileKind"] = "File kind",
        ["format"] = "Format",
        ["backToFileKind"] = "Back",
        ["currentApp"] = "Current app",
        ["recommendedApps"] = "Recommended apps",
        ["recommended"] = "Recommended",
        ["availableApp"] = "Available",
        ["knownForFormat"] = "Known for this format",
        ["usedBefore"] = "Used before",
        ["loadingApps"] = "Finding apps for this format.",
        ["noCandidateApps"] = "No candidate apps found.",
        ["changeInWindows"] = "Change in Windows",
        ["openAppDefaults"] = "Open app defaults",
        ["formatSettingsHint"] = "Windows will ask you to choose {app} for {extension}.",
        ["appDefaultsHint"] = "Windows will open {app}'s default app page. Find {extension} there and confirm your choice.",
        ["formats"] = "Formats",
        ["openDefaultApps"] = "Open default apps",
        ["technicalDetails"] = "Technical details",
        ["noDefaultApp"] = "No default app",
        ["defaultAppSet"] = "default app set",
        ["oneFormat"] = "1 format",
        ["formatCount"] = "{count} formats",
        ["appCount"] = "{count} apps",
        ["hasUnsetFormats"] = "has unset formats",
        ["moreApps"] = "{count} more apps",
        ["readingDefaultsTitle"] = "Reading defaults",
        ["readingDefaultsBody"] = "Checking the apps Windows uses for your files.",
        ["loadFailedTitle"] = "Could not read defaults",
        ["current"] = "Current",
        ["none"] = "none",
        ["setAsDefault"] = "Set as default",
        ["confirmSetDefault"] = "Set {app} as the default app for {extension}? This will immediately change your Windows default app setting.",
        ["setDefaultToast"] = "{extension} now opens with {app}.",
        ["openSettings"] = "Open settings"
    };

    private static readonly Dictionary<string, string> Zh = new()
    {
        ["appTagline"] = "为文件类型选择打开应用。",
        ["searchPlaceholder"] = "搜索文件或应用",
        ["refresh"] = "刷新",
        ["fileKinds"] = "文件类型",
        ["emptyNoMatch"] = "没有匹配的文件类型。",
        ["all"] = "全部",
        ["needsReview"] = "需检查",
        ["noFileKindSelected"] = "未选择文件类型",
        ["pickFileKind"] = "选择一种文件类型，查看当前打开应用。",
        ["fileKind"] = "文件类型",
        ["format"] = "格式",
        ["backToFileKind"] = "返回",
        ["currentApp"] = "当前使用",
        ["recommendedApps"] = "推荐应用",
        ["recommended"] = "推荐",
        ["availableApp"] = "可用",
        ["knownForFormat"] = "适合此格式",
        ["usedBefore"] = "曾经使用",
        ["loadingApps"] = "正在查找适合此格式的应用。",
        ["noCandidateApps"] = "没有找到候选应用。",
        ["changeInWindows"] = "在 Windows 中更改",
        ["openAppDefaults"] = "打开应用默认设置",
        ["formatSettingsHint"] = "Windows 会让你为 {extension} 选择 {app}。",
        ["appDefaultsHint"] = "Windows 会打开 {app} 的默认应用页面，请在其中找到 {extension} 并确认选择。",
        ["formats"] = "格式",
        ["openDefaultApps"] = "默认应用",
        ["technicalDetails"] = "技术明细",
        ["noDefaultApp"] = "未设置默认应用",
        ["defaultAppSet"] = "已设置默认应用",
        ["oneFormat"] = "1 种格式",
        ["formatCount"] = "{count} 种格式",
        ["appCount"] = "{count} 个应用",
        ["hasUnsetFormats"] = "有未设置",
        ["moreApps"] = "另有 {count} 个应用",
        ["readingDefaultsTitle"] = "正在读取默认项",
        ["readingDefaultsBody"] = "正在检查 Windows 用哪些应用打开你的文件。",
        ["loadFailedTitle"] = "无法读取默认项",
        ["current"] = "当前",
        ["none"] = "无",
        ["setAsDefault"] = "设为默认",
        ["confirmSetDefault"] = "要将 {extension} 的默认打开应用改为 {app} 吗？此操作会立即修改 Windows 默认应用设置。",
        ["setDefaultToast"] = "{extension} 现在将使用 {app} 打开。",
        ["openSettings"] = "打开设置"
    };

    public bool IsChinese { get; private set; } = true;

    public string LanguageLabel => IsChinese ? "中" : "EN";

    public void ToggleLanguage()
    {
        IsChinese = !IsChinese;
    }

    public string T(string key, params (string Key, string Value)[] values)
    {
        var dictionary = IsChinese ? Zh : En;
        var text = dictionary.TryGetValue(key, out var template) ? template : key;
        foreach (var (name, replacement) in values)
        {
            text = text.Replace($"{{{name}}}", replacement);
        }

        return text;
    }
}
