#!/usr/bin/env pwsh
# FluxIndex Local Build Script
# Builds and publishes FluxIndex packages to local NuGet feed

param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "D:\data\FluxIndex\nupkg",
    [switch]$CleanFirst
)

$ErrorActionPreference = "Stop"

Write-Host "===================================" -ForegroundColor Cyan
Write-Host "FluxIndex Local Build Script" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan
Write-Host ""

# Change to FluxIndex directory
Set-Location "D:\data\FluxIndex"

# Clean if requested
if ($CleanFirst) {
    Write-Host "Cleaning previous build artifacts..." -ForegroundColor Yellow
    dotnet clean FluxIndex.slnx -c $Configuration
    if (Test-Path $OutputPath) {
        Remove-Item "$OutputPath\FluxIndex*.nupkg" -Force -ErrorAction SilentlyContinue
    }
}

# Ensure output directory exists
if (-not (Test-Path $OutputPath)) {
    New-Item -Path $OutputPath -ItemType Directory | Out-Null
}

# Restore dependencies
Write-Host "Restoring dependencies..." -ForegroundColor Yellow
dotnet restore FluxIndex.slnx

# Build
Write-Host "Building FluxIndex..." -ForegroundColor Yellow
dotnet build FluxIndex.slnx -c $Configuration --no-restore

# Run tests (skip Redis tests as they may not be available locally)
Write-Host "Running tests..." -ForegroundColor Yellow
dotnet test FluxIndex.slnx -c $Configuration --no-build --verbosity minimal --filter "FullyQualifiedName!~Redis"

# Pack all FluxIndex packages
Write-Host "Creating NuGet packages..." -ForegroundColor Yellow

$packableProjects = @(
    "src/FluxIndex/FluxIndex.csproj",
    "src/FluxIndex.SDK/FluxIndex.SDK.csproj",
    "src/FluxIndex.AI.OpenAI/FluxIndex.AI.OpenAI.csproj",
    "src/FluxIndex.Storage.SQLite/FluxIndex.Storage.SQLite.csproj",
    "src/FluxIndex.Storage.PostgreSQL/FluxIndex.Storage.PostgreSQL.csproj",
    "src/FluxIndex.Cache.Redis/FluxIndex.Cache.Redis.csproj",
    "src/FluxIndex.Extensions.FileFlux/FluxIndex.Extensions.FileFlux.csproj"
)

foreach ($project in $packableProjects) {
    if (Test-Path $project) {
        Write-Host "  Packing $project..." -ForegroundColor Gray
        dotnet pack $project -c $Configuration --no-build -o $OutputPath
    }
}

# List created packages
Write-Host ""
Write-Host "===================================" -ForegroundColor Green
Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "===================================" -ForegroundColor Green
Write-Host ""
Write-Host "Created packages:" -ForegroundColor Green
Get-ChildItem "$OutputPath\FluxIndex*.nupkg" | Sort-Object LastWriteTime -Descending | Select-Object -First 10 | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "To use these packages in FilerBasis:" -ForegroundColor Yellow
Write-Host "  1. Update package references in Filer.Basis.FluxIndex.csproj" -ForegroundColor Gray
Write-Host "  2. Run: dotnet restore" -ForegroundColor Gray
Write-Host ""
