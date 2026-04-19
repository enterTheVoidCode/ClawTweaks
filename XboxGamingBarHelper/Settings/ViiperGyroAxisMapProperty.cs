using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Single IMU axis-mapping selector. The emulated device's X / Y / Z gyro (and accel)
    /// channel reads from the chosen source axis, optionally negated. Valid values:
    /// "X", "Y", "Z", "-X", "-Y", "-Z".
    ///
    /// Three instances are wired in SettingsManager — one per axis — because each is an
    /// independent user choice persisted under its own LocalSettings key.
    /// </summary>
    internal class ViiperGyroAxisMapProperty : HelperProperty<string, SettingsManager>
    {
        private readonly string settingsKey;
        private readonly string defaultValue;

        public ViiperGyroAxisMapProperty(SettingsManager inManager, Function function, string inSettingsKey, string inDefault)
            : base(LoadFromSettings(inSettingsKey, inDefault), null, function, inManager)
        {
            settingsKey = inSettingsKey;
            defaultValue = inDefault;
            Logger.Info($"{function} loaded: {Value}");
        }

        private static string LoadFromSettings(string key, string fallback)
        {
            if (LocalSettingsHelper.TryGetValue<string>(key, out var value) && IsValid(value))
            {
                return value;
            }
            return fallback;
        }

        private void SaveToSettings()
        {
            LocalSettingsHelper.SetValue(settingsKey, IsValid(Value) ? Value : defaultValue);
        }

        private static bool IsValid(string value)
        {
            return value == "X" || value == "Y" || value == "Z"
                || value == "-X" || value == "-Y" || value == "-Z";
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"{Function} changed: {Value}");
            SaveToSettings();
        }
    }
}
