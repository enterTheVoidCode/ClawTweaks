# =====================================================================================
#  ClawTweaks — Installer
#
#  How to install:
#    1. Extract the WHOLE ZIP to a folder (keep all files together).
#    2. Open a terminal in that folder and run:
#         powershell -ExecutionPolicy Bypass -File .\Install.ps1
#       Approve the UAC prompt (needed to trust the signing certificate and install).
#
#  What it does: trusts the bundled signing certificate, then installs the ClawTweaks
#  app package together with its runtime dependencies. No external tools are downloaded
#  — install the required tools (ViGEmBus, HidHide, RTSS, PawnIO) afterwards from the
#  in-app Setup tab.
#
#  Tip: close the ClawTweaks widget / Xbox Game Bar before updating an existing install.
# =====================================================================================

param([switch]$Elevated)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Definition

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Appx + PKI cmdlets are most reliable under Windows PowerShell 5.1 (Add-AppxPackage is not
# natively available in PowerShell 7). Ensure we run elevated AND under Windows PowerShell 5.1
# by relaunching there once.
$winPs = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not $Elevated -and ((-not (Test-Admin)) -or ($PSVersionTable.PSEdition -eq 'Core'))) {
    Start-Process $winPs -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "$PSCommandPath", '-Elevated'
    )
    return
}

function Finish($code) {
    if ($Elevated) { Write-Host ""; Write-Host "Press Enter to close..." -ForegroundColor DarkGray; [void](Read-Host) }
    exit $code
}

try {
    Set-Location $here
    Write-Host "Installing ClawTweaks..." -ForegroundColor Cyan

    if (-not (Test-Admin)) {
        Write-Host "Administrator rights are required (UAC was declined?). Re-run as administrator." -ForegroundColor Red
        Finish 1
    }

    # 1. Trust the bundled signing certificate (self-signed) so the package can be sideloaded.
    $cer = Get-ChildItem -Path $here -Filter *.cer -File | Select-Object -First 1
    if ($null -eq $cer) {
        Write-Host "No .cer certificate found next to this script. Did you extract the whole ZIP?" -ForegroundColor Red
        Finish 1
    }
    # TrustedPeople is the designated store for sideloaded app signing certs (same as
    # Add-AppDevPackage). We deliberately do NOT add it to the Root CA store — adding a root
    # CA is an antivirus/EDR red flag and is unnecessary for sideload.
    Import-Certificate -FilePath $cer.FullName -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    Write-Host "Trusted signing certificate: $($cer.Name)" -ForegroundColor Green

    # 2. Locate the app package.
    $pkg = Get-ChildItem -Path $here -Filter *.msix -File | Select-Object -First 1
    if ($null -eq $pkg) { $pkg = Get-ChildItem -Path $here -Filter *.msixbundle -File | Select-Object -First 1 }
    if ($null -eq $pkg) { $pkg = Get-ChildItem -Path $here -Filter *.appxbundle -File | Select-Object -First 1 }
    if ($null -eq $pkg) { $pkg = Get-ChildItem -Path $here -Filter *.appx -File | Select-Object -First 1 }
    if ($null -eq $pkg) {
        Write-Host "No app package (.msix/.appx) found next to this script." -ForegroundColor Red
        Finish 1
    }

    # 3. Collect runtime dependencies (x64 + any neutral packages at the Dependencies root).
    $deps = New-Object System.Collections.Generic.List[string]
    $depRoot = Join-Path $here 'Dependencies'
    foreach ($d in @((Join-Path $depRoot 'x64'), $depRoot)) {
        if (Test-Path $d) {
            Get-ChildItem -Path $d -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Extension -eq '.appx' -or $_.Extension -eq '.msix' } |
                ForEach-Object { $deps.Add($_.FullName) }
        }
    }

    # 4. Install the package (+ dependencies), shutting down a running instance if needed.
    Write-Host "Installing package: $($pkg.Name)" -ForegroundColor Cyan
    if ($deps.Count -gt 0) {
        Add-AppxPackage -Path $pkg.FullName -DependencyPath $deps.ToArray() -ForceApplicationShutdown
    } else {
        Add-AppxPackage -Path $pkg.FullName -ForceApplicationShutdown
    }

    # 5. Refresh the elevated helper. It runs from a deployed copy under the package LocalCache
    #    (launched by the scheduled task "ClawTweaks\ClawTweaksHelper"), NOT from the MSIX itself.
    #    An in-place update leaves the OLD helper PROCESS running, so new pipe commands the widget
    #    sends (e.g. the in-app tool setup) silently do nothing until the next reboot/logon. End the
    #    task, kill the running helper and delete the deployed copy — the widget redeploys the NEW
    #    version automatically on next launch (no reboot needed).
    try {
        $appPkg = Get-AppxPackage | Where-Object { $_.Name -like '*ClawTweaks*' } | Select-Object -First 1
        & schtasks.exe /End /TN "ClawTweaks\ClawTweaksHelper" 2>$null | Out-Null
        Get-Process -Name "XboxGamingBarHelper" -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 600
        if ($appPkg) {
            $helperDir = Join-Path $env:LOCALAPPDATA "Packages\$($appPkg.PackageFamilyName)\LocalCache\ClawTweaks\Helper"
            if (Test-Path $helperDir) { Remove-Item -Path $helperDir -Recurse -Force -ErrorAction SilentlyContinue }
        }
        Write-Host "Refreshed helper (new version deploys on next launch)." -ForegroundColor Green
    }
    catch {
        Write-Host "Note: could not refresh the running helper; reboot once if new features don't appear." -ForegroundColor DarkYellow
    }

    Write-Host ""
    Write-Host "ClawTweaks installed successfully." -ForegroundColor Green
    Write-Host "Open it via the Xbox Game Bar (Win+G), then complete setup in the Setup tab." -ForegroundColor Gray
    Finish 0
}
catch {
    Write-Host ""
    Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
    Finish 1
}
