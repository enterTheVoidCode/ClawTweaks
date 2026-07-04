# Claw 8 AI+ EX — Hardware Manifest

Measured on-device 2026-07-03. All values below are direct probe output; none are
assumed or backfilled from A2VM code. See
[CLAW8_EX_PORT_LOG.md](CLAW8_EX_PORT_LOG.md) for narrative context and decisions.

## 1a. Identity

```
Win32_ComputerSystemProduct:
  Vendor            : Micro-Star International Co., Ltd.
  Name              : Claw 8 EX AI+ CG3EM
  Version           : REV:1.0
  IdentifyingNumber : K2605N0098484

Win32_ComputerSystem:
  Manufacturer    : Micro-Star International Co., Ltd.
  Model           : Claw 8 EX AI+ CG3EM
  SystemFamily    : Claw
  SystemSKUNumber : 1T91.1

Win32_BaseBoard:
  Manufacturer : Micro-Star International Co., Ltd.
  Product      : MS-1T91
  Version      : REV:1.0

Win32_BIOS:
  SMBIOSBIOSVersion : E1T91IMS.105
  ReleaseDate       : 2026-05-11

Win32_Processor:
  Name                      : Intel(R) Arc(TM) G3 Extreme
  Manufacturer              : GenuineIntel
  NumberOfCores             : 14
  NumberOfLogicalProcessors : 14
  MaxClockSpeed             : 1900 MHz

Win32_VideoController:
  Name                 : Intel(R) Arc(TM) B390 GPU
  DriverVersion        : 32.0.101.8724
  AdapterCompatibility : Intel Corporation

OS: Microsoft Windows 11 Home, Version 10.0.26200 (Build 26200)
```

Note: `Win32_Processor.Name` reporting "Intel(R) Arc(TM) G3 Extreme" is the literal
measured string — recorded as-is per the port's "never correct a measurement" rule, even
though that reads like GPU branding on a CPU field. Not investigated further; irrelevant
to device-detection/feature-gating logic, which keys off `Win32_ComputerSystemProduct`,
not CPU name.

## 1b. Controller HID

`Get-PnpDevice` (VID_0DB0, all classes) shows a large family of `USB\VID_0DB0&PID_1901&IG_xx`
and `HID\VID_0DB0&PID_1901&...` device nodes (XInput-mode composite device, IG_00 through
IG_13 — many more sub-interfaces than the A2VM code comments describe, but the composite
device itself is `VID_0DB0&PID_1901`, matching A2VM). One `HID\VID_0DB0&PID_1902\...` node
exists but is `Status=Unknown` (not currently enumerated — device is in XInput mode right
now, DInput-mode node is a leftover PnP entry from a prior mode switch, not live).

`Diagnostics/Claw8EXProbes/HidInventory` (built against the repo's existing
`packages/HidSharp.2.1.0`) enumerated live HID devices with VID 0x0DB0:

| DevicePath (suffix) | VID | PID | Release | MaxIn/Out/Feature | UsagePage/Usage |
|---|---|---|---|---|---|
| `...ig_05#8&a80e406...` | 0x0DB0 | 0x1901 | 0x0000 | 15/0/0 | 0x0001 / 0x00010005 (game pad) |
| `...mi_01#7&22f9439c...` | 0x0DB0 | 0x1901 | 0x0411 | 64/64/0 | **0xFFA0 / 0x00010001** (cmd iface) |
| `...mi_02&col01#...\kbd` | 0x0DB0 | 0x1901 | 0x0411 | 9/2/0 | 0x0001 / 0x00010006 (keyboard) |
| `...mi_02&col02#...` | 0x0DB0 | 0x1901 | 0x0411 | 8/0/0 | 0x0001 / 0x00010002 (mouse) |
| `...mi_02&col03#...` | 0x0DB0 | 0x1901 | 0x0411 | 5/0/0 | 0x000C / 0x000C0001 (consumer ctrl) |

**MI_01 (command interface): UsagePage 0xFFA0, Usage 0x0001, 64-byte in/out reports —
matches the A2VM code assumption exactly (`FFA0`/`0001`).** PID for XInput mode is 0x1901,
also matching A2VM. DInput-mode PID (0x1902) and its usage page/usage were NOT captured
live (device wasn't in that mode at probe time) — deferred to Phase 3 P2 (requires sending
the SwitchMode command and re-enumerating).

Firmware version, M1/M2 parameter reads, and mode-switch round-trip: deferred to Phase 3
(P2) per plan — those require sending commands, not just reading enumeration data.

## 1c. Gyro / Sensors

```
Get-PnpDevice -Class Sensor:
  Simple Device Orientation Sensor   SWD\SENSORSWDEVICEENUMERATOR\SDO#...   Status=OK
  HID Sensor Collection V2           HID\VID_8087&PID_0AC2\7&38CDE06&0&0000  Status=OK
```

`VID_8087` = Intel. This is the Intel Integrated Sensor Hub (ISH/ISS). Its HardwareID is
`HID\VID_8087&UP:0020_U:0001` — HID Sensor usage page 0x0020, usage 0x0001, which is only
the generic top-level "Sensor" collection type; the HardwareID alone does not reveal
whether a Gyroscope (usage 0x76) or Accelerometer3D (usage 0x73) sub-collection exists
underneath it. Determining that needs raw HID report-descriptor parsing, not attempted in
Phase 1.

**`Diagnostics/Claw8EXProbes/GyroProbe`** (net8.0-windows10.0.19041.0, calls
`Windows.Devices.Sensors.Gyrometer.GetDefault()` / `Accelerometer.GetDefault()`, mirroring
`XboxGamingBarHelper/ControllerEmulation/GyroSourceAdapters.cs`'s
`WindowsSensorGyroSourceAdapter`) result:

```
Gyrometer.GetDefault(): NULL - no gyrometer present
Accelerometer.GetDefault(): NULL - no accelerometer present
```

**This differs from the A2VM assumption** (A2VM's gyro comes from this exact API and
works). Ruled out privacy/consent as the cause: `HKLM\...\CapabilityAccessManager\
ConsentStore\sensors.custom` = `Allow`, `HKCU\...\ConsentStore\location` = `Allow`
(checked as the most likely mundane explanation before treating this as a hardware fact).
Not further root-caused in Phase 1 — carried forward to Phase 2 (check whether
HandheldCompanion's EX/Panther-Lake class, if any, sources gyro differently) and Phase 3
P1 (attempt raw-HID sensor-collection parsing as a fallback source if the WinRT API stays
null). **If this holds, gyro support on the EX cannot reuse `WindowsSensorGyroSourceAdapter`
as-is and needs a different source — this is a real risk to gyro feature parity, not a
probe artifact.**

## 1d. ACPI-WMI (fan / charge limit / TDP interface)

```
Get-CimClass -Namespace root\WMI | Where CimClassName -match MSI:
  MSI_ACPI  (present, along with MSI_AP, MSI_Device, MSI_CPU, MSI_Master_Battery,
             MSI_Slave_Battery, MSI_Power, MSI_VGA, MSI_System, MSI_Software, MSI_Event,
             ISR_MSI, and unrelated MSiSCSI_* iSCSI-initiator classes)

Get-PnpDevice | Where InstanceId -match PNP0C14:
  ACPI\PNP0C14\DSARDEV   "Microsoft Windows Management Interface for ACPI"
  ACPI\PNP0C14\0         "Microsoft Windows Management Interface for ACPI"
  ACPI\PNP0C14\TESTDEV   "Microsoft Windows Management Interface for ACPI"
```

`Get-CimInstance -Namespace root\WMI -ClassName MSI_ACPI` returns **Access Denied** when
run non-elevated (silently returns 0 instances if you swallow the error — a trap). Re-ran
elevated via a one-off elevated PowerShell script:

```
InstanceName : ACPI\PNP0C14\0_0
Count: 1
```

**Matches the A2VM code's hardcoded instance path (`ACPI\PNP0C14\0_0`) exactly.**

`MSI_ACPI` method set (`CimClassMethods`, from the class definition, no elevation needed
to read the schema):
`GetPackage, SetPackage, Get_EC, Set_EC, Get_BIOS, Set_BIOS, Get_SMBUS, Set_SMBUS,
Get_MasterBattery, Set_MasterBattery, Get_SlaveBattery, Set_SlaveBattery, Get_Temperature,
Set_Temperature, Get_Thermal, Set_Thermal, Get_Fan, Set_Fan, Get_Device, Set_Device,
Get_Power, Set_Power, Get_Debug, Set_Debug, Get_AP, Set_AP, Get_Data, Set_Data, Get_WMI,
Get_PE, Set_PE, Get_EC2, Get_BIOS_64, Set_BIOS_64, Get_SMBUS_64, Set_SMBUS_64,
Get_Thermal_64, Set_Thermal_64`

This is a larger method set than the plan's placeholder list — includes `Get_AP`/`Set_AP`
(referenced by the plan as a call site to check) plus many more. Cross-referencing against
actual `MsiClawWmi.Get(`/`.Set(` call sites in code is Phase 2 work (repo inventory), not
repeated here.

**MSI Center M SDK v3.0.2605.2101** is installed (per `HKLM\...\Uninstall`) — this is
presumably what registers the `MSI_ACPI` WMI class/driver. The full MSI Center M
*application* is NOT installed (no matching uninstall entry, no running process).

## 1e. Software environment

```
Windows: 11 Home, 10.0.26200 (Build 26200)
MSI Center M (full app): NOT installed
MSI Center M SDK: v3.0.2605.2101 (installed)
ViGEmBus / HidHide: NOT found via Get-PnpDevice FriendlyName match (not yet installed —
  ClawTweaks installs these itself via its driver-check flow, not done in this probe)
kx.exe: not found anywhere under the repo tree (not yet fetched/bundled on this machine)
```

## Diff table: A2VM (code assumption) vs EX (measured)

| Item | A2VM (code assumption) | EX (measured) | Same? |
|---|---|---|---|
| WMI Vendor | contains "Micro-Star" | "Micro-Star International Co., Ltd." | ✅ Yes |
| WMI Product Name | contains "A2VM" | "Claw 8 EX AI+ CG3EM" | ❌ **No** — this is the whole reason `MSIClawConfig.Matches()` fails |
| Board ID | MS-1T52 | **MS-1T91** | ❌ No (different board, as expected for a different SoC gen) |
| CPU | Lunar Lake (Core Ultra 200V) | Panther Lake, 14C/14T, 1900MHz base (WMI Name string oddly reads "Intel(R) Arc(TM) G3 Extreme" — recorded verbatim) | ❌ No (different generation, as expected) |
| Controller VID/PID (XInput mode) | 0DB0/1901 | 0DB0/1901 | ✅ Yes |
| Controller VID/PID (DInput mode) | 0DB0/1902 | 0DB0/1902 (PnP node present but not live-verified; not currently in that mode) | ⚠️ Probably yes, not confirmed live — Phase 3 |
| Cmd iface UsagePage/Usage (XInput) | FFA0/0001 | FFA0/0001 (measured live via HidSharp) | ✅ Yes |
| Cmd iface UsagePage/Usage (DInput) | FFF0/0040 | not captured (device not in DInput mode at probe time) | ⚠️ Unknown — Phase 3 |
| Windows Gyrometer present | yes | **NULL — Gyrometer.GetDefault() returns null** | ❌ **No — real divergence, needs Phase 2/3 investigation** |
| Gyro axis orientation vs A1M remap | matches ClawA1M table | N/A (no gyrometer to test) | ❌ Blocked on gyro presence |
| MSI_ACPI at ACPI\PNP0C14\0_0 | yes | yes (confirmed elevated) | ✅ Yes |
| MSI_ACPI method set | Get_AP/Set_AP/Get_WMI/… | Confirmed present, plus more (Get_EC2, Get_BIOS_64, Get_SMBUS_64, Get_Thermal_64, etc. — likely newer SDK surface) | ✅ Superset — A2VM's methods all present |
| MSI Center M present | yes (implied by A2VM support) | Full app NOT installed; SDK-only (v3.0.2605.2101) IS installed | ⚠️ Different but WMI class works regardless |

## Open items carried to Phase 2 / Phase 3

1. **Gyro:** `Gyrometer.GetDefault()` returns null. Not a privacy/consent issue (confirmed
   `Allow`). Needs: (a) Phase 2 check of whether any known reference project handles a
   gyrometer-less Claw variant, and what fallback source they use; (b) Phase 3 raw-HID
   parsing of the Intel ISH `HID Sensor Collection V2` node to see if a Gyroscope
   sub-collection exists that Windows just isn't surfacing through the WinRT Sensor API for
   some driver reason (worth also checking Windows Update / newer Intel ISH driver before
   concluding "no gyro hardware").
2. **DInput mode HID details:** VID/PID/usage page for PID 0x1902 not captured live — needs
   the Phase 3 P2 mode-switch probe.
3. **Firmware version / M1/M2 params:** not read yet — Phase 3 P2.
