# Repository Guidelines

## Project Structure & Module Organization

- `OpenWithManager.sln` is the solution entry point.
- `src/OpenWithManager.App/` contains the WPF/WebView2 app project.
- `MainWindow.xaml` and `MainWindow.xaml.cs` host the WPF shell and WebView2 bridge.
- `Models/` contains data types for associations and diffs.
- `Services/` contains registry reads, Windows Settings launch logic, export, and import comparison.
- `Web/` contains the embedded interface: `index.html`, `styles.css`, and `app.js`.

No test project exists yet. Add future tests under a parallel `tests/` directory, for example `tests/OpenWithManager.App.Tests/`.

## Build, Test, and Development Commands

Run commands from the repository root on Windows with .NET 8 installed.

```powershell
dotnet restore
dotnet build OpenWithManager.sln
dotnet run --project .\src\OpenWithManager.App\OpenWithManager.App.csproj
dotnet test
```

- `dotnet restore` downloads NuGet packages, including WebView2.
- `dotnet build` compiles the WPF application.
- `dotnet run` starts the desktop app locally.
- `dotnet test` is the expected command once a test project is added.

Visual Studio 2022 with the .NET desktop workload is also supported.

## Coding Style & Naming Conventions

Use nullable reference types and implicit usings as configured in the project. Follow standard C# naming: `PascalCase` for public types and methods, `_camelCase` for private readonly fields, and `camelCase` for local variables and JavaScript functions. Keep service classes focused and place shared data contracts in `Models/`.

Use 4-space indentation for C# and XAML, and 2-space indentation for HTML, CSS, and JavaScript. Prefer clear async method names ending in `Async` when they return `Task`.

## Testing Guidelines

When adding tests, prefer focused unit tests for `Services/` behavior and small integration tests around export/import JSON formats. Name test files after the class under test, such as `ExportImportServiceTests.cs`. Avoid tests that modify real Windows default app associations; mock registry-dependent behavior where possible.

## Commit & Pull Request Guidelines

The current history uses descriptive, sentence-style commit messages, for example: `Initial commit of OpenWith Manager application...`. Continue with concise summaries that explain the user-visible change.

Pull requests should include a short description, manual verification steps, linked issues when applicable, and screenshots or screen recordings for UI changes. Call out Windows version, .NET SDK version, and WebView2-related behavior when relevant.

## Security & Configuration Tips

Do not force-write default app associations. Windows 10/11 protects these choices, and direct registry writes can be ignored or reset. Prefer read-only inspection, export/import comparison, and official Windows Settings pages.
