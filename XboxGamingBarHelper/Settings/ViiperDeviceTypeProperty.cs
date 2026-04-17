using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// VIIPER virtual device type selector. Persisted globally.
    /// Valid values match libviiper device type strings:
    ///   xbox360, dualshock4, dualsenseedge, xboxelite2,
    ///   steam-generic, switchpro, joycon-pair.
    /// </summary>
    internal class ViiperDeviceTypeProperty : HelperProperty<string, SettingsManager>
    {
        public const string Default = "xbox360";
        private const string SettingsKey = "ViiperDeviceType";

        public ViiperDeviceTypeProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_DeviceType, inManager)
        {
            Logger.Info($"ViiperDeviceType loaded: {Value}");
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
            Logger.Info($"ViiperDeviceType changed: {Value}");
            SaveToSettings();
        }
    }
}
