using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// VIIPER input source selector. Persisted globally.
    /// Valid values: "XInput" (default, any connected Xbox-style controller)
    /// or "LegionHid" (Legion Go controllers via raw HID for gyro/paddles).
    /// </summary>
    internal class ViiperInputSourceProperty : HelperProperty<string, SettingsManager>
    {
        public const string Default = "XInput";
        private const string SettingsKey = "ViiperInputSource";

        public ViiperInputSourceProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_InputSource, inManager)
        {
            Logger.Info($"ViiperInputSource loaded: {Value}");
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
            Logger.Info($"ViiperInputSource changed: {Value}");
            SaveToSettings();
        }
    }
}
