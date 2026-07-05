using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenWithManager.App.Models;
using OpenWithManager.App.Services;
using Microsoft.Win32;

namespace OpenWithManager.App;

public partial class MainWindow : Window
{
    private readonly FileAssociationService _fileAssociations = new();
    private readonly ShellAssociationService _shellAssociations = new();
    private readonly FileKindService _fileKinds;
    private readonly FormatCandidateService _formatCandidates;
    private readonly WindowsSettingsService _settings = new();
    private readonly ExportImportService _exports = new();
    private readonly ObservableCollection<FileKindSummary> _visibleKinds = [];

    private List<FileKindSummary> _allKinds = [];
    private string _status = "All";
    private string _query = "";
    private FileKindSummary? _selectedKind;
    private FormatCandidateResult? _selectedFormat;
    private FormatAppCandidate? _selectedCandidate;
    private bool _isChinese = true;
    private bool _isLoading;
    private string? _loadError;

    public MainWindow()
    {
        _fileKinds = new FileKindService(_fileAssociations);
        _formatCandidates = new FormatCandidateService(_fileAssociations, _shellAssociations);
        InitializeComponent();
        UpdateStaticText();
        KindList.ItemsSource = _visibleKinds;
        Loaded += async (_, _) => await LoadFileKindsAsync();
    }

    private async Task LoadFileKindsAsync()
    {
        _isLoading = true;
        _loadError = null;
        _selectedFormat = null;
        _selectedCandidate = null;
        RenderLoading();

        try
        {
            var kinds = await Task.Run(_fileKinds.GetFileKinds);
            _allKinds = kinds;
            _selectedKind = _selectedKind is null
                ? _allKinds.FirstOrDefault()
                : _allKinds.FirstOrDefault(kind => kind.Id == _selectedKind.Id) ?? _allKinds.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            _allKinds = [];
            _selectedKind = null;
        }
        finally
        {
            _isLoading = false;
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        var visible = _allKinds
            .Where(MatchesFilter)
            .ToList();

        _visibleKinds.Clear();
        foreach (var kind in visible)
        {
            _visibleKinds.Add(kind);
        }

        if (_selectedKind is null || !visible.Any(kind => kind.Id == _selectedKind.Id))
        {
            _selectedKind = visible.FirstOrDefault() ?? _allKinds.FirstOrDefault();
        }

        KindList.SelectedItem = _selectedKind;
        EmptyText.Visibility = ShouldShowEmptyText(visible) ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = t("emptyNoMatch");
        UpdateStaticText();
        UpdateFilterButtons();
        RenderDetail();
    }

    private bool MatchesFilter(FileKindSummary kind)
    {
        if (_status == "Review" && kind.Status == "Consistent")
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_query))
        {
            return true;
        }

        var text = string.Join(" ", new[]
        {
            kind.DisplayName,
            kind.ShortName,
            kind.Description,
            kind.PrimaryAppName,
            kind.PrimaryProgId,
            kind.Status,
            string.Join(" ", kind.Extensions),
            string.Join(" ", kind.Items.SelectMany(item => new[]
            {
                item.Extension,
                item.Description,
                item.FriendlyName,
                item.ProgId,
                item.Source
            }))
        });

        return text.Contains(_query, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldShowEmptyText(IReadOnlyCollection<FileKindSummary> visible)
    {
        var hasFilter = !string.IsNullOrWhiteSpace(_query) || _status != "All";
        return !_isLoading
            && _loadError is null
            && _allKinds.Count > 0
            && visible.Count == 0
            && hasFilter;
    }

    private void RenderLoading()
    {
        _visibleKinds.Clear();
        EmptyText.Visibility = Visibility.Collapsed;
        DetailPanel.Children.Clear();
        AddTitle(t("readingDefaultsTitle"));
        AddMuted(t("readingDefaultsBody"));
    }

    private void RenderDetail()
    {
        DetailPanel.Children.Clear();

        if (_loadError is not null)
        {
            AddTitle(t("loadFailedTitle"));
            AddMuted(_loadError);
            return;
        }

        if (_selectedFormat is not null)
        {
            RenderFormatDetail(_selectedFormat);
            return;
        }

        if (_selectedKind is null)
        {
            AddTitle(t("noFileKindSelected"));
            AddMuted(t("pickFileKind"));
            return;
        }

        AddEyebrow(t("fileKind"));
        AddTitle(_selectedKind.DisplayName);
        AddStatus(_selectedKind);
        AddSection(t("currentApp"), DisplayAppName(_selectedKind.PrimaryAppName), true);

        AddSectionLabel(t("includedFormats"));
        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 22) };
        foreach (var extension in _selectedKind.Extensions)
        {
            var button = new Button
            {
                Content = FormatCode(extension),
                Tag = extension,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 7, 12, 7)
            };
            button.Click += async (_, _) => await SelectFormatAsync((string)button.Tag);
            chips.Children.Add(button);
        }
        DetailPanel.Children.Add(chips);

        if (_selectedKind.Outliers.Count > 0)
        {
            AddSectionLabel(t("exceptions"));
            foreach (var outlier in _selectedKind.Outliers)
            {
                AddMuted($"{outlier.Extension.ToUpperInvariant()}  {DisplayAppName(outlier.AppName)}");
            }
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 10) };
        actions.Children.Add(MakeButton(t("chooseApp"), async (_, _) => await OpenDefaultSettingsAsync(), true));
        actions.Children.Add(MakeButton(t("openDefaultApps"), async (_, _) => await OpenDefaultSettingsAsync()));
        DetailPanel.Children.Add(actions);
        AddMuted(t("settingsHint"));

        AddTechnicalItems(_selectedKind.Items);
    }

    private async Task SelectFormatAsync(string extension)
    {
        DetailPanel.Children.Clear();
        AddTitle(t("loadingApps"));
        try
        {
            _selectedFormat = await Task.Run(() => _formatCandidates.GetCandidates(extension));
            _selectedCandidate = _selectedFormat.Current ?? _selectedFormat.Candidates.FirstOrDefault();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, t("loadFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RenderDetail();
    }

    private void RenderFormatDetail(FormatCandidateResult format)
    {
        var item = _selectedKind?.Items.FirstOrDefault(value => value.Extension == format.Extension);
        _selectedCandidate ??= format.Current ?? format.Candidates.FirstOrDefault();

        var back = MakeButton(t("backToFileKind"), (_, _) =>
        {
            _selectedFormat = null;
            _selectedCandidate = null;
            RenderDetail();
        });
        DetailPanel.Children.Add(back);

        AddEyebrow(t("format"));
        AddTitle(item?.Description ?? format.Extension);
        AddSection(t("currentApp"), DisplayAppName(format.Current?.AppName), true);
        AddSectionLabel(t("recommendedApps"));

        if (format.Candidates.Count == 0)
        {
            AddMuted(t("noCandidateApps"));
        }
        else
        {
            foreach (var candidate in format.Candidates)
            {
                var row = MakeCandidateButton(candidate);
                DetailPanel.Children.Add(row);
            }
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 8) };
        actions.Children.Add(MakeButton(FormatActionLabel(_selectedCandidate), async (_, _) => await OpenFormatSettingsAsync(format.Extension, _selectedCandidate), true));
        if (_selectedCandidate?.CanMakeDefault == true)
        {
            actions.Children.Add(MakeButton(t("setAsDefault"), async (_, _) => await MakeFormatDefaultAsync(format.Extension, _selectedCandidate)));
        }
        DetailPanel.Children.Add(actions);

        AddMuted(FormatSettingsHint(format.Extension, _selectedCandidate));
        if (item is not null)
        {
            AddTechnicalItems([item]);
        }
    }

    private Button MakeCandidateButton(FormatAppCandidate candidate)
    {
        var isSelected = candidate == _selectedCandidate;
        var button = new Button
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BorderBrush = new SolidColorBrush(isSelected ? Color.FromRgb(37, 39, 33) : Color.FromRgb(216, 213, 204)),
            Background = new SolidColorBrush(isSelected ? Color.FromRgb(246, 244, 237) : Color.FromRgb(255, 254, 250)),
            Content = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    new TextBlock
                    {
                        Text = CandidateSourceLabel(candidate.Source),
                        Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = DisplayAppName(candidate.AppName),
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(37, 39, 33))
                    }
                }
            }
        };

        DockPanel.SetDock(((DockPanel)button.Content).Children[0], Dock.Right);
        button.Click += (_, _) =>
        {
            _selectedCandidate = candidate;
            RenderDetail();
        };
        return button;
    }

    private async Task OpenDefaultSettingsAsync()
    {
        await Task.Run(() => _settings.OpenDefaultApps());
    }

    private async Task OpenFormatSettingsAsync(string extension, FormatAppCandidate? candidate)
    {
        await Task.Run(() => _settings.OpenDefaultApps(candidate?.SettingsParameterName, candidate?.SettingsParameterValue));
        MessageBox.Show(this, FormatSettingsHint(extension, candidate), t("openSettings"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task MakeFormatDefaultAsync(string extension, FormatAppCandidate? candidate)
    {
        if (candidate?.CanMakeDefault != true || string.IsNullOrWhiteSpace(candidate.ShellHandlerId))
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            t("confirmSetDefault", ("extension", FormatCode(extension)), ("app", DisplayAppName(candidate.AppName))),
            t("setAsDefault"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            await Task.Run(() => _shellAssociations.MakeDefault(extension, candidate.ShellHandlerId, candidate.AppName));
            MessageBox.Show(this, t("setDefaultToast", ("extension", FormatCode(extension)), ("app", DisplayAppName(candidate.AppName))), t("setAsDefault"), MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadFileKindsAsync();
            await SelectFormatAsync(extension);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, t("setAsDefault"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddStatus(FileKindSummary kind)
    {
        var text = kind.Status switch
        {
            "Mixed" => kind.Outliers.Count == 1 ? t("oneException") : t("exceptionsCount", ("count", kind.Outliers.Count.ToString())),
            "Missing" => t("noAppSet"),
            _ => t("allSet")
        };
        AddMuted(text);
    }

    private void AddTechnicalItems(IReadOnlyCollection<FileAssociationItem> items)
    {
        var expander = new Expander
        {
            Header = t("technicalDetails"),
            Margin = new Thickness(0, 20, 0, 0)
        };
        var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var item in items)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"{item.Extension}  {DisplayAppName(item.FriendlyName ?? item.ProgId)}  {item.ProgId ?? t("none")}",
                Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
                Margin = new Thickness(0, 0, 0, 6)
            });
        }
        expander.Content = stack;
        DetailPanel.Children.Add(expander);
    }

    private void AddSection(string label, string value, bool large = false)
    {
        AddSectionLabel(label);
        DetailPanel.Children.Add(new Border
        {
            Margin = new Thickness(0, 8, 0, 22),
            Padding = new Thickness(14),
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 219, 211)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(246, 244, 237)),
            Child = new TextBlock
            {
                Text = value,
                FontSize = large ? 20 : 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(37, 39, 33))
            }
        });
    }

    private void AddTitle(string text)
    {
        DetailPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 39, 33)),
            Margin = new Thickness(0, 0, 0, 10)
        });
    }

    private void AddEyebrow(string text)
    {
        DetailPanel.Children.Add(new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(122, 119, 111)),
            Margin = new Thickness(0, 0, 0, 8)
        });
    }

    private void AddSectionLabel(string text)
    {
        DetailPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(122, 119, 111)),
            Margin = new Thickness(0, 8, 0, 0)
        });
    }

    private void AddMuted(string text)
    {
        DetailPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
    }

    private Button MakeButton(string text, RoutedEventHandler click, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(primary ? Color.FromRgb(37, 39, 33) : Color.FromRgb(255, 254, 250)),
            Foreground = new SolidColorBrush(primary ? Color.FromRgb(255, 254, 250) : Color.FromRgb(37, 39, 33)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 213, 204))
        };
        button.Click += click;
        return button;
    }

    private void UpdateFilterButtons()
    {
        AllFilterButton.Content = $"{t("all")} ({_allKinds.Count})";
        ReviewFilterButton.Content = $"{t("needsReview")} ({_allKinds.Count(NeedsReview)})";
    }

    private static bool NeedsReview(FileKindSummary kind)
    {
        return kind.Status != "Consistent";
    }

    private string DisplayAppName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) || name == "No default app" ? t("noDefaultApp") : name;
    }

    private string FormatActionLabel(FormatAppCandidate? candidate)
    {
        return HasAppSettingsLink(candidate) ? t("openAppDefaults") : t("changeInWindows");
    }

    private string FormatSettingsHint(string extension, FormatAppCandidate? candidate)
    {
        var key = HasAppSettingsLink(candidate) ? "appDefaultsHint" : "formatSettingsHint";
        return t(key, ("extension", FormatCode(extension)), ("app", DisplayAppName(candidate?.AppName)));
    }

    private static bool HasAppSettingsLink(FormatAppCandidate? candidate)
    {
        return !string.IsNullOrWhiteSpace(candidate?.SettingsParameterName)
            && !string.IsNullOrWhiteSpace(candidate.SettingsParameterValue);
    }

    private string CandidateSourceLabel(string source)
    {
        return source switch
        {
            "Current" => t("current"),
            "RegisteredApplication" or "ShellRecommended" => t("recommended"),
            "ShellHandler" => t("availableApp"),
            "OpenWithProgids" => t("knownForFormat"),
            "OpenWithList" => t("usedBefore"),
            _ => source
        };
    }

    private static string FormatCode(string extension)
    {
        return extension.TrimStart('.').ToUpperInvariant();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text.Trim();
        ApplyFilter();
    }

    private void OnKindSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KindList.SelectedItem is not FileKindSummary kind || _selectedKind?.Id == kind.Id)
        {
            return;
        }

        _selectedKind = kind;
        _selectedFormat = null;
        _selectedCandidate = null;
        RenderDetail();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await LoadFileKindsAsync();
    }

    private void OnAllFilterClicked(object sender, RoutedEventArgs e)
    {
        _status = "All";
        ApplyFilter();
    }

    private void OnReviewFilterClicked(object sender, RoutedEventArgs e)
    {
        _status = "Review";
        ApplyFilter();
    }

    private void OnLanguageClicked(object sender, RoutedEventArgs e)
    {
        _isChinese = !_isChinese;
        ApplyFilter();
    }

    private async void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        await OpenDefaultSettingsAsync();
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export default app associations",
            Filter = "JSON files (*.json)|*.json",
            FileName = $"default-app-associations-{DateTime.Now:yyyyMMdd-HHmm}.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var associations = _fileAssociations.GetKnownAssociations();
        _exports.Export(dialog.FileName, associations);
        MessageBox.Show(this, t("exportToast", ("count", associations.Count.ToString())), t("export"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnImportClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import default app associations",
            Filter = "JSON files (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var imported = _exports.Import(dialog.FileName);
        var current = _fileAssociations.GetKnownAssociations();
        var diff = _exports.Compare(current, imported);
        var changed = diff.Count(item => item.Status != "Same");
        MessageBox.Show(this, $"{t("importedSummary", ("count", imported.Count.ToString()))}\n{t("changed")}: {changed}", t("snapshotComparison"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string t(string key, params (string Key, string Value)[] values)
    {
        var text = (_isChinese ? Zh : En).TryGetValue(key, out var template) ? template : key;
        foreach (var (name, replacement) in values)
        {
            text = text.Replace($"{{{name}}}", replacement);
        }
        return text;
    }

    private void UpdateStaticText()
    {
        TaglineText.Text = t("appTagline");
        SearchBox.ToolTip = t("searchPlaceholder");
        LanguageButton.Content = _isChinese ? "中" : "EN";
        RefreshButton.Content = t("refresh");
        ExportButton.Content = t("export");
        CompareButton.Content = t("compare");
        SettingsButton.Content = t("openSettings");
        FileKindsLabel.Text = t("fileKinds");
    }

    private static readonly Dictionary<string, string> En = new()
    {
        ["appTagline"] = "Choose apps for file kinds.",
        ["searchPlaceholder"] = "Search files or apps",
        ["refresh"] = "Refresh",
        ["compare"] = "Compare",
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
        ["includedFormats"] = "Included formats",
        ["exceptions"] = "Exceptions",
        ["chooseApp"] = "Choose app",
        ["openDefaultApps"] = "Open default apps",
        ["settingsHint"] = "Windows Settings will open.",
        ["technicalDetails"] = "Technical details",
        ["noDefaultApp"] = "No default app",
        ["noAppSet"] = "No app set",
        ["allSet"] = "All set",
        ["oneException"] = "1 exception",
        ["exceptionsCount"] = "{count} exceptions",
        ["readingDefaultsTitle"] = "Reading defaults",
        ["readingDefaultsBody"] = "Checking the apps Windows uses for your files.",
        ["loadFailedTitle"] = "Could not read defaults",
        ["exportToast"] = "Exported {count} file associations.",
        ["importedSummary"] = "{count} imported associations compared with this PC.",
        ["snapshotComparison"] = "Snapshot comparison",
        ["current"] = "Current",
        ["none"] = "none",
        ["changed"] = "Changed",
        ["setAsDefault"] = "Set as default",
        ["confirmSetDefault"] = "Set {app} as the default app for {extension}? This will immediately change your Windows default app setting.",
        ["setDefaultToast"] = "{extension} now opens with {app}.",
        ["openSettings"] = "Open settings",
        ["export"] = "Export"
    };

    private static readonly Dictionary<string, string> Zh = new()
    {
        ["appTagline"] = "为文件类型选择打开应用。",
        ["searchPlaceholder"] = "搜索文件或应用",
        ["refresh"] = "刷新",
        ["compare"] = "对比",
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
        ["includedFormats"] = "包含格式",
        ["exceptions"] = "例外",
        ["chooseApp"] = "选择应用",
        ["openDefaultApps"] = "默认应用",
        ["settingsHint"] = "将打开 Windows 设置。",
        ["technicalDetails"] = "技术明细",
        ["noDefaultApp"] = "未设置默认应用",
        ["noAppSet"] = "未设置",
        ["allSet"] = "已设置",
        ["oneException"] = "1 个例外",
        ["exceptionsCount"] = "{count} 个例外",
        ["readingDefaultsTitle"] = "正在读取默认项",
        ["readingDefaultsBody"] = "正在检查 Windows 用哪些应用打开你的文件。",
        ["loadFailedTitle"] = "无法读取默认项",
        ["exportToast"] = "已导出 {count} 个文件关联。",
        ["importedSummary"] = "已将 {count} 个导入关联与本机对比。",
        ["snapshotComparison"] = "快照对比",
        ["current"] = "当前",
        ["none"] = "无",
        ["changed"] = "已改变",
        ["setAsDefault"] = "设为默认",
        ["confirmSetDefault"] = "要将 {extension} 的默认打开应用改为 {app} 吗？此操作会立即修改 Windows 默认应用设置。",
        ["setDefaultToast"] = "{extension} 现在将使用 {app} 打开。",
        ["openSettings"] = "打开设置",
        ["export"] = "导出"
    };
}
