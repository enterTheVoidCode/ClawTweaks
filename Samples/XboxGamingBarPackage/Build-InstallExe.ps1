<#
.SYNOPSIS
    Converts Install.ps1 to Install.exe using ps2exe for each generated package folder.

.DESCRIPTION
    This script is called after the main build to create Install.exe from Install.ps1.
    It searches for *_Test package folders and converts the Install.ps1 in each one.

.PARAMETER PackageDir
    The AppPackages directory containing the built packages.

.PARAMETER IconPath
    Optional path to an ICO file for the EXE icon.

.EXAMPLE
    .\Build-InstallExe.ps1 -PackageDir ".\AppPackages"
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$PackageDir,

    [string]$IconPath = $null
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Building Install.exe" -ForegroundColor White
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Ensure ps2exe is installed
if (-not (Get-Module -ListAvailable -Name ps2exe)) {
    Write-Host "Installing ps2exe module..." -ForegroundColor Yellow
    try {
        # Suppress all prompts for non-interactive mode
        $ProgressPreference = 'SilentlyContinue'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

        # Install NuGet provider if needed (required for Install-Module)
        $null = Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope CurrentUser -ErrorAction SilentlyContinue

        # Install module with all prompts suppressed
        Install-Module -Name ps2exe -Force -Scope CurrentUser -AllowClobber -SkipPublisherCheck -ErrorAction Stop
        Write-Host "ps2exe module installed successfully." -ForegroundColor Green
    }
    catch {
        Write-Host "ERROR: Failed to install ps2exe module: $_" -ForegroundColor Red
        Write-Host "Please install manually: Install-Module -Name ps2exe -Force -Scope CurrentUser" -ForegroundColor Yellow
        exit 1
    }
}

Import-Module ps2exe -ErrorAction Stop

# Find package folders
$packageFolders = Get-ChildItem -Path $PackageDir -Directory -Filter "*_Test" -ErrorAction SilentlyContinue

if (-not $packageFolders -or $packageFolders.Count -eq 0) {
    Write-Host "No *_Test package folders found in: $PackageDir" -ForegroundColor Yellow
    exit 0
}

Write-Host "Found $($packageFolders.Count) package folder(s)" -ForegroundColor Gray

$successCount = 0
$failCount = 0

$templateScript = Join-Path $PSScriptRoot "InstallTemplate\Install GoTweaks.ps1"
if (-not (Test-Path $templateScript)) {
    Write-Host "ERROR: Template script not found: $templateScript" -ForegroundColor Red
    exit 1
}

foreach ($folder in $packageFolders) {
    $scriptPath = Join-Path $folder.FullName "Install GoTweaks.ps1"
    $exePath = Join-Path $folder.FullName "Install.exe"

    # Copy our custom installer script to the package folder
    Write-Host "  Copying Install GoTweaks.ps1 to $($folder.Name)..." -ForegroundColor Gray
    Copy-Item -Path $templateScript -Destination $scriptPath -Force

    if (-not (Test-Path $scriptPath)) {
        Write-Host "  SKIP: $($folder.Name) - Failed to copy 'Install GoTweaks.ps1'" -ForegroundColor Yellow
        continue
    }

    Write-Host ""
    Write-Host "Converting: $($folder.Name)" -ForegroundColor Cyan

    # Build ps2exe parameters
    $ps2exeParams = @{
        InputFile = $scriptPath
        OutputFile = $exePath
        NoConsole = $false           # Keep console for user feedback
        RequireAdmin = $false        # Script handles elevation itself
        Title = "GoTweaks Installer"
        Description = "Installer for GoTweaks Xbox Game Bar Widget"
        Company = "GoTweaks"
        Product = "GoTweaks"
        Copyright = "Copyright (c) GoTweaks"
        Version = "1.0.0.0"
    }

    # Add icon if provided and exists
    if ($IconPath -and (Test-Path $IconPath)) {
        $ps2exeParams.IconFile = $IconPath
        Write-Host "  Using icon: $IconPath" -ForegroundColor Gray
    }

    try {
        # ps2exe writes to host, capture it
        $null = Invoke-ps2exe @ps2exeParams 2>&1

        if (Test-Path $exePath) {
            $exeSize = (Get-Item $exePath).Length / 1KB
            Write-Host "  SUCCESS: Created Install.exe ($([math]::Round($exeSize, 1)) KB)" -ForegroundColor Green
            $successCount++
        }
        else {
            Write-Host "  FAIL: Install.exe was not created" -ForegroundColor Red
            $failCount++
        }
    }
    catch {
        Write-Host "  FAIL: $_" -ForegroundColor Red
        $failCount++
    }
}

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Summary: $successCount succeeded, $failCount failed" -ForegroundColor White
Write-Host "=============================================" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}
exit 0
