<#
.SYNOPSIS
    Starts FluxIndex Demo (backend + frontend) in separate terminals.

.DESCRIPTION
    - Kills existing processes on ports 5011 (backend) and 5173 (frontend)
    - Starts .NET backend in a new terminal
    - Starts Vite frontend in a new terminal

.EXAMPLE
    .\start.ps1

.EXAMPLE
    .\start.ps1 -BackendOnly

.EXAMPLE
    .\start.ps1 -FrontendOnly
#>

param(
    [switch]$BackendOnly,
    [switch]$FrontendOnly,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Port configuration
$BackendPort = 5011
$FrontendPort = 5173

# Colors
function Write-Header { param($msg) Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Step { param($msg) Write-Host "  > $msg" -ForegroundColor Yellow }
function Write-Success { param($msg) Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Info { param($msg) Write-Host "  $msg" -ForegroundColor Gray }

# Kill process on port
function Stop-ProcessOnPort {
    param([int]$Port)

    $connections = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
    if ($connections) {
        $pids = $connections | Select-Object -ExpandProperty OwningProcess -Unique
        foreach ($pid in $pids) {
            $process = Get-Process -Id $pid -ErrorAction SilentlyContinue
            if ($process) {
                Write-Step "Killing process '$($process.ProcessName)' (PID: $pid) on port $Port"
                Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 500
            }
        }
        Write-Success "Port $Port cleared"
    } else {
        Write-Info "Port $Port is available"
    }
}

# Check if .env exists
function Test-EnvFile {
    $envPath = Join-Path $ScriptDir ".env"
    if (-not (Test-Path $envPath)) {
        Write-Host "`n[WARNING] .env file not found!" -ForegroundColor Red
        Write-Host "  Copy .env.example to .env and configure your API keys." -ForegroundColor Yellow
        Write-Host "  FluxImprover features will be disabled without OPENAI_API_KEY.`n" -ForegroundColor Yellow
    }
}

# Start backend
function Start-Backend {
    Write-Header "Starting Backend (Port $BackendPort)"
    Stop-ProcessOnPort $BackendPort

    $backendDir = Join-Path $ScriptDir "FluxIndex.Demo"

    if (Get-Command wt.exe -ErrorAction SilentlyContinue) {
        # Windows Terminal available - use cmd to chain commands
        wt.exe -w 0 nt --title "FluxIndex Backend" -d "$backendDir" cmd /k "dotnet run"
    } else {
        # Fallback to regular PowerShell
        Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$backendDir'; dotnet run"
    }

    Write-Success "Backend starting at http://localhost:$BackendPort"
}

# Start frontend
function Start-Frontend {
    Write-Header "Starting Frontend (Port $FrontendPort)"
    Stop-ProcessOnPort $FrontendPort

    $frontendDir = Join-Path $ScriptDir "fluxindex-ui"

    # Check if node_modules exists
    $nodeModules = Join-Path $frontendDir "node_modules"
    if (-not (Test-Path $nodeModules)) {
        Write-Step "Installing npm dependencies..."
        Push-Location $frontendDir
        npm install
        Pop-Location
    }

    if (Get-Command wt.exe -ErrorAction SilentlyContinue) {
        # Windows Terminal available - use cmd to chain commands
        wt.exe -w 0 nt --title "FluxIndex Frontend" -d "$frontendDir" cmd /k "npm run dev"
    } else {
        # Fallback to regular PowerShell
        Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$frontendDir'; npm run dev"
    }

    Write-Success "Frontend starting at http://localhost:$FrontendPort"
}

# Main
Write-Host "`n" -NoNewline
Write-Host "  FluxIndex Demo Launcher" -ForegroundColor White -BackgroundColor DarkBlue
Write-Host "  Backend: http://localhost:$BackendPort" -ForegroundColor Gray
Write-Host "  Frontend: http://localhost:$FrontendPort" -ForegroundColor Gray

Test-EnvFile

if (-not $FrontendOnly) {
    Start-Backend
}

if (-not $BackendOnly) {
    Start-Frontend
}

Write-Header "Startup Complete"

if (-not $BackendOnly -and -not $FrontendOnly) {
    Write-Info "Both services are starting in separate terminals."
    Write-Info ""
    Write-Info "URLs:"
    Write-Info "  Frontend: http://localhost:$FrontendPort"
    Write-Info "  Backend:  http://localhost:$BackendPort"
    Write-Info "  API Docs: http://localhost:$BackendPort/swagger (if enabled)"
    Write-Info ""

    if (-not $NoBrowser) {
        # Wait a bit then open browser
        Write-Step "Opening browser in 5 seconds..."
        Start-Sleep -Seconds 5
        Start-Process "http://localhost:$FrontendPort"
    }
}

Write-Host ""
