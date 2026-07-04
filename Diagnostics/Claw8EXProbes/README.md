# Claw 8 EX Port — Hardware Probes

Standalone, read-only-by-default console probes used while porting ClawTweaks to the
MSI Claw 8 AI+ EX. Each is a throwaway diagnostic tool, not part of the shipped app —
run them, read the console output, copy findings into `docs/hardware/CLAW8_EX_HARDWARE.md`
and `docs/hardware/CLAW8_EX_PORT_LOG.md`.

## HidInventory

`dotnet build -c Release` (net472), then run the resulting `.exe`. Lists every HID device
matching MSI's vendor ID (`0x0DB0`) found via `HidSharp` (the same library
`XboxGamingBarHelper` uses) — VID/PID, device path, usage page/usage per top-level
collection, max report lengths. Falls back to listing every HID device on the system if no
VID_0DB0 devices are found. Purely enumeration — sends no commands, changes no state.

**Phase 1 finding:** confirmed the XInput-mode command interface (`MI_01`) reports
UsagePage `0xFFA0`, Usage `0x0001`, matching the A2VM assumption exactly.

## GyroProbe

`dotnet build -c Release` (net8.0-windows10.0.19041.0), then run the resulting `.exe`.
Calls `Windows.Devices.Sensors.Gyrometer.GetDefault()` / `Accelerometer.GetDefault()` —
the same WinRT API `ControllerEmulation/GyroSourceAdapters.cs`'s
`WindowsSensorGyroSourceAdapter` uses — reports whether each is present, then samples
5 seconds of readings at rest if a gyrometer is found.

**Phase 1 finding:** both `Gyrometer.GetDefault()` and `Accelerometer.GetDefault()`
returned `null` on the EX, despite an Intel Sensor Hub HID collection being present in
Device Manager. Not caused by Windows motion-sensor privacy settings (checked: `Allow`).
Root cause not yet determined — see `docs/hardware/CLAW8_EX_HARDWARE.md` section 1c and
the Phase 1 entry in `docs/hardware/CLAW8_EX_PORT_LOG.md`. This is the top open risk
carried into Phase 2/3.
