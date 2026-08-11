$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$release = Join-Path $projectRoot 'releases\Windows-Repair-Center.exe'
$buildScript = Join-Path $PSScriptRoot 'Build.ps1'

$sourceFiles = @(
    (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File),
    (Get-Item -LiteralPath (Join-Path $projectRoot 'VERSION')),
    (Get-Item -LiteralPath $buildScript)
)
$latestSource = ($sourceFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
$needsBuild = -not (Test-Path -LiteralPath $release) -or $latestSource -gt (Get-Item -LiteralPath $release).LastWriteTime

if ($needsBuild) {
    try {
        & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $buildScript
        if ($LASTEXITCODE -ne 0) { throw "Build exited with code $LASTEXITCODE" }
    }
    catch {
        if (-not (Test-Path -LiteralPath $release)) {
            Add-Type -AssemblyName System.Windows.Forms
            [System.Windows.Forms.MessageBox]::Show(
                "Windows Repair Center build failed and no previous version is available.`n`n$($_.Exception.Message)",
                "Windows Repair Center", 0, 16) | Out-Null
            exit 2
        }
    }
}

Start-Process -FilePath $release -WorkingDirectory (Split-Path -Parent $release)
