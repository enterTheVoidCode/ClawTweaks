using Shared.Enums;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices.LegionGoS
{
    /// <summary>
    /// Configuration for Lenovo Legion Go S (budget model)
    /// Released: 2025
    /// </summary>
    public class LegionGoSConfig : DeviceConfig
    {
        public override DeviceType DeviceType => DeviceType.LegionGoS;
        public override string DisplayName => "Legion Go S";
        public override string Manufacturer => "LENOVO";

        public override IReadOnlyList<string> ModelIds => new[]
        {
            "83L3",           // Legion Go S
        };

        // Features - Go S has different HID structure (VID 0x1A86 vs 0x17EF)
        // Most controller features don't work, but RGB lighting uses a different protocol that works
        public override bool SupportsWmiTdp => true;           // Same WMI - works
        public override bool SupportsControllerRemap => false; // Different HID - doesn't work
        public override bool SupportsRgbLighting => true;      // Different HID but lighting protocol implemented
        public override bool SupportsGyro => false;            // Different HID - doesn't work
        public override bool HasTouchpad => false;             // Touchpad settings use HID - not tested/working
        public override bool HasScrollWheel => false;          // Go S does not have scroll wheel
        public override bool HasDetachableControllers => false; // Go S has integrated controllers (not detachable)
    }
}
