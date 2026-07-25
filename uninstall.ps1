# COM3D2 Trainer - Uninstaller
$ErrorActionPreference = 'SilentlyContinue'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$gameRoot = $null
foreach ($base in @($scriptDir, (Split-Path $scriptDir -Parent))) {
    if (Test-Path (Join-Path $base 'COM3D2x64.exe')) { $gameRoot = $base; break }
}
if (-not $gameRoot) {
    $gameRoot = (Read-Host 'Game folder path (containing COM3D2x64.exe)').Trim('"').Trim()
}
$dll = Join-Path $gameRoot "BepInEx\plugins\COM3D2InGameTrainer.dll"
if (Test-Path $dll) {
    Remove-Item $dll -Force
    Write-Host "[OK] Deleted: $dll" -ForegroundColor Green
} else {
    Write-Host "[i] Trainer DLL not found (maybe already uninstalled)" -ForegroundColor Yellow
}
Get-ChildItem (Join-Path $gameRoot "BepInEx\plugins\COM3D2InGameTrainer.dll.bak.*") -ErrorAction SilentlyContinue | Remove-Item -Force
Write-Host 'Uninstall done.'
