using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Sub-device PID selector used when the VIIPER device type is set to a Steam controller.
    /// Valid values: "generic", "steam-deck", "legion-go", "legion-go-s", "legion-go-2",
    /// "rog-ally", "msi-claw", "zotac-zone".
    /// </summary>
    internal class ViiperSteamSubDeviceProperty : HelperProperty<string, SettingsManager>
    {
        public const string Default = "legion-go";
        private const string SettingsKey = "ViiperSteamSubDevice";

        public ViiperSteamSubDeviceProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_SteamSubDevice, inManager)
        {
            Logger.Info($"ViiperSteamSubDevice loaded: {Value}");
        }

        private static string LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<string>(SettingsKey, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
            return Default;
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(SettingsKey, Value ?? Default);
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ViiperSteamSubDevice changed: {Value}");
            SaveToSettings();
        }

        /// <summary>
        /// Maps the sub-device name to its Steam VID (always 0x28DE) and PID.
        /// </summary>
        public static bool TryGetSteamVidPid(string subDevice, out ushort vid, out ushort pid)
        {
            vid = 0x28DE;
            switch (subDevice)
            {
                case "generic":      pid = 0x12F0; return true;
                case "steam-deck":   pid = 0x1205; return true;
                case "legion-go":    pid = 0x12FE; return true;
                case "legion-go-s":  pid = 0x12FF; return true;
                case "legion-go-2":  pid = 0x12FB; return true;
                case "rog-ally":     pid = 0x12FD; return true;
                case "msi-claw":     pid = 0x12FA; return true;
                case "zotac-zone":   pid = 0x12FC; return true;
                default:             pid = 0; vid = 0; return false;
            }
        }
    }
}
