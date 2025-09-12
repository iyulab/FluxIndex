# PowerShell script to update package version
param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [Parameter(Mandatory=$false)]
    [string]$Suffix = "",
    
    [Parameter(Mandatory=$false)]
    [switch]$CommitChanges = $false,
    
    [Parameter(Mandatory=$false)]
    [switch]$CreateTag = $false
)

$ErrorActionPreference = "Stop"

# Validate version format
if (-not ($Version -match '^\d+\.\d+\.\d+$')) {
    Write-Error "Invalid version format. Expected: X.Y.Z (e.g., 1.0.0)"
    exit 1
}

# Build full version string
$FullVersion = $Version
if ($Suffix) {
    $FullVersion = "$Version-$Suffix"
}

$AssemblyVersion = "$Version.0"
$FileVersion = "$Version.0"

Write-Host "Updating version to: $FullVersion" -ForegroundColor Green

# Update Directory.Build.props
$propsFile = "src\Directory.Build.props"
if (-not (Test-Path $propsFile)) {
    Write-Error "Directory.Build.props not found at: $propsFile"
    exit 1
}

$content = Get-Content $propsFile -Raw

# Update Version
$content = $content -replace '<Version>.*?</Version>', "<Version>$FullVersion</Version>"

# Update AssemblyVersion
$content = $content -replace '<AssemblyVersion>.*?</AssemblyVersion>', "<AssemblyVersion>$AssemblyVersion</AssemblyVersion>"

# Update FileVersion
$content = $content -replace '<FileVersion>.*?</FileVersion>', "<FileVersion>$FileVersion</FileVersion>"

# Save changes
$content | Set-Content $propsFile -NoNewline

Write-Host "✅ Updated Directory.Build.props" -ForegroundColor Green

# Update root Directory.Build.props if it exists
$rootPropsFile = "Directory.Build.props"
if (Test-Path $rootPropsFile) {
    $rootContent = Get-Content $rootPropsFile -Raw
    $rootContent = $rootContent -replace '<Version>.*?</Version>', "<Version>$FullVersion</Version>"
    $rootContent = $rootContent -replace '<AssemblyVersion>.*?</AssemblyVersion>', "<AssemblyVersion>$AssemblyVersion</AssemblyVersion>"
    $rootContent = $rootContent -replace '<FileVersion>.*?</FileVersion>', "<FileVersion>$FileVersion</FileVersion>"
    $rootContent | Set-Content $rootPropsFile -NoNewline
    Write-Host "✅ Updated root Directory.Build.props" -ForegroundColor Green
}

# Show git diff
Write-Host "`nChanges made:" -ForegroundColor Yellow
git diff src/Directory.Build.props

# Commit changes if requested
if ($CommitChanges) {
    Write-Host "`nCommitting changes..." -ForegroundColor Yellow
    git add src/Directory.Build.props
    if (Test-Path $rootPropsFile) {
        git add $rootPropsFile
    }
    
    $commitMessage = "chore: bump version to $FullVersion"
    git commit -m $commitMessage
    Write-Host "✅ Changes committed: $commitMessage" -ForegroundColor Green
    
    # Create tag if requested
    if ($CreateTag) {
        $tagName = "v$FullVersion"
        Write-Host "`nCreating tag: $tagName" -ForegroundColor Yellow
        git tag -a $tagName -m "Release version $FullVersion"
        Write-Host "✅ Tag created: $tagName" -ForegroundColor Green
        Write-Host "Don't forget to push the tag: git push origin $tagName" -ForegroundColor Cyan
    }
} else {
    Write-Host "`nChanges not committed. To commit, run with -CommitChanges flag" -ForegroundColor Yellow
}

Write-Host "`n📦 Version update complete!" -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Review the changes" -ForegroundColor White
Write-Host "  2. Run tests: dotnet test" -ForegroundColor White
Write-Host "  3. Build packages: dotnet pack" -ForegroundColor White
if (-not $CommitChanges) {
    Write-Host "  4. Commit changes: git commit -am 'chore: bump version to $FullVersion'" -ForegroundColor White
}
Write-Host "  5. Push to trigger CI/CD: git push" -ForegroundColor White