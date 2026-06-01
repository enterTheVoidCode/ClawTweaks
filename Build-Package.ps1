#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the ClawTweaks release MSIX and assembles a ready-to-deploy installer folder.

.DESCRIPTION
    1. Creates / reuses a self-signed code-signing certificate (CurrentUser\My).
    2. Writes the thumbprint into the WAP project so MSBuild signs the package.
    3. Exports the public certificate (.cer) so the installer can trust it.
    4. Runs MSBuild on the WAP project (Release|x64).
    5. Copies Install.ps1 into Build\ and creates a ZIP.

.EXAMPLE
    .\Build-Package.ps1
#>

$ErrorActionPreference = "Stop"

$LogFile = Join-Path $PSScriptRoot "Build\build_log.txt"
New-Item -ItemType Directory -Force -Path (Split-Path $LogFile) | Out-Null
Start-Transcript -Path $LogFile -Force | Out-Null

$ScriptDir   = $PSScriptRoot
$WapProj     = Join-Path $ScriptDir "XboxGamingBarPackage\XboxGamingBarPackage.wapproj"
$InstallPs1  = Join-Path $ScriptDir "XboxGamingBarPackage\InstallTemplate\Install.ps1"
$InstallBat  = Join-Path $ScriptDir "XboxGamingBarPackage\InstallTemplate\Install.bat"
$AppPackages = Join-Path $ScriptDir "XboxGamingBarPackage\AppPackages"
$CertPfxPath = Join-Path $ScriptDir "XboxGamingBarPackage\ClawTweaks.pfx"
$CertSubject = "CN=ClawTweaks Dev, O=MSIClaw"
$BuildDir    = Join-Path $ScriptDir "Build"

function Write-Step { param([string]$Msg) Write-Host "`n>> $Msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Msg) Write-Host "   [OK] $Msg" -ForegroundColor Green }
function Write-Info { param([string]$Msg) Write-Host "        $Msg" -ForegroundColor DarkGray }
function Write-Err  {
    param([string]$Msg)
    Write-Host "   [X] $Msg" -ForegroundColor Red
    try { Stop-Transcript | Out-Null } catch { }
    exit 1
}

function Find-MSBuild {
    $vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vsWhere) {
        $vsPath = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        if ($vsPath) {
            $candidate = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $candidate) { return $candidate }
        }
    }
    $roots = @(
        "C:\Program Files\Microsoft Visual Studio\18\Community",
        "C:\Program Files\Microsoft Visual Studio\18\Professional",
        "C:\Program Files\Microsoft Visual Studio\18\Enterprise",
        "C:\Program Files\Microsoft Visual Studio\2022\Community",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise"
    )
    foreach ($root in $roots) {
        $candidate = Join-Path $root "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $candidate) { return $candidate }
    }
    $fromPath = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($fromPath) { return $fromPath.Source }
    return $null
}

try { Clear-Host } catch { }
Write-Host ""
Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host "    ClawTweaks - Build and Package Script     " -ForegroundColor White
Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host ""

Write-Step "Locating MSBuild..."
$msbuild = Find-MSBuild
if (-not $msbuild) { Write-Err "MSBuild not found. Install Visual Studio 2022." }
Write-Ok $msbuild

if (-not (Test-Path $WapProj))    { Write-Err "WAP project not found: $WapProj" }
if (-not (Test-Path $InstallPs1)) { Write-Err "Install.ps1 not found: $InstallPs1" }
if (-not (Test-Path $InstallBat)) { Write-Err "Install.bat not found: $InstallBat" }

Write-Step "Preparing code-signing certificate..."
$pfxValid = $false
if (Test-Path $CertPfxPath) {
    try {
        $x509 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertPfxPath)
        if ($x509 -and $x509.Thumbprint) {
            $thumbprint  = $x509.Thumbprint
            $signingCert = $x509
            $pfxValid    = $true
            Write-Ok "Reusing existing PFX (thumbprint $($thumbprint.Substring(0,16))...)"
        } else {
            Write-Info "Existing PFX loaded but has no thumbprint - will regenerate."
        }
    } catch {
        Write-Info "Failed to load existing PFX ($_) - will regenerate."
    }
}
if (-not $pfxValid) {
    try { Import-Module PKI -ErrorAction Stop } catch { }
    $signingCert = New-SelfSignedCertificate `
        -Type Custom -Subject $CertSubject -KeyUsage DigitalSignature `
        -FriendlyName "ClawTweaks Dev Signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}") `
        -NotAfter (Get-Date).AddYears(5)
    $thumbprint = $signingCert.Thumbprint
    Write-Ok "Created cert $($thumbprint.Substring(0,16))..."
    $emptyPass = New-Object System.Security.SecureString
    $null = Export-PfxCertificate -Cert $signingCert -FilePath $CertPfxPath -Password $emptyPass
    Write-Ok "PFX exported: $CertPfxPath"
}
Write-Info "Thumbprint: $thumbprint"

Write-Step "Patching WAP project with certificate thumbprint..."
[xml]$wap = Get-Content $WapProj -Encoding UTF8
$ns = New-Object System.Xml.XmlNamespaceManager($wap.NameTable)
$ns.AddNamespace("ms", "http://schemas.microsoft.com/developer/msbuild/2003")
foreach ($pg in $wap.SelectNodes("//ms:PropertyGroup", $ns)) {
    $thumbNode = $pg.SelectSingleNode("ms:PackageCertificateThumbprint", $ns)
    if ($thumbNode) {
        $thumbNode.InnerText = $thumbprint
        $pfxNode = $pg.SelectSingleNode("ms:PackageCertificateKeyFile", $ns)
        if ($pfxNode) { $pfxNode.InnerText = $CertPfxPath }
        else {
            $newNode = $wap.CreateElement("PackageCertificateKeyFile","http://schemas.microsoft.com/developer/msbuild/2003")
            $newNode.InnerText = $CertPfxPath
            $pg.AppendChild($newNode) | Out-Null
        }
        $genNode = $pg.SelectSingleNode("ms:GenerateTemporaryStoreCertificate", $ns)
        if ($genNode) { $genNode.InnerText = "False" }
    }
}
$wap.Save($WapProj)
Write-Ok "Thumbprint written to wapproj"

Write-Step "Auto-incrementing package version..."
$ManifestPath = Join-Path $ScriptDir "XboxGamingBarPackage\Package.appxmanifest"
[xml]$manifest = Get-Content $ManifestPath -Encoding UTF8
$identityNode  = $manifest.Package.Identity
$currentVer    = $identityNode.Version        # saved for rollback on build failure
$parts         = $currentVer.Split('.')
$parts[3]      = ([int]$parts[3] + 1).ToString()
$newVer        = $parts -join '.'
$identityNode.Version = $newVer
$manifest.Save($ManifestPath)
Write-Ok "Version bumped: $currentVer -> $newVer"

Write-Step "Restoring NuGet packages (packages.config)..."
$helperCsproj   = Join-Path $ScriptDir "XboxGamingBarHelper\XboxGamingBarHelper.csproj"
$pkgConfig      = Join-Path $ScriptDir "XboxGamingBarHelper\packages.config"
$packagesDir    = Join-Path $ScriptDir "packages"
# Locate nuget.exe - download it if not available in PATH
$nugetExe = Get-Command nuget.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $nugetExe) {
    $nugetExe = Join-Path $env:TEMP "nuget.exe"
    if (-not (Test-Path $nugetExe)) {
        Write-Info "Downloading nuget.exe..."
        Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nugetExe -UseBasicParsing
    }
}
Write-Info "Using nuget.exe: $nugetExe"
& $nugetExe restore $pkgConfig -PackagesDirectory $packagesDir -Source "https://api.nuget.org/v3/index.json" -NonInteractive
if ($LASTEXITCODE -ne 0) { Write-Err "nuget restore failed with exit code $LASTEXITCODE" }
Write-Ok "Restore complete"

Write-Step "Building Release|x64..."
Write-Info "This may take a minute..."
$buildArgs = @(
    $WapProj,
    "/restore",
    "/p:Configuration=Release",
    "/p:Platform=x64",
    "/p:AppxPackageDir=$AppPackages\",
    "/p:UapAppxPackageBuildMode=SideloadOnly",
    "/p:AppxBundle=Never",
    "/p:AppxPackageSigningEnabled=True",
    "/p:PackageCertificateThumbprint=$thumbprint",
    "/p:PackageCertificateKeyFile=$CertPfxPath",
    "/p:RuntimeIdentifier=win-x64",
    "/verbosity:minimal",
    "/nologo"
)
& $msbuild @buildArgs
if ($LASTEXITCODE -ne 0) {
    # Roll back the version increment so failed builds don't cause gaps
    # e.g.  0.1.x.130 → (fail) → manifest stays at 0.1.x.130 → next build → 0.1.x.131
    # without rollback: 0.1.x.130 → (fail) → manifest at 0.1.x.131 → next → 0.1.x.132 (gap!)
    try {
        [xml]$mfRollback = Get-Content $ManifestPath -Encoding UTF8
        $mfRollback.Package.Identity.Version = $currentVer
        $mfRollback.Save($ManifestPath)
        Write-Host "   [!] Version rolled back to $currentVer (build failed)" -ForegroundColor Yellow
    } catch {
        Write-Host "   [!] Could not roll back version: $_" -ForegroundColor Yellow
    }
    Write-Err "MSBuild failed with exit code $LASTEXITCODE"
}
Write-Ok "Build succeeded"

Write-Step "Locating build output..."
$pkgFolder = Get-ChildItem $AppPackages -Directory -Filter "*Release*" -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $pkgFolder) {
    $pkgFolder = Get-ChildItem $AppPackages -Directory -ErrorAction SilentlyContinue |
                 Where-Object { (Get-ChildItem $_.FullName -Filter "*.msix" -ErrorAction SilentlyContinue).Count -gt 0 } |
                 Sort-Object LastWriteTime -Descending | Select-Object -First 1
}
if (-not $pkgFolder) { Write-Err "No package folder found under $AppPackages" }
Write-Ok "Package folder: $($pkgFolder.FullName)"

$InstallerDir = Join-Path $BuildDir "Installer"
$SubDir       = Join-Path $InstallerDir "_Installer"
Write-Step "Assembling installer folder: $InstallerDir"

# Wipe and recreate to ensure a clean state
if (Test-Path $InstallerDir) { Remove-Item $InstallerDir -Recurse -Force }
New-Item -ItemType Directory -Path $InstallerDir | Out-Null
New-Item -ItemType Directory -Path $SubDir       | Out-Null
Write-Info "Cleared installer folder"

# All package files go into _Installer\ — only Install.bat lives in the root
Copy-Item "$($pkgFolder.FullName)\*" -Destination $SubDir -Recurse -Force
Write-Ok "Package files copied to _Installer\"
Export-Certificate -Cert $signingCert -FilePath (Join-Path $SubDir "ClawTweaks.cer") -Type CERT | Out-Null
Write-Ok "Certificate exported to _Installer\"
Copy-Item $InstallPs1 -Destination (Join-Path $SubDir "Install.ps1") -Force
Write-Ok "Install.ps1 copied to _Installer\"

# Install.bat is the only file in the root — this is what the user double-clicks
Copy-Item $InstallBat -Destination (Join-Path $InstallerDir "Install.bat") -Force
Write-Ok "Install.bat copied to root (user-facing entry point)"

Write-Step "Creating installer ZIP..."
$ZipPath = Join-Path $BuildDir "ClawTweaks_${newVer}_Installer.zip"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$InstallerDir\*" -DestinationPath $ZipPath
$sizeMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Ok "ZIP created: $ZipPath ($sizeMB MB)"

Write-Host ""
Write-Host "  =============================================" -ForegroundColor Green
Write-Host "    Package ready!                            " -ForegroundColor White
Write-Host "  =============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  ZIP: $ZipPath" -ForegroundColor White
Write-Host "  Version: $newVer" -ForegroundColor Gray
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Cyan
Write-Host "  1. Copy ZIP to the MSI Claw" -ForegroundColor Gray
Write-Host "  2. Extract and run: powershell -ExecutionPolicy Bypass -File .\Install.ps1" -ForegroundColor Gray
Write-Host ""
Write-Host "  Log: $LogFile" -ForegroundColor DarkGray
Write-Host ""

Stop-Transcript | Out-Null
