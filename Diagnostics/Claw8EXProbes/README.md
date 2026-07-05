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
Device Manager. Not caused by Windows motion-sensor privacy settings (checked: `Allow`),
not caused by dock/undock state (tested both, see port log). Root cause not yet
determined — see `docs/hardware/CLAW8_EX_HARDWARE.md` section 1c and the Phase 1/3 entries
in `docs/hardware/CLAW8_EX_PORT_LOG.md`. This is the top open risk carried into Phase 4.

## SensorDescriptorProbe

`dotnet build -c Release` (net472). Hand-rolled HID report-descriptor walker (bypasses
HidSharp's higher-level `ReportDescriptor`/`DeviceItem` abstraction, which only surfaces
top-level Application collections) that tracks every Main/Global/Local item, so nested
Collection/Usage announcements are visible — that's where a HID Sensor page (`0x0020`)
device would declare a Gyroscope (`0x0076`) sub-collection. Filters to Intel (`VID_8087`)
devices by default.

**Phase 3 finding:** dead end before it could even parse anything — the Intel ISH's "HID
Sensor Collection V2" node is claimed under PnP Class `Sensor`, not `HIDClass`, so its raw
HID device interface isn't enumerable via HidSharp (or any generic HID API) at all. This
is the Sensor Class Extension's exclusive-ownership-by-design, not something fixable from
a probe.

## ControllerMotionProbe

`dotnet build -c Release` (net472). Sends HandheldCompanion's `SetMotionStatus` vendor HID
command (`{0x0F,0,0,0x3C,0x2F,1}`, same wire format as `MSIClawHidController.cs`'s
`SwitchMode`) to the Claw controller's command interface, then listens on every openable
`VID_0DB0` HID interface for 12 seconds while the device is physically moved, looking for
streamed motion data. Restores with `{0x0F,0,0,0x3C,0x2F,0}` before exiting.

**Phase 3 finding:** zero input reports arrived on any interface. Tests the hypothesis
that gyro data streams from the controller hardware itself (as opposed to the laptop
chassis's Intel ISH) — came back empty too.

## DInputMotionProbe

`dotnet build -c Release` (net472). Switches the controller to DInput mode (same
`SwitchMode` command pattern), then acquires it via `SharpDX.DirectInput` exactly like
`ClawButtonMonitor.cs`'s `FindAndAcquireJoystick()`, and polls `JoystickState` for 12
seconds looking for vendor-extended axes/sliders that a raw HID report read might not
distinguish from noise. Restores XInput mode before exiting.

**Phase 3 finding:** DirectInput enumerated zero gamepad/joystick/driving devices
system-wide (not a VID/PID mismatch — a completely empty result). Another dead end.

All four gyro-sourcing avenues (WinRT sensor, chassis raw HID, controller HID reports,
DirectInput axes) came back empty — see the "Four gyro-sourcing avenues tried" entry in
`docs/hardware/CLAW8_EX_PORT_LOG.md` for the full writeup. **Superseded — the fifth avenue
worked; see CustomSensorProbe below.**

## CustomSensorProbe

`dotnet build -c Release` (net8.0-windows10.0.19041.0). The probe that found the gyro.
Registry/CM-API discovery showed the ISH publishes the IMU only under HID-usage-derived
*custom sensor* interface classes (`{000000XX-766d-4333-8262-27e82dd158b1}`, XX = HID
sensor usage) — "Physical Gyrometer" (0x76), "Physical Accelerometer" (0x73), "Shake
Gesture" (0x233), "Simple DMD" (0x302) — while `GUID_DEVINTERFACE_SENSOR` and the standard
Gyrometer/Accelerometer interface classes have zero instances system-wide. That is the
root cause of `Gyrometer.GetDefault()` returning null: nothing is wrong, the sensors are
just not published where any standard API looks. This probe opens each of the four via
`Windows.Devices.Sensors.Custom.CustomSensor` and dumps ~5 s of readings.

**Phase 3 finding (2026-07-05): gyro streams at ~100 Hz (10 ms min interval), accel at
~500 Hz (2 ms).** Reading property bag: `{C458F8A7-4AE8-4777-9607-2E9BDD65110A}` PIDs
161/162/163 = X/Y/Z. Full decode + at-rest values in the port log. This is the data source
the EX gyro adapter uses.
