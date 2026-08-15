param([switch]$InstallDesktopShortcut)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' |
    Sort-Object Name | ForEach-Object { $_.FullName }
$build = Join-Path $projectRoot 'build'
$release = Join-Path $projectRoot 'releases\Windows-Repair-Center.exe'
$version = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
$versionedRelease = Join-Path $projectRoot ("releases\Windows-Repair-Center-v{0}.exe" -f $version)
$stagedRelease = Join-Path $build ("Windows-Repair-Center-staged-{0}-{1}.exe" -f $version, ([Guid]::NewGuid().ToString('N')))
$pendingRelease = Join-Path $build 'Windows-Repair-Center.pending.exe'
$manifestPath = Join-Path $projectRoot 'releases\manifest.json'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$icon = Join-Path $build 'Windows-Repair-Center.ico'
$manifest = Join-Path $projectRoot 'assets\app.manifest'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found: $compiler"
}

New-Item -ItemType Directory -Path $build -Force | Out-Null
& (Join-Path $PSScriptRoot 'GenerateIcon.ps1') -OutputPath $icon
& $compiler /nologo /target:winexe /optimize+ /platform:x64 `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
    /reference:System.Management.dll `
    /win32icon:$icon /win32manifest:$manifest `
    /out:$stagedRelease $sources
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $stagedRelease -Destination $versionedRelease -Force
$releaseUpdated = $true
try {
    # The currently running EXE can be locked by Windows.  Never stop the user's
    # window just to publish a source edit; leave a pending binary for the next
    # launcher run instead.
    Copy-Item -LiteralPath $stagedRelease -Destination $release -Force
    Remove-Item -LiteralPath $pendingRelease -Force -ErrorAction SilentlyContinue
}
catch {
    $releaseUpdated = $false
    Copy-Item -LiteralPath $stagedRelease -Destination $pendingRelease -Force
    Write-Warning ("Release is in use; new build staged. Close the current window and launch again to apply it. Reason: " + $_.Exception.Message)
}
$releaseForShortcut = if ($releaseUpdated) { $release } else { $versionedRelease }
$hash = (Get-FileHash -LiteralPath $versionedRelease -Algorithm SHA256).Hash
$sourceCommit = (& git -C $projectRoot rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0) {
    $sourceCommit = 'uncommitted'
}
$dirty = (& git -C $projectRoot status --porcelain 2>$null)
if (-not [String]::IsNullOrWhiteSpace(($dirty -join "`n"))) {
    $sourceCommit = 'uncommitted'
}
$manifest = [ordered]@{
    project = 'windows-repair-center'
    version = $version
    source_commit = "$sourceCommit".Trim()
    build_date = (Get-Date -Format 'yyyy-MM-dd')
    platform = 'Windows x64 / .NET Framework WinForms'
    artifacts = @(
        [ordered]@{
            file = Split-Path -Leaf $versionedRelease
            sha256 = $hash
            bytes = (Get-Item -LiteralPath $versionedRelease).Length
        }
    )
    known_limitations = @(
        'Cannot patch third-party GPU or Codex application defects'
        'Microsoft Store restaging depends on Windows App Installer and network availability'
        'Memory relief force-closes the selected programs; unsaved work in them is lost'
        'Resource Guard is a machine-level safety net and does not replace clean process shutdown in each project'
    )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

if ($InstallDesktopShortcut) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shortcutName = [Text.Encoding]::UTF8.GetString(
        [Convert]::FromBase64String('5Liq5Lq66IGa5ZCI5bel5YW3566xLmxuaw==')
    )
    $shortcutPath = Join-Path $desktop $shortcutName
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $launcher = Join-Path $PSScriptRoot 'run-windows-repair-center.vbs'
    # Some minimal PowerShell hosts do not define WINDIR even though Windows is
    # present.  SystemRoot is the stable environment variable for the launcher.
    $systemRoot = [Environment]::GetEnvironmentVariable('SystemRoot')
    if ([String]::IsNullOrWhiteSpace($systemRoot)) { $systemRoot = 'C:\Windows' }
    $shortcut.TargetPath = Join-Path $systemRoot 'System32\wscript.exe'
    $shortcut.Arguments = '"' + $launcher + '"'
    $shortcut.WorkingDirectory = $projectRoot
    $shortcut.IconLocation = "$releaseForShortcut,0"
    $shortcut.Description = 'Personal local toolbox with one-click tools and safe repair'
    $shortcut.Save()

    $legacyBackup = Join-Path $env:LOCALAPPDATA ("WindowsRepairCenter\backups\desktop-shortcuts\" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $legacyBackup -Force | Out-Null
    $legacyNames = @(
        [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('V2luZG93cyDkv67lpI3kuK3lv4MubG5r')),
        [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('Q29kZXgg5oGi5aSN5Lit5b+DLmxuaw==')),
        [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5LiA6ZSu5L+u5aSN6JOd54mZ6Z+z6aKRLmxuaw==')),
        [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('6byg5qCH6K+t6Z+z6L6T5YWlLeWQr+WKqOS/ruWkjS5sbms='))
    )
    foreach ($legacyName in $legacyNames) {
        $legacyPath = Join-Path $desktop $legacyName
        if (Test-Path -LiteralPath $legacyPath) {
            Copy-Item -LiteralPath $legacyPath -Destination (Join-Path $legacyBackup $legacyName) -Force
            Remove-Item -LiteralPath $legacyPath -Force
        }
    }
}

[pscustomobject]@{
    Release = $release
    VersionedRelease = $versionedRelease
    ReleaseUpdated = $releaseUpdated
    PendingRelease = if (-not $releaseUpdated) { $pendingRelease } else { '' }
    Bytes = (Get-Item -LiteralPath $release).Length
    SHA256 = $hash
    Manifest = $manifestPath
    DesktopShortcut = if ($InstallDesktopShortcut) { $shortcutPath } else { '' }
}
