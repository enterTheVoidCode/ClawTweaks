using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// VIIPER gyro source selector. Persisted globally.
    /// Valid values: "None", "Left" (Legion L controller IMU),
    /// "Right" (Legion R controller IMU), "Handheld" (Windows sensor / device IMU).
    /// </summary>
    internal class ViiperGyroSourceProperty : HelperProperty<string, SettingsManager>
    {
        public const string Default = "Left";
        private const string SettingsKey = "ViiperGyroSource";

        public ViiperGyroSourceProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_GyroSource, inManager)
        {
            Logger.Info($"ViiperGyroSource loaded: {Value}");
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
            Logger.Info($"ViiperGyroSource changed: {Value}");
            SaveToSettings();
        }
    }
}
