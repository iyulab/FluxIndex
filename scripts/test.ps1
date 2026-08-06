# FluxIndex test runner (CI and local)
#
# Enumerates test projects by discovery and requires every one of them to pass.
#
# History worth keeping in view: this script used to carry a hardcoded list of three projects —
# one of which had not existed for some time and was skipped with a warning — and gated on
# `$passRate -ge 75.0`, so a quarter of the suite could fail while the run reported success. The
# threshold's stated basis ("~78.57%, 55/70 tests") described a suite that no longer exists; the
# current one is two orders of magnitude larger. A pass-rate gate is worse than no gate: it makes
# failure look like policy.

param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",
    [switch]$NoBuild,
    [switch]$Coverage,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

function Write-ColorOutput {
    param([string]$Message, [string]$Color = "White")
    $previousColor = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $Color
    Write-Output $Message
    $host.UI.RawUI.ForegroundColor = $previousColor
}

Write-ColorOutput "`n===================================" "Cyan"
Write-ColorOutput "FluxIndex Test Runner" "Cyan"
Write-ColorOutput "===================================" "Cyan"
Write-Output ""

# Discovery, not an allowlist: a list cannot notice a project that was added, and hides one that
# was deleted behind a warning nobody reads.
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProjects = @(
    Get-ChildItem -Path (Join-Path $repoRoot "tests") -Filter "*.Tests.csproj" -Recurse -File |
        ForEach-Object { $_.FullName } |
        Sort-Object
)

if ($testProjects.Count -eq 0) {
    Write-ColorOutput "ERROR: no test projects discovered under tests/." "Red"
    Write-Output "Discovery returning nothing means a broken path or a moved tree, never 'nothing to test'."
    exit 1
}

Write-Output "Discovered $($testProjects.Count) test project(s):"
$testProjects | ForEach-Object { Write-Output "  $(Split-Path -Leaf $_)" }
Write-Output ""

# Categories excluded here are excluded for a reason that holds on any machine:
#   Integration  - needs an external service (Testcontainers/Docker, a live database)
#   Performance  - asserts on wall-clock time, which a shared runner cannot make meaningful
# Everything else must pass. Excluding by category rather than by project also covers the case an
# allowlist cannot express: a service-dependent test living inside an otherwise self-contained
# project.
$categoryFilter = "Category!=Integration&Category!=Performance"

$testArgs = @("test", "--verbosity", $Verbosity, "--configuration", $Configuration, "--filter", $categoryFilter)
if ($NoBuild)  { $testArgs += "--no-build" }
if ($Coverage) { $testArgs += "--collect:XPlat Code Coverage" }

$allResults = @()
$totalPassed = 0
$totalFailed = 0
$totalSkipped = 0
$totalTests = 0
$failedProjects = @()

foreach ($project in $testProjects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)

    Write-ColorOutput "`nRunning tests for: $projectName" "Yellow"
    Write-Output "-----------------------------------"

    $testOutput = & dotnet @testArgs $project 2>&1
    $exitCode = $LASTEXITCODE
    $testOutput | ForEach-Object { Write-Output $_ }

    $passed = 0; $failed = 0; $skipped = 0; $total = 0
    foreach ($line in $testOutput) {
        if ($line -match "(?:Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)") {
            $failed = [int]$matches[1]
            $passed = [int]$matches[2]
            $skipped = [int]$matches[3]
            $total = [int]$matches[4]
        }
    }

    $allResults += [PSCustomObject]@{
        Project = $projectName
        Passed = $passed; Failed = $failed; Skipped = $skipped; Total = $total
        ExitCode = $exitCode
    }

    $totalPassed += $passed
    $totalFailed += $failed
    $totalSkipped += $skipped
    $totalTests += $total

    if ($exitCode -eq 0) {
        if ($total -eq 0) {
            # Not a failure: a project whose tests are all Integration/Performance filters down to
            # nothing, and `dotnet test` exits 0. Reported so it is visible rather than inferred.
            Write-ColorOutput "Result: no tests matched the category filter" "Yellow"
        } else {
            Write-ColorOutput "Result: PASSED ($passed/$total)" "Green"
        }
    }
    else {
        Write-ColorOutput "Result: FAILED ($failed failed, $passed passed, $skipped skipped)" "Red"
        $failedProjects += $projectName
    }
}

Write-Output ""
Write-ColorOutput "===================================" "Cyan"
Write-ColorOutput "Summary" "Cyan"
Write-ColorOutput "===================================" "Cyan"
Write-Output ""
Write-Output "Project                              Passed  Failed  Skipped  Total"
Write-Output "--------------------------------------------------------------------------------"
foreach ($result in $allResults) {
    $line = "$($result.Project.PadRight(35)) $($result.Passed.ToString().PadLeft(6))  $($result.Failed.ToString().PadLeft(6))  $($result.Skipped.ToString().PadLeft(8))  $($result.Total.ToString().PadLeft(6))"
    $color = if ($result.Failed -gt 0) { "Red" } elseif ($result.Total -eq 0) { "Yellow" } else { "Green" }
    Write-ColorOutput $line $color
}
Write-Output "--------------------------------------------------------------------------------"
Write-Output ""
Write-Output "  Total Tests:    $totalTests"
Write-ColorOutput "  Passed:         $totalPassed" "Green"
Write-Output "  Skipped:        $totalSkipped"
if ($totalFailed -gt 0) { Write-ColorOutput "  Failed:         $totalFailed" "Red" }
else                    { Write-Output "  Failed:         $totalFailed" }
Write-Output ""

if ($Coverage) {
    Write-ColorOutput "Coverage reports:" "Cyan"
    foreach ($project in $testProjects) {
        $coverageDir = Join-Path ([System.IO.Path]::GetDirectoryName($project)) "TestResults"
        if (Test-Path $coverageDir) { Write-Output "  $coverageDir" }
    }
    Write-Output ""
}

# Any failure fails the run. There is no acceptable pass rate below 100%: a test that is expected
# to fail is either a defect to fix or a category to exclude, and both are decisions to make
# explicitly rather than to absorb into a threshold.
if ($totalFailed -gt 0 -or $failedProjects.Count -gt 0) {
    Write-ColorOutput "OVERALL RESULT: FAILED" "Red"
    if ($failedProjects.Count -gt 0) {
        Write-Output "Failing project(s): $($failedProjects -join ', ')"
    }
    exit 1
}

Write-ColorOutput "OVERALL RESULT: PASSED ($totalPassed passed, $totalSkipped skipped)" "Green"
exit 0
