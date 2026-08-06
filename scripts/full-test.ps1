# FluxIndex local test runner (.env.local aware)
#
# A thin wrapper over test.ps1. The two scripts used to be near-duplicates that differed only
# in the banner they printed — neither loaded .env.local; they just reported whether it existed,
# and the tests read their own configuration. Keeping two copies of the runner meant fixes landed
# in one of them: this one still listed `FluxIndex.Tests.Core` and two siblings that were renamed
# long ago, so every project was "not found", nothing ran, and it always exited PASSED.
#
# Behaviour now lives in test.ps1 alone. This script reports the mode and delegates.

param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "normal",
    [switch]$NoBuild,
    [switch]$Coverage,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$envLocalPath = Join-Path $repoRoot ".env.local"

Write-Output ""
if (Test-Path $envLocalPath) {
    Write-Output "Real API mode: .env.local found at $envLocalPath"
    Write-Output "Tests that read it will call the live API, and that costs money."
} else {
    Write-Output "Mock mode: no .env.local at $envLocalPath"
    Write-Output "See .env.local.example to set up real-API runs."
}
Write-Output ""

$forwarded = @{
    Verbosity     = $Verbosity
    Configuration = $Configuration
}
if ($NoBuild)  { $forwarded.NoBuild  = $true }
if ($Coverage) { $forwarded.Coverage = $true }

& (Join-Path $PSScriptRoot "test.ps1") @forwarded
exit $LASTEXITCODE
