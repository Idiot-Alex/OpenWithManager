# OpenWith Manager

A Windows desktop MVP for viewing and managing file default app associations.

This project uses:

- WPF/XAML for the native Windows interface
- C# services for registry reads, Shell association handler discovery, and Windows Settings launch

## Current MVP

- View common file extensions and their current default ProgID
- Search by extension, category, app name, or ProgID
- Filter by file type
- Open Windows default apps settings
- Show candidate apps for a selected file format
- Guide users to change a default app in Windows Settings

The app must not silently, forcibly, or programmatically change default app associations. Windows 10/11 protects default app choices, and direct registry writes or legacy Shell association APIs can be ignored or reset by the OS. Any default app change should be user-initiated and confirmed in official Windows Settings pages.

Windows Settings does not expose a stable public URI that opens the candidate app picker for a specific extension such as `.js`. The app can open the default apps page or an app-specific defaults page when Windows supports it, then help the user search for the extension.

## Requirements

- Windows 10 or Windows 11
- Visual Studio 2022 with .NET desktop development workload
- .NET 8 SDK

## Run

Open `OpenWithManager.sln` in Visual Studio on Windows, restore NuGet packages, and run `OpenWithManager.App`.

Or from a Windows terminal with .NET SDK installed:

```powershell
dotnet restore
dotnet run --project .\src\OpenWithManager.App\OpenWithManager.App.csproj
```

## Project Structure

```text
src/OpenWithManager.App/
  MainWindow.xaml
  MainWindow.xaml.cs
  Models/
  Services/
  Resources/
    Styles.xaml
  ViewModels/
```

## Next Good Features

- Add custom extension tracking
- Improve installed app discovery
- Add safer per-extension help text
- Add system restore point reminder before advanced changes
- Add clearer before/after review for user-confirmed association changes
