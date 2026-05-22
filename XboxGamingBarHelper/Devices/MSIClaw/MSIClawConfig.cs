using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Configuration for MSI Claw handhelds (A1M, A2VM series).
    ///
    /// NOTE: GoTweaks queries Win32_ComputerSystemProduct.Name (product display name),
    /// NOT Win32_ComputerSystem.Model (board ID like "MS-1T52").
    /// Confirmed WMI Name values:
    ///   "Claw 8 AI+ A2VM"  — Claw 8 AI+ A2VM (Lunar Lake, MS-1T52)
    ///   "Claw 7 AI+ A2VM"  — Claw 7 AI+ A2VM (Lunar Lake, MS-1T42)  [assumed]
    ///   "Claw A1M"         — Claw A1M (Meteor Lake, MS-1T41)         [assumed]
    ///
    /// Detection: Manufacturer contains "Micro-Star" AND Model contains "Claw".
    /// This is future-proof for any upcoming MSI Claw model.
    ///
    /// Key differences from Legion Go:
    ///   - No detachable controllers (single integrated controller)
    ///   - No touchpad
    ///   - No scroll wheel
    ///   - TDP via Intel IGCL (not WMI TDP)
    ///   - XInput-based controller emulation (no Legion HID path)
    /// </summary>
    public class MSIClawConfig : DeviceConfig
    {
        public override DeviceType DeviceType => DeviceType.MSIClaw;
        public override string DisplayName => "MSI Claw";

        /// <summary>
        /// WMI Win32_ComputerSystemProduct.Vendor for MSI devices.
        /// Full string is "Micro-Star International Co., Ltd." — use substring match.
        /// </summary>
        public override string Manufacturer => "Micro-Star";

        /// <summary>
        /// Known Win32_ComputerSystemProduct.Name values (product display names).
        /// Matches() is overridden below with a "Claw" substring check as the primary
        /// detection path — ModelIds is kept as a fallback for exact-match scenarios.
        /// </summary>
        public override IReadOnlyList<string> ModelIds => new[]
        {
            "Claw 8 AI+ A2VM",  // Claw 8 AI+ A2VM (Lunar Lake, MS-1T52) — confirmed
            "Claw 7 AI+ A2VM",  // Claw 7 AI+ A2VM (Lunar Lake, MS-1T42) — assumed
            "Claw A1M",         // Claw A1M (Meteor Lake, MS-1T41)        — assumed
            // Legacy board IDs (Win32_ComputerSystem.Model — not used by GoTweaks,
            // but kept as documentation / future fallback).
            // "MS-1T41", "MS-1T42", "MS-1T52", "MS-1T8K"
        };

        /// <summary>
        /// Matches any MSI device whose product name contains "Claw".
        /// This covers all current and future MSI Claw models without needing an
        /// exhaustive list of exact product names.
        /// </summary>
        public override bool Matches(DeviceInfo deviceInfo)
        {
            // Manufacturer must contain "Micro-Star" (case-insensitive)
            if (deviceInfo.Manufacturer.IndexOf(Manufacturer, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            // Product name (Win32_ComputerSystemProduct.Name) must contain "Claw"
            if (deviceInfo.Model.IndexOf("Claw", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // Fallback: exact match against known product names in ModelIds
            return base.Matches(deviceInfo);
        }

        // Feature flags
        public override bool SupportsWmiTdp             => false;  // TDP via Intel IGCL, not WMI
        public override bool SupportsControllerRemap    => true;   // XInput controller emulation works
        public override bool SupportsRgbLighting        => false;  // No on-controller RGB (handled by IGCL separately)
        public override bool SupportsGyro               => true;   // Built-in IMU present
        public override bool HasTouchpad                => false;  // No touchpad
        public override bool HasScrollWheel             => false;  // No scroll wheel
        public override bool HasDetachableControllers   => false;  // Integrated controller only
    }
}
