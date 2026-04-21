@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "OUTDIR=%ROOT%\ScriptDump"
set "RUNNER=%ROOT%\__script_dump_runner.ps1"
set "SELF=%~f0"

echo [script_dump] Root:   %ROOT%
echo [script_dump] Output: %OUTDIR%
echo.

call :WriteRunner
if errorlevel 1 (
  echo [script_dump] Failed to write PowerShell runner.
  echo.
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%RUNNER%" -Root "%ROOT%" -OutDir "%OUTDIR%"
set "EXITCODE=%ERRORLEVEL%"

if exist "%RUNNER%" del /f /q "%RUNNER%" >nul 2>nul

echo.
if not "%EXITCODE%"=="0" (
  echo [script_dump] Failed with exit code %EXITCODE%.
  echo.
  pause
  exit /b %EXITCODE%
)

echo [script_dump] Completed successfully.
echo.
pause
exit /b 0

:WriteRunner
setlocal EnableDelayedExpansion
set "FOUND="
> "%RUNNER%" (
  for /f "usebackq delims=" %%L in ("%SELF%") do (
    if defined FOUND echo(%%L
    if "%%L"=="__POWERSHELL_BELOW__" set FOUND=1
  )
)
endlocal & exit /b 0

goto :eof
__POWERSHELL_BELOW__
param(
    [string]$Root,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$rootPath = [System.IO.Path]::GetFullPath($Root)
$outDir = [System.IO.Path]::GetFullPath($OutDir)
$manifestPath = Join-Path $outDir '_MANIFEST.txt'
$generatedAt = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
$topPrefix = 'TOP_Scripts_'
$topExtension = '.txt'
$maxCharsPerTop = 900000
$maxEntriesPerTop = 24

if (Test-Path -LiteralPath $outDir)
{
    Remove-Item -LiteralPath $outDir -Recurse -Force
}

New-Item -ItemType Directory -Path $outDir | Out-Null

$scriptFiles = Get-ChildItem -LiteralPath $rootPath -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notlike ($outDir + '*') } |
    Sort-Object FullName

if ($null -eq $scriptFiles -or $scriptFiles.Count -eq 0)
{
    throw "No .cs files found under '$rootPath'."
}

function Get-TopFileName([int]$number)
{
    return ('{0}{1:000}{2}' -f $topPrefix, $number, $topExtension)
}

$manifestLines = New-Object System.Collections.Generic.List[string]
$manifestLines.Add('=== TOP: Scripts ===')

$currentTopNumber = 1
$currentTopChars = 0
$currentTopEntries = 0
$currentTopIndexLines = New-Object System.Collections.Generic.List[string]
$currentTopBodyLines = New-Object System.Collections.Generic.List[string]
$currentTopManifestLines = New-Object System.Collections.Generic.List[string]

function Start-NewTop
{
    param([int]$number)

    $script:currentTopChars = 0
    $script:currentTopEntries = 0
    $script:currentTopIndexLines = New-Object System.Collections.Generic.List[string]
    $script:currentTopBodyLines = New-Object System.Collections.Generic.List[string]
    $script:currentTopManifestLines = New-Object System.Collections.Generic.List[string]

    $script:currentTopIndexLines.Add('# INDEX')
    $script:currentTopIndexLines.Add('# Group: Scripts')
    $script:currentTopIndexLines.Add("# Generated: $generatedAt")
    $script:currentTopIndexLines.Add('# --------------------------------')
}

function Flush-CurrentTop
{
    param([int]$number)

    if ($script:currentTopEntries -le 0)
    {
        return
    }

    $topFileName = Get-TopFileName $number
    $topPath = Join-Path $outDir $topFileName

    $topLines = New-Object System.Collections.Generic.List[string]
    foreach ($line in $script:currentTopIndexLines) { $topLines.Add($line) }
    $topLines.Add('')
    foreach ($line in $script:currentTopBodyLines) { $topLines.Add($line) }

    [System.IO.File]::WriteAllLines($topPath, $topLines, [System.Text.UTF8Encoding]::new($false))

    $manifestLines.Add("OUT: $topFileName")
    foreach ($line in $script:currentTopManifestLines) { $manifestLines.Add($line) }
}

Start-NewTop $currentTopNumber

foreach ($file in $scriptFiles)
{
    $fullPath = $file.FullName
    $content = [System.IO.File]::ReadAllText($fullPath)
    if ($null -eq $content) { $content = '' }

    if ($content.Length -eq 0)
    {
        if ($currentTopEntries -ge $maxEntriesPerTop -or $currentTopChars -ge $maxCharsPerTop)
        {
            Flush-CurrentTop $currentTopNumber
            $currentTopNumber++
            Start-NewTop $currentTopNumber
        }

        $displayPath = $fullPath
        $currentTopIndexLines.Add("# - $displayPath")
        $currentTopBodyLines.Add("===== $displayPath =====")
        $currentTopBodyLines.Add('')
        $currentTopManifestLines.Add("  + $displayPath")
        $currentTopEntries++
        $currentTopChars += 1
        continue
    }

    $offset = 0
    $partNumber = 1

    while ($offset -lt $content.Length)
    {
        if ($currentTopEntries -ge $maxEntriesPerTop -or $currentTopChars -ge $maxCharsPerTop)
        {
            Flush-CurrentTop $currentTopNumber
            $currentTopNumber++
            Start-NewTop $currentTopNumber
        }

        $available = $maxCharsPerTop - $currentTopChars
        if ($available -le 0)
        {
            Flush-CurrentTop $currentTopNumber
            $currentTopNumber++
            Start-NewTop $currentTopNumber
            $available = $maxCharsPerTop
        }

        $remaining = $content.Length - $offset
        $take = [Math]::Min($available, $remaining)
        if ($take -le 0) { $take = $remaining }

        $slice = $content.Substring($offset, $take)
        $displayPath = $fullPath
        if ($offset -gt 0 -or $take -lt $content.Length)
        {
            $displayPath = "$fullPath (PART $partNumber)"
        }

        $currentTopIndexLines.Add("# - $displayPath")
        $currentTopBodyLines.Add("===== $displayPath =====")
        $currentTopBodyLines.Add('')

        if ($slice.Length -gt 0)
        {
            foreach ($line in ([System.Text.RegularExpressions.Regex]::Split($slice, "`r`n|`n|`r")))
            {
                $currentTopBodyLines.Add($line)
            }
        }

        if ($slice.Length -eq 0 -or ((-not $slice.EndsWith("`n")) -and (-not $slice.EndsWith("`r"))))
        {
            $currentTopBodyLines.Add('')
        }

        $currentTopBodyLines.Add('')
        $currentTopManifestLines.Add("  + $displayPath")

        $currentTopEntries++
        $currentTopChars += [Math]::Max($slice.Length, 1)
        $offset += $take
        $partNumber++
    }
}

Flush-CurrentTop $currentTopNumber
[System.IO.File]::WriteAllLines($manifestPath, $manifestLines, [System.Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host 'Done.'
Write-Host ('Root:      ' + $rootPath)
Write-Host ('Output:    ' + $outDir)
Write-Host ('Manifest:  ' + $manifestPath)
Write-Host ('Files:     ' + $scriptFiles.Count)
