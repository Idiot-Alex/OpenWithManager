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
    private readonly WindowsSettingsService _settings = new();
    private readonly ExportImportService _exports = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public MainWindow()
    {
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

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BridgeRequest? request = null;

        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(e.WebMessageAsJson, _jsonOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.Action))
            {
                throw new InvalidOperationException("Invalid bridge request.");
            }

            var data = await HandleRequestAsync(request);
            await ReplyAsync(request.Id, true, data, null);
        }
        catch (Exception ex)
        {
            await ReplyAsync(request?.Id, false, null, ex.Message);
        }
    }

    private Task<object?> HandleRequestAsync(BridgeRequest request)
    {
        object? result = request.Action switch
        {
            "associations:list" => _fileAssociations.GetKnownAssociations(),
            "settings:openDefaultApps" => OpenDefaultApps(),
            "settings:openExtension" => OpenExtensionSettings(request.Payload),
            "config:export" => ExportConfig(),
            "config:import" => ImportConfig(),
            _ => throw new InvalidOperationException($"Unknown action: {request.Action}")
        };

        return Task.FromResult(result);
    }

    private object OpenDefaultApps()
    {
        _settings.OpenDefaultApps();
        return new { opened = true };
    }

    private object OpenExtensionSettings(JsonElement payload)
    {
        var extension = payload.TryGetProperty("extension", out var value) ? value.GetString() : null;
        _settings.OpenDefaultApps(extension);
        return new { opened = true };
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

    private Task ReplyAsync(string? id, bool ok, object? data, string? error)
    {
        var response = JsonSerializer.Serialize(new
        {
            id,
            ok,
            data,
            error
        }, _jsonOptions);

        Browser.CoreWebView2.PostWebMessageAsJson(response);
        return Task.CompletedTask;
    }

    private sealed record BridgeRequest(string? Id, string Action, JsonElement Payload);
}
