using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Controls how a Guide-equivalent press (Legion Mode, Xbox Guide) is translated
    /// when the VIIPER backend is active.
    ///   "Native"  – forward to the emulated device's native Guide/PS button.
    ///   "GameBar" – suppress the native Guide press and send a Win+G keystroke instead,
    ///               so the Xbox Game Bar opens. Useful when the emulated device type
    ///               is DS4/DSE and the user still wants the physical Guide to open
    ///               the overlay.
    /// </summary>
    internal class ViiperGuideButtonModeProperty : HelperProperty<string, SettingsManager>
    {
        public const string Default = "Native";
        private const string SettingsKey = "ViiperGuideButtonMode";

        public ViiperGuideButtonModeProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_GuideButtonMode, inManager)
        {
            Logger.Info($"ViiperGuideButtonMode loaded: {Value}");
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
            Logger.Info($"ViiperGuideButtonMode changed: {Value}");
            SaveToSettings();
        }
    }
}
