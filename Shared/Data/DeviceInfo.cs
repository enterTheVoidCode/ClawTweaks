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

        // ── Fallback identity fields ─────────────────────────────────────────────
        // Model above comes from SMBIOS type 1, which RMA boards are shipped with unprogrammed
        // ("Please change product name"). These four survive that, and ClawHardwareId identifies the
        // device from them when Model cannot. See ClawHardwareId for the ladder.

        /// <summary>
        /// Board code, e.g. "MS-1T52" (Claw 8 AI+) / "MS-1T91" (Claw 8 EX).
        /// From Win32_BaseBoard.Product (SMBIOS type 2).
        /// </summary>
        public string BaseBoardProduct { get; set; } = "Unknown";

        /// <summary>
        /// SKU number, e.g. "1T52.1". From Win32_ComputerSystem.SystemSKUNumber (SMBIOS type 1).
        /// </summary>
        public string SystemSku { get; set; } = "Unknown";

        /// <summary>
        /// CPU marketing name, e.g. "Intel(R) Core(TM) Ultra 7 258V". From Win32_Processor.Name.
        /// </summary>
        public string ProcessorName { get; set; } = "Unknown";

        /// <summary>
        /// CPUID string, e.g. "Intel64 Family 6 Model 189 Stepping 1". From Win32_Processor.Caption.
        /// This is the reliable platform signal — the Claw 8 EX's marketing name is not.
        /// </summary>
        public string ProcessorCaption { get; set; } = "Unknown";

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
        /// Whether this device's controller firmware supports a REVERSE-ENGINEERED, verified
        /// button→keyboard remap (MSI Claw A2VM only). Gates the optional firmware keyboard backend.
        /// </summary>
        public bool SupportsFirmwareKeyboardRemap { get; set; } = false;

        /// <summary>
        /// Whether this device has a touchpad
        /// </summary>
        public bool HasTouchpad { get; set; } = false;

        /// <summary>
        /// Whether this device has scroll wheel functionality (Legion Go specific)
        /// </summary>
        public bool HasScrollWheel { get; set; } = false;

        /// <summary>
        /// Whether this device has detachable left/right controllers (Legion Go/Go2 yes, Go S no)
        /// </summary>
        public bool HasDetachableControllers { get; set; } = false;

        /// <summary>
        /// Whether this device supports fan control (e.g., GPD devices)
        /// </summary>
        public bool SupportsFanControl { get; set; } = false;

        /// <summary>
        /// Whether this device exposes the Drivers tab (GPU driver updates etc.). Default true;
        /// per-model on the MSI Claw (e.g. off on the Claw 8 EX / AMD A8 for now).
        /// </summary>
        public bool SupportsDriverManagement { get; set; } = true;

        /// <summary>
        /// Whether the advanced CPU controls (scheduling policy, P/E core max frequency) are offered.
        /// Default true. Off on Panther Lake (Claw 8 EX): they are not reliably persistent there and buy
        /// little even on Lunar Lake, so the EX exposes the Boost toggle only.
        /// </summary>
        public bool SupportsCpuAdvanced { get; set; } = true;

        /// <summary>
        /// Starting value of the gyro's gravity-relative ("Accelerometer") axis toggle on a fresh
        /// install — NOT a support flag, the toggle is always offered and a stored user choice always
        /// wins. Default true. False on the Claw 8 EX: its accelerometer axes are not verified against
        /// our A1M-derived remap and users report the gyro is only usable with the toggle off.
        /// </summary>
        public bool GyroWorldSpaceDefault { get; set; } = true;

        /// <summary>
        /// TDP power-limit ceiling for PL1 (sustained power). Also the base TDP slider maximum.
        /// </summary>
        public int MaxPL1 { get; set; } = 30;

        /// <summary>
        /// TDP power-limit ceiling for PL2 (boost power). Also the TDP Boost slider maximum.
        /// </summary>
        public int MaxPL2 { get; set; } = 37;

        /// <summary>
        /// Minimum PL2-over-PL1 headroom this platform enforces (e.g. MSI Claw A2VM requires
        /// PL2 &gt;= PL1 + 1; Claw 8 EX requires PL2 &gt;= PL1 + 2).
        /// </summary>
        public int Pl2MinOffset { get; set; } = 1;

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
