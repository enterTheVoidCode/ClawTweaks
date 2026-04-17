using Shared.Enums;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices.LegionGo2
{
    /// <summary>
    /// Configuration for Lenovo Legion Go 2 (Ryzen AI)
    /// Released: 2025
    /// </summary>
    public class LegionGo2Config : DeviceConfig
    {
        public override DeviceType DeviceType => DeviceType.LegionGo2;
        public override string DisplayName => "Legion Go 2";
        public override string Manufacturer => "LENOVO";

        public override IReadOnlyList<string> ModelIds => new[]
        {
            "83N0",           // Legion Go 2
        };

        public override IReadOnlyList<string> VariantIds => new[]
        {
            "8ASP2",          // Legion Go 2 variant (Ryzen AI 9 HX 370)
            "8AHP2",          // Legion Go 2 variant
        };

        // Features
        public override bool SupportsWmiTdp => true;
        public override bool SupportsControllerRemap => true;
        public override bool SupportsRgbLighting => true;
        public override bool SupportsGyro => true;
        public override bool HasTouchpad => true;
        public override bool HasScrollWheel => true;
        public override bool HasDetachableControllers => true;
    }
}
