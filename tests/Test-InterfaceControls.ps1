$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot '..\src\WindowsRepairCenter.cs')
$checks = [ordered]@{
    'low-frequency-actions-use-more-menu' = $source.Contains('moreMenu = new ContextMenuStrip') -and $source.Contains('reportMenuItem') -and $source.Contains('recordMenuItem')
    'report-and-record-are-not-permanent-buttons' = -not ($source.Contains('reportButton = MakeButton') -or $source.Contains('recordButton = MakeButton'))
    'repair-action-is-contextual' = $source.Contains('repairButton.Visible = canRepair') -and $source.Contains('findings.TryGetValue(selectedModule.Id, out finding) && finding.CanRepair')
    'scan-label-explains-next-action' = ([regex]::Matches($source, 'scanButton\.Text\s*=\s*"[^"]+"')).Count -ge 2
    'openclaw-actions-degrade-when-controller-missing' = $source.Contains('openClawActions.Visible = isOpenClaw && OpenClawController.IsAvailable')
    'quick-tools-are-right-pane-pages' = $source.Contains('moduleButtons["quick-tools"]') -and $source.Contains('ShowEmbeddedPage') -and $source.Contains('new QuickToolsForm(')
    'settings-are-right-pane-pages' = $source.Contains('moduleButtons["settings"]') -and $source.Contains('ShowSettingsPage') -and $source.Contains('new SettingsForm(')
    'product-is-personal-toolbox' = (Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot '..\src\ProductExperience.cs')).Contains('ProductName =')
    'dark-mode-is-persisted' = (Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot '..\src\ProductExperience.cs')).Contains('ColorMode=')
}
$failed = $checks.GetEnumerator() | Where-Object { -not $_.Value }
if ($failed) { throw ('Interface control checks failed: ' + (($failed | ForEach-Object Key) -join ', ')) }
[pscustomobject]@{ Result = 'PASS'; Checks = $checks.Count; Details = (($checks.GetEnumerator() | ForEach-Object { "$($_.Key)=PASS" }) -join '; ') }
