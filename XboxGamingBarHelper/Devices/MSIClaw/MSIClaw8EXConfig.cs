using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Configuration for the MSI Claw 8 AI+ EX handheld (Intel Panther Lake).
    ///
    /// SUPPORTED MODELS ONLY:
    ///   "Claw 8 EX AI+ CG3EM" — MS-1T91, Panther Lake — confirmed WMI Name
    ///     (measured on-device 2026-07-03, docs/hardware/CLAW8_EX_HARDWARE.md)
    ///
    /// Detection key: Win32_ComputerSystemProduct.Name must contain "Claw 8 EX".
    /// The A2VM family reads "Claw 8 AI+ A2VM" (no "EX" token) and the A1M reads
    /// "Claw A1M", so neither can match this config — and this device's name
    /// contains no "A2VM", so it can never match <see cref="MSIClawConfig"/>.
    /// The two MSI configs are mutually exclusive by construction.
    ///
    /// Shares <see cref="DeviceType.MSIClaw"/> with the A2VM config so every
    /// existing `switch (deviceType)` keeps working; EX-specific behavior is
    /// selected at runtime (e.g. the gyro adapter's CustomSensor fallback) or by
    /// feature flags below.
    ///
    /// Hardware facts measured on-device (docs/hardware/CLAW8_EX_PORT_LOG.md):
    ///   - Controller VID/PID + command-interface usage pages identical to A2VM
    ///     (0x0DB0/0x1901, 0xFFA0/0x0001 in XInput mode; PID 0x1902 appears after
    ///     SwitchMode→DInput). Controller firmware bcdDevice = 0x0411.
    ///   - MSI_ACPI WMI class present at ACPI\PNP0C14\0_0 (same path as A2VM).
    ///   - Gyro/accel exist but are published ONLY as HID custom sensors
    ///     ("Physical Gyrometer"/"Physical Accelerometer"); Gyrometer.GetDefault()
    ///     is null. Handled by CustomSensorGyroSourceAdapter fallback (2026-07-05).
    /// </summary>
    public class MSIClaw8EXConfig : DeviceConfig
    {
        public override DeviceType DeviceType => DeviceType.MSIClaw;
        public override string DisplayName => "MSI Claw 8 EX";

        /// <summary>
        /// WMI Win32_ComputerSystemProduct.Vendor for MSI devices.
        /// Full string is "Micro-Star International Co., Ltd." — use substring match.
        /// </summary>
        public override string Manufacturer => "Micro-Star";

        /// <summary>
        /// Supported Win32_ComputerSystemProduct.Name values (product display names).
        /// Matches() is overridden below; ModelIds is kept for documentation / fallback.
        /// </summary>
        public override IReadOnlyList<string> ModelIds => new[]
        {
            "Claw 8 EX AI+ CG3EM", // Claw 8 AI+ EX (Panther Lake, MS-1T91) — confirmed
        };

        /// <summary>
        /// Matches the Panther Lake "Claw 8 EX" family. Substring (not exact) match so a
        /// regional/RAM-config suffix variation of the confirmed "Claw 8 EX AI+ CG3EM"
        /// name still matches; the "EX" token is what separates this generation from
        /// the A2VM/A1M names.
        /// </summary>
        public override bool Matches(DeviceInfo deviceInfo)
        {
            if (deviceInfo.Manufacturer.IndexOf(Manufacturer, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            return deviceInfo.Model.IndexOf("Claw 8 EX", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Feature flags — set from measured Phase 1/3 probe results, not assumptions.
        public override bool SupportsWmiTdp             => false;  // Same as A2VM: TDP flows through the MSIClaw ACPI-WMI path, not the generic WMI-TDP flag
        public override bool SupportsControllerRemap    => true;   // Command interface + mode switch verified identical to A2VM (Phase 1/3 probes)
        public override bool SupportsRgbLighting        => false;  // Controller fw 0x0411 is NOT in MsiClawLedController's firmware→RGB-address table; nearest-match would write unverified EEPROM addresses — keep off until probed (Phase 3 P7)
        public override bool SupportsGyro               => true;   // Streams via CustomSensor ("Physical Gyrometer", ~100 Hz) — verified 2026-07-05
        public override bool HasTouchpad                => false;  // No touchpad
        public override bool HasScrollWheel             => false;  // No scroll wheel
        public override bool HasDetachableControllers   => false;  // Integrated controller only
    }
}
