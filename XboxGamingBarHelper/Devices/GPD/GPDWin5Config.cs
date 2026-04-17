using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices.GPD
{
    /// <summary>
    /// Configuration for GPD Win 5
    /// </summary>
    public class GPDWin5Config : DeviceConfig
    {
        public override DeviceType DeviceType => DeviceType.GPDWin5;
        public override string DisplayName => "GPD Win 5";
        public override string Manufacturer => "GPD";

        public override IReadOnlyList<string> ModelIds => new[]
        {
            "G1618-05",
            "GPD WIN 5",
            "WIN 5",
        };

        // Features
        public override bool SupportsWmiTdp => false;
        public override bool SupportsControllerRemap => true; // HID controller remap supported via GPDWin5Controller
        public override bool SupportsRgbLighting => false;
        public override bool SupportsGyro => false;
        public override bool HasTouchpad => false;
        public override bool HasScrollWheel => false;
        public override bool HasDetachableControllers => false;
        public override bool SupportsFanControl => true;

        /// <summary>
        /// GPD uses contains-based matching for model identifiers
        /// </summary>
        public override bool Matches(DeviceInfo deviceInfo)
        {
            // Check manufacturer
            if (!deviceInfo.Manufacturer.ToUpperInvariant().Contains(Manufacturer.ToUpperInvariant()))
                return false;

            // Check model IDs using contains (GPD uses partial matches)
            foreach (var modelId in ModelIds)
            {
                if (deviceInfo.Model.IndexOf(modelId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    deviceInfo.Version.IndexOf(modelId, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
