using Shared.Enums;

namespace Shared.Data
{
    /// <summary>
    /// Contains device information obtained from WMI queries.
    /// Used for device-specific feature detection.
    /// </summary>
    public class DeviceInfo
    {
        /// <summary>
        /// Device manufacturer (e.g., "LENOVO", "ASUS", "Valve")
        /// From Win32_ComputerSystemProduct.Vendor
        /// </summary>
        public string Manufacturer { get; set; } = "Unknown";

        /// <summary>
        /// Device model identifier (e.g., "83E1", "83N0")
        /// From Win32_ComputerSystemProduct.Name
        /// </summary>
        public string Model { get; set; } = "Unknown";

        /// <summary>
        /// Device version/SKU (e.g., "Legion Go 8APU1")
        /// From Win32_ComputerSystemProduct.Version
        /// </summary>
        public string Version { get; set; } = "Unknown";

        /// <summary>
        /// System family (e.g., "Legion Go", "ROG Ally")
        /// From Win32_ComputerSystem.SystemFamily
        /// </summary>
        public string SystemFamily { get; set; } = "Unknown";

        /// <summary>
        /// Detected device type based on manufacturer and model matching
        /// </summary>
        public DeviceType DeviceType { get; set; } = DeviceType.Generic;

        /// <summary>
        /// Whether this device supports WMI-based TDP control (Lenovo GAMEZONE)
        /// </summary>
        public bool SupportsWmiTdp { get; set; } = false;

        /// <summary>
        /// Whether this device has Legion-style controller remapping support
        /// </summary>
        public bool SupportsControllerRemap { get; set; } = false;

        /// <summary>
        /// Whether this device has RGB lighting control via WMI
        /// </summary>
        public bool SupportsRgbLighting { get; set; } = false;

        /// <summary>
        /// Whether this device supports gyroscope features
        /// </summary>
        public bool SupportsGyro { get; set; } = false;

        /// <summary>
        /// Whether this device has a touchpad
        /// </summary>
        public bool HasTouchpad { get; set; } = false;

        /// <summary>
        /// Whether this device has scroll wheel functionality (Legion Go specific)
        /// </summary>
        public bool HasScrollWheel { get; set; } = false;

        /// <summary>
        /// Whether this device supports fan control (e.g., GPD devices)
        /// </summary>
        public bool SupportsFanControl { get; set; } = false;

        /// <summary>
        /// Checks if this is any Legion device (Go, Go 2, or Go S)
        /// </summary>
        public bool IsLegionDevice => DeviceType == DeviceType.LegionGo || DeviceType == DeviceType.LegionGo2 || DeviceType == DeviceType.LegionGoS;

        public override string ToString()
        {
            return $"{Manufacturer} {Model} ({Version}) - Type: {DeviceType}";
        }
    }
}
