#Requires -Version 5.1
<#
.SYNOPSIS
    Custom installer for GoTweaks Xbox Game Bar Widget.

.DESCRIPTION
    Professional installer that handles Debug-to-Release upgrades, dependency checking,
    and blocking process management with user-friendly prompts.

.PARAMETER Force
    Suppress confirmation prompts for silent/unattended installation.

.PARAMETER SkipCertificate
    Skip certificate installation (use if certificate is already trusted).

.PARAMETER CleanInstall
    Remove existing package before installing (loses user settings/profiles).
    Default is to update in-place which preserves user data.

.EXAMPLE
    .\Install.ps1
    Interactive installation with prompts. Updates existing install, preserving settings.

.EXAMPLE
    .\Install.ps1 -Force
    Silent installation without prompts.

.EXAMPLE
    .\Install.ps1 -CleanInstall
    Remove existing package first (fresh install, loses settings).

.NOTES
    Must be run as Administrator.
#>

param(
    [switch]$Force = $false,
    [switch]$SkipCertificate = $false,
    [switch]$CleanInstall = $false
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
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
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
    if ($Force) { $paramArgs += "-Force" }
    if ($SkipCertificate) { $paramArgs += "-SkipCertificate" }
    if ($CleanInstall) { $paramArgs += "-CleanInstall" }

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

    # Extract the core package name (remove .Debug suffix if present)
    # e.g., "Microsoft.VCLibs.140.00.Debug" -> "Microsoft.VCLibs.140.00"
    $coreName = $PackageBaseName -replace '\.Debug$', ''

    # Check if either the exact package or the non-debug variant is installed
    $installedPackages = Get-AppxPackage -ErrorAction SilentlyContinue

    foreach ($pkg in $installedPackages) {
        # Check for exact match (e.g., Microsoft.VCLibs.140.00.Debug)
        if ($pkg.Name -eq $PackageBaseName) {
            return $true
        }
        # Check for non-debug variant (e.g., Microsoft.VCLibs.140.00)
        if ($pkg.Name -eq $coreName) {
            return $true
        }
        # Check for x64 specific variants
        if ($pkg.Name -eq "$coreName.x64" -or $pkg.Name -eq "$PackageBaseName.x64") {
            return $true
        }
    }

    return $false
}

function Get-MissingDependencies {
    param([array]$DependencyPackages)

    $missing = @()

    foreach ($dep in $DependencyPackages) {
        # Extract package name from filename
        # e.g., "Microsoft.VCLibs.x64.14.00.Desktop.Debug.appx" -> base name analysis
        $baseName = $dep.BaseName -replace '\.x64$|\.x86$|\.arm64$|\.arm$', ''

        # Try to extract a cleaner package name for checking
        # Handle patterns like "Microsoft.VCLibs.x64.14.00.Desktop.Debug"
        if ($baseName -match '^(Microsoft\.[^.]+)\.x64\.(.+)$') {
            # Reformat: Microsoft.VCLibs.x64.14.00 -> Microsoft.VCLibs.14.00
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

    # Extract version from package filename
    # e.g., "XboxGamingBarPackage_0.3.1137.0_x64.msixbundle" -> "0.3.1137.0"
    if ($PackagePath -match '_(\d+\.\d+\.\d+\.\d+)_') {
        return $Matches[1]
    }
    return "Unknown"
}

#endregion

#region Main Script

Clear-Host

# Get installer directory
$ScriptDir = Get-InstallerDirectory

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
Write-Host "         GoTweaks Installer                    " -ForegroundColor White
Write-Host "         Xbox Game Bar Widget                  " -ForegroundColor Gray
Write-Host "                                               " -ForegroundColor Cyan
Write-Host "         Version: $packageVersion                      " -ForegroundColor DarkGray
Write-Host "                                               " -ForegroundColor Cyan
Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  This installer will set up GoTweaks on your system." -ForegroundColor Gray
Write-Host "  GoTweaks provides TDP control, performance monitoring," -ForegroundColor Gray
Write-Host "  and more for handheld gaming devices." -ForegroundColor Gray
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

# Phase 1: Check Administrator
Write-Step -Step 1 -Total 6 -Message "Checking administrator privileges..."

if (-not (Test-Administrator)) {
    Write-Info "Requesting elevation to Administrator..."
    Request-Elevation
    exit 0
}
Write-Success "Running as Administrator"

# Phase 2: Locate package files
Write-Step -Step 2 -Total 6 -Message "Locating package files..."

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
# Note: We don't force-reinstall dependencies to avoid conflicts with other apps (Dolby, DTS, etc.)
$DependenciesDir = Join-Path $ScriptDir "Dependencies"
$depPackages = @()
if (Test-Path $DependenciesDir) {
    $allDepPackages = Get-DependencyPackages -DependenciesDir $DependenciesDir
    if ($allDepPackages.Count -gt 0) {
        $depPackages = Get-MissingDependencies -DependencyPackages $allDepPackages
        $skippedCount = $allDepPackages.Count - $depPackages.Count
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

# Phase 3: Check for blocking processes
Write-Step -Step 3 -Total 6 -Message "Checking for blocking processes..."

$runningBlockers = Get-RunningBlockers

if ($runningBlockers.Count -gt 0) {
    Write-Warn "The following apps need to be closed:"
    Write-Host ""
    foreach ($proc in $runningBlockers) {
        Write-Host "       - $proc" -ForegroundColor Yellow
    }
    Write-Host ""

    if (-not $Force) {
        Write-Host "       Please close Xbox Game Bar (Win+G then close it)" -ForegroundColor Cyan
        Write-Host "       and any GoTweaks windows, then press Enter." -ForegroundColor Cyan
        Write-Host ""
        Write-Host "       Or press 'F' to force-close these apps: " -ForegroundColor DarkGray -NoNewline
        $response = Read-Host

        # Check again after user says they closed
        $stillRunning = Get-RunningBlockers

        if ($stillRunning.Count -gt 0) {
            if ($response -eq 'F' -or $response -eq 'f') {
                Write-Info "Force-closing blocking processes..."
                $killedProcesses = Stop-BlockingProcesses
                if ($killedProcesses.Count -gt 0) {
                    Write-Success "Closed: $($killedProcesses -join ', ')"
                }
            }
            else {
                Write-Warn "Some apps are still running: $($stillRunning -join ', ')"
                Write-Info "Attempting to close them..."
                $killedProcesses = Stop-BlockingProcesses
                if ($killedProcesses.Count -gt 0) {
                    Write-Success "Closed: $($killedProcesses -join ', ')"
                }
            }
        }
        else {
            Write-Success "All blocking apps closed"
        }
    }
    else {
        Write-Info "Force mode: Closing blocking processes..."
        $killedProcesses = Stop-BlockingProcesses
        if ($killedProcesses.Count -gt 0) {
            Write-Success "Closed: $($killedProcesses -join ', ')"
        }
    }
}
else {
    Write-Success "No blocking processes"
}

# Phase 4: Handle existing package
if ($CleanInstall) {
    Write-Step -Step 4 -Total 6 -Message "Removing existing package (clean install)..."

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
    Write-Step -Step 4 -Total 6 -Message "Checking existing installation..."
    $existingPkg = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
    if ($existingPkg) {
        Write-Info "Will update existing installation (preserving settings)"
        Write-Info "Current version: $($existingPkg.Version)"
    }
    else {
        Write-Info "Fresh installation"
    }
}

# Phase 5: Install certificate
Write-Step -Step 5 -Total 6 -Message "Installing certificate..."

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

# Phase 6: Install package
Write-Step -Step 6 -Total 6 -Message "Installing package..."

$maxRetries = 2
$retryCount = 0
$installSuccess = $false

while ($retryCount -lt $maxRetries -and -not $installSuccess) {
    try {
        Stop-BlockingProcesses -Quiet | Out-Null

        # Install main package without forcing dependency reinstall
        # Windows will use already-installed shared dependencies (VCLibs etc.)
        # This avoids conflicts with other apps using those dependencies
        Write-Info "Installing GoTweaks package..."
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
            # Don't count this as a retry, try main package again
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
    Write-Host "  Then click the Widgets menu to add GoTweaks" -ForegroundColor Cyan
    Write-Host ""
}
else {
    Write-Warn "Installation may have succeeded. Press Win+G to verify."
}

Exit-WithPause -ExitCode 0

#endregion
