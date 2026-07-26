param([switch]$InstallDesktopShortcut)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'src\CodexRecoveryCenter.cs'
$build = Join-Path $projectRoot 'build'
$release = Join-Path $projectRoot 'releases\Codex-Recovery-Center.exe'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found: $compiler"
}

New-Item -ItemType Directory -Path $build -Force | Out-Null
& $compiler /nologo /target:winexe /optimize+ /platform:x64 `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /out:$release $source
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

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
    $shortcut.Description = [Text.Encoding]::UTF8.GetString(
        [Convert]::FromBase64String('6K+K5pat44CB5L+u5aSN5bm25a6J5YWo5ZCv5YqoIENvZGV4')
    )
    $shortcut.Save()
}

$hash = (Get-FileHash -LiteralPath $release -Algorithm SHA256).Hash
[pscustomobject]@{
    Release = $release
    Bytes = (Get-Item -LiteralPath $release).Length
    SHA256 = $hash
    DesktopShortcut = if ($InstallDesktopShortcut) { $shortcutPath } else { '' }
}
