# =====================================================================================
#  ClawTweaks — Installer
#
#  How to install:
#    1. Extract the whole ZIP to a folder.
#    2. Open a terminal in that folder and run:
#         powershell -ExecutionPolicy Bypass -File .\Install.ps1
#       (Approve the UAC prompt — it is needed to trust the signing certificate.)
#
#  What it does: installs the signing certificate, the required runtime dependencies
#  and the ClawTweaks app package by calling the standard Add-AppDevPackage.ps1 that
#  ships next to this script.
#
#  No external tools are downloaded by this installer. Install the required tools
#  (ViGEmBus, HidHide, RTSS, PawnIO) afterwards from the in-app Setup tab.
#
#  Tip: close the ClawTweaks widget / Xbox Game Bar before updating an existing install.
# =====================================================================================

param(
    [switch]$Force = $false
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $here

$addDevPackage = Join-Path $here 'Add-AppDevPackage.ps1'
if (-not (Test-Path $addDevPackage)) {
    Write-Host "Add-AppDevPackage.ps1 was not found next to this script." -ForegroundColor Red
    Write-Host "Make sure you extracted the ENTIRE ZIP (all files must stay together)." -ForegroundColor Red
    exit 1
}

$argList = @()
if ($Force) { $argList += '-Force' }
# Skip the Visual Studio sideloading telemetry job — keeps the install clean/minimal.
$argList += '-SkipLoggingTelemetry'

Write-Host "Installing ClawTweaks..." -ForegroundColor Cyan
& $addDevPackage @argList
