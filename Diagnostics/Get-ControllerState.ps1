# =============================================================================
# ClawTweaks Controller State Diagnostic
# Captures live state: Windows controllers (XInput + WMI), HidHide, ViGEm
#
# Usage: .\Get-ControllerState.ps1 -Phase 1
#        Phase 1 = Standard MSI mode
#        Phase 2 = After Center M deactivation
#        Phase 3 = After controller emulation enabled
#        Phase 4 = After controller emulation disabled
# =============================================================================

param(
    [Parameter(Mandatory=$false)]
    [ValidateRange(1,4)]
    [int]$Phase = 1
)

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$separator = "=" * 70

# Admin check — HidHide CLI requires elevation
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host ""
    Write-Host "  *** WARNING: NOT running as Administrator ***" -ForegroundColor Red
    Write-Host "  *** HidHide CLI calls will fail with Access Denied ***" -ForegroundColor Red
    Write-Host "  *** Re-run in an elevated PowerShell for complete HidHide data ***" -ForegroundColor Red
    Write-Host ""
}

function Write-Section([string]$title) {
    Write-Host ""
    Write-Host "  [$title]" -ForegroundColor Yellow
    Write-Host "  " + ("-" * 60) -ForegroundColor DarkGray
}

Write-Host $separator -ForegroundColor Cyan
Write-Host "  ClawTweaks Controller Diagnostic  --  PHASE $Phase" -ForegroundColor Cyan
Write-Host "  $timestamp" -ForegroundColor Cyan
Write-Host $separator -ForegroundColor Cyan

# =============================================================================
# SECTION 1: XInput Slots (0-3) via xinput1_4.dll
# =============================================================================
Write-Section "1. XInput Slots (xinput1_4.dll)"

try {
    $xinputType = @'
using System;
using System.Runtime.InteropServices;
public class XInputDiag {
    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD {
        public ushort wButtons;
        public byte bLeftTrigger, bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }
    [DllImport("xinput1_4.dll", EntryPoint="XInputGetState")]
    public static extern uint GetState(uint dwUserIndex, ref XINPUT_STATE pState);
}
'@
    if (-not ([System.Management.Automation.PSTypeName]'XInputDiag').Type) {
        Add-Type -TypeDefinition $xinputType
    }

    $connectedSlots = @()
    for ($i = 0; $i -lt 4; $i++) {
        $state = New-Object XInputDiag+XINPUT_STATE
        $result = [XInputDiag]::GetState([uint32]$i, [ref]$state)
        if ($result -eq 0) {
            Write-Host "  Slot $i : CONNECTED  (packet=$($state.dwPacketNumber))" -ForegroundColor Green
            $connectedSlots += $i
        } else {
            Write-Host "  Slot $i : not connected  (err=$result)" -ForegroundColor DarkGray
        }
    }
    Write-Host "  --> Connected slots: $($connectedSlots.Count)  [$($connectedSlots -join ', ')]" -ForegroundColor Cyan
} catch {
    Write-Host "  ERROR loading XInput: $($_.Exception.Message)" -ForegroundColor Red
}

# =============================================================================
# SECTION 2: MSI Claw devices (VID_0DB0) - PnP tree
# =============================================================================
Write-Section "2. MSI Claw PnP Devices  (VID_0DB0)"

$msiAll = Get-CimInstance -ClassName Win32_PnPEntity |
    Where-Object { $_.PNPDeviceID -match "VID_0DB0" } |
    Sort-Object PNPDeviceID

if ($msiAll) {
    $msiAll | ForEach-Object {
        $pidHex = if ($_.PNPDeviceID -match "PID_([0-9A-Fa-f]+)") { $matches[1].ToUpper() } else { "????" }
        $modeLabel = switch ($pidHex) {
            "1901" { "XInput+Keyboard (PID_1901)" }
            "1902" { "DInput gamepad   (PID_1902)" }
            default { "PID_$pidHex" }
        }
        $color = if ($_.Status -eq "OK") { "Green" } else { "Yellow" }
        Write-Host "  [$($_.Status.PadRight(8))] $modeLabel" -ForegroundColor $color
        Write-Host "             $($_.PNPDeviceID)" -ForegroundColor DarkGray
        if ($_.Name) { Write-Host "             Name: $($_.Name)" -ForegroundColor DarkGray }
    }
    $pid1901 = ($msiAll | Where-Object { $_.PNPDeviceID -match "PID_1901" }).Count
    $pid1902 = ($msiAll | Where-Object { $_.PNPDeviceID -match "PID_1902" }).Count
    Write-Host "  --> PID_1901 entries: $pid1901 | PID_1902 entries: $pid1902" -ForegroundColor Cyan
} else {
    Write-Host "  No MSI Claw devices found" -ForegroundColor Red
}

# =============================================================================
# SECTION 3: All game controller / joystick devices (WMI)
# =============================================================================
Write-Section "3. All Gamepad/Controller PnP Devices"

$allControllers = Get-CimInstance -ClassName Win32_PnPEntity |
    Where-Object {
        $_.PNPDeviceID -match "HID" -and (
            $_.Name -match "Controller|Joystick|Gamepad|Xbox" -or
            $_.PNPDeviceID -match "VID_0DB0|VID_045E&PID_028E"
        )
    } |
    Sort-Object PNPDeviceID

if ($allControllers) {
    $allControllers | ForEach-Object {
        $isViGEm  = $_.PNPDeviceID -match "VID_045E.*PID_028E"
        $isMSIClaw = $_.PNPDeviceID -match "VID_0DB0"
        $tag = if ($isViGEm) { " <<< ViGEm Virtual >>>" } elseif ($isMSIClaw) { " [MSI Claw]" } else { "" }
        $col = if ($isViGEm) { "Cyan" } elseif ($isMSIClaw) { "Green" } else { "White" }
        Write-Host "  [$($_.Status.PadRight(8))] $($_.Name)$tag" -ForegroundColor $col
        Write-Host "             $($_.PNPDeviceID)" -ForegroundColor DarkGray
    }
    Write-Host "  --> Total: $($allControllers.Count)" -ForegroundColor Cyan
} else {
    Write-Host "  None found matching gamepad/controller criteria" -ForegroundColor Gray
}

# =============================================================================
# SECTION 4: HidHide state (CLI + Registry)
# =============================================================================
Write-Section "4. HidHide State"

# Find CLI
$hidHideCLI = @(
    "${env:ProgramFiles}\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe",
    "${env:ProgramFiles}\Nefarius Software Solutions e.U.\HidHide\x64\HidHideCLI.exe",
    "${env:ProgramFiles(x86)}\Nefarius Software Solutions\HidHide\HidHideCLI.exe",
    "${env:ProgramFiles(x86)}\Nefarius Software Solutions e.U.\HidHide\HidHideCLI.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

# Direct device object access test - uses Win32 CreateFile via P/Invoke (FileStream cannot open device paths)
Write-Host ""
Write-Host "  [Direct device access test: \\.\HidHide]" -ForegroundColor DarkGray
try {
    if (-not ([System.Management.Automation.PSTypeName]'HidHideDiag').Type) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class HidHideDiag {
    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Auto)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr hObject);
    public static readonly IntPtr INVALID = new IntPtr(-1);
    public static IntPtr OpenHidHide() {
        // GENERIC_READ|GENERIC_WRITE = 0xC0000000u, FILE_SHARE_READ|WRITE = 3, OPEN_EXISTING = 3
        return CreateFile("\\\\.\\HidHide", 0xC0000000u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
    }
}
'@
    }
    $handle = [HidHideDiag]::OpenHidHide()
    if ($handle -eq [HidHideDiag]::INVALID) {
        $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
        $errMsg = switch ($err) {
            2    { "ERROR_FILE_NOT_FOUND (device does not exist)" }
            5    { "ERROR_ACCESS_DENIED (DACL blocks this process)" }
            default { "Win32 error $err" }
        }
        Write-Host "  \\.\HidHide access     : FAILED - $errMsg" -ForegroundColor Red
    } else {
        [HidHideDiag]::CloseHandle($handle) | Out-Null
        Write-Host "  \\.\HidHide access     : ACCESSIBLE" -ForegroundColor Green
    }
} catch {
    Write-Host "  \\.\HidHide access     : test error - $($_.Exception.Message)" -ForegroundColor Yellow
}

# Service status (Get-Service, not just registry)
$hhSvc = Get-Service -Name "HidHide" -ErrorAction SilentlyContinue
if ($hhSvc) {
    $svcCol = if ($hhSvc.Status -eq "Running") { "Green" } else { "Red" }
    Write-Host "  HidHide service status : $($hhSvc.Status)  (StartType=$($hhSvc.StartType))" -ForegroundColor $svcCol
} else {
    Write-Host "  HidHide service        : NOT FOUND via Get-Service" -ForegroundColor Red
}

# PnP device for HidHide kernel driver
$hhPnp = Get-PnpDevice -ErrorAction SilentlyContinue |
    Where-Object { $_.FriendlyName -match "HidHide" -or $_.InstanceId -match "HidHide" }
if ($hhPnp) {
    $hhPnp | ForEach-Object {
        $col = if ($_.Status -eq "OK") { "Green" } else { "Yellow" }
        Write-Host "  HidHide PnP device     : $($_.FriendlyName)  [$($_.Status)]" -ForegroundColor $col
        Write-Host "                           $($_.InstanceId)" -ForegroundColor DarkGray
    }
} else {
    Write-Host "  HidHide PnP device     : not found in PnP tree" -ForegroundColor Yellow
}

# Registry: cloaking active flag
# HidHide stores IsActive under HKLM\SYSTEM\CurrentControlSet\Services\HidHide\Parameters
$hidHideParams = Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\HidHide\Parameters" -ErrorAction SilentlyContinue
if ($hidHideParams) {
    $isActive = $hidHideParams.IsActive
    $activeLabel = if ($isActive) { "ACTIVE (cloaking ON)" } else { "inactive (cloaking off)" }
    $activeColor = if ($isActive) { "Red" } else { "Green" }
    Write-Host "  Cloaking (registry IsActive): $activeLabel" -ForegroundColor $activeColor
} else {
    $hhServiceReg = Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\HidHide" -ErrorAction SilentlyContinue
    if ($hhServiceReg) {
        # HidHide 1.5.x does not persist IsActive to the registry Parameters subkey.
        # Cloaking state is held in driver memory only; infer from blocked device count below.
        Write-Host "  Registry: service key exists (Start=$($hhServiceReg.Start)), Parameters subkey not found (normal for HidHide 1.5.x)" -ForegroundColor DarkGray
    } else {
        Write-Host "  Registry: HidHide NOT installed (no service key)" -ForegroundColor Red
    }
}

if ($hidHideCLI) {
    Write-Host "  CLI path: $hidHideCLI" -ForegroundColor DarkGray
    # Version info
    try {
        $cliVer = (Get-Item $hidHideCLI).VersionInfo
        Write-Host "  CLI version: $($cliVer.FileVersion)  (Product: $($cliVer.ProductVersion))" -ForegroundColor DarkGray
    } catch {
        Write-Host "  CLI version: could not read" -ForegroundColor DarkGray
    }
    Write-Host "  Running as Admin: $isAdmin" -ForegroundColor $(if ($isAdmin) { "Green" } else { "Red" })

    # --dev-list: all devices + blocked state
    Write-Host ""
    Write-Host "  [--dev-list raw output]" -ForegroundColor DarkGray
    $devListRaw = & $hidHideCLI --dev-list 2>&1
    if ($devListRaw) {
        $devListRaw | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    } else {
        Write-Host "    (no output)" -ForegroundColor DarkGray
    }

    # Parse blocked entries
    $blockedLines = $devListRaw | Where-Object { $_ -match '--dev-hide\s+"(.+)"' }
    $blockedIds = $blockedLines | ForEach-Object {
        if ($_ -match '--dev-hide\s+"(.+)"') { $matches[1] }
    }
    Write-Host ""
    # Phase 3: exactly 1 blocked device (MI_00&COL01) is correct HC behavior → GREEN.
    # All other phases: any blocked device is a problem → RED.
    $blockedCountColor = if ($Phase -eq 3) {
        if ($blockedIds.Count -eq 1) { "Green" } elseif ($blockedIds.Count -eq 0) { "Yellow" } else { "Red" }
    } else {
        if ($blockedIds.Count -gt 0) { "Red" } else { "Green" }
    }
    Write-Host "  Blocked device count: $($blockedIds.Count)" -ForegroundColor $blockedCountColor
    if ($blockedIds) {
        $blockedIds | ForEach-Object {
            $isExpectedP3 = ($Phase -eq 3) -and ($_ -match "VID_0DB0.*PID_1902.*MI_00&COL01")
            $isMSI = $_ -match "VID_0DB0"
            $col  = if ($isExpectedP3) { "Green" } elseif ($isMSI) { "DarkYellow" } else { "Gray" }
            $note = if ($isExpectedP3) { "  (expected - HC: primary DInput gamepad collection)" } else { "" }
            Write-Host "    HIDDEN: $_$note" -ForegroundColor $col
        }
    }

    # --app-list: whitelisted applications
    Write-Host ""
    Write-Host "  [--app-list raw output]" -ForegroundColor DarkGray
    $appListRaw = & $hidHideCLI --app-list 2>&1
    if ($appListRaw) {
        $appListRaw | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    } else {
        Write-Host "    (no output)" -ForegroundColor DarkGray
    }

    # --inv-state: inverse application list state (allowlist vs. blocklist mode)
    # NOTE: this is NOT the cloaking/IsActive state.
    # IsActive (cloaking on/off) is set via the HidHide API (service.IsActive = true/false).
    # --inv-off = normal allowlist mode (expected); --inv-on = inverted blocklist mode (wrong)
    Write-Host ""
    $invRaw = & $hidHideCLI --inv-state 2>&1
    Write-Host "  Inverse app list (--inv-state): $invRaw" -ForegroundColor $(if ("$invRaw" -match "--inv-on") { "Red" } else { "Green" })
} else {
    Write-Host "  HidHide CLI not found (checked standard install paths)" -ForegroundColor Yellow
}

# =============================================================================
# SECTION 5: ViGEm state
# =============================================================================
Write-Section "5. ViGEm State"

# Service
$vigemSvc = Get-Service -Name "ViGEmBus" -ErrorAction SilentlyContinue
if ($vigemSvc) {
    $svcColor = if ($vigemSvc.Status -eq "Running") { "Green" } else { "Red" }
    Write-Host "  ViGEmBus service: $($vigemSvc.Status)" -ForegroundColor $svcColor
} else {
    Write-Host "  ViGEmBus service: NOT FOUND" -ForegroundColor Red
}

# PnP device (ViGEmBus bus device)
$vigemBusDev = Get-CimInstance -ClassName Win32_PnPEntity |
    Where-Object { $_.PNPDeviceID -match "ROOT\\VIGEMBUS|VIGEMBUS" -or $_.Name -match "ViGEm" }
if ($vigemBusDev) {
    $vigemBusDev | ForEach-Object {
        Write-Host "  ViGEmBus device: $($_.Name)  [$($_.Status)]" -ForegroundColor Green
        Write-Host "    $($_.PNPDeviceID)" -ForegroundColor DarkGray
    }
} else {
    Write-Host "  ViGEmBus PnP device: not found" -ForegroundColor Gray
}

# Virtual Xbox360 controllers (VID_045E PID_028E = standard MS Xbox 360 signature ViGEm uses)
$vigemVirtual = Get-CimInstance -ClassName Win32_PnPEntity |
    Where-Object { $_.PNPDeviceID -match "VID_045E.*PID_028E" }
if ($vigemVirtual) {
    Write-Host "  Virtual Xbox360 (VID_045E PID_028E): $($vigemVirtual.Count) device(s)" -ForegroundColor Cyan
    $vigemVirtual | ForEach-Object {
        Write-Host "    [$($_.Status)] $($_.Name)" -ForegroundColor Cyan
        Write-Host "         $($_.PNPDeviceID)" -ForegroundColor DarkGray
    }
} else {
    Write-Host "  Virtual Xbox360 (VID_045E PID_028E): none" -ForegroundColor Gray
}

# =============================================================================
# SUMMARY
# =============================================================================
Write-Host ""
Write-Host $separator -ForegroundColor Cyan
Write-Host "  PHASE $Phase SUMMARY" -ForegroundColor Cyan
Write-Host $separator -ForegroundColor Cyan

# Expected states per phase
$expected = switch ($Phase) {
    1 { @{ XInputCount=">=1 (physical MSI)"; PID1901="present"; PID1902="absent"; HidHide="inactive, 0 blocked"; ViGEm="service present, 0 virtual" } }
    2 { @{ XInputCount=">=1 (same as phase 1)"; PID1901="present"; PID1902="absent"; HidHide="inactive, 0 blocked"; ViGEm="service present, 0 virtual" } }
    3 { @{ XInputCount="1 (ViGEm virtual only)"; PID1901="absent (DInput mode)"; PID1902="present (hidden)"; HidHide="ACTIVE, 1 blocked (MI_00&COL01 only)"; ViGEm="service running, 1 virtual" } }
    4 { @{ XInputCount=">=1 (physical restored)"; PID1901="present"; PID1902="absent"; HidHide="inactive, 0 blocked"; ViGEm="service present, 0 virtual" } }
}

Write-Host "  Expected for Phase ${Phase}:" -ForegroundColor White
$expected.GetEnumerator() | ForEach-Object {
    Write-Host "    $($_.Key.PadRight(16)): $($_.Value)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "  Captured at: $timestamp" -ForegroundColor DarkGray
Write-Host $separator -ForegroundColor Cyan
Write-Host ""
