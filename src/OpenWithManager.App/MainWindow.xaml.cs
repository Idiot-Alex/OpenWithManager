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
    private readonly FileKindDisplayService _fileKindDisplay;
    private readonly MainWindowState _state = new();
    private readonly AppPreferences _preferences = AppPreferencesService.Load();
    private readonly ObservableCollection<FileKindListItem> _visibleKinds = [];
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _refreshOnNextActivation;
    private int _formatSelectionVersion;

    private static readonly Color UiInkColor = Color.FromRgb(32, 36, 42);
    private static readonly Color UiMutedColor = Color.FromRgb(99, 112, 128);
    private static readonly Color UiLabelColor = Color.FromRgb(104, 116, 130);
    private static readonly Color UiSurfaceColor = Color.FromRgb(255, 255, 255);
    private static readonly Color UiSubtleColor = Color.FromRgb(238, 241, 244);
    private static readonly Color UiLineColor = Color.FromRgb(216, 222, 230);
    private static readonly Color UiHoverColor = Color.FromRgb(243, 246, 248);
    private static readonly Color UiSelectedColor = Color.FromRgb(232, 238, 243);

    private static SolidColorBrush UiBrush(Color color) => new(color);

    public MainWindow()
    {
        _text.SetLanguage(_preferences.IsChinese);
        _fileKinds = new FileKindService(_fileAssociations);
        _formatCandidates = new FormatCandidateService(_fileAssociations, _shellAssociations);
        _fileKindDisplay = new FileKindDisplayService(_text, _icons);
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
        ClearSelectedFormat();
        RenderLoading();

        try
        {
            var kinds = await Task.Run(() => _fileKinds.GetFileKinds());
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
            .Select(_fileKindDisplay.CreateListItem)
            .ToList();
        var selectedId = _state.SelectedKind?.Id;
        var nextSelected = visible.Count == 0
            ? null
            : visible.FirstOrDefault(item => item.Kind.Id == selectedId) ?? visible.First();

        if (nextSelected?.Kind.Id != selectedId)
        {
            ClearSelectedFormat();
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
        _ = SelectFirstFormatIfNeededAsync();
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
                item.Category,
                item.Description,
                item.FriendlyName,
                item.ProgId,
                item.Source,
                item.ContentType,
                item.PerceivedType
            }))
        });

        return text.Contains(_state.Query, StringComparison.OrdinalIgnoreCase);
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

    private void ClearSelectedFormat()
    {
        _formatSelectionVersion++;
        _state.SelectedFormat = null;
        _state.SelectedCandidate = null;
    }

    private void RenderLoading()
    {
        _visibleKinds.Clear();
        EmptyText.Visibility = Visibility.Collapsed;
        DetailHeaderPanel.Children.Clear();
        DetailPanel.Children.Clear();
        FormatWorkPanel.Children.Clear();
        AddTitle(t("readingDefaultsTitle"));
        AddHeaderMuted(t("readingDefaultsBody"));
    }

    private void RenderDetail()
    {
        DetailHeaderPanel.Children.Clear();
        DetailPanel.Children.Clear();
        FormatWorkPanel.Children.Clear();

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

        AddFileKindOverview(_state.SelectedKind);

        if (_state.SelectedFormat is not null)
        {
            AddSelectedFormatPanel(_state.SelectedFormat);
        }
        else
        {
            AddNoFormatSelectedPanel();
        }

        AddSectionLabel(t("formatDistribution"));
        AddFormatRows(_state.SelectedKind);

        AddTechnicalItems(_state.SelectedKind.Items);
    }

    private void AddFileKindOverview(FileKindSummary kind)
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = t("fileKind"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiBrush(UiLabelColor),
            Margin = new Thickness(0, 0, 0, 6)
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = kind.DisplayName,
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiBrush(UiInkColor),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = _fileKindDisplay.FormatKindSummary(kind),
            Foreground = UiBrush(UiMutedColor),
            Margin = new Thickness(0, 5, 0, 0)
        });
        root.Children.Add(titleStack);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        right.Children.Add(MakeBadgeGroup(_fileKindDisplay.BuildAppBadges(kind), new Thickness(0, 0, 14, 0)));
        right.Children.Add(MakeButton(t("openDefaultApps"), async (_, _) => await OpenDefaultSettingsAsync()));
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        DetailHeaderPanel.Children.Add(root);
    }

    private FrameworkElement MakeBadgeGroup(IReadOnlyCollection<AppIconBadge> badges, Thickness margin)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = margin,
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (var badge in badges)
        {
            stack.Children.Add(MakeAppBadge(badge));
        }

        return stack;
    }

    private FrameworkElement MakeAppBadge(AppIconBadge badge)
    {
        var holder = new Border
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(4, 0, 0, 0),
            Background = UiBrush(UiSurfaceColor),
            BorderBrush = UiBrush(UiLineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            ToolTip = badge.Label
        };

        if (badge.IsOverflow)
        {
            holder.Child = new TextBlock
            {
                Text = badge.Initial,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiBrush(UiMutedColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return holder;
        }

        holder.Child = badge.Icon is null
            ? MakeGenericAppGlyph(13)
            : new Image
            {
                Source = badge.Icon,
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

        return holder;
    }

    private async Task SelectFirstFormatIfNeededAsync()
    {
        if (_state.LoadError is not null || _state.SelectedKind is null || _state.SelectedFormat is not null)
        {
            return;
        }

        var firstExtension = _state.SelectedKind.Extensions.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstExtension))
        {
            return;
        }

        await SelectFormatAsync(firstExtension);
    }

    private async Task SelectFormatAsync(string extension)
    {
        var requestVersion = ++_formatSelectionVersion;
        var selectedKindId = _state.SelectedKind?.Id;
        _state.SelectedFormat = null;
        _state.SelectedCandidate = null;
        try
        {
            var result = await Task.Run(() => _formatCandidates.GetCandidates(extension));
            if (_state.SelectedKind?.Id != selectedKindId || requestVersion != _formatSelectionVersion)
            {
                return;
            }

            _state.SelectedFormat = result;
            _state.SelectedCandidate = result.Current ?? result.Candidates.FirstOrDefault();
        }
        catch (Exception ex)
        {
            if (requestVersion != _formatSelectionVersion)
            {
                return;
            }

            ShowToast(ex.Message, true);
        }

        if (requestVersion != _formatSelectionVersion)
        {
            return;
        }

        RenderDetail();
        FormatWorkScrollViewer.ScrollToTop();
    }

    private void AddSelectedFormatPanel(FormatCandidateResult format)
    {
        var item = _state.SelectedKind?.Items.FirstOrDefault(value => value.Extension == format.Extension);
        _state.SelectedCandidate ??= format.Current ?? format.Candidates.FirstOrDefault();

        AddWorkSectionLabel(t("formatActions"));
        var header = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var extensionBadge = new Border
        {
            Background = UiBrush(UiSurfaceColor),
            BorderBrush = UiBrush(UiLineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock
            {
                Text = FormatExtensionLabel(format.Extension),
                Foreground = UiBrush(UiInkColor),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            }
        };
        header.Children.Add(extensionBadge);

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = item?.Description ?? format.Description,
            Foreground = UiBrush(UiInkColor),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = t("currentUsing", ("app", DisplayAppName(format.Current?.AppName))),
            Foreground = UiBrush(UiMutedColor),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(titleStack);
        FormatWorkPanel.Children.Add(header);

        if (_state.SelectedCandidate is not null)
        {
            FormatWorkPanel.Children.Add(new TextBlock
            {
                Text = t("selectedApp", ("app", DisplayAppName(_state.SelectedCandidate.AppName))),
                Foreground = UiBrush(UiMutedColor),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
        }

        var primaryAction = MakeButton(FormatActionLabel(_state.SelectedCandidate), async (_, _) => await OpenFormatSettingsAsync(format.Extension, _state.SelectedCandidate), true);
        primaryAction.HorizontalAlignment = HorizontalAlignment.Stretch;
        primaryAction.HorizontalContentAlignment = HorizontalAlignment.Center;
        primaryAction.Margin = new Thickness(0, 0, 0, 8);
        FormatWorkPanel.Children.Add(primaryAction);

        var copyAction = MakeButton(t("copyFormat"), (_, _) => CopyFormatToClipboard(format.Extension));
        copyAction.HorizontalAlignment = HorizontalAlignment.Stretch;
        copyAction.HorizontalContentAlignment = HorizontalAlignment.Center;
        copyAction.Margin = new Thickness(0, 0, 0, 10);
        FormatWorkPanel.Children.Add(copyAction);

        AddWorkMuted(t("settingsSearchHint", ("extension", FormatExtensionLabel(format.Extension))));
        AddWorkSectionLabel(t("availableApps"));
        if (format.Candidates.Count == 0)
        {
            AddWorkMuted(t("noCandidateApps"));
        }
        else
        {
            var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var candidate in format.Candidates)
            {
                list.Children.Add(MakeCandidateButton(candidate));
            }

            FormatWorkPanel.Children.Add(list);
        }
    }

    private void AddNoFormatSelectedPanel()
    {
        AddWorkSectionLabel(t("formatActions"));
        FormatWorkPanel.Children.Add(new TextBlock
        {
            Text = t("pickFormat"),
            Foreground = UiBrush(UiMutedColor),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
    }

    private Button MakeCandidateButton(FormatAppCandidate candidate)
    {
        var isSelected = candidate == _state.SelectedCandidate;
        var isCurrent = string.Equals(candidate.Source, "Current", StringComparison.OrdinalIgnoreCase);
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = DisplayAppName(candidate.AppName),
            FontWeight = FontWeights.SemiBold,
            Foreground = UiBrush(UiInkColor),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(name, 1);
        var source = new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            Visibility = _preferences.ShowCandidateSources || isCurrent ? Visibility.Visible : Visibility.Collapsed,
            Background = UiBrush(UiHoverColor),
            BorderBrush = UiBrush(UiLineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new TextBlock
            {
                Text = CandidateSourceLabel(candidate.Source),
                Foreground = UiBrush(UiMutedColor),
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
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BorderBrush = UiBrush(isSelected ? UiInkColor : UiLineColor),
            Background = UiBrush(isSelected ? UiSelectedColor : UiSurfaceColor),
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
            Background = UiBrush(UiSubtleColor),
            BorderBrush = UiBrush(UiLineColor),
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
                    Background = UiBrush(UiMutedColor),
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
            Foreground = UiBrush(UiInkColor)
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
        AddTechnicalCell(grid, t("technicalSource"), 2, true);
        return grid;
    }

    private Grid MakeTechnicalRow(FileAssociationItem item)
    {
        var grid = MakeTechnicalGrid();
        grid.Margin = new Thickness(0, 0, 0, 6);
        AddTechnicalCell(grid, FormatExtensionLabel(item.Extension), 0, false);
        AddTechnicalCell(grid, DisplayAppName(item.FriendlyName ?? item.ProgId), 1, false);
        AddTechnicalCell(grid, TechnicalSourceLabel(item.Source), 2, false);
        return grid;
    }

    private static Grid MakeTechnicalGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
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
            Foreground = UiBrush(isHeader ? UiLabelColor : UiMutedColor),
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
                _fileKindDisplay.DisplaySummaryAppName(item?.FriendlyName ?? item?.ProgId, kind),
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
            Foreground = UiBrush(UiLabelColor),
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
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 132 });
        return content;
    }

    private Button MakeFormatRowButton(string extension, string description, string appName, AppIconLocation? icon, bool isSelected)
    {
        var content = MakeFormatRowGrid();

        var code = new TextBlock
        {
            Text = FormatExtensionLabel(extension),
            Foreground = UiBrush(UiInkColor),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var descriptionText = new TextBlock
        {
            Text = description,
            Foreground = UiBrush(UiInkColor),
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
            Foreground = UiBrush(UiMutedColor),
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
            BorderBrush = UiBrush(isSelected ? UiInkColor : UiLineColor),
            Background = UiBrush(isSelected ? UiSelectedColor : UiSurfaceColor)
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
            Foreground = UiBrush(UiInkColor),
            Margin = new Thickness(0, 0, 0, 10)
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
            Foreground = UiBrush(UiLabelColor),
            Margin = new Thickness(0, 8, 0, 0)
        });
    }

    private void AddWorkSectionLabel(string text)
    {
        AddWorkSectionLabel(FormatWorkPanel, text);
    }

    private static void AddWorkSectionLabel(Panel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiBrush(UiLabelColor),
            Margin = new Thickness(0, 8, 0, 0)
        });
    }

    private void AddMuted(string text)
    {
        DetailPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = UiBrush(UiMutedColor),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
    }

    private void AddWorkMuted(string text)
    {
        AddWorkMuted(FormatWorkPanel, text, new Thickness(0, 0, 0, 8));
    }

    private static void AddWorkMuted(Panel panel, string text, Thickness margin)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = UiBrush(UiMutedColor),
            TextWrapping = TextWrapping.Wrap,
            Margin = margin
        });
    }

    private void AddHeaderMuted(string text)
    {
        AddHeaderElement(new TextBlock
        {
            Text = text,
            Foreground = UiBrush(UiMutedColor),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        });
    }

    private void ShowToast(string message, bool isError = false)
    {
        _toastTimer.Stop();
        ToastText.Text = message;
        ToastBorder.Background = UiBrush(isError ? Color.FromRgb(119, 44, 44) : UiInkColor);
        ToastBorder.BorderBrush = UiBrush(isError ? Color.FromRgb(147, 67, 67) : Color.FromRgb(52, 58, 66));
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
        UpdateSearchPlaceholder();
        ApplyFilter();
    }

    private void OnKindSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KindList.SelectedItem is not FileKindListItem item || _state.SelectedKind?.Id == item.Kind.Id)
        {
            return;
        }

        _state.SelectedKind = item.Kind;
        ClearSelectedFormat();
        RenderDetail();
        _ = SelectFirstFormatIfNeededAsync();
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

    private void OnPreferencesClicked(object sender, RoutedEventArgs e)
    {
        SyncPreferencesPanel();
        PreferencesOverlay.Visibility = Visibility.Visible;
    }

    private void OnClosePreferencesClicked(object sender, RoutedEventArgs e)
    {
        ClosePreferences();
    }

    private void OnPreferencesOverlayMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ClosePreferences();
    }

    private void ClosePreferences()
    {
        PreferencesOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnChineseLanguageClicked(object sender, RoutedEventArgs e)
    {
        SetLanguage(true);
    }

    private void OnEnglishLanguageClicked(object sender, RoutedEventArgs e)
    {
        SetLanguage(false);
    }

    private void SetLanguage(bool isChinese)
    {
        if (_text.IsChinese == isChinese)
        {
            SyncPreferencesPanel();
            return;
        }

        _text.SetLanguage(isChinese);
        _preferences.IsChinese = _text.IsChinese;
        SavePreferences();
        ApplyFilter();
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

    private string t(string key, params (string Key, string Value)[] values)
    {
        return _text.T(key, values);
    }

    private void UpdateStaticText()
    {
        TaglineText.Text = t("appTagline");
        SearchBox.ToolTip = t("searchPlaceholder");
        SearchPlaceholderText.Text = t("searchPlaceholder");
        UpdateSearchPlaceholder();
        RefreshButton.Content = t("refresh");
        PreferencesButton.Content = t("preferences");
        FileKindsLabel.Text = t("fileKinds");
        SyncPreferencesPanel();
    }

    private void UpdateSearchPlaceholder()
    {
        SearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SyncPreferencesPanel()
    {
        PreferencesTitle.Text = t("preferences");
        PreferencesSubtitle.Text = t("preferencesSubtitle");
        ClosePreferencesButton.Content = t("close");
        PreferencesDisplayLabel.Text = t("preferencesDisplay");
        LanguageLabel.Text = t("language");
        ChineseLanguageButton.Content = t("chinese");
        EnglishLanguageButton.Content = t("english");
        ChineseLanguageButton.Style = (Style)FindResource(_text.IsChinese ? "PrimaryButton" : "IconButton");
        EnglishLanguageButton.Style = (Style)FindResource(_text.IsChinese ? "IconButton" : "PrimaryButton");
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
