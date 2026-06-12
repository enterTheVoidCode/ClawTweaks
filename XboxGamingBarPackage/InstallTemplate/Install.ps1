# =====================================================================================
#  ClawTweaks - Installer
#
#  How to install:
#    1. Extract the WHOLE ZIP to a folder (keep all files together).
#    2. Open a terminal in that folder and run:
#         powershell -ExecutionPolicy Bypass -File .\Install.ps1
#       Approve the UAC prompt (needed to trust the signing certificate and install).
#
#  What it does: trusts the bundled signing certificate, then installs the ClawTweaks
#  app package together with its runtime dependencies. No external tools are downloaded
#  - install the required tools (ViGEmBus, HidHide, RTSS, PawnIO) afterwards from the
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
    # Auto-close after a short delay instead of waiting for a key - a lingering console
    # window is awkward on a handheld. (No Read-Host: nothing to confirm on a touch device.)
    if ($Elevated) { Write-Host ""; Write-Host "Closing in 3 seconds..." -ForegroundColor DarkGray; Start-Sleep -Seconds 3 }
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
    # Add-AppDevPackage). We deliberately do NOT add it to the Root CA store - adding a root
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

    # 3.5 Stop any OLD helper BEFORE installing. This is the version-independent safety net:
    #     Install.ps1 ships fresh with every release, so it protects users updating from a build
    #     that LACKS the in-app multi-instance guard (older versions). The deployed helper runs
    #     outside the MSIX (scheduled task), survives the package swap, and holds the hardware
    #     (LHM kernel driver, KX MCHBAR MMIO, MSI WMI/EC). If it stays alive while the new package
    #     registers and Game Bar relaunches, the old and new builds can touch ring0 at the same
    #     time -> hard reset (Kernel-Power 41). End its task (current + legacy name), kill EVERY
    #     helper process by name (covers multiple/stale instances), and let the kernel release the
    #     hardware handles before we install + relaunch.
    Write-Host "Stopping any running ClawTweaks helper before install..." -ForegroundColor DarkGray
    try {
        # End the scheduled task first so it cannot relaunch the helper mid-install.
        foreach ($tn in @("ClawTweaks\ClawTweaksHelper", "GoTweaks\GoTweaksHelper")) {
            & schtasks.exe /End /TN $tn 2>$null | Out-Null
        }

        # Kill the helper(s) + widget and WAIT until the helper is verifiably gone before we install.
        # We do NOT proceed on a fixed delay - we poll (re-killing each round) until no helper process
        # remains, so the new package never registers alongside a live old helper. ~15s ceiling.
        $deadline = (Get-Date).AddSeconds(15)
        do {
            Get-Process -Name "XboxGamingBarHelper" -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue
            Get-Process -Name "XboxGamingBar" -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 300
            $still = @(Get-Process -Name "XboxGamingBarHelper" -ErrorAction SilentlyContinue)
        } while ($still.Count -gt 0 -and (Get-Date) -lt $deadline)

        if ($still.Count -gt 0) {
            Write-Host "Warning: a helper process is still running after 15s - continuing anyway." -ForegroundColor DarkYellow
        } else {
            Start-Sleep -Milliseconds 500   # let kernel release hardware handles / MMIO maps
            Write-Host "Helper stopped - proceeding with install." -ForegroundColor DarkGray
        }
    }
    catch { }

    # 4. Install the package (+ dependencies), shutting down a running instance if needed.
    #    -ForceUpdateFromAnyVersion allows installing over a HIGHER manifest version too. This is
    #    needed for the one-time switch of the internal-build numbering to the release-line scheme
    #    (0.1.285.x -> 0.1.4.x is a manifest downgrade) and for any deliberate rollback via ZIP.
    Write-Host "Installing package: $($pkg.Name)" -ForegroundColor Cyan
    if ($deps.Count -gt 0) {
        Add-AppxPackage -Path $pkg.FullName -DependencyPath $deps.ToArray() -ForceApplicationShutdown -ForceUpdateFromAnyVersion
    } else {
        Add-AppxPackage -Path $pkg.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion
    }

    # 5. INSTALL ONLY here, then let the HELPER redeploy itself via its own (signed, UAC-consented)
    #    setup on next Game Bar open. We deliberately do NOT copy the helper out + create/start the
    #    scheduled task FROM THIS SCRIPT: doing that script-driven persistence (PowerShell copies an
    #    .exe to LocalCache + touches a scheduled task) tripped Defender's behavioral cloud detection
    #    "Behavior:Win32/Persistence.A!ml" on the self-signed helper and quarantined it. The helper's
    #    own --setup (compiled, signed, launched with a UAC prompt) doing the same deploy+task is far
    #    less ML-suspicious and was AV-clean. So: kill the old widget+helper, drop the deployed copy,
    #    and the widget triggers the helper setup (one UAC) on next open. The real long-term fix for the
    #    behavioral flags is a reputable code-signing cert (Azure Trusted Signing).
    try {
        $appPkg = Get-AppxPackage | Where-Object { $_.Name -like '*ClawTweaks*' } | Select-Object -First 1

        # Kill the widget so the Game Bar reloads the freshly-installed version on next open.
        Get-Process -Name "XboxGamingBar" -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue

        # End the task + kill the helper, then drop the deployed copy so the widget redeploys the NEW
        # helper (via its own --setup + UAC) on next launch.
        & schtasks.exe /End /TN "ClawTweaks\ClawTweaksHelper" 2>$null | Out-Null
        Get-Process -Name "XboxGamingBarHelper" -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 600

        if ($appPkg) {
            $helperDir = Join-Path $env:LOCALAPPDATA "Packages\$($appPkg.PackageFamilyName)\LocalCache\ClawTweaks\Helper"
            if (Test-Path $helperDir) { Remove-Item -Path $helperDir -Recurse -Force -ErrorAction SilentlyContinue }
        }
        Write-Host "Stopped old widget + helper. The new helper deploys on next Game Bar open (one UAC)." -ForegroundColor Green
    }
    catch {
        Write-Host "Note: could not stop old processes; reboot once if new features do not appear." -ForegroundColor DarkYellow
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
