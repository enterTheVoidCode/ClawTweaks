<#
.SYNOPSIS
    Builds the standalone, controller-navigable ClawTweaks setup.exe and assembles a setup ZIP.

.DESCRIPTION
    This is SEPARATE from Build-Package.ps1 (which is never touched). It:
      1. Publishes ClawTweaksSetup as a self-contained single-file win-x64 exe (no runtime needed).
      2. Copies the freshly-built app package + signing .cer + Dependencies next to the exe, plus
         the helper's Setup-Tools.ps1, so the Install phase can find and install everything.
      3. Zips the result into Build\ClawTweaksSetup_<version>.zip.

    Run Build-Package.ps1 FIRST (it produces the .msix + .cer under Build\Installer). Then run this.

.PARAMETER InstallerDir
    Folder holding the built package files (.msix + .cer + Dependencies). Defaults to Build\Installer.
#>
param(
    [string]$InstallerDir = $null,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot          # repo root (ClawTweaksSetup is one level down)
$proj = Join-Path $PSScriptRoot 'ClawTweaksSetup.csproj'
if (-not $InstallerDir) { $InstallerDir = Join-Path $root 'Build\Installer' }

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Building ClawTweaks setup.exe" -ForegroundColor White
Write-Host "=============================================" -ForegroundColor Cyan

# 1. Publish self-contained single-file exe.
$publishDir = Join-Path $PSScriptRoot 'bin\publish'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Write-Host ">> Publishing self-contained single-file exe..." -ForegroundColor Gray
& dotnet publish $proj -c $Configuration -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
# AssemblyName carries the version + "Setup" suffix (see ClawTweaksSetup.csproj), so the exe name
# isn't a fixed literal any more — pick up whatever single exe actually landed in $publishDir.
$exeItem = Get-ChildItem -Path $publishDir -Filter 'CTW_Center_*_Setup.exe' -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $exeItem) { throw "No CTW_Center_*_Setup.exe found after publish." }
$exe = $exeItem.FullName
Write-Host "   [OK] $exe" -ForegroundColor Green

# 2. Locate the built package + cert.
if (-not (Test-Path $InstallerDir)) {
    throw "Installer dir not found: $InstallerDir. Run Build-Package.ps1 first."
}
$msix = Get-ChildItem -Path $InstallerDir -Filter *.msix -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $msix) { $msix = Get-ChildItem -Path $InstallerDir -Filter *.msixbundle -File -ErrorAction SilentlyContinue | Select-Object -First 1 }
$cer  = Get-ChildItem -Path $InstallerDir -Filter *.cer  -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $msix) { throw "No .msix found in $InstallerDir." }
if (-not $cer)  { throw "No .cer found in $InstallerDir." }

# Derive a version label from the package file name (…_0.1.7.57_x64.msix -> 0.1.7.57).
$ver = 'dev'
if ($msix.BaseName -match '(\d+\.\d+\.\d+\.\d+)') { $ver = $Matches[1] }

# 3. Assemble the setup folder: setup.exe + package + cer + Dependencies + Setup-Tools.ps1.
$outDir = Join-Path $root ("Build\ClawTweaksSetup_" + $ver)
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null

Copy-Item $exe -Destination $outDir
Copy-Item $msix.FullName -Destination $outDir
Copy-Item $cer.FullName  -Destination $outDir

$depSrc = Join-Path $InstallerDir 'Dependencies'
if (Test-Path $depSrc) { Copy-Item $depSrc -Destination (Join-Path $outDir 'Dependencies') -Recurse }

$setupTools = Join-Path $root 'XboxGamingBarHelper\Setup\Setup-Tools.ps1'
if (Test-Path $setupTools) { Copy-Item $setupTools -Destination $outDir }

Write-Host ">> Assembled setup folder: $outDir" -ForegroundColor Gray
Write-Host "   [OK] setup.exe + $($msix.Name) + $($cer.Name)" -ForegroundColor Green

# 4. Zip it.
$zip = Join-Path $root ("Build\ClawTweaksSetup_" + $ver + ".zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zip
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Setup ready!  ($ver)" -ForegroundColor White
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Folder: $outDir"
Write-Host "  ZIP:    $zip  ($zipMb MB)"
Write-Host ""
Write-Host "  Copy the folder (or extracted ZIP) to the Claw and run $($exeItem.Name)." -ForegroundColor Gray
