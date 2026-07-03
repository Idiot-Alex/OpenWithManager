# OpenWith Manager

A Windows desktop MVP for viewing and managing file default app associations.

This project uses:

- WPF for the native Windows host
- WebView2 for the HTML/CSS/JavaScript interface
- C# services for registry reads, Windows Settings launch, export, and import comparison

## Current MVP

- View common file extensions and their current default ProgID
- Search by extension, category, app name, or ProgID
- Filter by file type
- Open Windows default apps settings
- Export a JSON snapshot of current associations
- Import a JSON snapshot and compare it with the current machine

The first version intentionally does not force-write default app associations. Windows 10/11 protects default app choices, and direct registry writes can be ignored or reset by the OS.

## Requirements

- Windows 10 or Windows 11
- Visual Studio 2022 with .NET desktop development workload
- .NET 8 SDK
- WebView2 Runtime

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
  Web/
    index.html
    styles.css
    app.js
```

## Next Good Features

- Add custom extension tracking
- Add installed app discovery
- Add safer per-extension help text
- Add system restore point reminder before advanced changes
- Add optional automatic repair only for associations that can be changed safely
