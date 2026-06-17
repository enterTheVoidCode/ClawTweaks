<#
.SYNOPSIS
    ClawTweaks tool prerequisite check + installer.

.DESCRIPTION
    Detects and (if missing) installs the four required tools:
      - PawnIO           (kernel driver for CPU/GPU power sensors - required by LHM 0.9.6+)
      - ViGEmBus driver  (virtual controller emulation)
      - HidHide          (hides the physical controller during emulation)
      - RTSS             (FPS limiter + on-screen overlay)

    This is the same detection/install logic that shipped in the original ClawTweaks
    installer (Install.ps1), extracted into a standalone, NON-INTERACTIVE script that
    the app triggers via its (already-elevated) helper. It downloads from the official
    vendor/GitHub sources only and never prompts - every missing tool is installed.

    Run elevated. Exit code 0 = all tools present afterwards, 1 = at least one missing.

    -Only <pawnio|vigem|hidhide|rtss> limits the run to a single tool (used by the per-tool
    install buttons); empty/omitted checks and installs all four.
#>
param([string]$Only = '')

$ErrorActionPreference = 'Continue'

function Test-ShouldRun { param([string]$Id) return ([string]::IsNullOrEmpty($Only) -or $Only -ieq $Id) }

#region Logging helpers
function Write-Info    { param([string]$Message) Write-Host "       $Message" -ForegroundColor Gray }
function Write-Success { param([string]$Message) Write-Host "  [OK] $Message" -ForegroundColor Green }
function Write-Warn    { param([string]$Message) Write-Host "  [!] $Message" -ForegroundColor Yellow }
function Write-Err     { param([string]$Message) Write-Host "  [X] $Message" -ForegroundColor Red }
#endregion

#region Detection
function Test-PawnIOWorking {
    # The only reliable test: try to open the \\.\PawnIO device directly (in-process — no nested
    # powershell child, which Defender's script ML treats as suspicious). Service status is
    # unreliable: the service can show "Running" while the driver binary is missing.
    try {
        $handle = [System.IO.File]::Open('\\.\PawnIO',
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::ReadWrite)
        $handle.Close()
        return $true
    }
    catch { return $false }
}

function Test-ViGEmInstalled {
    $svc = Get-Service -Name "ViGEmBus" -ErrorAction SilentlyContinue
    if ($svc) { return $true }
    $inf = Get-ChildItem "C:\Windows\INF" -Filter "ViGEmBus*.inf" -ErrorAction SilentlyContinue
    if ($inf) { return $true }
    $dev = Get-PnpDevice -FriendlyName "*ViGEm*" -ErrorAction SilentlyContinue
    if ($dev) { return $true }
    return $false
}

function Test-HidHideInstalled {
    $svc = Get-Service -Name "HidHide" -ErrorAction SilentlyContinue
    if ($svc) { return $true }
    $reg = Get-ItemProperty "HKLM:\SOFTWARE\Nefarius Software Solutions e.U.\HidHide" -ErrorAction SilentlyContinue
    if ($reg) { return $true }
    $dev = Get-PnpDevice -FriendlyName "*HidHide*" -ErrorAction SilentlyContinue
    if ($dev) { return $true }
    return $false
}

function Test-RTSSInstalled {
    $paths = @(
        "$env:ProgramFiles\RivaTuner Statistics Server\RTSS.exe",
        "${env:ProgramFiles(x86)}\RivaTuner Statistics Server\RTSS.exe"
    )
    foreach ($p in $paths) {
        if (Test-Path $p) { return $true }
    }
    $reg = Get-ItemProperty "HKLM:\SOFTWARE\Guru3D\RTSS" -ErrorAction SilentlyContinue
    if ($reg) { return $true }
    $reg2 = Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Guru3D\RTSS" -ErrorAction SilentlyContinue
    if ($reg2) { return $true }
    $proc = Get-Process -Name "RTSS" -ErrorAction SilentlyContinue
    if ($proc) { return $true }
    return $false
}
#endregion

#region Install backends
function Get-WingetExe {
    $wg = Get-Command "winget.exe" -ErrorAction SilentlyContinue
    if ($wg) { return $wg.Source }
    $candidate = "$env:LOCALAPPDATA\Microsoft\WindowsApps\winget.exe"
    if (Test-Path $candidate) { return $candidate }
    # The per-user app-execution alias is not visible to an elevated/SYSTEM context;
    # resolve the real winget.exe from the installed Microsoft.DesktopAppInstaller package.
    try {
        $pkg = Get-AppxPackage -AllUsers -Name "Microsoft.DesktopAppInstaller" -ErrorAction SilentlyContinue |
               Select-Object -First 1
        if ($pkg) {
            $real = Join-Path $pkg.InstallLocation "winget.exe"
            if (Test-Path $real) { return $real }
        }
    } catch { }
    return $null
}

function Install-ViaWinget {
    param([string]$PackageId, [string]$DisplayName)
    $winget = Get-WingetExe
    if (-not $winget) {
        Write-Warn "winget not found. Install $DisplayName manually."
        return $false
    }
    Write-Info "Running: winget install --id $PackageId ..."
    $result = & $winget install --id $PackageId --silent --accept-package-agreements --accept-source-agreements 2>&1
    if ($LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq -1978335189) {
        # -1978335189 = APPINSTALLER_ERROR_NO_APPLICABLE_INSTALLER (already installed / no update needed)
        return $true
    }
    Write-Warn "winget returned exit code $LASTEXITCODE"
    Write-Info ($result | Out-String).Trim()
    return $false
}

# NOTE: all four tools are installed exclusively via winget (Microsoft's package manager). There is
# deliberately no in-script fetch-then-launch fallback: that pattern makes Microsoft Defender's
# script ML flag this file (Program:Script/Wacapew.A!ml). winget itself retrieves the signed vendor
# package, so this script never fetches or launches a binary of its own. If winget is missing/fails,
# we simply point the user to the vendor page.
#endregion

#region Installers
function Install-PawnIO {
    Write-Info "Attempting PawnIO install via winget (namazso.PawnIO)..."
    $ok = Install-ViaWinget -PackageId "namazso.PawnIO" -DisplayName "PawnIO"
    if ($ok) { return $true }
    Write-Warn "Automatic PawnIO install failed. Get it from: https://github.com/namazso/PawnIO/releases"
    return $false
}

function Install-ViGEmBus {
    # winget package id is "ViGEm.ViGEmBus" (NOT "Nefarius.ViGEmBus", which does not exist and returns
    # "No package found"). HidHide is published under Nefarius.*, ViGEmBus under ViGEm.*.
    Write-Info "Attempting install via winget (ViGEm.ViGEmBus)..."
    $ok = Install-ViaWinget -PackageId "ViGEm.ViGEmBus" -DisplayName "ViGEmBus"
    if ($ok) { return $true }
    Write-Warn "Automatic ViGEmBus install failed (winget). Get it from: https://github.com/nefarius/ViGEmBus/releases"
    return $false
}

function Install-HidHide {
    Write-Info "Attempting install via winget (Nefarius.HidHide)..."
    $ok = Install-ViaWinget -PackageId "Nefarius.HidHide" -DisplayName "HidHide"
    if ($ok) { return $true }
    Write-Warn "Automatic HidHide install failed (winget). Get it from: https://github.com/nefarius/HidHide/releases"
    return $false
}

function Install-RTSS {
    Write-Info "Attempting install via winget (Guru3D.RTSS)..."
    $ok = Install-ViaWinget -PackageId "Guru3D.RTSS" -DisplayName "RivaTuner Statistics Server"
    if ($ok) { return $true }
    Write-Warn "Automatic RTSS install failed. Get it from: https://www.guru3d.com/files-details/rtss-rivatuner-statistics-server-download.html"
    return $false
}
#endregion

# --- Main: check each tool, install if missing (non-interactive) ---
Write-Host ""
Write-Host "ClawTweaks - checking required tools..." -ForegroundColor Cyan
$allOk = $true

# PawnIO
if (Test-ShouldRun 'pawnio') {
Write-Host ""
Write-Host "       Checking PawnIO (hardware sensor kernel driver)..." -ForegroundColor Gray
if (Test-PawnIOWorking) {
    Write-Success "PawnIO: working"
} else {
    Write-Warn "PawnIO: NOT installed - installing..."
    if (Install-PawnIO) { Write-Success "PawnIO installed - a reboot may be required for the driver to activate" }
    else { Write-Err "PawnIO install failed - power sensors may show 0W"; $allOk = $false }
}
}

# ViGEmBus — LEGACY emulation backend. The default backend is now VIIPER (usbip-win2), so ViGEm is
# NOT part of the one-click "all" run anymore. It is installed only when explicitly requested via the
# per-tool button (-Only vigem). This prevents re-installing ViGEm on devices running VIIPER.
if ($Only -ieq 'vigem') {
Write-Host ""
Write-Host "       Checking ViGEmBus (virtual controller driver)..." -ForegroundColor Gray
if (Test-ViGEmInstalled) {
    Write-Success "ViGEmBus: installed"
} else {
    Write-Warn "ViGEmBus: NOT installed - installing..."
    if (Install-ViGEmBus) { Write-Success "ViGEmBus installed successfully" }
    else { Write-Err "ViGEmBus install failed - controller emulation will not work"; $allOk = $false }
}
}

# HidHide
if (Test-ShouldRun 'hidhide') {
Write-Host ""
Write-Host "       Checking HidHide (controller hiding driver)..." -ForegroundColor Gray
if (Test-HidHideInstalled) {
    Write-Success "HidHide: installed"
} else {
    Write-Warn "HidHide: NOT installed - installing..."
    if (Install-HidHide) { Write-Success "HidHide installed successfully - a reboot may be required" }
    else { Write-Err "HidHide install failed"; $allOk = $false }
}
}

# RTSS
if (Test-ShouldRun 'rtss') {
Write-Host ""
Write-Host "       Checking RTSS (RivaTuner Statistics Server)..." -ForegroundColor Gray
if (Test-RTSSInstalled) {
    Write-Success "RTSS: installed"
} else {
    Write-Warn "RTSS: NOT installed - installing..."
    if (Install-RTSS) { Write-Success "RTSS installed successfully" }
    else { Write-Err "RTSS install failed - FPS limiter/overlay will not work"; $allOk = $false }
}
}

Write-Host ""
if ($allOk) { Write-Host "All required tools are present." -ForegroundColor Green; exit 0 }
else { Write-Host "Some tools are still missing - see messages above." -ForegroundColor Yellow; exit 1 }
