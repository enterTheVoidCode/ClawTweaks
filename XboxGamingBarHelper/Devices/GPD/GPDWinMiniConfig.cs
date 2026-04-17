using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices.GPD
{
    /// <summary>
    /// Configuration for GPD Win Mini
    /// </summary>
    public class GPDWinMiniConfig : DeviceConfig
    {
        public override DeviceType DeviceType => DeviceType.GPDWinMini;
        public override string DisplayName => "GPD Win Mini";
        public override string Manufacturer => "GPD";

        public override IReadOnlyList<string> ModelIds => new[]
        {
            "G1617",
            "GPD WIN MINI",
            "WIN MINI",
        };

        // Features
        public override bool SupportsWmiTdp => false;
        public override bool SupportsControllerRemap => false;
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
