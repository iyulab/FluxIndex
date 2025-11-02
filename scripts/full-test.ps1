# FluxIndex Full Test Runner (Local Development)
# Runs all test projects with .env.local support (Real API + Mock tests)
# For CI/CD, use scripts/mock-test.ps1 instead

param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "normal",
    [switch]$NoBuild,
    [switch]$Coverage,
    [string]$Filter = "",
    [switch]$FailFast,
    [string]$Configuration = "Debug",
    [switch]$Parallel
)

$ErrorActionPreference = "Stop"

# Colors for output
function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    $previousColor = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $Color
    Write-Output $Message
    $host.UI.RawUI.ForegroundColor = $previousColor
}

# Display header
Write-ColorOutput "`n===================================" "Cyan"
Write-ColorOutput "FluxIndex Full Test Suite (Local)" "Cyan"
Write-ColorOutput "===================================" "Cyan"
Write-Output ""

# Check for .env.local and display mode
$envLocalPath = "D:\data\FluxIndex\.env.local"
if (Test-Path $envLocalPath) {
    Write-ColorOutput "✓ Running in Real API mode (.env.local found)" "Green"
    Write-ColorOutput "  Tests will use actual OpenAI API (API costs apply)" "Yellow"
} else {
    Write-ColorOutput "⚠ Running in Mock mode (.env.local not found)" "Yellow"
    Write-Output "  For real API testing, create .env.local file"
    Write-Output "  See .env.local.example for setup instructions"
}
Write-Output ""

# Test project paths
$testProjects = @(
    "tests/FluxIndex.Tests.Core/FluxIndex.Tests.Core.csproj",
    "tests/FluxIndex.Tests.AI.OpenAI/FluxIndex.Tests.AI.OpenAI.csproj",
    "tests/FluxIndex.Tests.SDK/FluxIndex.Tests.SDK.csproj"
)

# Storage for results
$allResults = @()
$totalPassed = 0
$totalFailed = 0
$totalSkipped = 0
$totalTests = 0

# Build dotnet test arguments
$testArgs = @(
    "test",
    "--verbosity", $Verbosity,
    "--configuration", $Configuration
)

if ($NoBuild) {
    $testArgs += "--no-build"
}

if ($FailFast) {
    $testArgs += "--blame-crash"
}

if ($Filter) {
    $testArgs += "--filter", $Filter
}

if ($Parallel) {
    $testArgs += "--parallel"
}

if ($Coverage) {
    $testArgs += "--collect:`"XPlat Code Coverage`""
}

# Run tests for each project
foreach ($project in $testProjects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)

    Write-ColorOutput "`nRunning tests for: $projectName" "Yellow"
    Write-Output "Project: $project"
    if (Test-Path $envLocalPath) {
        Write-Output "Mode: Real API + Mock"
    } else {
        Write-Output "Mode: Mock only"
    }
    Write-Output "-----------------------------------"

    # Check if project exists
    if (-not (Test-Path $project)) {
        Write-ColorOutput "WARNING: Project not found: $project" "Red"
        continue
    }

    # Run tests
    $testOutput = & dotnet @testArgs $project 2>&1
    $exitCode = $LASTEXITCODE

    # Parse output for test results
    $passed = 0
    $failed = 0
    $skipped = 0
    $total = 0

    foreach ($line in $testOutput) {
        if ($line -match "Passed!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)") {
            $failed = [int]$matches[1]
            $passed = [int]$matches[2]
            $skipped = [int]$matches[3]
            $total = [int]$matches[4]
        }
        elseif ($line -match "Failed!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)") {
            $failed = [int]$matches[1]
            $passed = [int]$matches[2]
            $skipped = [int]$matches[3]
            $total = [int]$matches[4]
        }
        elseif ($line -match "Total tests:\s+(\d+)") {
            $total = [int]$matches[1]
        }
        elseif ($line -match "Passed:\s+(\d+)") {
            $passed = [int]$matches[1]
        }
        elseif ($line -match "Failed:\s+(\d+)") {
            $failed = [int]$matches[1]
        }
        elseif ($line -match "Skipped:\s+(\d+)") {
            $skipped = [int]$matches[1]
        }
    }

    # Store results
    $result = [PSCustomObject]@{
        Project = $projectName
        Passed = $passed
        Failed = $failed
        Skipped = $skipped
        Total = $total
        ExitCode = $exitCode
    }
    $allResults += $result

    # Update totals
    $totalPassed += $passed
    $totalFailed += $failed
    $totalSkipped += $skipped
    $totalTests += $total

    # Display project results
    if ($exitCode -eq 0) {
        Write-ColorOutput "Result: PASSED ($passed/$total tests)" "Green"
    }
    else {
        Write-ColorOutput "Result: FAILED ($failed failures, $passed passed, $skipped skipped)" "Red"
    }
}

# Display summary
Write-Output ""
Write-ColorOutput "===================================" "Cyan"
if (Test-Path $envLocalPath) {
    Write-ColorOutput "Full Test Summary (Real API Mode)" "Cyan"
} else {
    Write-ColorOutput "Full Test Summary (Mock Mode)" "Cyan"
}
Write-ColorOutput "===================================" "Cyan"
Write-Output ""

# Summary table
Write-Output "Project                              Passed  Failed  Skipped  Total"
Write-Output "--------------------------------------------------------------------------------"
foreach ($result in $allResults) {
    $projectPadded = $result.Project.PadRight(35)
    $passedPadded = $result.Passed.ToString().PadLeft(6)
    $failedPadded = $result.Failed.ToString().PadLeft(6)
    $skippedPadded = $result.Skipped.ToString().PadLeft(8)
    $totalPadded = $result.Total.ToString().PadLeft(6)

    $color = if ($result.Failed -gt 0) { "Red" } elseif ($result.Passed -eq $result.Total -and $result.Total -gt 0) { "Green" } else { "Yellow" }
    Write-ColorOutput "$projectPadded $passedPadded  $failedPadded  $skippedPadded  $totalPadded" $color
}
Write-Output "--------------------------------------------------------------------------------"

# Overall statistics
$passRate = if ($totalTests -gt 0) { [math]::Round(($totalPassed / $totalTests) * 100, 2) } else { 0 }

Write-Output ""
Write-Output "Overall Statistics:"
Write-Output "  Total Tests:    $totalTests"
Write-ColorOutput "  Passed:         $totalPassed" "Green"
if ($totalFailed -gt 0) {
    Write-ColorOutput "  Failed:         $totalFailed" "Red"
} else {
    Write-Output "  Failed:         $totalFailed"
}
Write-Output "  Skipped:        $totalSkipped"
Write-ColorOutput "  Pass Rate:      $passRate%" $(if ($passRate -eq 100) { "Green" } elseif ($passRate -ge 80) { "Yellow" } else { "Red" })
Write-Output ""

# Test mode information
if (Test-Path $envLocalPath) {
    Write-ColorOutput "Test Mode: Real API + Mock" "Cyan"
    Write-Output "  - OpenAI tests use actual API (costs apply)"
    Write-Output "  - Higher test coverage with integration validation"
    Write-Output "  - Expected pass rate: ~84% (59 of 70 tests)"
} else {
    Write-ColorOutput "Test Mode: Mock Only" "Cyan"
    Write-Output "  - All tests use mocked responses"
    Write-Output "  - Fast and free execution"
    Write-Output "  - Expected pass rate: ~79% (55 of 70 tests)"
    Write-Output ""
    Write-ColorOutput "For real API testing:" "Yellow"
    Write-Output "  1. Copy .env.local.example to .env.local"
    Write-Output "  2. Add your OpenAI API key"
    Write-Output "  3. Run this script again"
}
Write-Output ""

# Coverage report location
if ($Coverage) {
    Write-ColorOutput "Code coverage reports generated in:" "Cyan"
    foreach ($project in $testProjects) {
        $projectDir = [System.IO.Path]::GetDirectoryName($project)
        $coverageDir = Join-Path $projectDir "TestResults"
        if (Test-Path $coverageDir) {
            Write-Output "  $coverageDir"
        }
    }
    Write-Output ""
}

# Final result
if ($totalFailed -gt 0) {
    Write-ColorOutput "OVERALL RESULT: FAILED" "Red"
    exit 1
}
else {
    Write-ColorOutput "OVERALL RESULT: PASSED" "Green"
    exit 0
}
