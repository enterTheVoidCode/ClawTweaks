using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Settings;

namespace XboxGamingBarHelper.Systems
{
    internal class SdrWhiteLevelSyncModeProperty : HelperProperty<int, SystemManager>
    {
        private const string SettingsKey = "SdrWhiteLevelSyncMode";

        public SdrWhiteLevelSyncModeProperty(SystemManager inManager)
            : base(LoadFromSettings(), null, Function.SdrWhiteLevelSyncMode, inManager)
        {
            Logger.Info($"SdrWhiteLevelSyncMode loaded: {(SdrWhiteLevelSyncMode)Value}");
        }

        private static int LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<int>(SettingsKey, out var value)) return value;
            return (int)SdrWhiteLevelSyncMode.Off;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            LocalSettingsHelper.SetValue(SettingsKey, Value);
            Logger.Info($"SdrWhiteLevelSyncMode changed to {(SdrWhiteLevelSyncMode)Value}");
            Manager?.OnSdrWhiteLevelSyncModeChanged((SdrWhiteLevelSyncMode)Value);
        }

        public SdrWhiteLevelSyncMode Mode => (SdrWhiteLevelSyncMode)Value;
    }
}
