# publish-local-cli.ps1
# Publishes FluxIndex.CLI as a global dotnet tool for local testing

param(
    [switch]$Force,
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$CliProject = Join-Path $ProjectRoot "src\FluxIndex.CLI\FluxIndex.CLI.csproj"
$PackageOutputPath = Join-Path $ProjectRoot "artifacts\packages"
$ToolName = "fluxindex"
$PackageId = "FluxIndex.CLI"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " FluxIndex CLI Local Publisher" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if project exists
if (-not (Test-Path $CliProject)) {
    Write-Host "ERROR: CLI project not found at: $CliProject" -ForegroundColor Red
    exit 1
}

# Step 1: Uninstall existing tool if present
Write-Host "[1/4] Checking for existing installation..." -ForegroundColor Yellow
$existingTool = dotnet tool list -g | Select-String $PackageId
if ($existingTool) {
    Write-Host "  -> Uninstalling existing $PackageId..." -ForegroundColor Gray
    dotnet tool uninstall -g $PackageId 2>&1 | Out-Null
    Write-Host "  -> Uninstalled successfully" -ForegroundColor Green
} else {
    Write-Host "  -> No existing installation found" -ForegroundColor Gray
}

# Step 2: Clean and create package output directory
Write-Host "[2/4] Preparing package directory..." -ForegroundColor Yellow
if (Test-Path $PackageOutputPath) {
    Remove-Item -Path $PackageOutputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $PackageOutputPath -Force | Out-Null
Write-Host "  -> Package directory: $PackageOutputPath" -ForegroundColor Gray

# Step 3: Pack the CLI project
Write-Host "[3/4] Packing CLI project..." -ForegroundColor Yellow
$packArgs = @(
    "pack",
    $CliProject,
    "-c", "Release",
    "-o", $PackageOutputPath
)
if ($Verbose) {
    $packArgs += "--verbosity", "normal"
} else {
    $packArgs += "--verbosity", "quiet"
}

$packResult = & dotnet @packArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to pack CLI project" -ForegroundColor Red
    Write-Host $packResult
    exit 1
}

# Find the generated package
$package = Get-ChildItem -Path $PackageOutputPath -Filter "*.nupkg" | Select-Object -First 1
if (-not $package) {
    Write-Host "ERROR: No package file found in $PackageOutputPath" -ForegroundColor Red
    exit 1
}
Write-Host "  -> Package created: $($package.Name)" -ForegroundColor Green

# Step 4: Install as global tool
Write-Host "[4/4] Installing as global tool..." -ForegroundColor Yellow
$packageVersion = $package.Name -replace "FluxIndex\.CLI\.", "" -replace "\.nupkg$", ""
$installArgs = @(
    "tool", "install",
    "-g", $PackageId,
    "--version", $packageVersion,
    "--add-source", $PackageOutputPath
)

$installResult = & dotnet @installArgs 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to install tool" -ForegroundColor Red
    Write-Host $installResult
    exit 1
}
Write-Host "  -> Installed successfully (version $packageVersion)" -ForegroundColor Green

# Verify installation
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Installation Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Show tool info
Write-Host "Tool Information:" -ForegroundColor Yellow
$toolInfo = dotnet tool list -g | Select-String $ToolName
Write-Host "  $toolInfo" -ForegroundColor Gray
Write-Host ""

# Show usage examples
Write-Host "Usage Examples:" -ForegroundColor Yellow
Write-Host "  fluxindex --help           # Show help" -ForegroundColor Gray
Write-Host "  fluxindex init             # Initialize FluxIndex" -ForegroundColor Gray
Write-Host "  fluxindex memorize <path>  # Memorize documents" -ForegroundColor Gray
Write-Host "  fluxindex status           # Show status" -ForegroundColor Gray
Write-Host "  fluxindex serve            # Start MCP server" -ForegroundColor Gray
Write-Host ""

# Quick verification
Write-Host "Quick Verification:" -ForegroundColor Yellow
try {
    $helpOutput = & $ToolName --help 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  -> CLI is working correctly!" -ForegroundColor Green
    } else {
        Write-Host "  -> Warning: CLI returned non-zero exit code" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  -> Warning: Could not verify CLI execution" -ForegroundColor Yellow
    Write-Host "     You may need to restart your terminal" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
