param([string]$Executable =
    "$PSScriptRoot\..\releases\Codex-Recovery-Center.exe")

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $projectRoot 'src\ResourceGuard.cs'
$source = Get-Content -Raw -Encoding UTF8 -LiteralPath $sourcePath

foreach ($required in @(
    'HardPrivateBytes = 6L * 1024 * 1024 * 1024',
    'OrphanDevServerGrace = TimeSpan.FromHours(4)',
    'OrphanHandleLimit = 3000',
    'OrphanConsoleThreadLimit = 256',
    'memory.CommitPressurePercent >= 85.0')) {
    if (-not $source.Contains($required)) {
        throw "Missing guard rule: $required"
    }
}

if (-not (Test-Path -LiteralPath $Executable)) {
    throw "Executable not found: $Executable"
}

$report = Join-Path $env:TEMP 'codex-resource-guard-test.txt'
$process = Start-Process -FilePath $Executable `
    -ArgumentList @('--resource-guard-self-test', ('"' + $report + '"')) `
    -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Resource guard self-test failed: $($process.ExitCode)"
}

$text = Get-Content -Raw -Encoding UTF8 -LiteralPath $report
if ($text -notmatch 'success=True') {
    throw "Resource guard did not report success: $text"
}

[pscustomobject]@{
    Result = 'PASS'
    Executable = $Executable
    Report = $text -replace '\r?\n', '; '
}
