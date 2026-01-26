using NLog;
using Shared.Data;
using System.Collections.Generic;
using XboxGamingBarHelper.Devices.GPD;
using XboxGamingBarHelper.Devices.LegionGo;
using XboxGamingBarHelper.Devices.LegionGo2;
using XboxGamingBarHelper.Devices.LegionGoS;

namespace XboxGamingBarHelper.Devices
{
    /// <summary>
    /// Registry of all supported device configurations.
    /// Add new devices here to enable automatic detection.
    /// </summary>
    public static class DeviceRegistry
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// All registered device configurations, ordered by detection priority.
        /// More specific devices should be listed before generic ones.
        /// </summary>
        private static readonly List<DeviceConfig> RegisteredDevices = new List<DeviceConfig>
        {
            // Legion devices (order matters for detection priority)
            new LegionGo2Config(),   // Check Go 2 first (newer, has variant IDs)
            new LegionGoSConfig(),   // Check Go S before original Go
            new LegionGoConfig(),    // Original Legion Go

            // GPD devices (check Win Mini before Win 4/5 as Win Mini has more specific model)
            new GPDWinMiniConfig(),  // GPD Win Mini (G1617)
            new GPDWin4Config(),     // GPD Win 4 Series (G1618-04)
            new GPDWin5Config(),     // GPD Win 5 (G1618-05)

            // Future devices can be added here:
            // new ROGAllyConfig(),
            // new SteamDeckConfig(),
            // new AyaNeoConfig(),
        };

        /// <summary>
        /// Gets all registered device configurations
        /// </summary>
        public static IReadOnlyList<DeviceConfig> Devices => RegisteredDevices;

        /// <summary>
        /// Attempts to match a DeviceInfo to a known device configuration.
        /// Returns the matching config or null if no match found.
        /// </summary>
        public static DeviceConfig FindMatchingDevice(DeviceInfo deviceInfo)
        {
            foreach (var config in RegisteredDevices)
            {
                if (config.Matches(deviceInfo))
                {
                    Logger.Info($"Device matched: {config.DisplayName} ({config.DeviceType})");
                    return config;
                }
            }

            Logger.Info($"No matching device config found for: {deviceInfo.Manufacturer} {deviceInfo.Model}");
            return null;
        }

        /// <summary>
        /// Registers a new device configuration at runtime.
        /// Useful for testing or dynamic device support.
        /// </summary>
        public static void RegisterDevice(DeviceConfig config)
        {
            if (config != null && !RegisteredDevices.Contains(config))
            {
                RegisteredDevices.Add(config);
                Logger.Info($"Registered new device config: {config.DisplayName}");
            }
        }

        /// <summary>
        /// Gets a device config by device type
        /// </summary>
        public static DeviceConfig GetByType(Shared.Enums.DeviceType deviceType)
        {
            foreach (var config in RegisteredDevices)
            {
                if (config.DeviceType == deviceType)
                    return config;
            }
            return null;
        }
    }
}
