# COM3D2 Trainer - One-click installer
# Usage: double-click 安装.bat  (or: powershell -ExecutionPolicy Bypass -File install.ps1)
# Installs only the trainer DLL. Game not included. Requires BepInEx x64 in the target game.

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$dllName = 'COM3D2InGameTrainer.dll'
$srcDll = Join-Path $scriptDir $dllName

function Find-GameRoot {
    # 1) script dir / parent dir is the game dir
    foreach ($base in @($scriptDir, (Split-Path $scriptDir -Parent))) {
        if (Test-Path (Join-Path $base 'COM3D2x64.exe')) { return $base }
    }
    # 2) registry uninstall entries for COM3D2 / Custom Order Maid
    $keys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    foreach ($k in $keys) {
        try {
            Get-ItemProperty $k -ErrorAction SilentlyContinue | ForEach-Object {
                $dn = $_.DisplayName
                if ($dn -and ($dn -match 'COM3D2|Custom Order Maid')) {
                    $loc = $_.InstallLocation
                    if ($loc -and (Test-Path (Join-Path $loc 'COM3D2x64.exe'))) { return $loc }
                }
            }
        } catch { }
    }
    # 3) common paths
    $guess = @(
        "$env:USERPROFILE\Desktop\COM3D2",
        'C:\COM3D2', 'D:\COM3D2',
        'C:\Program Files (x86)\Steam\steamapps\common\Custom Order Maid 3D2',
        'D:\Steam\steamapps\common\Custom Order Maid 3D2'
    )
    foreach ($g in $guess) {
        if ($g -and (Test-Path (Join-Path $g 'COM3D2x64.exe'))) { return $g }
    }
    return $null
}

Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host '   COM3D2 Trainer Installer' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Path $srcDll)) {
    Write-Host '[X] Trainer DLL not found: $dllName' -ForegroundColor Red
    Write-Host '    Make sure install.ps1 and $dllName are in the same folder.'
    exit 1
}

$gameRoot = Find-GameRoot
if (-not $gameRoot) {
    Write-Host '[!] Could not auto-detect COM3D2 install folder.' -ForegroundColor Yellow
    Write-Host '    Paste the game folder path (the one containing COM3D2x64.exe),'
    Write-Host '    or drop this installer into the game folder and re-run.'
    $gameRoot = Read-Host 'Game folder path'
    $gameRoot = $gameRoot.Trim('"').Trim()
}
if (-not (Test-Path (Join-Path $gameRoot 'COM3D2x64.exe'))) {
    Write-Host '[X] COM3D2x64.exe not found in:' -ForegroundColor Red
    Write-Host "    $gameRoot"
    exit 1
}
Write-Host "[OK] Game folder: $gameRoot" -ForegroundColor Green

$bepDir = Join-Path $gameRoot 'BepInEx'
$bepCore = Join-Path $bepDir 'core\BepInEx.dll'
if (-not (Test-Path $bepCore)) {
    Write-Host ''
    Write-Host '[X] BepInEx not detected.' -ForegroundColor Red
    Write-Host '    This trainer is a BepInEx plugin. Install BepInEx x64 (Unity Mono) first:'
    Write-Host '    https://github.com/BepInEx/BepInEx'
    Write-Host '    Marker file expected: <game>\BepInEx\core\BepInEx.dll'
    exit 1
}
Write-Host '[OK] BepInEx detected' -ForegroundColor Green

$pluginsDir = Join-Path $bepDir 'plugins'
if (-not (Test-Path $pluginsDir)) { New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null }
$dst = Join-Path $pluginsDir $dllName

# Warn if the game is running (DLL would be locked)
$running = Get-Process -Name 'COM3D2x64' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host ''
    Write-Host '[!] COM3D2x64.exe is running (PID ' $running.Id ').' -ForegroundColor Yellow
    Write-Host '    The game holds the plugin DLL locked. Close the game first, then re-run this installer.'
    $cont = Read-Host 'Continue anyway? (y/N)'
    if ($cont -trim() -ne 'y') { Write-Host 'Aborted.'; exit 1 }
}

if (Test-Path $dst) {
    $bak = "$dst.bak.$(Get-Date -Format yyyyMMdd_HHmmss)"
    Copy-Item $dst $bak -Force
    Write-Host "[backup] old version backed up: $(Split-Path $bak -Leaf)" -ForegroundColor DarkGray
}

Copy-Item $srcDll $dst -Force
Write-Host "[OK] Installed: $dst" -ForegroundColor Green

Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ' Install complete!' -ForegroundColor Green
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ' Launch the game, enter the club, press [F9] to toggle the panel.'
Write-Host ' Uninstall: run uninstall.bat, or delete plugins\COM3D2InGameTrainer.dll'
Write-Host ''
