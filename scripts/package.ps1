param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$Version = "",

    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "OpenWithManager.sln"
$projectPath = Join-Path $repoRoot "src\OpenWithManager.App\OpenWithManager.App.csproj"
$projectDir = Split-Path -Parent $projectPath
$artifactsDir = Join-Path $repoRoot "artifacts"
$publishRoot = Join-Path $artifactsDir "publish"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found. Install the .NET 8 SDK before packaging."
}

if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw "Solution file was not found: $solutionPath"
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project file was not found: $projectPath"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath

function Get-ProjectProperty {
    param([Parameter(Mandatory)][string]$Name)

    foreach ($propertyGroup in $projectXml.Project.PropertyGroup) {
        $node = $propertyGroup.SelectSingleNode($Name)
        if ($null -ne $node -and -not [string]::IsNullOrWhiteSpace($node.InnerText)) {
            return $node.InnerText.Trim()
        }
    }

    return ""
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectProperty -Name "Version"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version is missing. Set <Version> in the project file or pass -Version."
}

$applicationIcon = Get-ProjectProperty -Name "ApplicationIcon"

if ([string]::IsNullOrWhiteSpace($applicationIcon)) {
    Write-Warning "No application icon is configured. Add an .ico and set <ApplicationIcon> before a public release."
}
else {
    $iconPath = Join-Path $projectDir $applicationIcon
    if (-not (Test-Path -LiteralPath $iconPath)) {
        Write-Warning "Application icon is configured but missing: $iconPath"
    }
}

$publishDir = Join-Path $publishRoot $Runtime
$packageName = "OpenWithManager-$Version-$Runtime"
$packageDir = Join-Path $artifactsDir $packageName
$zipPath = Join-Path $artifactsDir "$packageName.zip"

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

foreach ($path in @($publishDir, $packageDir, $zipPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

if (-not $SkipRestore) {
    dotnet restore $solutionPath
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDir `
    -p:UseAppHost=true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

$exePath = Join-Path $publishDir "OpenWithManager.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Publish completed, but OpenWithManager.exe was not found in $publishDir"
}

New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Get-ChildItem -LiteralPath $publishDir -Force | Copy-Item -Destination $packageDir -Recurse -Force

$readmePath = Join-Path $repoRoot "README.md"
if (Test-Path -LiteralPath $readmePath) {
    Copy-Item -LiteralPath $readmePath -Destination (Join-Path $packageDir "README.md") -Force
}

$manifestPath = Join-Path $packageDir "PACKAGE.txt"
@(
    "Name: OpenWith Manager"
    "Version: $Version"
    "Runtime: $Runtime"
    "Configuration: $Configuration"
    "Self-contained: true"
    "Single-file: false"
    "Trimmed: false"
    "Generated: $((Get-Date).ToString("u"))"
) | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force

$zipItem = Get-Item -LiteralPath $zipPath
Write-Host "Package created:"
Write-Host "  Folder: $packageDir"
Write-Host "  Zip:    $zipPath"
Write-Host ("  Size:   {0:N1} MB" -f ($zipItem.Length / 1MB))
