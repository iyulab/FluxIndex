<#
.SYNOPSIS
    FluxIndex Demo Setup Script for Windows
.DESCRIPTION
    Sets up Docker containers and initializes the FluxIndex demo environment
.PARAMETER Action
    The action to perform: start, stop, restart, status, clean, logs
.PARAMETER Service
    Specific service to target: all, postgres, neo4j, redis
.EXAMPLE
    .\setup.ps1 start
    .\setup.ps1 stop
    .\setup.ps1 logs postgres
#>

param(
    [Parameter(Position=0)]
    [ValidateSet("start", "stop", "restart", "status", "clean", "logs", "build", "test")]
    [string]$Action = "start",

    [Parameter(Position=1)]
    [ValidateSet("all", "postgres", "neo4j", "redis")]
    [string]$Service = "all"
)

$ErrorActionPreference = "Stop"
$DemoDir = $PSScriptRoot
$ComposeFile = Join-Path $DemoDir "docker-compose.yml"
$EnvFile = Join-Path $DemoDir ".env"
$EnvExampleFile = Join-Path $DemoDir ".env.example"

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " $Message" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Test-DockerRunning {
    try {
        $null = docker info 2>&1
        return $true
    }
    catch {
        return $false
    }
}

function Initialize-Environment {
    if (-not (Test-Path $EnvFile)) {
        if (Test-Path $EnvExampleFile) {
            Copy-Item $EnvExampleFile $EnvFile
            Write-Success "Created .env from .env.example"
        }
        else {
            Write-Warning ".env.example not found"
        }
    }
}

function Start-Services {
    Write-Header "Starting FluxIndex Demo Services"

    if (-not (Test-DockerRunning)) {
        Write-Error "Docker is not running. Please start Docker Desktop."
        exit 1
    }

    Initialize-Environment

    Push-Location $DemoDir
    try {
        if ($Service -eq "all") {
            docker compose up -d
        }
        else {
            docker compose up -d $Service
        }

        Write-Host ""
        Write-Success "Services started successfully!"
        Write-Host ""
        Write-Host "Service URLs:" -ForegroundColor Yellow
        Write-Host "  PostgreSQL: localhost:5432"
        Write-Host "  Neo4j:      http://localhost:7474 (Browser)"
        Write-Host "  Neo4j Bolt: bolt://localhost:7687"
        Write-Host "  Redis:      localhost:6379"
        Write-Host ""
        Write-Host "To start the demo application:" -ForegroundColor Yellow
        Write-Host "  cd FluxIndex.Demo && dotnet run"
        Write-Host ""
    }
    finally {
        Pop-Location
    }
}

function Stop-Services {
    Write-Header "Stopping FluxIndex Demo Services"

    Push-Location $DemoDir
    try {
        if ($Service -eq "all") {
            docker compose down
        }
        else {
            docker compose stop $Service
        }
        Write-Success "Services stopped"
    }
    finally {
        Pop-Location
    }
}

function Restart-Services {
    Stop-Services
    Start-Services
}

function Get-ServiceStatus {
    Write-Header "FluxIndex Demo Service Status"

    Push-Location $DemoDir
    try {
        docker compose ps
        Write-Host ""

        # Health checks
        Write-Host "Health Checks:" -ForegroundColor Yellow

        # PostgreSQL
        try {
            $pgResult = docker compose exec -T postgres pg_isready -U fluxindex 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Success "PostgreSQL: Healthy"
            }
            else {
                Write-Warning "PostgreSQL: Not Ready"
            }
        }
        catch {
            Write-Warning "PostgreSQL: Not Running"
        }

        # Neo4j
        try {
            $neo4jResult = Invoke-WebRequest -Uri "http://localhost:7474" -TimeoutSec 5 -ErrorAction SilentlyContinue
            Write-Success "Neo4j: Healthy (HTTP $($neo4jResult.StatusCode))"
        }
        catch {
            Write-Warning "Neo4j: Not Ready"
        }

        # Redis
        try {
            $redisResult = docker compose exec -T redis redis-cli ping 2>&1
            if ($redisResult -match "PONG") {
                Write-Success "Redis: Healthy"
            }
            else {
                Write-Warning "Redis: Not Ready"
            }
        }
        catch {
            Write-Warning "Redis: Not Running"
        }
    }
    finally {
        Pop-Location
    }
}

function Clear-Services {
    Write-Header "Cleaning FluxIndex Demo Environment"

    $confirm = Read-Host "This will remove all containers and volumes. Continue? (y/N)"
    if ($confirm -ne "y" -and $confirm -ne "Y") {
        Write-Host "Cancelled."
        return
    }

    Push-Location $DemoDir
    try {
        docker compose down -v --remove-orphans
        Write-Success "All containers and volumes removed"
    }
    finally {
        Pop-Location
    }
}

function Get-ServiceLogs {
    Write-Header "FluxIndex Demo Service Logs"

    Push-Location $DemoDir
    try {
        if ($Service -eq "all") {
            docker compose logs --tail=100 -f
        }
        else {
            docker compose logs --tail=100 -f $Service
        }
    }
    finally {
        Pop-Location
    }
}

function Build-Demo {
    Write-Header "Building FluxIndex Demo Application"

    $projectPath = Join-Path $DemoDir "FluxIndex.Demo"
    Push-Location $projectPath
    try {
        dotnet build
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Build completed successfully"
        }
        else {
            Write-Error "Build failed"
            exit 1
        }
    }
    finally {
        Pop-Location
    }
}

function Test-Api {
    Write-Header "Testing FluxIndex Demo API"

    $baseUrl = "http://localhost:5000"

    Write-Host "Testing endpoints..." -ForegroundColor Yellow

    # Health check
    try {
        $health = Invoke-RestMethod -Uri "$baseUrl/api/health" -TimeoutSec 10
        Write-Success "Health: $($health.Status)"
    }
    catch {
        Write-Error "Health check failed: $($_.Exception.Message)"
        Write-Host "Make sure the demo application is running (dotnet run)"
        return
    }

    # Status
    try {
        $status = Invoke-RestMethod -Uri "$baseUrl/api/status" -TimeoutSec 10
        Write-Success "Status: $($status.TotalDocuments) docs, $($status.TotalChunks) chunks"
        Write-Host "  Storage: $($status.StorageInfo)"
        Write-Host "  Embedding: $($status.EmbeddingModel)"
    }
    catch {
        Write-Warning "Status endpoint failed"
    }

    # Documents list
    try {
        $docs = Invoke-RestMethod -Uri "$baseUrl/api/documents" -TimeoutSec 10
        Write-Success "Documents: Retrieved $($docs.Count) documents"
    }
    catch {
        Write-Warning "Documents endpoint failed"
    }

    Write-Host ""
    Write-Host "API tests completed!" -ForegroundColor Green
}

# Main execution
switch ($Action) {
    "start"   { Start-Services }
    "stop"    { Stop-Services }
    "restart" { Restart-Services }
    "status"  { Get-ServiceStatus }
    "clean"   { Clear-Services }
    "logs"    { Get-ServiceLogs }
    "build"   { Build-Demo }
    "test"    { Test-Api }
}
