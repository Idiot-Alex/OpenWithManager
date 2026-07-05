using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenWithManager.App.Models;
using OpenWithManager.App.Services;
using OpenWithManager.App.ViewModels;
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
    private readonly LocalizationService _text = new();
    private readonly AppIconService _icons = new();
    private readonly MainWindowState _state = new();
    private readonly ObservableCollection<FileKindSummary> _visibleKinds = [];
    private bool _refreshOnNextActivation;

    public MainWindow()
    {
        _fileKinds = new FileKindService(_fileAssociations);
        _formatCandidates = new FormatCandidateService(_fileAssociations, _shellAssociations);
        InitializeComponent();
        UpdateStaticText();
        KindList.ItemsSource = _visibleKinds;
        Loaded += async (_, _) => await LoadFileKindsAsync();
        Activated += async (_, _) => await RefreshAfterExternalSettingsAsync();
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
            var kinds = await Task.Run(_fileKinds.GetFileKinds);
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
            .Select(LocalizeKind)
            .ToList();
        var selectedId = _state.SelectedKind?.Id;
        var nextSelected = visible.Count == 0
            ? null
            : visible.FirstOrDefault(kind => kind.Id == selectedId) ?? visible.First();

        if (nextSelected?.Id != selectedId)
        {
            _state.SelectedFormat = null;
            _state.SelectedCandidate = null;
        }

        _state.SelectedKind = nextSelected;

        _visibleKinds.Clear();
        foreach (var kind in visible)
        {
            _visibleKinds.Add(kind);
        }

        KindList.SelectedItem = _state.SelectedKind;
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
            StatusLabel(kind.Status),
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

    private FileKindSummary LocalizeKind(FileKindSummary kind)
    {
        return kind with { StatusText = StatusLabel(kind.Status) };
    }

    private string StatusLabel(FileKindStatus status)
    {
        return status switch
        {
            FileKindStatus.Mixed => t("mixedStatus"),
            FileKindStatus.Missing => t("missingStatus"),
            _ => t("consistentStatus")
        };
    }

    private bool ShouldShowEmptyText(IReadOnlyCollection<FileKindSummary> visible)
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
        DetailPanel.Children.Clear();
        AddTitle(t("readingDefaultsTitle"));
        AddMuted(t("readingDefaultsBody"));
    }

    private void RenderDetail()
    {
        DetailPanel.Children.Clear();

        if (_state.LoadError is not null)
        {
            AddTitle(t("loadFailedTitle"));
            AddMuted(_state.LoadError);
            return;
        }

        if (_state.SelectedFormat is not null)
        {
            RenderFormatDetail(_state.SelectedFormat);
            return;
        }

        if (_state.SelectedKind is null)
        {
            AddTitle(t("noFileKindSelected"));
            AddMuted(t("pickFileKind"));
            return;
        }

        AddEyebrow(t("fileKind"));
        AddTitle(_state.SelectedKind.DisplayName);
        AddStatus(_state.SelectedKind);
        AddSection(t("currentApp"), DisplayAppName(_state.SelectedKind.PrimaryAppName), true);

        AddSectionLabel(t("includedFormats"));
        AddFormatRows(_state.SelectedKind);

        if (_state.SelectedKind.Outliers.Count > 0)
        {
            AddSectionLabel(t("exceptions"));
            foreach (var outlier in _state.SelectedKind.Outliers)
            {
                AddMuted($"{outlier.Extension.ToUpperInvariant()}  {DisplayAppName(outlier.AppName)}");
            }
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 10) };
        actions.Children.Add(MakeButton(t("chooseApp"), async (_, _) => await OpenDefaultSettingsAsync(), true));
        actions.Children.Add(MakeButton(t("openDefaultApps"), async (_, _) => await OpenDefaultSettingsAsync()));
        DetailPanel.Children.Add(actions);
        AddMuted(t("settingsHint"));

        AddTechnicalItems(_state.SelectedKind.Items);
    }

    private async Task SelectFormatAsync(string extension)
    {
        DetailPanel.Children.Clear();
        AddTitle(t("loadingApps"));
        try
        {
            _state.SelectedFormat = await Task.Run(() => _formatCandidates.GetCandidates(extension));
            _state.SelectedCandidate = _state.SelectedFormat.Current ?? _state.SelectedFormat.Candidates.FirstOrDefault();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, t("loadFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RenderDetail();
    }

    private void RenderFormatDetail(FormatCandidateResult format)
    {
        var item = _state.SelectedKind?.Items.FirstOrDefault(value => value.Extension == format.Extension);
        _state.SelectedCandidate ??= format.Current ?? format.Candidates.FirstOrDefault();

        var back = MakeButton(t("backToFileKind"), (_, _) =>
        {
            _state.SelectedFormat = null;
            _state.SelectedCandidate = null;
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
        actions.Children.Add(MakeButton(FormatActionLabel(_state.SelectedCandidate), async (_, _) => await OpenFormatSettingsAsync(format.Extension, _state.SelectedCandidate), true));
        if (_state.SelectedCandidate?.CanMakeDefault == true)
        {
            actions.Children.Add(MakeButton(t("setAsDefault"), async (_, _) => await MakeFormatDefaultAsync(format.Extension, _state.SelectedCandidate)));
        }
        DetailPanel.Children.Add(actions);

        AddMuted(FormatSettingsHint(format.Extension, _state.SelectedCandidate));
        if (item is not null)
        {
            AddTechnicalItems([item]);
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
        var icon = _icons.GetIcon(candidate.Icon);
        var holder = new Border
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(240, 238, 230)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(221, 217, 207)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7)
        };

        holder.Child = icon is null
            ? new TextBlock
            {
                Text = CandidateInitial(candidate.AppName),
                Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
            : new Image
            {
                Source = icon,
                Width = 20,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

        return holder;
    }

    private async Task OpenDefaultSettingsAsync()
    {
        await Task.Run(() => _settings.OpenDefaultApps());
        _refreshOnNextActivation = true;
    }

    private async Task OpenFormatSettingsAsync(string extension, FormatAppCandidate? candidate)
    {
        await Task.Run(() => _settings.OpenDefaultApps(candidate?.SettingsParameterName, candidate?.SettingsParameterValue));
        _refreshOnNextActivation = true;
        MessageBox.Show(this, FormatSettingsHint(extension, candidate), t("openSettings"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            FileKindStatus.Mixed => kind.Outliers.Count == 1 ? t("oneException") : t("exceptionsCount", ("count", kind.Outliers.Count.ToString())),
            FileKindStatus.Missing => t("noAppSet"),
            _ => t("allSet")
        };
        AddMuted(text);
    }

    private void AddTechnicalItems(IReadOnlyCollection<FileAssociationItem> items)
    {
        var expander = new Expander
        {
            Header = t("technicalDetails"),
            Margin = new Thickness(0, 20, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(37, 39, 33))
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

    private void AddFormatRows(FileKindSummary kind)
    {
        var byExtension = kind.Items.ToDictionary(item => item.Extension, StringComparer.OrdinalIgnoreCase);
        var rows = new StackPanel { Margin = new Thickness(0, 8, 0, 22) };

        foreach (var extension in kind.Extensions)
        {
            byExtension.TryGetValue(extension, out var item);
            rows.Children.Add(MakeFormatRowButton(extension, DisplayAppName(item?.FriendlyName ?? item?.ProgId)));
        }

        DetailPanel.Children.Add(rows);
    }

    private Button MakeFormatRowButton(string extension, string appName)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var code = new TextBlock
        {
            Text = FormatCode(extension),
            Foreground = new SolidColorBrush(Color.FromRgb(37, 39, 33)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var app = new TextBlock
        {
            Text = appName,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 106, 98)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(app, 1);
        content.Children.Add(code);
        content.Children.Add(app);

        var button = new Button
        {
            Content = content,
            Tag = extension,
            Style = (Style)FindResource("BaseButton"),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 9, 12, 9),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        button.Click += async (_, _) => await SelectFormatAsync((string)button.Tag);
        return button;
    }

    private void AddSection(string label, string value, bool large = false)
    {
        AddSectionLabel(label);
        DetailPanel.Children.Add(new Border
        {
            Margin = new Thickness(0, 8, 0, 22),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 219, 211)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(246, 244, 237)),
            CornerRadius = new CornerRadius(8),
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

    private static string CandidateInitial(string appName)
    {
        return string.IsNullOrWhiteSpace(appName) ? "?" : appName.Trim()[0].ToString().ToUpperInvariant();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _state.Query = SearchBox.Text.Trim();
        ApplyFilter();
    }

    private void OnKindSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KindList.SelectedItem is not FileKindSummary kind || _state.SelectedKind?.Id == kind.Id)
        {
            return;
        }

        _state.SelectedKind = kind;
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
        return _text.T(key, values);
    }

    private void UpdateStaticText()
    {
        TaglineText.Text = t("appTagline");
        SearchBox.ToolTip = t("searchPlaceholder");
        LanguageButton.Content = _text.LanguageLabel;
        RefreshButton.Content = t("refresh");
        ExportButton.Content = t("export");
        CompareButton.Content = t("compare");
        SettingsButton.Content = t("openSettings");
        FileKindsLabel.Text = t("fileKinds");
    }
}
