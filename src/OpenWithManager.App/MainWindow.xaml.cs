using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OpenWithManager.App.Models;
using OpenWithManager.App.Services;
using OpenWithManager.App.ViewModels;

namespace OpenWithManager.App;

public partial class MainWindow : Window
{
    private readonly FileAssociationService _fileAssociations = new();
    private readonly ShellAssociationService _shellAssociations = new();
    private readonly FileKindService _fileKinds;
    private readonly FormatCandidateService _formatCandidates;
    private readonly WindowsSettingsService _settings = new();
    private readonly LocalizationService _text = new();
    private readonly AppIconService _icons = new();
    private readonly MainWindowState _state = new();
    private readonly AppPreferences _preferences = AppPreferencesService.Load();
    private readonly ObservableCollection<FileKindListItem> _visibleKinds = [];
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _refreshOnNextActivation;

    public MainWindow()
    {
        _text.SetLanguage(_preferences.IsChinese);
        _fileKinds = new FileKindService(_fileAssociations);
        _formatCandidates = new FormatCandidateService(_fileAssociations, _shellAssociations);
        InitializeComponent();
        UpdateStaticText();
        KindList.ItemsSource = _visibleKinds;
        Loaded += async (_, _) => await LoadFileKindsAsync();
        Activated += async (_, _) => await RefreshAfterExternalSettingsAsync();
        _toastTimer.Tick += (_, _) => HideToast();
    }

    private async Task LoadFileKindsAsync()
    {
        _state.IsLoading = true;
        _state.LoadError = null;
        _state.SelectedFormat = null;
        _state.SelectedCandidate = null;
        RenderLoading();

        try
        {
            var useDeveloperFormatView = _preferences.UseDeveloperFormatView;
            _fileAssociations.UseDeveloperFormatView = useDeveloperFormatView;
            var kinds = await Task.Run(() => _fileKinds.GetFileKinds(useDeveloperFormatView));
            _state.AllKinds = kinds;
            _state.SelectedKind = _state.SelectedKind is null
                ? _state.AllKinds.FirstOrDefault()
                : _state.AllKinds.FirstOrDefault(kind => kind.Id == _state.SelectedKind.Id) ?? _state.AllKinds.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _state.LoadError = ex.Message;
            _state.AllKinds = [];
            _state.SelectedKind = null;
        }
        finally
        {
            _state.IsLoading = false;
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        var visible = _state.AllKinds
            .Where(MatchesFilter)
            .Select(MakeListItem)
            .ToList();
        var selectedId = _state.SelectedKind?.Id;
        var nextSelected = visible.Count == 0
            ? null
            : visible.FirstOrDefault(item => item.Kind.Id == selectedId) ?? visible.First();

        if (nextSelected?.Kind.Id != selectedId)
        {
            _state.SelectedFormat = null;
            _state.SelectedCandidate = null;
        }

        _state.SelectedKind = nextSelected?.Kind;

        _visibleKinds.Clear();
        foreach (var kind in visible)
        {
            _visibleKinds.Add(kind);
        }

        KindList.SelectedItem = nextSelected;
        EmptyText.Visibility = ShouldShowEmptyText(visible) ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = t("emptyNoMatch");
        UpdateStaticText();
        UpdateFilterButtons();
        RenderDetail();
    }

    private bool MatchesFilter(FileKindSummary kind)
    {
        if (_state.Status == "Review" && kind.Status == FileKindStatus.Consistent)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_state.Query))
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
            kind.Status.ToString(),
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

        return text.Contains(_state.Query, StringComparison.OrdinalIgnoreCase);
    }

    private FileKindListItem MakeListItem(FileKindSummary kind)
    {
        return new FileKindListItem(
            kind,
            kind.DisplayName,
            FormatKindSummary(kind),
            BuildAppBadges(kind));
    }

    private string FormatKindSummary(FileKindSummary kind)
    {
        var formatCount = kind.Items.Count;
        var formatText = t(formatCount == 1 ? "oneFormat" : "formatCount", ("count", formatCount.ToString()));
        var hasMissing = kind.Items.Any(item => string.IsNullOrWhiteSpace(item.ProgId));
        if (hasMissing)
        {
            return $"{formatText} · {t("hasUnsetFormats")}";
        }

        var appCount = CountDistinctApps(kind);
        var appText = appCount <= 1
            ? DisplaySummaryAppName(kind.PrimaryAppName, kind)
            : t("appCount", ("count", appCount.ToString()));

        return $"{formatText} · {appText}";
    }

    private string DisplaySummaryAppName(string? name, FileKindSummary kind)
    {
        var displayName = DisplayAppName(name);
        return IsFileKindName(displayName, kind) ? t("defaultAppSet") : displayName;
    }

    private static bool IsFileKindName(string name, FileKindSummary kind)
    {
        var normalizedName = NormalizeLabel(name);
        return normalizedName == NormalizeLabel(kind.DisplayName)
            || normalizedName == NormalizeLabel(kind.Description)
            || normalizedName == NormalizeLabel(kind.ShortName);
    }

    private static string NormalizeLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized.EndsWith('s') ? normalized[..^1] : normalized;
    }

    private static int CountDistinctApps(FileKindSummary kind)
    {
        return kind.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.ProgId))
            .Select(item => DisplayAppKey(item.FriendlyName ?? item.ProgId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private IReadOnlyCollection<AppIconBadge> BuildAppBadges(FileKindSummary kind)
    {
        var appGroups = kind.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.ProgId))
            .GroupBy(item => DisplayAppKey(item.FriendlyName ?? item.ProgId), StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                AppName = DisplaySummaryAppName(group.First().FriendlyName ?? group.First().ProgId, kind),
                Count = group.Count(),
                Icon = group.Select(item => item.Icon).FirstOrDefault(icon => icon is not null)
            })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.AppName)
            .ToList();

        var badges = appGroups
            .Take(3)
            .Select(group => new AppIconBadge(group.AppName, "", _icons.GetIcon(group.Icon)))
            .ToList();

        var remaining = appGroups.Count - badges.Count;
        if (remaining > 0)
        {
            badges.Add(new AppIconBadge(t("moreApps", ("count", remaining.ToString())), $"+{remaining}", null, true));
        }

        return badges;
    }

    private static string DisplayAppKey(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
    }

    private bool ShouldShowEmptyText(IReadOnlyCollection<FileKindListItem> visible)
    {
        var hasFilter = !string.IsNullOrWhiteSpace(_state.Query) || _state.Status != "All";
        return !_state.IsLoading
            && _state.LoadError is null
            && _state.AllKinds.Count > 0
            && visible.Count == 0
            && hasFilter;
    }

    private void RenderLoading()
    {
        _visibleKinds.Clear();
        EmptyText.Visibility = Visibility.Collapsed;
        DetailHeaderPanel.Children.Clear();
        DetailPanel.Children.Clear();
        AddTitle(t("readingDefaultsTitle"));
        AddHeaderMuted(t("readingDefaultsBody"));
    }

    private void RenderDetail()
    {
        DetailHeaderPanel.Children.Clear();
        DetailPanel.Children.Clear();

        if (_state.LoadError is not null)
        {
            AddTitle(t("loadFailedTitle"));
            AddHeaderMuted(_state.LoadError);
            return;
        }

        if (_state.SelectedKind is null)
        {
            AddTitle(t("noFileKindSelected"));
            AddHeaderMuted(t("pickFileKind"));
            return;
        }

        AddEyebrow(t("fileKind"));
        AddTitle(_state.SelectedKind.DisplayName);
        AddSummary(FormatKindSummary(_state.SelectedKind));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 22) };
        actions.Children.Add(MakeButton(t("openDefaultApps"), async (_, _) => await OpenDefaultSettingsAsync(), true));
        AddHeaderElement(actions);

        AddSectionLabel(t("formats"));
        AddFormatRows(_state.SelectedKind);

        if (_state.SelectedFormat is not null)
        {
            AddSelectedFormatPanel(_state.SelectedFormat);
            AddCandidateApps(_state.SelectedFormat);
        }

        AddTechnicalItems(_state.SelectedKind.Items);
    }

    private async Task SelectFormatAsync(string extension)
    {
        _state.SelectedFormat = null;
        _state.SelectedCandidate = null;
        try
        {
            _state.SelectedFormat = await Task.Run(() => _formatCandidates.GetCandidates(extension));
            _state.SelectedCandidate = _state.SelectedFormat.Current ?? _state.SelectedFormat.Candidates.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, true);
        }

        RenderDetail();
    }

    private void AddSelectedFormatPanel(FormatCandidateResult format)
    {
        var item = _state.SelectedKind?.Items.FirstOrDefault(value => value.Extension == format.Extension);
        _state.SelectedCandidate ??= format.Current ?? format.Candidates.FirstOrDefault();

        AddSectionLabel(t("selectedFormat"));
        DetailPanel.Children.Add(new TextBlock
        {
            Text = FormatExtensionLabel(format.Extension),
            Foreground = new SolidColorBrush(Color.FromRgb(37, 39, 33)),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 2)
        });
        DetailPanel.Children.Add(new TextBlock
        {
            Text = item?.Description ?? format.Description,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        DetailPanel.Children.Add(new TextBlock
        {
            Text = t("currentUsing", ("app", DisplayAppName(format.Current?.AppName))),
            Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, _state.SelectedCandidate is null ? 12 : 4)
        });
        if (_state.SelectedCandidate is not null)
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = t("targetApp", ("app", DisplayAppName(_state.SelectedCandidate.AppName))),
                Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(MakeButton(FormatActionLabel(_state.SelectedCandidate), async (_, _) => await OpenFormatSettingsAsync(format.Extension, _state.SelectedCandidate), true));
        actions.Children.Add(MakeButton(t("copyFormat"), (_, _) => CopyFormatToClipboard(format.Extension)));
        DetailPanel.Children.Add(actions);

        AddMuted(t("settingsSearchHint", ("extension", FormatExtensionLabel(format.Extension))));
    }

    private void AddCandidateApps(FormatCandidateResult format)
    {
        AddSectionLabel(t("availableApps"));

        if (format.Candidates.Count == 0)
        {
            AddMuted(t("noCandidateApps"));
            return;
        }

        foreach (var candidate in format.Candidates)
        {
            var row = MakeCandidateButton(candidate);
            DetailPanel.Children.Add(row);
        }
    }

    private Button MakeCandidateButton(FormatAppCandidate candidate)
    {
        var isSelected = candidate == _state.SelectedCandidate;
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = DisplayAppName(candidate.AppName),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 39, 33)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(name, 1);
        var source = new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            Visibility = _preferences.ShowCandidateSources ? Visibility.Visible : Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromRgb(246, 244, 237)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(221, 217, 207)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new TextBlock
            {
                Text = CandidateSourceLabel(candidate.Source),
                Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(source, 2);
        content.Children.Add(MakeCandidateIcon(candidate));
        content.Children.Add(name);
        content.Children.Add(source);

        var button = new Button
        {
            Style = (Style)FindResource("BaseButton"),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10),
            MinHeight = 46,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BorderBrush = new SolidColorBrush(isSelected ? Color.FromRgb(37, 39, 33) : Color.FromRgb(216, 213, 204)),
            Background = new SolidColorBrush(isSelected ? Color.FromRgb(246, 244, 237) : Color.FromRgb(255, 254, 250)),
            Content = content
        };
        button.Click += (_, _) =>
        {
            _state.SelectedCandidate = candidate;
            RenderDetail();
        };
        return button;
    }

    private FrameworkElement MakeCandidateIcon(FormatAppCandidate candidate)
    {
        return MakeAppIcon(candidate.Icon, candidate.AppName, 30, 20, new Thickness(0, 0, 10, 0));
    }

    private FrameworkElement MakeAppIcon(AppIconLocation? location, string appName, double holderSize, double iconSize, Thickness margin)
    {
        var icon = _icons.GetIcon(location);
        var holder = new Border
        {
            Width = holderSize,
            Height = holderSize,
            Margin = margin,
            Background = new SolidColorBrush(Color.FromRgb(240, 238, 230)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(221, 217, 207)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(holderSize >= 30 ? 7 : 6)
        };

        holder.Child = icon is null
            ? MakeGenericAppGlyph(holderSize >= 30 ? 14 : 12)
            : new Image
            {
                Source = icon,
                Width = iconSize,
                Height = iconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

        return holder;
    }

    private static FrameworkElement MakeGenericAppGlyph(double size)
    {
        var grid = new Grid
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                var cell = new Border
                {
                    Margin = new Thickness(1.5),
                    Background = new SolidColorBrush(Color.FromRgb(133, 130, 121)),
                    CornerRadius = new CornerRadius(1.5)
                };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }

    private async Task OpenDefaultSettingsAsync()
    {
        await Task.Run(() => _settings.OpenDefaultApps());
        _refreshOnNextActivation = _preferences.AutoRefreshAfterSettings;
    }

    private async Task OpenFormatSettingsAsync(string extension, FormatAppCandidate? candidate)
    {
        await Task.Run(() => _settings.OpenDefaultApps(candidate?.SettingsParameterName, candidate?.SettingsParameterValue));
        _refreshOnNextActivation = _preferences.AutoRefreshAfterSettings;
        ShowToast(FormatSettingsHint(extension, candidate));
    }

    private void CopyFormatToClipboard(string extension)
    {
        var label = FormatExtensionLabel(extension);
        Clipboard.SetText(label);
        ShowToast(t("formatCopied", ("extension", label)));
    }

    private async Task RefreshAfterExternalSettingsAsync()
    {
        if (!_refreshOnNextActivation || _state.IsLoading)
        {
            return;
        }

        _refreshOnNextActivation = false;
        var selectedExtension = _state.SelectedFormat?.Extension;
        await LoadFileKindsAsync();
        if (!string.IsNullOrWhiteSpace(selectedExtension))
        {
            await SelectFormatAsync(selectedExtension);
        }
    }

    private void AddTechnicalItems(IReadOnlyCollection<FileAssociationItem> items)
    {
        var expander = new Expander
        {
            Header = t("technicalDetails"),
            IsExpanded = _preferences.ShowTechnicalDetails,
            Margin = new Thickness(0, 20, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(37, 39, 33))
        };

        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(MakeTechnicalHeader());

        foreach (var item in items)
        {
            stack.Children.Add(MakeTechnicalRow(item));
        }

        expander.Content = stack;
        DetailPanel.Children.Add(expander);
    }

    private Grid MakeTechnicalHeader()
    {
        var grid = MakeTechnicalGrid();
        grid.Margin = new Thickness(0, 0, 0, 6);
        AddTechnicalCell(grid, t("technicalFormat"), 0, true);
        AddTechnicalCell(grid, t("technicalCurrentApp"), 1, true);
        AddTechnicalCell(grid, t("technicalProgId"), 2, true);
        AddTechnicalCell(grid, t("technicalSource"), 3, true);
        return grid;
    }

    private Grid MakeTechnicalRow(FileAssociationItem item)
    {
        var grid = MakeTechnicalGrid();
        grid.Margin = new Thickness(0, 0, 0, 6);
        AddTechnicalCell(grid, FormatExtensionLabel(item.Extension), 0, false);
        AddTechnicalCell(grid, DisplayAppName(item.FriendlyName ?? item.ProgId), 1, false);
        AddTechnicalCell(grid, item.ProgId ?? t("none"), 2, false);
        AddTechnicalCell(grid, TechnicalSourceLabel(item.Source), 3, false);
        return grid;
    }

    private static Grid MakeTechnicalGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        return grid;
    }

    private static void AddTechnicalCell(Grid grid, string text, int column, bool isHeader)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontSize = isHeader ? 11 : 12,
            FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(isHeader ? Color.FromRgb(122, 119, 111) : Color.FromRgb(108, 106, 98)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void AddFormatRows(FileKindSummary kind)
    {
        var byExtension = kind.Items.ToDictionary(item => item.Extension, StringComparer.OrdinalIgnoreCase);
        var rows = new StackPanel { Margin = new Thickness(0, 8, 0, 22) };
        rows.Children.Add(MakeFormatTableHeader());

        foreach (var extension in kind.Extensions)
        {
            byExtension.TryGetValue(extension, out var item);
            var isSelected = string.Equals(_state.SelectedFormat?.Extension, extension, StringComparison.OrdinalIgnoreCase);
            rows.Children.Add(MakeFormatRowButton(
                extension,
                item?.Description ?? extension,
                DisplaySummaryAppName(item?.FriendlyName ?? item?.ProgId, kind),
                item?.Icon,
                isSelected));
        }

        DetailPanel.Children.Add(rows);
    }

    private Grid MakeFormatTableHeader()
    {
        var content = MakeFormatRowGrid();
        content.Margin = new Thickness(12, 0, 12, 6);
        AddFormatHeaderCell(content, t("formatTableFormat"), 0);
        AddFormatHeaderCell(content, t("formatTableDescription"), 1);
        AddFormatHeaderCell(content, t("formatTableCurrentApp"), 2);
        return content;
    }

    private static void AddFormatHeaderCell(Grid grid, string text, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(122, 119, 111)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = column == 2 ? new Thickness(32, 0, 0, 0) : new Thickness(0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private static Grid MakeFormatRowGrid()
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        return content;
    }

    private Button MakeFormatRowButton(string extension, string description, string appName, AppIconLocation? icon, bool isSelected)
    {
        var content = MakeFormatRowGrid();

        var code = new TextBlock
        {
            Text = FormatExtensionLabel(extension),
            Foreground = new SolidColorBrush(Color.FromRgb(37, 39, 33)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var descriptionText = new TextBlock
        {
            Text = description,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 39, 33)),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var app = new Grid();
        app.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        app.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        app.Children.Add(MakeAppIcon(icon, appName, 24, 16, new Thickness(0, 0, 8, 0)));

        var appNameText = new TextBlock
        {
            Text = appName,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(appNameText, 1);
        app.Children.Add(appNameText);

        Grid.SetColumn(descriptionText, 1);
        Grid.SetColumn(app, 2);
        content.Children.Add(code);
        content.Children.Add(descriptionText);
        content.Children.Add(app);

        var button = new Button
        {
            Content = content,
            Tag = extension,
            Style = (Style)FindResource("BaseButton"),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 9, 12, 9),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BorderBrush = new SolidColorBrush(isSelected ? Color.FromRgb(37, 39, 33) : Color.FromRgb(216, 213, 204)),
            Background = new SolidColorBrush(isSelected ? Color.FromRgb(246, 244, 237) : Color.FromRgb(255, 254, 250))
        };
        button.Click += async (_, _) => await SelectFormatAsync((string)button.Tag);
        return button;
    }

    private void AddTitle(string text)
    {
        AddHeaderElement(new TextBlock
        {
            Text = text,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 39, 33)),
            Margin = new Thickness(0, 0, 0, 10)
        });
    }

    private void AddSummary(string text)
    {
        AddHeaderElement(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        });
    }

    private void AddEyebrow(string text)
    {
        AddHeaderElement(new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(122, 119, 111)),
            Margin = new Thickness(0, 0, 0, 8)
        });
    }

    private void AddHeaderElement(UIElement element)
    {
        DetailHeaderPanel.Children.Add(element);
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

    private void AddHeaderMuted(string text)
    {
        AddHeaderElement(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        });
    }

    private void ShowToast(string message, bool isError = false)
    {
        _toastTimer.Stop();
        ToastText.Text = message;
        ToastBorder.Background = new SolidColorBrush(isError ? Color.FromRgb(119, 44, 44) : Color.FromRgb(35, 37, 31));
        ToastBorder.BorderBrush = new SolidColorBrush(isError ? Color.FromRgb(147, 67, 67) : Color.FromRgb(58, 61, 52));
        ToastBorder.Visibility = Visibility.Visible;
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        ToastBorder.Visibility = Visibility.Collapsed;
    }

    private Button MakeButton(string text, RoutedEventHandler click, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource(primary ? "PrimaryButton" : "IconButton"),
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 8, 12, 8),
        };
        button.Click += click;
        return button;
    }

    private void UpdateFilterButtons()
    {
        AllFilterButton.Content = $"{t("all")} ({_state.AllKinds.Count})";
        ReviewFilterButton.Content = $"{t("needsReview")} ({_state.AllKinds.Count(NeedsReview)})";
        AllFilterButton.Style = (Style)FindResource(_state.Status == "All" ? "PrimaryButton" : "IconButton");
        ReviewFilterButton.Style = (Style)FindResource(_state.Status == "Review" ? "PrimaryButton" : "IconButton");
    }

    private static bool NeedsReview(FileKindSummary kind)
    {
        return kind.Status != FileKindStatus.Consistent;
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
        return t(key, ("extension", FormatExtensionLabel(extension)), ("app", DisplayAppName(candidate?.AppName)));
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

    private string TechnicalSourceLabel(string source)
    {
        return source switch
        {
            "UserChoice" => t("sourceUserChoice"),
            "Shell" => t("sourceShell"),
            "Registry" => t("sourceRegistry"),
            "Unknown" => t("sourceUnknown"),
            _ => source
        };
    }

    private static string FormatCode(string extension)
    {
        return extension.TrimStart('.').ToUpperInvariant();
    }

    private static string FormatExtensionLabel(string extension)
    {
        return $".{FormatCode(extension)}";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _state.Query = SearchBox.Text.Trim();
        ApplyFilter();
    }

    private void OnKindSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KindList.SelectedItem is not FileKindListItem item || _state.SelectedKind?.Id == item.Kind.Id)
        {
            return;
        }

        _state.SelectedKind = item.Kind;
        _state.SelectedFormat = null;
        _state.SelectedCandidate = null;
        RenderDetail();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await LoadFileKindsAsync();
    }

    private void OnAllFilterClicked(object sender, RoutedEventArgs e)
    {
        _state.Status = "All";
        ApplyFilter();
    }

    private void OnReviewFilterClicked(object sender, RoutedEventArgs e)
    {
        _state.Status = "Review";
        ApplyFilter();
    }

    private void OnLanguageClicked(object sender, RoutedEventArgs e)
    {
        _text.ToggleLanguage();
        _preferences.IsChinese = _text.IsChinese;
        SavePreferences();
        ApplyFilter();
    }

    private void OnPreferencesClicked(object sender, RoutedEventArgs e)
    {
        SyncPreferencesPanel();
        PreferencesPanel.Visibility = Visibility.Visible;
    }

    private void OnClosePreferencesClicked(object sender, RoutedEventArgs e)
    {
        PreferencesPanel.Visibility = Visibility.Collapsed;
    }

    private void OnSettingsLanguageClicked(object sender, RoutedEventArgs e)
    {
        _text.ToggleLanguage();
        _preferences.IsChinese = _text.IsChinese;
        SavePreferences();
        ApplyFilter();
        SyncPreferencesPanel();
    }

    private async void OnDeveloperViewClicked(object sender, RoutedEventArgs e)
    {
        _preferences.UseDeveloperFormatView = DeveloperViewCheckBox.IsChecked == true;
        SavePreferences();
        _state.SelectedFormat = null;
        _state.SelectedCandidate = null;
        await LoadFileKindsAsync();
        SyncPreferencesPanel();
    }

    private void OnShowTechnicalDetailsClicked(object sender, RoutedEventArgs e)
    {
        _preferences.ShowTechnicalDetails = ShowTechnicalDetailsCheckBox.IsChecked == true;
        SavePreferences();
        RenderDetail();
        SyncPreferencesPanel();
    }

    private void OnCandidateSourcesClicked(object sender, RoutedEventArgs e)
    {
        _preferences.ShowCandidateSources = CandidateSourcesCheckBox.IsChecked == true;
        SavePreferences();
        RenderDetail();
        SyncPreferencesPanel();
    }

    private void OnAutoRefreshClicked(object sender, RoutedEventArgs e)
    {
        _preferences.AutoRefreshAfterSettings = AutoRefreshCheckBox.IsChecked == true;
        SavePreferences();
        SyncPreferencesPanel();
    }

    private async void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        await OpenDefaultSettingsAsync();
    }

    private string t(string key, params (string Key, string Value)[] values)
    {
        return _text.T(key, values);
    }

    private void UpdateStaticText()
    {
        TaglineText.Text = t("appTagline");
        SearchBox.ToolTip = t("searchPlaceholder");
        LanguageButton.Content = _text.LanguageLabel;
        RefreshButton.Content = t("refresh");
        PreferencesButton.Content = t("preferences");
        SettingsButton.Content = t("windowsSettings");
        FileKindsLabel.Text = t("fileKinds");
        SyncPreferencesPanel();
    }

    private void SyncPreferencesPanel()
    {
        PreferencesTitle.Text = t("preferences");
        PreferencesSubtitle.Text = t("preferencesSubtitle");
        ClosePreferencesButton.Content = t("close");
        PreferencesDisplayLabel.Text = t("preferencesDisplay");
        SettingsLanguageButton.Content = t("languageSetting", ("language", _text.LanguageLabel));
        DeveloperViewCheckBox.Content = t("developerFormatView");
        DeveloperViewCheckBox.IsChecked = _preferences.UseDeveloperFormatView;
        ShowTechnicalDetailsCheckBox.Content = t("showTechnicalDetails");
        ShowTechnicalDetailsCheckBox.IsChecked = _preferences.ShowTechnicalDetails;
        CandidateSourcesCheckBox.Content = t("showCandidateSources");
        CandidateSourcesCheckBox.IsChecked = _preferences.ShowCandidateSources;
        PreferencesBehaviorLabel.Text = t("preferencesBehavior");
        AutoRefreshCheckBox.Content = t("autoRefreshAfterSettings");
        AutoRefreshCheckBox.IsChecked = _preferences.AutoRefreshAfterSettings;
        PreferencesSafetyLabel.Text = t("preferencesSafety");
        PreferencesSafetyText.Text = t("preferencesSafetyBody");
    }

    private void SavePreferences()
    {
        AppPreferencesService.Save(_preferences);
    }
}
