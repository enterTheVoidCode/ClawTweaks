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

        // Features
        public override bool SupportsWmiTdp => true;
        public override bool SupportsControllerRemap => true;
        public override bool SupportsRgbLighting => true;
        public override bool SupportsGyro => true;
        public override bool HasTouchpad => true;
        public override bool HasScrollWheel => false;  // Go S does not have scroll wheel
    }
}
