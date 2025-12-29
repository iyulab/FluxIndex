<#
.SYNOPSIS
    FluxIndex Service Development Environment Starter

.DESCRIPTION
    Starts all development services including Docker infrastructure,
    backend API with hot-reload, and frontend with Vite HMR.
    Each service runs in a separate Windows Terminal tab.

.EXAMPLE
    .\start-dev.ps1

.EXAMPLE
    .\start-dev.ps1 -SkipDocker

.EXAMPLE
    .\start-dev.ps1 -BackendOnly
#>

param(
    [switch]$SkipDocker,
    [switch]$BackendOnly,
    [switch]$FrontendOnly,
    [switch]$WithTools,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

# Colors for output
function Write-Status { param($Message) Write-Host "[*] $Message" -ForegroundColor Cyan }
function Write-Success { param($Message) Write-Host "[+] $Message" -ForegroundColor Green }
function Write-Warning { param($Message) Write-Host "[!] $Message" -ForegroundColor Yellow }
function Write-Error { param($Message) Write-Host "[-] $Message" -ForegroundColor Red }

# Script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ServiceDir = $ScriptDir
$BackendDir = Join-Path $ServiceDir "src\FluxIndex.Stack.Api"
$FrontendDir = Join-Path $ServiceDir "frontend"

# Display help
if ($Help) {
    Write-Host @"

FluxIndex Service Development Starter
=====================================

Usage: .\start-dev.ps1 [options]

Options:
    -SkipDocker     Skip starting Docker infrastructure
    -BackendOnly    Start only the backend API
    -FrontendOnly   Start only the frontend
    -WithTools      Include development tools (Redis Commander)
    -Help           Show this help message

Services:
    Docker:    PostgreSQL (5433), Qdrant (6335/6336), Neo4j (7475/7688), Redis (6381)
    Backend:   ASP.NET Core API on http://localhost:6100
    Frontend:  Vite dev server on http://localhost:6101

"@
    exit 0
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  FluxIndex Service - Dev Environment  " -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# Check prerequisites
Write-Status "Checking prerequisites..."

# Check Docker
if (-not $SkipDocker -and -not $FrontendOnly) {
    $dockerRunning = docker info 2>$null
    if (-not $?) {
        Write-Error "Docker is not running. Please start Docker Desktop."
        exit 1
    }
    Write-Success "Docker is running"
}

# Check dotnet
if (-not $FrontendOnly) {
    $dotnetVersion = dotnet --version 2>$null
    if (-not $?) {
        Write-Error ".NET SDK is not installed"
        exit 1
    }
    Write-Success ".NET SDK $dotnetVersion found"
}

# Check Node.js
if (-not $BackendOnly) {
    $nodeVersion = node --version 2>$null
    if (-not $?) {
        Write-Error "Node.js is not installed"
        exit 1
    }
    Write-Success "Node.js $nodeVersion found"
}

# Check Windows Terminal
$wtExists = Get-Command wt -ErrorAction SilentlyContinue
if (-not $wtExists) {
    Write-Warning "Windows Terminal not found. Services will run in current window."
    $useWindowsTerminal = $false
} else {
    Write-Success "Windows Terminal found"
    $useWindowsTerminal = $true
}

Write-Host ""

# Start Docker infrastructure
if (-not $SkipDocker -and -not $FrontendOnly) {
    Write-Status "Checking Docker infrastructure..."

    # Check if required containers are already running
    $postgresRunning = docker ps --filter "name=fluxindex-postgres" --filter "status=running" -q 2>$null
    $qdrantRunning = docker ps --filter "name=fluxindex-qdrant" --filter "status=running" -q 2>$null
    $redisRunning = docker ps --filter "name=fluxindex-redis" --filter "status=running" -q 2>$null
    $neo4jRunning = docker ps --filter "name=fluxindex-neo4j" --filter "status=running" -q 2>$null

    if ($postgresRunning -and $qdrantRunning -and $redisRunning -and $neo4jRunning) {
        Write-Success "Docker infrastructure already running"
        Write-Host "    PostgreSQL: localhost:5433" -ForegroundColor Gray
        Write-Host "    Qdrant:     localhost:6335 (REST), localhost:6336 (gRPC)" -ForegroundColor Gray
        Write-Host "    Redis:      localhost:6381" -ForegroundColor Gray
        Write-Host "    Neo4j:      localhost:7475 (browser), localhost:7688 (bolt)" -ForegroundColor Gray
    } else {
        Write-Status "Starting Docker infrastructure..."

        Push-Location $ServiceDir
        try {
            # Check if docker-compose.dev.yml exists
            if (-not (Test-Path "docker-compose.dev.yml")) {
                Write-Error "docker-compose.dev.yml not found in $ServiceDir"
                exit 1
            }

            # Build docker-compose command
            $composeCmd = "docker-compose -f docker-compose.dev.yml"
            if ($WithTools) {
                $composeCmd += " --profile tools"
            }
            $composeCmd += " up -d"

            # Start containers
            Invoke-Expression $composeCmd 2>&1 | Out-Null

            if ($LASTEXITCODE -eq 0) {
                Write-Success "Docker infrastructure started"
                Write-Host "    PostgreSQL: localhost:5433" -ForegroundColor Gray
                Write-Host "    Qdrant:     localhost:6335 (REST), localhost:6336 (gRPC)" -ForegroundColor Gray
                Write-Host "    Redis:      localhost:6381" -ForegroundColor Gray
                Write-Host "    Neo4j:      localhost:7475 (browser), localhost:7688 (bolt)" -ForegroundColor Gray
                if ($WithTools) {
                    Write-Host "    Redis Commander: localhost:8081" -ForegroundColor Gray
                }
            } else {
                Write-Error "Failed to start Docker infrastructure"
                Write-Host "    Try running: docker-compose -f docker-compose.dev.yml up -d" -ForegroundColor Yellow
                exit 1
            }
        }
        finally {
            Pop-Location
        }

        # Wait for services to be ready
        Write-Status "Waiting for services to be ready..."
        Start-Sleep -Seconds 5
    }

    # Health check for PostgreSQL
    Write-Status "Checking PostgreSQL..."
    $maxRetries = 15
    $retryCount = 0
    while ($retryCount -lt $maxRetries) {
        $pgReady = docker exec fluxindex-postgres pg_isready -U fluxindex 2>$null
        if ($?) {
            Write-Success "PostgreSQL is ready"
            break
        }
        $retryCount++
        Write-Host "    Waiting for PostgreSQL... ($retryCount/$maxRetries)" -ForegroundColor Gray
        Start-Sleep -Seconds 2
    }
    if ($retryCount -eq $maxRetries) {
        Write-Warning "PostgreSQL may not be fully ready yet"
    }

    # Health check for Qdrant
    Write-Status "Checking Qdrant..."
    $maxRetries = 10
    $retryCount = 0
    while ($retryCount -lt $maxRetries) {
        try {
            $qdrantReady = Invoke-RestMethod -Uri "http://localhost:6335/readyz" -Method Get -ErrorAction SilentlyContinue
            Write-Success "Qdrant is ready"
            break
        } catch {
            $retryCount++
            Write-Host "    Waiting for Qdrant... ($retryCount/$maxRetries)" -ForegroundColor Gray
            Start-Sleep -Seconds 2
        }
    }
    if ($retryCount -eq $maxRetries) {
        Write-Warning "Qdrant may not be fully ready yet"
    }

    Write-Host ""
}

# Function to start service in Windows Terminal tab
function Start-ServiceInTab {
    param(
        [string]$Title,
        [string]$WorkingDir,
        [string]$Command,
        [string]$TabColor
    )

    if ($useWindowsTerminal) {
        # Use Windows Terminal with new tab
        $wtCommand = "wt -w 0 nt --title `"$Title`" --tabColor `"$TabColor`" -d `"$WorkingDir`" cmd /k `"$Command`""
        Start-Process powershell -ArgumentList "-Command", $wtCommand -NoNewWindow
    } else {
        # Fallback: Start in new PowerShell window
        Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$WorkingDir'; $Command" -WorkingDirectory $WorkingDir
    }
}

# Start Backend API with hot-reload
if (-not $FrontendOnly) {
    Write-Status "Starting Backend API with hot-reload..."

    if (-not (Test-Path $BackendDir)) {
        Write-Error "Backend directory not found: $BackendDir"
        exit 1
    }

    if ($useWindowsTerminal) {
        # Start backend in a NEW Windows Terminal window (not attached to existing)
        wt --title "FluxIndex API" --tabColor "#512BD4" -d $BackendDir cmd /k "dotnet watch run --urls http://localhost:6100"
    } else {
        Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$BackendDir'; dotnet watch run --urls http://localhost:6100" -WorkingDirectory $BackendDir
    }

    Write-Success "Backend API starting on http://localhost:6100"
    Write-Host "    Swagger UI: http://localhost:6100/swagger" -ForegroundColor Gray
    Write-Host ""

    # Wait for Windows Terminal window to be created before adding frontend tab
    Start-Sleep -Seconds 3
}

# Start Frontend with Vite HMR
if (-not $BackendOnly) {
    Write-Status "Starting Frontend with Vite HMR..."

    if (-not (Test-Path $FrontendDir)) {
        Write-Error "Frontend directory not found: $FrontendDir"
        exit 1
    }

    # Check if node_modules exists
    $nodeModulesDir = Join-Path $FrontendDir "node_modules"
    if (-not (Test-Path $nodeModulesDir)) {
        Write-Status "Installing frontend dependencies..."
        Push-Location $FrontendDir
        npm install
        Pop-Location
    }

    if ($useWindowsTerminal) {
        # Add frontend as a new tab in the backend's terminal window (most recent window)
        wt -w 0 nt --title "FluxIndex UI" --tabColor "#61DAFB" -d $FrontendDir cmd /k "npm run dev -- --port 6101"
    } else {
        Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$FrontendDir'; npm run dev -- --port 6101" -WorkingDirectory $FrontendDir
    }

    Write-Success "Frontend starting on http://localhost:6101"
    Write-Host ""
}

# Wait for backend to be ready and get/create API key
if (-not $FrontendOnly) {
    Write-Status "Waiting for Backend API to be ready..."
    $maxRetries = 30
    $retryCount = 0
    $apiReady = $false

    while ($retryCount -lt $maxRetries) {
        try {
            $healthCheck = Invoke-RestMethod -Uri "http://localhost:6100/health" -Method Get -TimeoutSec 2 -ErrorAction SilentlyContinue
            $apiReady = $true
            Write-Success "Backend API is ready"
            break
        } catch {
            $retryCount++
            if ($retryCount % 5 -eq 0) {
                Write-Host "    Waiting for API... ($retryCount/$maxRetries)" -ForegroundColor Gray
            }
            Start-Sleep -Seconds 1
        }
    }

    if ($apiReady) {
        # Try to get existing API keys or create a dev key
        try {
            $apiKeysResponse = Invoke-RestMethod -Uri "http://localhost:6100/api/v1/apikeys" -Method Get -TimeoutSec 5 -ErrorAction SilentlyContinue
            $existingKeys = $apiKeysResponse.data

            if ($existingKeys -and $existingKeys.Count -gt 0) {
                $devKey = $existingKeys | Where-Object { $_.name -eq "Development" } | Select-Object -First 1
                if ($devKey) {
                    Write-Success "Development API Key exists (prefix: $($devKey.keyPrefix))"
                }
            } else {
                # Create a new development API key
                $body = @{
                    name = "Development"
                    role = "Admin"
                } | ConvertTo-Json

                $newKeyResponse = Invoke-RestMethod -Uri "http://localhost:6100/api/v1/apikeys" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 5 -ErrorAction SilentlyContinue

                if ($newKeyResponse.data.rawKey) {
                    Write-Host ""
                    Write-Host "========================================" -ForegroundColor Yellow
                    Write-Host "  NEW API KEY CREATED                  " -ForegroundColor Yellow
                    Write-Host "========================================" -ForegroundColor Yellow
                    Write-Host ""
                    Write-Host "  API Key: $($newKeyResponse.data.rawKey)" -ForegroundColor Cyan
                    Write-Host "  Prefix:  $($newKeyResponse.data.keyPrefix)" -ForegroundColor Gray
                    Write-Host ""
                    Write-Host "  Save this key! It won't be shown again." -ForegroundColor Yellow
                    Write-Host ""
                }
            }
        } catch {
            Write-Host "    Note: Could not check/create API key (dev mode allows keyless access)" -ForegroundColor Gray
        }
    } else {
        Write-Warning "Backend API did not start in time. Check the backend terminal for errors."
    }

    Write-Host ""
}

# Summary
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Development Environment Ready!       " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Services:" -ForegroundColor White

if (-not $SkipDocker -and -not $FrontendOnly) {
    Write-Host "  [Docker]   PostgreSQL    http://localhost:5433" -ForegroundColor Gray
    Write-Host "  [Docker]   Qdrant        http://localhost:6335" -ForegroundColor Gray
    Write-Host "  [Docker]   Redis         http://localhost:6381" -ForegroundColor Gray
    Write-Host "  [Docker]   Neo4j         http://localhost:7475" -ForegroundColor Gray
    if ($WithTools) {
        Write-Host "  [Docker]   Redis Cmd     http://localhost:8081" -ForegroundColor Gray
    }
}

if (-not $FrontendOnly) {
    Write-Host "  [Backend]  API           http://localhost:6100" -ForegroundColor Cyan
    Write-Host "  [Backend]  Swagger       http://localhost:6100/swagger" -ForegroundColor Cyan
}

if (-not $BackendOnly) {
    Write-Host "  [Frontend] Vite Dev      http://localhost:6101" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Hot-reload is enabled for both backend and frontend." -ForegroundColor Green
Write-Host "Edit files and see changes automatically!" -ForegroundColor Green
Write-Host ""
Write-Host "To stop services:" -ForegroundColor White
Write-Host "  - Close the terminal tabs for backend/frontend" -ForegroundColor Gray
Write-Host "  - Run: docker-compose -f docker-compose.dev.yml down" -ForegroundColor Gray
Write-Host ""
