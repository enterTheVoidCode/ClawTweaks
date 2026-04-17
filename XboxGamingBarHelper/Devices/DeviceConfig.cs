using Shared.Data;
using Shared.Enums;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices
{
    /// <summary>
    /// Base class for device-specific configuration.
    /// Each supported device type should have its own config class that inherits from this.
    /// </summary>
    public abstract class DeviceConfig
    {
        /// <summary>
        /// The device type this config represents
        /// </summary>
        public abstract DeviceType DeviceType { get; }

        /// <summary>
        /// Display name for this device (e.g., "Legion Go", "Legion Go 2")
        /// </summary>
        public abstract string DisplayName { get; }

        /// <summary>
        /// Model identifiers that match this device (e.g., "83E1", "83N0")
        /// </summary>
        public abstract IReadOnlyList<string> ModelIds { get; }

        /// <summary>
        /// Variant identifiers found in Version/SystemFamily fields (e.g., "8ASP2")
        /// </summary>
        public virtual IReadOnlyList<string> VariantIds => new List<string>();

        /// <summary>
        /// Manufacturer name to match (e.g., "LENOVO")
        /// </summary>
        public abstract string Manufacturer { get; }

        // Feature support flags
        public abstract bool SupportsWmiTdp { get; }
        public abstract bool SupportsControllerRemap { get; }
        public abstract bool SupportsRgbLighting { get; }
        public abstract bool SupportsGyro { get; }
        public abstract bool HasTouchpad { get; }
        public abstract bool HasScrollWheel { get; }
        public abstract bool HasDetachableControllers { get; }
        public virtual bool SupportsFanControl => false;

        /// <summary>
        /// Applies this device's feature configuration to a DeviceInfo instance
        /// </summary>
        public virtual void ApplyFeatures(DeviceInfo deviceInfo)
        {
            deviceInfo.DeviceType = DeviceType;
            deviceInfo.SupportsWmiTdp = SupportsWmiTdp;
            deviceInfo.SupportsControllerRemap = SupportsControllerRemap;
            deviceInfo.SupportsRgbLighting = SupportsRgbLighting;
            deviceInfo.SupportsGyro = SupportsGyro;
            deviceInfo.HasTouchpad = HasTouchpad;
            deviceInfo.HasScrollWheel = HasScrollWheel;
            deviceInfo.HasDetachableControllers = HasDetachableControllers;
            deviceInfo.SupportsFanControl = SupportsFanControl;
        }

        /// <summary>
        /// Checks if this config matches the given device info
        /// </summary>
        public virtual bool Matches(DeviceInfo deviceInfo)
        {
            // Check manufacturer
            if (!deviceInfo.Manufacturer.ToUpperInvariant().Contains(Manufacturer.ToUpperInvariant()))
                return false;

            // Check model IDs
            foreach (var modelId in ModelIds)
            {
                if (deviceInfo.Model.Equals(modelId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Check variant IDs in Version/SystemFamily
            foreach (var variantId in VariantIds)
            {
                if (deviceInfo.Version.IndexOf(variantId, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    deviceInfo.SystemFamily.IndexOf(variantId, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
