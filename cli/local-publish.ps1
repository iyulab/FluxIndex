# FluxIndex CLI Local Publish Script
# Publishes the CLI tool as a .NET global tool for local testing

$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir "FluxIndex.CLI\FluxIndex.CLI.csproj"
$NupkgDir = Join-Path $ProjectDir "FluxIndex.CLI\nupkg"

Write-Host "Building and publishing FluxIndex CLI..." -ForegroundColor Cyan

# Uninstall existing version if present (ignore errors)
Write-Host "Removing existing installation..." -ForegroundColor Yellow
try { dotnet tool uninstall -g FluxIndex.CLI 2>&1 | Out-Null } catch { }

# Clean previous packages
if (Test-Path $NupkgDir) {
    Remove-Item "$NupkgDir\*.nupkg" -Force -ErrorAction SilentlyContinue
}

$ErrorActionPreference = "Stop"

# Build and pack the tool
Write-Host "Building and packing tool..." -ForegroundColor Yellow
dotnet build $ProjectFile -c Release
dotnet pack $ProjectFile -c Release --no-build -o $NupkgDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Pack failed!" -ForegroundColor Red
    exit 1
}

# Find the latest package
$Package = Get-ChildItem "$NupkgDir\FluxIndex.CLI.*.nupkg" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $Package) {
    Write-Host "No package found in $NupkgDir!" -ForegroundColor Red
    Write-Host "Checking PackageOutputPath..." -ForegroundColor Yellow
    exit 1
}

Write-Host "Installing from: $($Package.Name)" -ForegroundColor Yellow

# Install as global tool (using PackageId, not ToolCommandName)
dotnet tool install -g FluxIndex.CLI --add-source "$NupkgDir"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Install failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "FluxIndex CLI installed successfully!" -ForegroundColor Green
Write-Host "Run 'fluxindex --help' to get started." -ForegroundColor Cyan
