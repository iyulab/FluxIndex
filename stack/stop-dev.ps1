<#
.SYNOPSIS
    FluxIndex Service Development Environment Stopper

.DESCRIPTION
    Stops all development services including Docker infrastructure.

.EXAMPLE
    .\stop-dev.ps1

.EXAMPLE
    .\stop-dev.ps1 -KeepDocker
#>

param(
    [switch]$KeepDocker,
    [switch]$Help
)

$ErrorActionPreference = "Continue"

# Colors for output
function Write-Status { param($Message) Write-Host "[*] $Message" -ForegroundColor Cyan }
function Write-Success { param($Message) Write-Host "[+] $Message" -ForegroundColor Green }
function Write-Warning { param($Message) Write-Host "[!] $Message" -ForegroundColor Yellow }

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ($Help) {
    Write-Host @"

FluxIndex Service Development Stopper
=====================================

Usage: .\stop-dev.ps1 [options]

Options:
    -KeepDocker     Keep Docker infrastructure running
    -Help           Show this help message

"@
    exit 0
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  FluxIndex Service - Stopping Dev     " -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# Kill dotnet watch processes for this project
Write-Status "Stopping backend processes..."
$dotnetProcesses = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*FluxIndex.Stack*" -or $_.CommandLine -like "*watch*" }

if ($dotnetProcesses) {
    $dotnetProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Success "Backend processes stopped"
} else {
    Write-Host "    No backend processes found" -ForegroundColor Gray
}

# Kill Node processes for Vite
Write-Status "Stopping frontend processes..."
$nodeProcesses = Get-Process -Name "node" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*vite*" -or $_.CommandLine -like "*FluxIndex*" }

if ($nodeProcesses) {
    $nodeProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Success "Frontend processes stopped"
} else {
    Write-Host "    No frontend processes found" -ForegroundColor Gray
}

# Stop Docker infrastructure
if (-not $KeepDocker) {
    Write-Status "Stopping Docker infrastructure..."

    Push-Location $ScriptDir
    try {
        if (Test-Path "docker-compose.dev.yml") {
            docker-compose -f docker-compose.dev.yml down

            if ($?) {
                Write-Success "Docker infrastructure stopped"
            }
        } else {
            Write-Warning "docker-compose.dev.yml not found"
        }
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "    Docker infrastructure kept running (--KeepDocker)" -ForegroundColor Gray
}

Write-Host ""
Write-Success "Development environment stopped"
Write-Host ""
