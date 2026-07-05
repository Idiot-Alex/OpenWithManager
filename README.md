# OpenWith Manager

OpenWith Manager is a Windows desktop app for inspecting file default app
associations and guiding users to the official Windows Settings flow when they
want to change one.

The app is intentionally read-first. It helps users understand what Windows is
currently doing; it does not silently force-write default app associations or
bypass Windows default-app protections.

## Current App

- Browse common file kinds with a localized summary and current app icon group.
- Search by file kind, extension, description, app name, ProgID, or source.
- Filter to file kinds that need review.
- Inspect every included format for a file kind and see the current default app.
- Select a single format to view available apps and Windows Settings actions.
- Open Windows Default Apps or app-specific defaults pages when Windows supports
  that route.
- Configure preferences for language, technical details, candidate source labels,
  and auto-refresh after returning from Windows Settings.

The app does not currently include import, export, or compare workflows. Those
features were removed until there is a clear ordinary-user workflow for them.

## Safety Model

Do not force-write default app associations. In this project, that means:

- Do not change default apps without clear user intent and user confirmation.
- Do not silently write protected association registry values.
- Do not present a programmatic change as successful unless Windows actually
  reports or reflects that change.
- Prefer read-only registry and Shell inspection, then hand the user to official
  Windows Settings pages for changes.

Windows 10 and Windows 11 protect default app choices. Direct registry writes or
legacy Shell association APIs can be ignored, reset, or treated as unsafe by the
OS.

## Windows Settings Limits

Windows does not expose a stable public URI that opens the candidate app picker
for a specific extension such as `.js`. OpenWith Manager can open the default
apps page or, when available, an app-specific defaults page. The user may still
need to paste or search for the extension in Windows Settings.

## Requirements

- Windows 10 or Windows 11
- Visual Studio 2022 with the .NET desktop development workload
- .NET 8 SDK

## Build, Run, and Test

Run commands from the repository root on Windows:

```powershell
dotnet restore
dotnet build OpenWithManager.sln
dotnet run --project .\src\OpenWithManager.App\OpenWithManager.App.csproj
dotnet test
```

`dotnet test` is the expected command once a test project is added. No test
project exists yet.

## Project Structure

```text
OpenWithManager.sln
src/OpenWithManager.App/
  MainWindow.xaml
  MainWindow.xaml.cs
  Models/
  Resources/
    Styles.xaml
  Services/
  ViewModels/
```

## Manual Verification Checklist

- Build and run on Windows 10 or Windows 11.
- Confirm the left file kind list shows one row per file kind, localized summary
  text, and deduplicated default-app icon badges.
- Check a multi-format kind such as images and confirm each format row shows the
  correct current app.
- Select formats such as `.PDF`, `.PNG`, `.JS`, and `.TS` and verify the action
  panel stays visible while the format list scrolls.
- Confirm candidate app icons, fallback initials, and source labels behave
  consistently.
- Open preferences as an overlay and verify language, source-label, technical
  details, and auto-refresh options.
- Open Windows Settings from the app and confirm the app does not claim success
  until refreshed data actually changes.

## Next Priorities

- Add focused tests for file kind grouping, app identity deduplication, summary
  text, and icon location parsing.
- Improve diagnostics when Windows Settings or Shell association lookup cannot
  provide a reliable result.
- Continue small refactors that keep `MainWindow` focused on layout and move
  data shaping into services or view models.
- Validate packaged app icon handling on Windows with Photos, Paint, Snipping
  Tool, VS Code, and common browsers.
