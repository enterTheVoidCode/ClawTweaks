using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Configuration for MSI Claw 7/8 AI+ A2VM handhelds (Lunar Lake, Intel Core Ultra 200H).
    ///
    /// SUPPORTED MODELS ONLY:
    ///   "Claw 8 AI+ A2VM"   — MS-1T52, Lunar Lake — confirmed WMI Name
    ///   "Claw 7 AI+ A2VM"   — MS-1T42, Lunar Lake — assumed WMI Name
    ///   "Claw 7 AI+ A2VMX"  — MS-1T42 variant    — assumed WMI Name
    ///
    /// NOT SUPPORTED:
    ///   "Claw A1M"  — Meteor Lake (Intel Core Ultra 100H), different EC, different HW controller.
    ///   Any other Claw variant not based on Lunar Lake.
    ///
    /// Detection key: Win32_ComputerSystemProduct.Name must contain "A2VM".
    /// The A1M does NOT contain "A2VM" in its WMI name → excluded automatically.
    ///
    /// NOTE: GoTweaks queries Win32_ComputerSystemProduct.Name, NOT Win32_ComputerSystem.Model.
    ///
    /// Key differences from Legion Go:
    ///   - No detachable controllers (single integrated controller)
    ///   - No touchpad / scroll wheel
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
        /// Supported Win32_ComputerSystemProduct.Name values (product display names).
        /// Matches() is overridden below; ModelIds is kept for documentation / fallback.
        /// </summary>
        public override IReadOnlyList<string> ModelIds => new[]
        {
            "Claw 8 AI+ A2VM",  // Claw 8 AI+ A2VM  (Lunar Lake, MS-1T52) — confirmed
            "Claw 7 AI+ A2VM",  // Claw 7 AI+ A2VM  (Lunar Lake, MS-1T42) — assumed
            "Claw 7 AI+ A2VMX", // Claw 7 AI+ A2VMX (Lunar Lake, MS-1T42 variant) — assumed
        };

        /// <summary>
        /// Matches only Lunar Lake A2VM/A2VMX variants.
        /// The Claw A1M (Meteor Lake) is intentionally excluded — it has a different
        /// processor, EC firmware, and hardware controller.
        /// </summary>
        public override bool Matches(DeviceInfo deviceInfo)
        {
            // Manufacturer must contain "Micro-Star" (case-insensitive)
            if (deviceInfo.Manufacturer.IndexOf(Manufacturer, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            // Product name must contain "A2VM" — covers A2VM and A2VMX, excludes A1M.
            return deviceInfo.Model.IndexOf("A2VM", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Feature flags
        public override bool SupportsWmiTdp             => false;  // TDP via Intel IGCL, not WMI
        public override bool SupportsControllerRemap    => true;   // XInput controller emulation works
        public override bool SupportsRgbLighting        => true;   // Controller LED via HID vendor commands (firmware-version-aware)
        public override bool SupportsGyro               => true;   // Built-in IMU present
        public override bool SupportsFirmwareKeyboardRemap => true; // Firmware button→keyboard remap RE'd/verified on A2VM (MatchesModel gates to A2VM only)
        public override bool HasTouchpad                => false;  // No touchpad
        public override bool HasScrollWheel             => false;  // No scroll wheel
        public override bool HasDetachableControllers   => false;  // Integrated controller only
    }
}
