#Requires -Version 5.1
<#
.SYNOPSIS
    Custom installer for ClawTweaks Xbox Game Bar Widget.

.DESCRIPTION
    Professional installer that handles Debug-to-Release upgrades, dependency checking,
    prerequisite installation, and blocking process management with user-friendly prompts.

    Prerequisites checked and auto-installed if missing:
      - PawnIO           (kernel driver for CPU/GPU power sensors — required by LHM 0.9.6+)
      - ViGEmBus driver  (virtual controller emulation)
      - RTSS             (FPS limiter + overlay)

.PARAMETER Force
    Suppress confirmation prompts for silent/unattended installation.

.PARAMETER SkipCertificate
    Skip certificate installation (use if certificate is already trusted).

.PARAMETER CleanInstall
    Remove existing package before installing (loses user settings/profiles).
    Default is to update in-place which preserves user data.

.PARAMETER SkipPrereqs
    Skip prerequisite checks (ViGEmBus, RTSS).
    Use if you manage dependencies separately.

.EXAMPLE
    .\Install.ps1
    Interactive installation with prompts. Updates existing install, preserving settings.

.EXAMPLE
    .\Install.ps1 -Force
    Silent installation without prompts.

.EXAMPLE
    .\Install.ps1 -CleanInstall
    Remove existing package first (fresh install, loses settings).

.EXAMPLE
    .\Install.ps1 -SkipPrereqs
    Skip prerequisite checks.

.NOTES
    Must be run as Administrator.
#>

param(
    [switch]$Force          = $false,
    [switch]$SkipCertificate = $false,
    [switch]$CleanInstall   = $false,
    [switch]$SkipPrereqs    = $false
)

$ErrorActionPreference = "Stop"

# Global error handler
trap {
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Red
    Write-Host "  UNEXPECTED ERROR" -ForegroundColor Red
    Write-Host "=============================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "Stack Trace:" -ForegroundColor Yellow
    Write-Host $_.ScriptStackTrace -ForegroundColor Gray
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

#region EXE-Compatible Path Detection

function Get-InstallerPath {
    $exePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    if ($exePath -and $exePath -match '\.exe$' -and $exePath -notmatch 'powershell\.exe$|pwsh\.exe$') {
        return $exePath
    }
    if ($PSCommandPath) { return $PSCommandPath }
    if ($MyInvocation.MyCommand.Path) { return $MyInvocation.MyCommand.Path }
    return $null
}

function Get-InstallerDirectory {
    $installerPath = Get-InstallerPath
    if ($installerPath) {
        return [System.IO.Path]::GetDirectoryName($installerPath)
    }
    if ($PSScriptRoot) { return $PSScriptRoot }
    return (Get-Location).Path
}

function Test-RunningAsExe {
    $exePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    return ($exePath -and $exePath -match '\.exe$' -and $exePath -notmatch 'powershell\.exe$|pwsh\.exe$')
}

#endregion

$script:ScriptPath = Get-InstallerPath
$PackageName = "PlayandBuildCustom.10365195AA1EC"

# Processes that may block installation
$BlockingProcesses = @(
    "XboxGamingBarHelper",
    "GameBar",
    "GameBarFTServer",
    "GameBarPresenceWriter",
    "XboxGamingBarWidget"
)

#region Helper Functions

function Write-Step {
    param([int]$Step, [int]$Total, [string]$Message)
    Write-Host "`n[$Step/$Total] " -ForegroundColor Cyan -NoNewline
    Write-Host $Message -ForegroundColor White
}

function Write-Success {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "       $Message" -ForegroundColor Gray
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  [!] $Message" -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Message)
    Write-Host "  [X] $Message" -ForegroundColor Red
}

function Exit-WithPause {
    param([int]$ExitCode = 0)
    Write-Host ""
    Write-Host "Press any key to exit..." -ForegroundColor Gray
    try {
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    catch {
        Read-Host "Press Enter to exit"
    }
    exit $ExitCode
}

function Test-Administrator {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Request-Elevation {
    $elevateScriptPath = $script:ScriptPath

    if (-not $elevateScriptPath) {
        Write-Err "Cannot determine installer path for elevation."
        Write-Host "Please run this installer as Administrator manually." -ForegroundColor Yellow
        pause
        exit 1
    }

    $paramArgs = @()
    if ($Force)           { $paramArgs += "-Force" }
    if ($SkipCertificate) { $paramArgs += "-SkipCertificate" }
    if ($CleanInstall)    { $paramArgs += "-CleanInstall" }
    if ($SkipPrereqs)     { $paramArgs += "-SkipPrereqs" }

    try {
        if (Test-RunningAsExe) {
            if ($paramArgs.Count -gt 0) {
                $proc = Start-Process -FilePath $elevateScriptPath -Verb RunAs -ArgumentList ($paramArgs -join " ") -PassThru -Wait
            }
            else {
                $proc = Start-Process -FilePath $elevateScriptPath -Verb RunAs -PassThru -Wait
            }
        }
        else {
            $argString = "-ExecutionPolicy Bypass -File `"$elevateScriptPath`""
            if ($paramArgs.Count -gt 0) {
                $argString += " " + ($paramArgs -join " ")
            }
            $proc = Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $argString -PassThru -Wait
        }
        exit $proc.ExitCode
    }
    catch {
        Write-Err "Failed to elevate to Administrator: $_"
        Write-Host "Please right-click the installer and select 'Run as Administrator'." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Press any key to exit..." -ForegroundColor Gray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 1
    }
}

function Get-RunningBlockers {
    $running = @()
    foreach ($procName in $BlockingProcesses) {
        $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
        if ($procs) {
            $running += $procName
        }
    }
    return $running
}

function Stop-BlockingProcesses {
    param([switch]$Quiet)

    $killed = @()
    foreach ($procName in $BlockingProcesses) {
        $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
        if ($procs) {
            foreach ($proc in $procs) {
                try {
                    $proc | Stop-Process -Force -ErrorAction Stop
                    $killed += $procName
                }
                catch {
                    if (-not $Quiet) {
                        Write-Warn "Could not stop $procName (PID: $($proc.Id))"
                    }
                }
            }
        }
    }

    if ($killed.Count -gt 0) {
        Start-Sleep -Milliseconds 1500
    }

    return ($killed | Select-Object -Unique)
}

function Get-DependencyPackages {
    param([string]$DependenciesDir)

    $packages = @()

    # Only get x64 dependencies - this project only supports x64
    $x64Path = Join-Path $DependenciesDir "x64"
    if (Test-Path $x64Path) {
        $packages += Get-ChildItem -Path $x64Path -Filter "*.appx" -ErrorAction SilentlyContinue
        $packages += Get-ChildItem -Path $x64Path -Filter "*.msix" -ErrorAction SilentlyContinue
    }

    return $packages
}

function Test-DependencyInstalled {
    param([string]$PackageBaseName)

    $coreName = $PackageBaseName -replace '\.Debug$', ''
    $installedPackages = Get-AppxPackage -ErrorAction SilentlyContinue

    foreach ($pkg in $installedPackages) {
        if ($pkg.Name -eq $PackageBaseName) { return $true }
        if ($pkg.Name -eq $coreName)        { return $true }
        if ($pkg.Name -eq "$coreName.x64" -or $pkg.Name -eq "$PackageBaseName.x64") { return $true }
    }

    return $false
}

function Get-MissingDependencies {
    param([array]$DependencyPackages)

    $missing = @()

    foreach ($dep in $DependencyPackages) {
        $baseName = $dep.BaseName -replace '\.x64$|\.x86$|\.arm64$|\.arm$', ''

        if ($baseName -match '^(Microsoft\.[^.]+)\.x64\.(.+)$') {
            $baseName = "$($Matches[1]).$($Matches[2])"
        }

        if (-not (Test-DependencyInstalled -PackageBaseName $baseName)) {
            $missing += $dep
        }
    }

    return $missing
}

function Get-PackageVersion {
    param([string]$PackagePath)
    if ($PackagePath -match '_(\d+\.\d+\.\d+\.\d+)_') {
        return $Matches[1]
    }
    return "Unknown"
}

#endregion

#region Prerequisite Detection & Installation

function Test-PawnIOWorking {
    # The only reliable test: try to open the \\.\PawnIO device.
    # Service status is unreliable — the service can show "Running" while the
    # driver binary is missing (deinstalled), causing error code 2 (FILE_NOT_FOUND).
    try {
        $result = & powershell -NonInteractive -Command {
            try {
                $handle = [System.IO.File]::Open('\\.\PawnIO',
                    [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::ReadWrite,
                    [System.IO.FileShare]::ReadWrite)
                $handle.Close()
                exit 0
            } catch { exit 1 }
        }
        return ($LASTEXITCODE -eq 0)
    }
    catch { return $false }
}

function Test-PawnIOServiceExists {
    $svc = Get-Service -Name "PawnIO" -ErrorAction SilentlyContinue
    return ($svc -ne $null)
}

function Install-PawnIO {
    Write-Info "Attempting PawnIO install via winget (namazso.PawnIO)..."
    $ok = Install-ViaWinget -PackageId "namazso.PawnIO" -DisplayName "PawnIO"
    if ($ok) { return $true }

    Write-Warn "Automatic PawnIO install failed. Please install manually:"
    Write-Host "       https://github.com/namazso/PawnIO/releases" -ForegroundColor Cyan
    return $false
}

function Test-ViGEmInstalled {
    # Check for the kernel driver service
    $svc = Get-Service -Name "ViGEmBus" -ErrorAction SilentlyContinue
    if ($svc) { return $true }
    # Check for the INF file in Windows driver store
    $inf = Get-ChildItem "C:\Windows\INF" -Filter "ViGEmBus*.inf" -ErrorAction SilentlyContinue
    if ($inf) { return $true }
    # Check via PnP device
    $dev = Get-PnpDevice -FriendlyName "*ViGEm*" -ErrorAction SilentlyContinue
    if ($dev) { return $true }
    return $false
}

function Test-RTSSInstalled {
    # Check common install locations
    $paths = @(
        "$env:ProgramFiles\RivaTuner Statistics Server\RTSS.exe",
        "${env:ProgramFiles(x86)}\RivaTuner Statistics Server\RTSS.exe"
    )
    foreach ($p in $paths) {
        if (Test-Path $p) { return $true }
    }
    # Check registry (RTSS writes install path here)
    $reg = Get-ItemProperty "HKLM:\SOFTWARE\Guru3D\RTSS" -ErrorAction SilentlyContinue
    if ($reg) { return $true }
    $reg2 = Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Guru3D\RTSS" -ErrorAction SilentlyContinue
    if ($reg2) { return $true }
    # Check if process is running
    $proc = Get-Process -Name "RTSS" -ErrorAction SilentlyContinue
    if ($proc) { return $true }
    return $false
}

function Get-WingetExe {
    # Try winget from PATH
    $wg = Get-Command "winget.exe" -ErrorAction SilentlyContinue
    if ($wg) { return $wg.Source }
    # Try well-known location (Windows App Installer)
    $candidate = "$env:LOCALAPPDATA\Microsoft\WindowsApps\winget.exe"
    if (Test-Path $candidate) { return $candidate }
    return $null
}

function Install-ViaWinget {
    param(
        [string]$PackageId,
        [string]$DisplayName
    )
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

function Install-ViaDirectDownload {
    param(
        [string]$Url,
        [string]$InstallerName,
        [string]$SilentArgs,
        [string]$DisplayName
    )
    $tmpPath = Join-Path $env:TEMP $InstallerName
    try {
        Write-Info "Downloading $DisplayName..."
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add("User-Agent", "ClawTweaks-Installer/1.0")
        $wc.DownloadFile($Url, $tmpPath)
        Write-Info "Installing $DisplayName..."
        $proc = Start-Process -FilePath $tmpPath -ArgumentList $SilentArgs -Wait -PassThru
        Remove-Item $tmpPath -Force -ErrorAction SilentlyContinue
        return ($proc.ExitCode -eq 0 -or $proc.ExitCode -eq 1641 -or $proc.ExitCode -eq 3010)
    }
    catch {
        Write-Warn "Direct download/install failed: $_"
        if (Test-Path $tmpPath) { Remove-Item $tmpPath -Force -ErrorAction SilentlyContinue }
        return $false
    }
}

function Install-ViGEmBus {
    # Try winget first (cleanest)
    Write-Info "Attempting install via winget (Nefarius.ViGEmBus)..."
    $ok = Install-ViaWinget -PackageId "Nefarius.ViGEmBus" -DisplayName "ViGEmBus"
    if ($ok) { return $true }

    # Fallback: direct download from GitHub latest release
    Write-Info "winget unavailable or failed — trying direct download..."
    # GitHub API: get latest release asset URL
    try {
        $apiUrl   = "https://api.github.com/repos/nefarius/ViGEmBus/releases/latest"
        $headers  = @{ "User-Agent" = "ClawTweaks-Installer/1.0" }
        $release  = Invoke-RestMethod -Uri $apiUrl -Headers $headers -ErrorAction Stop
        $asset    = $release.assets | Where-Object { $_.name -like "*.exe" -and $_.name -notlike "*symbols*" } | Select-Object -First 1
        if ($asset) {
            $ok = Install-ViaDirectDownload `
                -Url          $asset.browser_download_url `
                -InstallerName "ViGEmBus_Setup.exe" `
                -SilentArgs   "/passive /norestart" `
                -DisplayName  "ViGEmBus"
            if ($ok) { return $true }
        }
    }
    catch {
        Write-Warn "GitHub API fetch failed: $_"
    }

    return $false
}

function Install-RTSS {
    # Try winget first
    Write-Info "Attempting install via winget (Guru3D.RTSS)..."
    $ok = Install-ViaWinget -PackageId "Guru3D.RTSS" -DisplayName "RivaTuner Statistics Server"
    if ($ok) { return $true }

    Write-Warn "Automatic RTSS install failed. Please install manually:"
    Write-Host "       https://www.guru3d.com/files-details/rtss-rivatuner-statistics-server-download.html" -ForegroundColor Cyan
    return $false
}

function Invoke-PrerequisiteCheck {
    $allOk = $true

    # --- PawnIO ---
    # Required by LibreHardwareMonitor 0.9.6+ for kernel-level hardware access (RAPL MSRs).
    # Without a working driver all power sensors return 0W in the overlay.
    # Detection opens \\.\PawnIO directly — service status alone is unreliable
    # (driver binary can be missing while service entry still exists after uninstall).
    Write-Host ""
    Write-Host "       Checking PawnIO (hardware sensor kernel driver)..." -ForegroundColor Gray
    if (Test-PawnIOWorking) {
        Write-Success "PawnIO: working"
    }
    else {
        if (Test-PawnIOServiceExists) {
            Write-Warn "PawnIO: service entry exists but driver is NOT functional (binary missing or not loaded)"
        }
        else {
            Write-Warn "PawnIO: NOT installed  (needed for CPU/GPU power sensors in overlay)"
        }
        $install = $Force
        if (-not $Force) {
            $response = Read-Host "       Install / reinstall PawnIO now? (Y/N)"
            $install  = ($response -eq 'Y' -or $response -eq 'y')
        }
        if ($install) {
            $ok = Install-PawnIO
            if ($ok) {
                Write-Success "PawnIO installed — a reboot may be required for the driver to activate"
            }
            else {
                Write-Warn "PawnIO install failed — power sensors will show 0W in overlay"
                Write-Info "Get it from: https://github.com/namazso/PawnIO/releases"
                $allOk = $false
            }
        }
        else {
            Write-Info "Skipped. CPU/GPU power sensors will not work without PawnIO."
            $allOk = $false
        }
    }

    # --- ViGEmBus ---
    Write-Host ""
    Write-Host "       Checking ViGEmBus (virtual controller driver)..." -ForegroundColor Gray
    if (Test-ViGEmInstalled) {
        Write-Success "ViGEmBus: installed"
    }
    else {
        Write-Warn "ViGEmBus: NOT installed  (needed for controller emulation)"
        $install = $Force
        if (-not $Force) {
            $response = Read-Host "       Install ViGEmBus now? (Y/N)"
            $install  = ($response -eq 'Y' -or $response -eq 'y')
        }
        if ($install) {
            $ok = Install-ViGEmBus
            if ($ok) {
                Write-Success "ViGEmBus installed successfully"
            }
            else {
                Write-Warn "ViGEmBus install failed — controller emulation will not work"
                Write-Info "Get it from: https://github.com/nefarius/ViGEmBus/releases"
                $allOk = $false
            }
        }
        else {
            Write-Info "Skipped. Controller emulation features will not work without ViGEmBus."
            $allOk = $false
        }
    }

    # --- RTSS ---
    Write-Host ""
    Write-Host "       Checking RTSS (RivaTuner Statistics Server)..." -ForegroundColor Gray
    if (Test-RTSSInstalled) {
        Write-Success "RTSS: installed"
    }
    else {
        Write-Warn "RTSS: NOT installed  (needed for FPS limiter and overlay)"
        $install = $Force
        if (-not $Force) {
            $response = Read-Host "       Install RTSS now? (Y/N)"
            $install  = ($response -eq 'Y' -or $response -eq 'y')
        }
        if ($install) {
            $ok = Install-RTSS
            if ($ok) {
                Write-Success "RTSS installed successfully"
            }
            else {
                Write-Warn "RTSS install failed — FPS limiter/overlay will not work"
                $allOk = $false
            }
        }
        else {
            Write-Info "Skipped. FPS limiter and overlay features will not work without RTSS."
            $allOk = $false
        }
    }

    return $allOk
}

#endregion

#region Main Script

Clear-Host

# Get installer directory
# When Install.ps1 lives in _Installer\ (called by Install.bat), the package
# files (.msix, .cer, Dependencies\) are in the parent folder — use that instead.
$ScriptDir = Get-InstallerDirectory
$parentDir = Split-Path $ScriptDir -Parent
if ((Split-Path $ScriptDir -Leaf) -eq "_Installer" -and (Test-Path $parentDir)) {
    $ScriptDir = $parentDir
}

# Find main package early so we can show version
$MainPackage = Get-ChildItem -Path $ScriptDir -Filter "*.msixbundle" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $MainPackage) {
    $MainPackage = Get-ChildItem -Path $ScriptDir -Filter "*.appxbundle" -ErrorAction SilentlyContinue | Select-Object -First 1
}
if (-not $MainPackage) {
    $MainPackage = Get-ChildItem -Path $ScriptDir -Filter "*.msix" -ErrorAction SilentlyContinue | Select-Object -First 1
}

$packageVersion = if ($MainPackage) { Get-PackageVersion -PackagePath $MainPackage.Name } else { "Unknown" }

# Welcome Banner
Write-Host ""
Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host "                                               " -ForegroundColor Cyan
Write-Host "         ClawTweaks Installer                  " -ForegroundColor White
Write-Host "         Xbox Game Bar Widget                  " -ForegroundColor Gray
Write-Host "                                               " -ForegroundColor Cyan
Write-Host "         Version: $packageVersion                      " -ForegroundColor DarkGray
Write-Host "                                               " -ForegroundColor Cyan
Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  This installer will set up ClawTweaks on your system." -ForegroundColor Gray
Write-Host "  ClawTweaks provides TDP control, performance monitoring," -ForegroundColor Gray
Write-Host "  and controller customization for the MSI Claw." -ForegroundColor Gray
Write-Host ""

# Check for existing installation
$existingPkg = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
if ($existingPkg) {
    Write-Host "  Existing installation detected: v$($existingPkg.Version)" -ForegroundColor Yellow
    Write-Host ""
}

if (-not $Force) {
    Write-Host "  Press Enter to continue or Ctrl+C to cancel..." -ForegroundColor DarkGray
    Read-Host | Out-Null
}

$TotalSteps = if ($SkipPrereqs) { 6 } else { 7 }

# Phase 1: Check Administrator
Write-Step -Step 1 -Total $TotalSteps -Message "Checking administrator privileges..."

if (-not (Test-Administrator)) {
    Write-Info "Requesting elevation to Administrator..."
    Request-Elevation
    exit 0
}
Write-Success "Running as Administrator"

# Phase 2: Locate package files
Write-Step -Step 2 -Total $TotalSteps -Message "Locating package files..."

if (-not $MainPackage) {
    $MainPackage = Get-ChildItem -Path $ScriptDir -Filter "*.appx" -ErrorAction SilentlyContinue | Select-Object -First 1
}

if (-not $MainPackage) {
    Write-Err "No package file found in $ScriptDir"
    Write-Info "Expected: .msixbundle, .appxbundle, .msix, or .appx"
    Exit-WithPause -ExitCode 1
}
Write-Success "Package: $($MainPackage.Name)"

# Find certificate
$Certificate = Get-ChildItem -Path $ScriptDir -Filter "*.cer" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($Certificate) {
    Write-Success "Certificate: $($Certificate.Name)"
}
else {
    Write-Info "No certificate file (may already be trusted)"
}

# Find dependencies - x64 only
$DependenciesDir = Join-Path $ScriptDir "Dependencies"
$depPackages = @()
if (Test-Path $DependenciesDir) {
    $allDepPackages = Get-DependencyPackages -DependenciesDir $DependenciesDir
    if ($allDepPackages.Count -gt 0) {
        $depPackages = Get-MissingDependencies -DependencyPackages $allDepPackages
        if ($depPackages.Count -gt 0) {
            Write-Success "Dependencies: $($depPackages.Count) missing (will install if needed)"
        }
        else {
            Write-Success "Dependencies: All present (using system libraries)"
        }
    }
}
else {
    Write-Info "No Dependencies folder"
}

# Phase 3: Prerequisites (ViGEmBus, RTSS) — skippable
if (-not $SkipPrereqs) {
    Write-Step -Step 3 -Total $TotalSteps -Message "Checking prerequisites..."
    $prereqsOk = Invoke-PrerequisiteCheck
    if (-not $prereqsOk) {
        Write-Host ""
        Write-Warn "Some prerequisites are missing. Related features will be unavailable."
        Write-Info "You can install them later and ClawTweaks will detect them automatically."
    }
}

$nextStep = if ($SkipPrereqs) { 3 } else { 4 }

# Phase 4: Check for blocking processes
Write-Step -Step $nextStep -Total $TotalSteps -Message "Checking for blocking processes..."
$nextStep++

$runningBlockers = Get-RunningBlockers

if ($runningBlockers.Count -gt 0) {
    Write-Info "Closing blocking processes automatically..."
    $killedProcesses = Stop-BlockingProcesses
    if ($killedProcesses.Count -gt 0) {
        Write-Success "Closed: $($killedProcesses -join ', ')"
    }
}
else {
    Write-Success "No blocking processes"
}

# Phase 5: Handle existing package
if ($CleanInstall) {
    Write-Step -Step $nextStep -Total $TotalSteps -Message "Removing existing package (clean install)..."
    $nextStep++

    $existingPkg = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
    if ($existingPkg) {
        Write-Warn "Clean install will remove user settings and profiles!"
        if (-not $Force) {
            Write-Host ""
            $response = Read-Host "       Continue with clean install? (Y/N)"
            if ($response -ne 'Y' -and $response -ne 'y') {
                Write-Info "Switching to update mode (preserving settings)..."
                $CleanInstall = $false
            }
        }

        if ($CleanInstall) {
            Write-Info "Removing: v$($existingPkg.Version)"
            try {
                Stop-BlockingProcesses -Quiet | Out-Null
                Remove-AppxPackage -Package $existingPkg.PackageFullName -ErrorAction Stop
                Write-Success "Removed existing package"
                Start-Sleep -Seconds 2
            }
            catch {
                Write-Warn "Could not remove: $_"
                Write-Info "Will attempt upgrade instead..."
            }
        }
    }
    else {
        Write-Info "No existing package to remove"
    }
}
else {
    Write-Step -Step $nextStep -Total $TotalSteps -Message "Checking existing installation..."
    $nextStep++

    $existingPkg = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
    if ($existingPkg) {
        Write-Info "Will update existing installation (preserving settings)"
        Write-Info "Current version: $($existingPkg.Version)"
    }
    else {
        Write-Info "Fresh installation"
    }
}

# Phase 6: Install certificate
Write-Step -Step $nextStep -Total $TotalSteps -Message "Installing certificate..."
$nextStep++

if ($SkipCertificate) {
    Write-Info "Skipped (--SkipCertificate)"
}
elseif (-not $Certificate) {
    Write-Info "No certificate file to install"
}
else {
    $signature = Get-AuthenticodeSignature -FilePath $MainPackage.FullName -ErrorAction SilentlyContinue

    if ($signature -and $signature.Status -eq "Valid") {
        Write-Success "Package signature already trusted"
    }
    else {
        $cert = Get-PfxCertificate -FilePath $Certificate.FullName
        $existingCert = Get-ChildItem -Path "Cert:\LocalMachine\TrustedPeople" -ErrorAction SilentlyContinue |
                        Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

        if ($existingCert) {
            Write-Success "Certificate already trusted"
        }
        else {
            if (-not $Force) {
                Write-Host ""
                Write-Host "       A developer certificate needs to be installed." -ForegroundColor Yellow
                Write-Host "       Subject: $($cert.Subject)" -ForegroundColor DarkGray
                Write-Host ""
                $response = Read-Host "       Install certificate? (Y/N)"
                if ($response -ne 'Y' -and $response -ne 'y') {
                    Write-Err "Certificate required. Installation cancelled."
                    Exit-WithPause -ExitCode 1
                }
            }

            $certResult = certutil.exe -addstore TrustedPeople $Certificate.FullName 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Certificate installed"
            }
            else {
                Write-Err "Failed to install certificate"
                Exit-WithPause -ExitCode 1
            }
        }
    }
}

# Phase 7: Install package
Write-Step -Step $nextStep -Total $TotalSteps -Message "Installing package..."

$maxRetries    = 2
$retryCount    = 0
$installSuccess = $false

while ($retryCount -lt $maxRetries -and -not $installSuccess) {
    try {
        Stop-BlockingProcesses -Quiet | Out-Null

        Write-Info "Installing ClawTweaks package..."
        Add-AppxPackage -Path $MainPackage.FullName `
            -ForceUpdateFromAnyVersion `
            -ErrorAction Stop

        $installSuccess = $true
    }
    catch {
        $retryCount++
        $errorMsg = $_.Exception.Message

        # Check if it's a dependency error
        if ($errorMsg -match "dependency" -and $depPackages.Count -gt 0) {
            Write-Warn "Missing dependencies, attempting to install them..."
            foreach ($dep in $depPackages) {
                try {
                    Write-Info "  -> $($dep.Name)"
                    Add-AppxPackage -Path $dep.FullName -ForceUpdateFromAnyVersion -ErrorAction SilentlyContinue
                }
                catch {
                    Write-Warn "    Could not install (may already be present)"
                }
            }
            $retryCount--
            Start-Sleep -Seconds 1
            continue
        }

        if ($retryCount -lt $maxRetries) {
            Write-Warn "Attempt $retryCount failed, retrying..."
            Start-Sleep -Seconds 2
        }
        else {
            Write-Err "Installation failed: $errorMsg"
            Write-Host ""
            Write-Host "       Troubleshooting:" -ForegroundColor Yellow
            Write-Host "       - Close Xbox Game Bar completely (Win+G, then X)" -ForegroundColor Gray
            Write-Host "       - Restart your computer and try again" -ForegroundColor Gray
            Write-Host "       - If issue persists, run: Install.exe -CleanInstall" -ForegroundColor Gray
            Exit-WithPause -ExitCode 1
        }
    }
}

# Verify installation
$installedPkg = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
if ($installedPkg) {
    Write-Host ""
    Write-Host "  =============================================" -ForegroundColor Green
    Write-Host "                                               " -ForegroundColor Green
    Write-Host "         Installation Complete!                " -ForegroundColor White
    Write-Host "                                               " -ForegroundColor Green
    Write-Host "  =============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Version: $($installedPkg.Version)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Press Win+G to open Xbox Game Bar" -ForegroundColor Cyan
    Write-Host "  Then click the Widgets menu to add ClawTweaks" -ForegroundColor Cyan
    Write-Host ""
}
else {
    Write-Warn "Installation may have succeeded. Press Win+G to verify."
}

Exit-WithPause -ExitCode 0

#endregion
