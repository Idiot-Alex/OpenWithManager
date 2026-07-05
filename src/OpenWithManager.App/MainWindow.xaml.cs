using System.IO;
using System.Text.Json;
using System.Windows;
using OpenWithManager.App.Services;
using Microsoft.Web.WebView2.Core;
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
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public MainWindow()
    {
        _fileKinds = new FileKindService(_fileAssociations);
        _formatCandidates = new FormatCandidateService(_fileAssociations, _shellAssociations);
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
            Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            var indexPath = Path.Combine(AppContext.BaseDirectory, "Web", "index.html");
            Browser.Source = new Uri(indexPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WebView2 failed to start", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BridgeRequest? request = null;

        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(e.WebMessageAsJson, _jsonOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.Action))
            {
                throw new InvalidOperationException("Invalid bridge request.");
            }

            var data = HandleRequest(request);
            Reply(request.Id, true, data, null);
        }
        catch (Exception ex)
        {
            Reply(request?.Id, false, null, ex.Message);
        }
    }

    private object? HandleRequest(BridgeRequest request)
    {
        switch (request.Action)
        {
            case "fileKinds:list":
                return _fileKinds.GetFileKinds();

            case "associations:list":
                return _fileAssociations.GetKnownAssociations();

            case "formats:candidates":
                var extension = request.Payload.TryGetProperty("extension", out var value)
                    ? value.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(extension))
                {
                    throw new InvalidOperationException("Missing extension.");
                }

                return _formatCandidates.GetCandidates(extension);

            case "settings:openDefaultApps":
                _settings.OpenDefaultApps();
                return new { opened = true };

            case "settings:openExtension":
                var parameterName = request.Payload.TryGetProperty("settingsParameterName", out var parameterNameValue)
                    ? parameterNameValue.GetString()
                    : null;
                var parameterValue = request.Payload.TryGetProperty("settingsParameterValue", out var parameterValueValue)
                    ? parameterValueValue.GetString()
                    : null;

                _settings.OpenDefaultApps(parameterName, parameterValue);
                return new { opened = true };

            case "formats:makeDefault":
                var defaultExtension = request.Payload.TryGetProperty("extension", out var defaultExtensionValue)
                    ? defaultExtensionValue.GetString()
                    : null;
                var shellHandlerId = request.Payload.TryGetProperty("shellHandlerId", out var shellHandlerIdValue)
                    ? shellHandlerIdValue.GetString()
                    : null;
                var appName = request.Payload.TryGetProperty("appName", out var appNameValue)
                    ? appNameValue.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(defaultExtension)
                    || string.IsNullOrWhiteSpace(shellHandlerId)
                    || string.IsNullOrWhiteSpace(appName))
                {
                    throw new InvalidOperationException("Missing default app selection.");
                }

                _shellAssociations.MakeDefault(defaultExtension, shellHandlerId, appName);
                return new { changed = true };

            case "config:export":
                return ExportConfig();

            case "config:import":
                return ImportConfig();

            default:
                throw new InvalidOperationException($"Unknown action: {request.Action}");
        }
    }

    private object ExportConfig()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export default app associations",
            Filter = "JSON files (*.json)|*.json",
            FileName = $"default-app-associations-{DateTime.Now:yyyyMMdd-HHmm}.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return new { cancelled = true };
        }

        var associations = _fileAssociations.GetKnownAssociations();
        _exports.Export(dialog.FileName, associations);
        return new { cancelled = false, path = dialog.FileName, count = associations.Count };
    }

    private object ImportConfig()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import default app associations",
            Filter = "JSON files (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return new { cancelled = true };
        }

        var imported = _exports.Import(dialog.FileName);
        var current = _fileAssociations.GetKnownAssociations();
        var diff = _exports.Compare(current, imported);

        return new { cancelled = false, path = dialog.FileName, count = imported.Count, diff };
    }

    private void Reply(string? id, bool ok, object? data, string? error)
    {
        var response = JsonSerializer.Serialize(new
        {
            id,
            ok,
            data,
            error
        }, _jsonOptions);

        Browser.CoreWebView2.PostWebMessageAsJson(response);
    }

    private sealed record BridgeRequest(string? Id, string Action, JsonElement Payload);
}
