#!/usr/bin/env pwsh
# Convert _logger.Log* calls to [LoggerMessage] source generator pattern
# Usage: .\convert-logging.ps1 [-File <path>] [-All]

param(
    [string]$File,
    [switch]$All,
    [string]$SourceDir = "$PSScriptRoot\..\src\FluxIndex.Core\Application\Services"
)

$ErrorActionPreference = 'Continue'

function Get-LogLevel($method) {
    switch ($method) {
        'LogInformation' { 'LogLevel.Information' }
        'LogDebug' { 'LogLevel.Debug' }
        'LogWarning' { 'LogLevel.Warning' }
        'LogError' { 'LogLevel.Error' }
        'LogTrace' { 'LogLevel.Trace' }
        'LogCritical' { 'LogLevel.Critical' }
        default { 'LogLevel.Information' }
    }
}

function ConvertFile($filePath) {
    $content = [System.IO.File]::ReadAllText($filePath)
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($filePath)

    # Check if file has _logger.Log calls
    if ($content -notmatch '_logger\.Log') {
        Write-Host "  SKIP: No _logger.Log calls in $fileName"
        return
    }

    # 1. Make class partial
    $classPattern = "public\s+class\s+$fileName"
    if ($content -match $classPattern -and $content -notmatch "public\s+partial\s+class\s+$fileName") {
        $content = $content -replace "public\s+class\s+$fileName", "public partial class $fileName"
        Write-Host "  Made class partial: $fileName"
    }

    # 2. Find all _logger.Log* calls and extract info
    # We use line-by-line approach for simpler parsing
    $lines = $content -split "`n"
    $newLines = @()
    $logMethods = @()
    $methodIdx = 0
    $i = 0
    $inMultiLineLog = $false
    $multiLineBuffer = ''
    $multiLineStartIdx = -1

    while ($i -lt $lines.Count) {
        $line = $lines[$i]

        # Check if this line starts a _logger.Log call
        if ($line -match '(\s*)_logger\.(Log(?:Information|Debug|Warning|Error|Trace|Critical))\s*\((.*)') {
            $indent = $Matches[1]
            $logMethod = $Matches[2]
            $restOfLine = $Matches[3]

            # Accumulate the full call (may span multiple lines)
            $fullCall = $restOfLine
            $parenDepth = ($restOfLine.ToCharArray() | Where-Object { $_ -eq '(' }).Count - ($restOfLine.ToCharArray() | Where-Object { $_ -eq ')' }).Count + 1

            $startLine = $i
            while ($parenDepth -gt 0 -and $i + 1 -lt $lines.Count) {
                $i++
                $fullCall += "`n" + $lines[$i]
                $parenDepth += ($lines[$i].ToCharArray() | Where-Object { $_ -eq '(' }).Count
                $parenDepth -= ($lines[$i].ToCharArray() | Where-Object { $_ -eq ')' }).Count
            }

            # Remove the trailing );
            $fullCall = $fullCall -replace '\);\s*$', ''

            $methodIdx++
            $level = Get-LogLevel $logMethod
            $methodName = "Log${fileName}${methodIdx}"
            # Shorten method name
            $shortName = $fileName -replace 'Service$', '' -replace 'Manager$', '' -replace 'Pipeline$', ''
            $methodName = "Log${shortName}${methodIdx}"

            # Parse the args
            # Simple approach: first arg might be exception, then message template, then params
            $hasException = $false
            $exceptionArg = ''
            $messageTemplate = ''
            $paramArgs = @()

            # Try to split by top-level commas
            $args = SplitTopLevelArgs $fullCall

            $argIdx = 0
            # Check for exception as first arg (for Warning/Error/Critical)
            if ($logMethod -in @('LogWarning', 'LogError', 'LogCritical') -and $args.Count -ge 2) {
                $firstArg = $args[0].Trim()
                if ($firstArg -notmatch '^"' -and $firstArg -notmatch '^\$"' -and $firstArg -notmatch '^@"') {
                    $secondArg = $args[1].Trim()
                    if ($secondArg -match '^"' -or $secondArg -match '^\$"' -or $secondArg -match '^@"') {
                        $hasException = $true
                        $exceptionArg = $firstArg
                        $argIdx = 1
                    }
                }
            }

            # Message template
            if ($argIdx -lt $args.Count) {
                $messageTemplate = $args[$argIdx].Trim()
                $argIdx++
            }

            # Remaining are parameters
            while ($argIdx -lt $args.Count) {
                $paramArgs += $args[$argIdx].Trim()
                $argIdx++
            }

            # Handle interpolated strings by converting to template
            if ($messageTemplate -match '^\$"' -or $messageTemplate -match '^\$@"') {
                # Convert interpolated string to template - this is complex, skip for now
                # Just use the literal as-is with a note
                $newLines += "${indent}// TODO: Convert interpolated string to LoggerMessage"
                $newLines += $lines[$startLine..$i]
                $i++
                continue
            }

            # Handle string concatenation
            if ($messageTemplate -match '\+') {
                $newLines += "${indent}// TODO: Convert string concatenation to LoggerMessage"
                $newLines += $lines[$startLine..$i]
                $i++
                continue
            }

            # Extract template placeholders
            $placeholders = [regex]::Matches($messageTemplate, '\{(\w+)(?::[^}]*)?\}') | ForEach-Object { $_.Groups[1].Value }

            # Build LoggerMessage method parameters
            $methodParams = @('ILogger logger')
            if ($hasException) { $methodParams += 'Exception ex' }

            for ($p = 0; $p -lt $placeholders.Count -and $p -lt $paramArgs.Count; $p++) {
                $paramName = $placeholders[$p]
                $paramExpr = $paramArgs[$p]
                $paramType = InferType $paramExpr

                # Ensure param name starts with lowercase for the method param
                $paramNameLower = $paramName.Substring(0,1).ToLower() + $paramName.Substring(1)
                $methodParams += "$paramType $paramNameLower"
            }

            # Build the method call
            $callArgs = @('_logger')
            if ($hasException) { $callArgs += $exceptionArg }
            for ($p = 0; $p -lt $placeholders.Count -and $p -lt $paramArgs.Count; $p++) {
                $callArgs += $paramArgs[$p]
            }

            $callLine = "${indent}${methodName}($($callArgs -join ', '));"
            $newLines += $callLine

            # Store method definition
            $logMethods += @{
                Name = $methodName
                Level = $level
                Message = $messageTemplate
                Params = ($methodParams -join ', ')
            }
        }
        else {
            $newLines += $line
        }

        $i++
    }

    if ($logMethods.Count -eq 0) {
        Write-Host "  No convertible log calls found in $fileName"
        return
    }

    # Build the LoggerMessage region
    $methodDefs = @()
    $methodDefs += ''
    $methodDefs += '    #region LoggerMessage Definitions'
    $methodDefs += ''
    foreach ($m in $logMethods) {
        $methodDefs += "    [LoggerMessage(Level = $($m.Level), Message = $($m.Message))]"
        $methodDefs += "    private static partial void $($m.Name)($($m.Params));"
        $methodDefs += ''
    }
    $methodDefs += '    #endregion'

    # Find position to insert - before the last } of the class
    $result = $newLines -join "`n"

    # Find the class closing brace
    # Look for pattern: #endregion\n} or just the last } at class level
    # Simple approach: find the last } in the file that's part of the class
    $resultLines = $result -split "`n"
    $insertIdx = -1
    $braceDepth = 0
    $inClass = $false

    for ($j = 0; $j -lt $resultLines.Count; $j++) {
        if ($resultLines[$j] -match 'public\s+partial\s+class') {
            $inClass = $true
        }
        if ($inClass) {
            $braceDepth += ($resultLines[$j].ToCharArray() | Where-Object { $_ -eq '{' }).Count
            $braceDepth -= ($resultLines[$j].ToCharArray() | Where-Object { $_ -eq '}' }).Count
            if ($braceDepth -le 0) {
                $insertIdx = $j
                break
            }
        }
    }

    if ($insertIdx -ge 0) {
        $before = $resultLines[0..($insertIdx-1)]
        $after = $resultLines[$insertIdx..($resultLines.Count-1)]
        $finalLines = $before + $methodDefs + $after
        $result = $finalLines -join "`n"
    }

    [System.IO.File]::WriteAllText($filePath, $result)
    Write-Host "  Converted $fileName : $($logMethods.Count) LoggerMessage methods"
}

function SplitTopLevelArgs($argsStr) {
    $args = @()
    $depth = 0
    $inString = $false
    $verbatim = $false
    $current = [System.Text.StringBuilder]::new()
    $escaped = $false

    for ($i = 0; $i -lt $argsStr.Length; $i++) {
        $c = $argsStr[$i]

        if ($escaped) {
            $current.Append($c) | Out-Null
            $escaped = $false
            continue
        }

        if ($inString) {
            $current.Append($c) | Out-Null
            if ($c -eq '\' -and -not $verbatim) {
                $escaped = $true
                continue
            }
            if ($c -eq '"') {
                if ($verbatim -and $i + 1 -lt $argsStr.Length -and $argsStr[$i+1] -eq '"') {
                    $current.Append($argsStr[++$i]) | Out-Null
                    continue
                }
                $inString = $false
                $verbatim = $false
            }
            continue
        }

        if ($c -eq '"') {
            $inString = $true
            if ($i -gt 0 -and $argsStr[$i-1] -eq '@') { $verbatim = $true }
            $current.Append($c) | Out-Null
            continue
        }

        if ($c -eq '(' -or $c -eq '[' -or $c -eq '{') { $depth++ }
        if ($c -eq ')' -or $c -eq ']' -or $c -eq '}') { $depth-- }

        if ($c -eq ',' -and $depth -eq 0) {
            $args += $current.ToString()
            $current.Clear() | Out-Null
            continue
        }

        $current.Append($c) | Out-Null
    }

    if ($current.Length -gt 0) {
        $args += $current.ToString()
    }

    return $args
}

function InferType($expr) {
    $expr = $expr.Trim()
    if ($expr -match '\.Count$' -or $expr -match '\.Count\b' -or $expr -match '\.Length$') { return 'int' }
    if ($expr -match '\.ElapsedMilliseconds$') { return 'long' }
    if ($expr -match '\.Elapsed$') { return 'TimeSpan' }
    if ($expr -match '^"') { return 'string' }
    if ($expr -match '\.ToString\(') { return 'string' }
    if ($expr -match 'string\.Join') { return 'string' }
    if ($expr -match '^\d+$') { return 'int' }
    if ($expr -match '^\d+\.\d+[fd]?$') { return 'double' }
    if ($expr -match '\.Message$') { return 'string' }
    return 'object'
}

# Main execution
if ($File) {
    Write-Host "Converting single file: $File"
    ConvertFile $File
}
elseif ($All) {
    $files = Get-ChildItem -Path $SourceDir -Filter "*.cs" -Recurse
    foreach ($f in $files) {
        Write-Host "Processing: $($f.Name)"
        ConvertFile $f.FullName
    }
}
else {
    Write-Host "Usage: .\convert-logging.ps1 -File <path> or -All"
    Write-Host "  -File <path>  Convert a single file"
    Write-Host "  -All          Convert all files in source directory"
    Write-Host "  -SourceDir    Override source directory (default: FluxIndex.Core/Application/Services)"
}
