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
        ["currentUsing"] = "Current · {app}",
        ["availableApps"] = "Available apps",
        ["pickFormat"] = "Select a format from the list to see apps and actions here.",
        ["formatTableFormat"] = "Format",
        ["formatTableDescription"] = "Description",
        ["formatTableCurrentApp"] = "Current app",
        ["selectedApp"] = "Selected app · {app}",
        ["formatActions"] = "Format actions",
        ["recommended"] = "Recommended",
        ["availableApp"] = "Available",
        ["knownForFormat"] = "Known for this format",
        ["usedBefore"] = "Used before",
        ["loadingApps"] = "Finding apps for this format.",
        ["noCandidateApps"] = "No candidate apps found.",
        ["changeInWindows"] = "Change in Windows",
        ["openAppDefaults"] = "Open app defaults",
        ["copyFormat"] = "Copy format",
        ["formatCopied"] = "{extension} copied.",
        ["settingsSearchHint"] = "Open Settings and search {extension}.",
        ["formatSettingsHint"] = "Windows does not provide a direct extension link here. Open Settings, paste {extension} in the file type search, then choose an app.",
        ["appDefaultsHint"] = "Windows will try to open {app}'s default app page. If it opens the default apps page instead, paste {extension} in the file type search.",
        ["formats"] = "Formats",
        ["formatDistribution"] = "Format distribution",
        ["openDefaultApps"] = "Open Windows defaults",
        ["technicalDetails"] = "Advanced info",
        ["technicalFormat"] = "Format",
        ["technicalCurrentApp"] = "Current app",
        ["technicalSource"] = "Source",
        ["sourceUserChoice"] = "User choice",
        ["sourceShell"] = "Shell",
        ["sourceRegistry"] = "Registry",
        ["sourceUnknown"] = "Unknown",
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
        ["preferences"] = "Preferences",
        ["preferencesSubtitle"] = "Display and behavior options.",
        ["close"] = "Close",
        ["preferencesDisplay"] = "Display",
        ["preferencesBehavior"] = "Behavior",
        ["preferencesSafety"] = "Safety",
        ["language"] = "Language",
        ["chinese"] = "Chinese",
        ["english"] = "English",
        ["showTechnicalDetails"] = "Show technical details by default",
        ["showCandidateSources"] = "Show candidate source labels",
        ["autoRefreshAfterSettings"] = "Refresh after returning from Windows Settings",
        ["preferencesSafetyBody"] = "OpenWith Manager reads default app associations and opens official Windows Settings pages. It does not write registry defaults or bypass Windows default-app protection."
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
        ["currentUsing"] = "当前使用 · {app}",
        ["availableApps"] = "可用应用",
        ["pickFormat"] = "从格式列表选择一种格式，这里会显示应用和操作。",
        ["formatTableFormat"] = "格式",
        ["formatTableDescription"] = "说明",
        ["formatTableCurrentApp"] = "当前应用",
        ["selectedApp"] = "准备选择 · {app}",
        ["formatActions"] = "格式操作",
        ["recommended"] = "推荐",
        ["availableApp"] = "可用",
        ["knownForFormat"] = "适合此格式",
        ["usedBefore"] = "曾经使用",
        ["loadingApps"] = "正在查找适合此格式的应用。",
        ["noCandidateApps"] = "没有找到候选应用。",
        ["changeInWindows"] = "在 Windows 中更改",
        ["openAppDefaults"] = "打开应用默认设置",
        ["copyFormat"] = "复制格式名",
        ["formatCopied"] = "已复制 {extension}。",
        ["settingsSearchHint"] = "打开设置后搜索 {extension}。",
        ["formatSettingsHint"] = "Windows 没有提供按扩展名直达候选列表的公开入口。打开设置后，将 {extension} 粘贴到文件类型搜索框，再选择应用。",
        ["appDefaultsHint"] = "Windows 会尝试打开 {app} 的默认应用页面。如果只打开默认应用页，请将 {extension} 粘贴到文件类型搜索框。",
        ["formats"] = "格式",
        ["formatDistribution"] = "格式分布",
        ["openDefaultApps"] = "打开 Windows 默认应用",
        ["technicalDetails"] = "高级信息",
        ["technicalFormat"] = "格式",
        ["technicalCurrentApp"] = "当前应用",
        ["technicalSource"] = "来源",
        ["sourceUserChoice"] = "用户选择",
        ["sourceShell"] = "Shell",
        ["sourceRegistry"] = "注册表",
        ["sourceUnknown"] = "未知",
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
        ["preferences"] = "偏好设置",
        ["preferencesSubtitle"] = "显示和行为选项。",
        ["close"] = "关闭",
        ["preferencesDisplay"] = "显示",
        ["preferencesBehavior"] = "行为",
        ["preferencesSafety"] = "安全",
        ["language"] = "语言",
        ["chinese"] = "中文",
        ["english"] = "English",
        ["showTechnicalDetails"] = "默认展开技术明细",
        ["showCandidateSources"] = "显示候选来源标签",
        ["autoRefreshAfterSettings"] = "从 Windows 设置返回后刷新",
        ["preferencesSafetyBody"] = "OpenWith Manager 只读取默认应用关联，并打开官方 Windows 设置页面。它不会写入注册表默认项，也不会绕过 Windows 默认应用保护。"
    };

    public bool IsChinese { get; private set; } = true;

    public void SetLanguage(bool isChinese)
    {
        IsChinese = isChinese;
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
