using Shared.Enums;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices.LegionGo
{
    /// <summary>
    /// Configuration for Lenovo Legion Go (original, Z1 Extreme)
    /// Released: 2023
    /// </summary>
    public class LegionGoConfig : DeviceConfig
    {
        public override DeviceType DeviceType => DeviceType.LegionGo;
        public override string DisplayName => "Legion Go";
        public override string Manufacturer => "LENOVO";

        public override IReadOnlyList<string> ModelIds => new[]
        {
            "83E1",           // Legion Go (original)
            "LNVNB161822",    // Legion Go variant
        };

        // Features
        public override bool SupportsWmiTdp => true;
        public override bool SupportsControllerRemap => true;
        public override bool SupportsRgbLighting => true;
        public override bool SupportsGyro => true;
        public override bool HasTouchpad => true;
        public override bool HasScrollWheel => true;
    }
}
