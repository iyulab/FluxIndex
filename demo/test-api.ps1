<#
.SYNOPSIS
    FluxIndex Demo API Test Script
.DESCRIPTION
    Comprehensive API testing for FluxIndex Demo endpoints
.PARAMETER BaseUrl
    Base URL of the demo API (default: http://localhost:5011)
.PARAMETER TestFile
    Path to a test file for upload testing
.EXAMPLE
    .\test-api.ps1
    .\test-api.ps1 -BaseUrl "http://localhost:5011" -TestFile "test.txt"
#>

param(
    [string]$BaseUrl = "http://localhost:5011",
    [string]$TestFile = ""
)

$ErrorActionPreference = "Continue"

# Test results tracking
$Script:TestsPassed = 0
$Script:TestsFailed = 0
$Script:TestResults = @()

function Write-TestHeader {
    param([string]$Message)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " $Message" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Details = ""
    )

    $Script:TestResults += @{
        Name = $TestName
        Passed = $Passed
        Details = $Details
    }

    if ($Passed) {
        $Script:TestsPassed++
        Write-Host "[PASS] " -ForegroundColor Green -NoNewline
        Write-Host "$TestName"
        if ($Details) { Write-Host "       $Details" -ForegroundColor Gray }
    }
    else {
        $Script:TestsFailed++
        Write-Host "[FAIL] " -ForegroundColor Red -NoNewline
        Write-Host "$TestName"
        if ($Details) { Write-Host "       $Details" -ForegroundColor Yellow }
    }
}

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Url,
        [string]$Method = "GET",
        [hashtable]$Body = $null,
        [scriptblock]$Validation = $null
    )

    try {
        $params = @{
            Uri = $Url
            Method = $Method
            ContentType = "application/json"
            TimeoutSec = 30
        }

        if ($Body) {
            $params.Body = ($Body | ConvertTo-Json -Depth 10)
        }

        $response = Invoke-RestMethod @params

        if ($Validation) {
            $validationResult = & $Validation $response
            if ($validationResult -is [bool]) {
                Write-TestResult -TestName $Name -Passed $validationResult
            }
            else {
                Write-TestResult -TestName $Name -Passed $true -Details $validationResult
            }
        }
        else {
            Write-TestResult -TestName $Name -Passed $true
        }

        return $response
    }
    catch {
        Write-TestResult -TestName $Name -Passed $false -Details $_.Exception.Message
        return $null
    }
}

function Test-FileUpload {
    param(
        [string]$FilePath,
        [string]$Url
    )

    try {
        $boundary = [System.Guid]::NewGuid().ToString()
        $fileName = [System.IO.Path]::GetFileName($FilePath)
        $fileContent = [System.IO.File]::ReadAllBytes($FilePath)
        $fileContentBase64 = [System.Convert]::ToBase64String($fileContent)

        # Use curl for multipart upload (more reliable)
        $result = curl.exe -s -X POST $Url `
            -F "file=@$FilePath" `
            -H "Accept: application/json" | ConvertFrom-Json

        if ($result.Success) {
            Write-TestResult -TestName "File Upload: $fileName" -Passed $true -Details "Chunks: $($result.ChunkCount), Time: $($result.ProcessingTimeMs)ms"
            return $result
        }
        else {
            Write-TestResult -TestName "File Upload: $fileName" -Passed $false -Details $result.Message
            return $null
        }
    }
    catch {
        Write-TestResult -TestName "File Upload: $fileName" -Passed $false -Details $_.Exception.Message
        return $null
    }
}

# Main test execution
Write-TestHeader "FluxIndex Demo API Tests"
Write-Host "Target: $BaseUrl"
Write-Host "Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host ""

# 1. Health Check
Write-Host "`n--- Health & Status Tests ---" -ForegroundColor Yellow
$health = Test-Endpoint -Name "Health Check" -Url "$BaseUrl/api/health" -Validation {
    param($r)
    if ($r.Status -eq "healthy") { "Status: $($r.Status), Chunks: $($r.DocumentChunks)" }
    else { $false }
}

# 2. Status Endpoint
$status = Test-Endpoint -Name "Status Endpoint" -Url "$BaseUrl/api/status" -Validation {
    param($r)
    "Backend: $($r.StorageBackend), Docs: $($r.TotalDocuments), Chunks: $($r.TotalChunks)"
}

# 3. Documents List
Write-Host "`n--- Document Management Tests ---" -ForegroundColor Yellow
$docs = Test-Endpoint -Name "List Documents" -Url "$BaseUrl/api/documents" -Validation {
    param($r)
    "Found $($r.Count) documents"
}

# 4. File Upload Test (if test file provided or create one)
if (-not $TestFile) {
    # Create a temporary test file
    $TestFile = Join-Path $env:TEMP "fluxindex_test.txt"
    @"
FluxIndex Test Document

This is a test document for FluxIndex demo API testing.
It contains sample content for semantic search validation.

Key features being tested:
- Document upload and processing
- Text chunking and embedding generation
- Vector storage in PostgreSQL with pgvector
- Semantic search functionality
- Reranking capabilities

The FluxIndex library provides a complete RAG infrastructure
for building AI-powered search and retrieval systems.
"@ | Out-File -FilePath $TestFile -Encoding utf8
    Write-Host "Created test file: $TestFile" -ForegroundColor Gray
}

if (Test-Path $TestFile) {
    $uploadResult = Test-FileUpload -FilePath $TestFile -Url "$BaseUrl/api/upload"

    if ($uploadResult -and $uploadResult.DocumentId) {
        # 5. Get Document Detail
        $docDetail = Test-Endpoint -Name "Get Document Detail" -Url "$BaseUrl/api/documents/$($uploadResult.DocumentId)" -Validation {
            param($r)
            "Title: $($r.Title), Chunks: $($r.TotalChunks)"
        }
    }
}

# 6. Search Tests
Write-Host "`n--- Search Tests ---" -ForegroundColor Yellow

# Basic search
$searchResult = Test-Endpoint -Name "Basic Search" -Url "$BaseUrl/api/search" -Method "POST" -Body @{
    Query = "FluxIndex features"
    TopK = 5
    UseReranker = $true
} -Validation {
    param($r)
    "Results: $($r.TotalResults), Time: $($r.SearchTimeMs)ms, Reranker: $($r.UsedReranker)"
}

# Search without reranker
$searchNoRerank = Test-Endpoint -Name "Search (No Reranker)" -Url "$BaseUrl/api/search" -Method "POST" -Body @{
    Query = "semantic search"
    TopK = 3
    UseReranker = $false
} -Validation {
    param($r)
    "Results: $($r.TotalResults), Time: $($r.SearchTimeMs)ms"
}

# 7. MCP Search
Write-Host "`n--- MCP Integration Tests ---" -ForegroundColor Yellow
$mcpResult = Test-Endpoint -Name "MCP Search Function" -Url "$BaseUrl/api/mcp/search" -Method "POST" -Body @{
    Query = "document processing"
    TopK = 5
    UseReranker = $true
    IncludeMetadata = $true
    MaxTokens = 3000
} -Validation {
    param($r)
    "Tool: $($r.ToolName), Results: $($r.Metadata.ResultsReturned), Tokens: ~$($r.Metadata.EstimatedTokens)"
}

# 8. Delete Test Document (cleanup)
if ($uploadResult -and $uploadResult.DocumentId) {
    Write-Host "`n--- Cleanup Tests ---" -ForegroundColor Yellow
    $deleteResult = Test-Endpoint -Name "Delete Document" -Url "$BaseUrl/api/documents/$($uploadResult.DocumentId)" -Method "DELETE" -Validation {
        param($r)
        $r.Message -eq "Document deleted"
    }
}

# Summary
Write-TestHeader "Test Summary"
Write-Host "Total Tests: $($Script:TestsPassed + $Script:TestsFailed)"
Write-Host "Passed: " -NoNewline
Write-Host $Script:TestsPassed -ForegroundColor Green
Write-Host "Failed: " -NoNewline
if ($Script:TestsFailed -gt 0) {
    Write-Host $Script:TestsFailed -ForegroundColor Red
}
else {
    Write-Host $Script:TestsFailed -ForegroundColor Green
}

# Return exit code
if ($Script:TestsFailed -gt 0) {
    exit 1
}
exit 0
