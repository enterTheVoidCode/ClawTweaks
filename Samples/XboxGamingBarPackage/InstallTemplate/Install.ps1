#Requires -Version 5.1
<#
.SYNOPSIS
    Custom installer for Xbox Gaming Bar Widget that handles Debug-to-Release upgrades seamlessly.

.DESCRIPTION
    This script replaces the default Microsoft Add-AppDevPackage.ps1 wrapper with a robust
    installer that forcefully terminates blocking processes, cleanly uninstalls existing
    packages, and installs dependencies reliably.

.PARAMETER Force
    Suppress confirmation prompts for silent/unattended installation.

.PARAMETER SkipCertificate
    Skip certificate installation (use if certificate is already trusted).

.PARAMETER SkipUninstall
    Skip uninstalling the existing package (not recommended for Debug-to-Release upgrades).

.EXAMPLE
    .\Install.ps1
    Interactive installation with prompts.

.EXAMPLE
    .\Install.ps1 -Force
    Silent installation without prompts.

.NOTES
    Must be run as Administrator.
    Copy this script to the build output folder before distribution.
#>

param(
    [switch]$Force = $false,
    [switch]$SkipCertificate = $false,
    [switch]$SkipUninstall = $false
)

$ErrorActionPreference = "Stop"

# Global error handler to catch crashes
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

# Get script path at script scope (not inside functions)
$script:ScriptPath = $MyInvocation.MyCommand.Path
if (-not $script:ScriptPath) {
    $script:ScriptPath = $PSCommandPath
}

# Package identity
$PackageName = "PlayandBuildCustom.10365195AA1EC"

# Processes to kill before installation
$BlockingProcesses = @(
    "XboxGamingBarHelper",
    "GameBar",
    "GameBarFTServer",
    "GameBarPresenceWriter",
    "XboxGamingBarWidget"
)

#region Helper Functions

function Write-Step {
    param(
        [int]$Step,
        [int]$Total,
        [string]$Message
    )
    Write-Host "`n[$Step/$Total] " -ForegroundColor Cyan -NoNewline
    Write-Host $Message -ForegroundColor White
}

function Write-Success {
    param([string]$Message)
    Write-Host "      $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "      $Message" -ForegroundColor Gray
}

function Write-Warn {
    param([string]$Message)
    Write-Host "      WARNING: $Message" -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Message)
    Write-Host "      ERROR: $Message" -ForegroundColor Red
}

function Exit-WithPause {
    param([int]$ExitCode = 0)
    Write-Host ""
    Write-Host "Press any key to exit..." -ForegroundColor Gray
    try {
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    catch {
        # ReadKey might fail in non-interactive mode, use Read-Host as fallback
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
    # Use script-scope path captured at startup
    $elevateScriptPath = $script:ScriptPath

    if (-not $elevateScriptPath) {
        Write-Err "Cannot determine script path for elevation."
        Write-Host "Please run this script as Administrator manually." -ForegroundColor Yellow
        pause
        exit 1
    }

    # Build argument string (not array) for proper handling of paths with spaces
    $argString = "-ExecutionPolicy Bypass -File `"$elevateScriptPath`""
    if ($Force) { $argString += " -Force" }
    if ($SkipCertificate) { $argString += " -SkipCertificate" }
    if ($SkipUninstall) { $argString += " -SkipUninstall" }

    Write-Info "Elevating: powershell.exe $argString"

    try {
        $proc = Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $argString -PassThru -Wait
        # Don't pause here - the elevated window handles its own pause
        exit $proc.ExitCode
    }
    catch {
        Write-Err "Failed to elevate to Administrator: $_"
        Write-Host "Please right-click the script and select 'Run as Administrator'." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Press any key to exit..." -ForegroundColor Gray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 1
    }
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
        # Wait for processes to terminate
        Start-Sleep -Milliseconds 1000

        # Verify termination
        $timeout = 10
        $elapsed = 0
        while ($elapsed -lt $timeout) {
            $stillRunning = $false
            foreach ($procName in $killed) {
                if (Get-Process -Name $procName -ErrorAction SilentlyContinue) {
                    $stillRunning = $true
                    break
                }
            }
            if (-not $stillRunning) { break }
            Start-Sleep -Seconds 1
            $elapsed++
        }
    }

    return $killed
}

function Get-ProcessorArchitecture {
    # Try to detect architecture accurately
    $arch = $env:PROCESSOR_ARCHITECTURE

    # Handle x86 PowerShell on 64-bit systems
    if ($arch -eq "x86") {
        if ($null -ne ${env:ProgramFiles(Arm)}) {
            return "arm64"
        }
        elseif ($null -ne ${env:ProgramFiles(x86)}) {
            return "amd64"
        }
    }

    return $arch.ToLower()
}

function Get-DependencyPackages {
    param([string]$DependenciesDir)

    $packages = @()
    $arch = Get-ProcessorArchitecture

    # Architecture folder mapping - order matters (prefer native arch first)
    $archFolders = @{
        "amd64" = @("x64", "x86")
        "x86"   = @("x86")
        "arm64" = @("arm64", "x64", "x86")  # ARM64 supports x64 emulation on Win11+
        "arm"   = @("arm", "x86")
    }

    $folders = $archFolders[$arch]
    if (-not $folders) { $folders = @("x64", "x86") }

    foreach ($folder in $folders) {
        $path = Join-Path $DependenciesDir $folder
        if (Test-Path $path) {
            $packages += Get-ChildItem -Path $path -Filter "*.appx" -ErrorAction SilentlyContinue
            $packages += Get-ChildItem -Path $path -Filter "*.msix" -ErrorAction SilentlyContinue
        }
    }

    # Note: Skip Win32 folder - it contains duplicates of architecture-specific packages
    # The x64/x86 folders already have the correct packages

    # Deduplicate by package base name (e.g., "Microsoft.UI.Xaml.2.8")
    # This handles cases where the same package exists in multiple arch folders
    $seen = @{}
    $uniquePackages = @()

    foreach ($pkg in $packages) {
        # Extract base package name (remove version and arch suffixes)
        # e.g., "Microsoft.UI.Xaml.2.8.appx" -> "Microsoft.UI.Xaml.2.8"
        $baseName = $pkg.BaseName -replace '\.x64$|\.x86$|\.arm64$|\.arm$', ''

        if (-not $seen.ContainsKey($baseName)) {
            $seen[$baseName] = $true
            $uniquePackages += $pkg
        }
    }

    return $uniquePackages
}

#endregion

#region Main Script

# Banner
Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Xbox Gaming Bar Widget Installer" -ForegroundColor White
Write-Host "=============================================" -ForegroundColor Cyan

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ScriptDir) {
    $ScriptDir = $PSScriptRoot
}
if (-not $ScriptDir) {
    $ScriptDir = (Get-Location).Path
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

# Find main package (.msixbundle or .appxbundle)
$MainPackage = Get-ChildItem -Path $ScriptDir -Filter "*.msixbundle" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $MainPackage) {
    $MainPackage = Get-ChildItem -Path $ScriptDir -Filter "*.appxbundle" -ErrorAction SilentlyContinue | Select-Object -First 1
}
if (-not $MainPackage) {
    $MainPackage = Get-ChildItem -Path $ScriptDir -Filter "*.msix" -ErrorAction SilentlyContinue | Select-Object -First 1
}
if (-not $MainPackage) {
    $MainPackage = Get-ChildItem -Path $ScriptDir -Filter "*.appx" -ErrorAction SilentlyContinue | Select-Object -First 1
}

if (-not $MainPackage) {
    Write-Err "No package file (.msixbundle, .appxbundle, .msix, .appx) found in $ScriptDir"
    Exit-WithPause -ExitCode 1
}
Write-Success "Found: $($MainPackage.Name)"

# Find certificate (.cer)
$Certificate = Get-ChildItem -Path $ScriptDir -Filter "*.cer" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($Certificate) {
    Write-Success "Certificate: $($Certificate.Name)"
}
else {
    Write-Info "No certificate file found (may already be trusted)"
}

# Find dependencies folder
$DependenciesDir = Join-Path $ScriptDir "Dependencies"
if (Test-Path $DependenciesDir) {
    $depPackages = Get-DependencyPackages -DependenciesDir $DependenciesDir
    Write-Success "Dependencies: $($depPackages.Count) packages found"
}
else {
    $depPackages = @()
    Write-Info "No Dependencies folder found"
}

# Phase 3: Stop blocking processes
Write-Step -Step 3 -Total 6 -Message "Stopping blocking processes..."

$killedProcesses = Stop-BlockingProcesses
if ($killedProcesses.Count -gt 0) {
    Write-Success "Stopped: $($killedProcesses -join ', ')"
}
else {
    Write-Info "No blocking processes found"
}

# Phase 4: Uninstall existing package
if (-not $SkipUninstall) {
    Write-Step -Step 4 -Total 6 -Message "Removing existing package..."

    $existingPkg = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
    if ($existingPkg) {
        Write-Info "Found: $($existingPkg.PackageFullName)"
        try {
            # Kill processes again just in case
            Stop-BlockingProcesses -Quiet | Out-Null

            Remove-AppxPackage -Package $existingPkg.PackageFullName -ErrorAction Stop
            Write-Success "Successfully removed existing package"

            # Wait for removal to complete
            Start-Sleep -Seconds 2
        }
        catch {
            Write-Warn "Could not remove existing package: $_"
            Write-Info "Continuing with installation (will attempt update)..."
        }
    }
    else {
        Write-Info "No existing package found"
    }
}
else {
    Write-Step -Step 4 -Total 6 -Message "Skipping package removal (--SkipUninstall)"
}

# Phase 5: Install certificate
Write-Step -Step 5 -Total 6 -Message "Installing certificate..."

if ($SkipCertificate) {
    Write-Info "Skipping certificate installation (--SkipCertificate)"
}
elseif (-not $Certificate) {
    Write-Info "No certificate file to install"
}
else {
    # Check if package signature is already trusted
    $signature = Get-AuthenticodeSignature -FilePath $MainPackage.FullName -ErrorAction SilentlyContinue

    if ($signature -and $signature.Status -eq "Valid") {
        Write-Success "Package signature already trusted"
    }
    else {
        # Validate certificate format
        $certResult = certutil.exe -verify $Certificate.FullName 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Err "Invalid certificate file: $($Certificate.Name)"
            Exit-WithPause -ExitCode 1
        }

        # Check if certificate already in store
        $cert = Get-PfxCertificate -FilePath $Certificate.FullName
        $existingCert = Get-ChildItem -Path "Cert:\LocalMachine\TrustedPeople" -ErrorAction SilentlyContinue |
                        Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

        if ($existingCert) {
            Write-Success "Certificate already in TrustedPeople store"
        }
        else {
            # Prompt for confirmation if not in Force mode
            if (-not $Force) {
                Write-Host ""
                Write-Host "      The package is signed with a developer certificate that needs to be installed." -ForegroundColor Yellow
                Write-Host "      Certificate: $($cert.Subject)" -ForegroundColor Yellow
                Write-Host ""
                $response = Read-Host "      Install certificate to TrustedPeople store? (Y/N)"
                if ($response -ne 'Y' -and $response -ne 'y') {
                    Write-Err "Certificate installation cancelled. Cannot proceed."
                    Exit-WithPause -ExitCode 1
                }
            }

            # Install certificate
            $certResult = certutil.exe -addstore TrustedPeople $Certificate.FullName 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Certificate installed to TrustedPeople store"
            }
            else {
                Write-Err "Failed to install certificate: $certResult"
                Exit-WithPause -ExitCode 1
            }
        }
    }
}

# Phase 6: Install package
Write-Step -Step 6 -Total 6 -Message "Installing main package..."

$maxRetries = 2
$retryCount = 0
$installSuccess = $false

while ($retryCount -lt $maxRetries -and -not $installSuccess) {
    try {
        # Kill blocking processes before each attempt
        Stop-BlockingProcesses -Quiet | Out-Null

        Write-Info "Installing: $($MainPackage.Name)"

        if ($depPackages.Count -gt 0) {
            Write-Info "With $($depPackages.Count) dependencies..."
            Add-AppxPackage -Path $MainPackage.FullName `
                -DependencyPath $depPackages.FullName `
                -ForceApplicationShutdown `
                -ForceUpdateFromAnyVersion `
                -ErrorAction Stop
        }
        else {
            Add-AppxPackage -Path $MainPackage.FullName `
                -ForceApplicationShutdown `
                -ForceUpdateFromAnyVersion `
                -ErrorAction Stop
        }

        $installSuccess = $true
        Write-Success "Package installed successfully!"
    }
    catch {
        $retryCount++
        $errorMsg = $_.Exception.Message

        if ($retryCount -lt $maxRetries) {
            Write-Warn "Installation attempt $retryCount failed: $errorMsg"
            Write-Info "Retrying after stopping processes..."
            Start-Sleep -Seconds 2
        }
        else {
            Write-Err "Installation failed after $maxRetries attempts"
            Write-Err $errorMsg

            # Provide troubleshooting hints
            Write-Host ""
            Write-Host "      Troubleshooting:" -ForegroundColor Yellow
            Write-Host "      - Close Xbox Game Bar (Win+G, then close)" -ForegroundColor Yellow
            Write-Host "      - Restart your computer and try again" -ForegroundColor Yellow
            Write-Host "      - Run: Get-AppxPackage *$PackageName* | Remove-AppxPackage" -ForegroundColor Yellow

            Exit-WithPause -ExitCode 1
        }
    }
}

# Verify installation
$installedPkg = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
if ($installedPkg) {
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Green
    Write-Host "  Installation Complete!" -ForegroundColor White
    Write-Host "=============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Package: $($installedPkg.Name)" -ForegroundColor Gray
    Write-Host "  Version: $($installedPkg.Version)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Press Win+G to open Xbox Game Bar and access the widget." -ForegroundColor Cyan
    Write-Host ""
}
else {
    Write-Warn "Package installation may have succeeded but verification failed."
    Write-Info "Try pressing Win+G to check if the widget is available."
}

# Always pause so user can see result
Exit-WithPause -ExitCode 0

#endregion
