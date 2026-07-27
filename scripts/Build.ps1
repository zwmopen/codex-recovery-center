param([switch]$InstallDesktopShortcut)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' |
    Sort-Object Name | ForEach-Object { $_.FullName }
$build = Join-Path $projectRoot 'build'
$release = Join-Path $projectRoot 'releases\Codex-Recovery-Center.exe'
$version = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
$versionedRelease = Join-Path $projectRoot ("releases\Codex-Recovery-Center-v{0}.exe" -f $version)
$manifestPath = Join-Path $projectRoot 'releases\manifest.json'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$icon = Join-Path $build 'Codex-Recovery-Center.ico'
$manifest = Join-Path $projectRoot 'assets\app.manifest'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found: $compiler"
}

New-Item -ItemType Directory -Path $build -Force | Out-Null
& (Join-Path $PSScriptRoot 'GenerateIcon.ps1') -OutputPath $icon
& $compiler /nologo /target:winexe /optimize+ /platform:x64 `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
    /win32icon:$icon /win32manifest:$manifest `
    /out:$release $sources
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $release -Destination $versionedRelease -Force
$hash = (Get-FileHash -LiteralPath $versionedRelease -Algorithm SHA256).Hash
$sourceCommit = (& git -C $projectRoot rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0) {
    $sourceCommit = 'uncommitted'
}
$manifest = [ordered]@{
    project = 'codex-recovery-center'
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
    )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

if ($InstallDesktopShortcut) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shortcutName = [Text.Encoding]::UTF8.GetString(
        [Convert]::FromBase64String('Q29kZXgg5oGi5aSN5Lit5b+DLmxuaw==')
    )
    $shortcutPath = Join-Path $desktop $shortcutName
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $release
    $shortcut.WorkingDirectory = Split-Path -Parent $release
    $shortcut.IconLocation = "$release,0"
    $shortcut.Description = [Text.Encoding]::UTF8.GetString(
        [Convert]::FromBase64String('6K+K5pat44CB5L+u5aSN5bm25a6J5YWo5ZCv5YqoIENvZGV4')
    )
    $shortcut.Save()
}

[pscustomobject]@{
    Release = $release
    VersionedRelease = $versionedRelease
    Bytes = (Get-Item -LiteralPath $release).Length
    SHA256 = $hash
    Manifest = $manifestPath
    DesktopShortcut = if ($InstallDesktopShortcut) { $shortcutPath } else { '' }
}
