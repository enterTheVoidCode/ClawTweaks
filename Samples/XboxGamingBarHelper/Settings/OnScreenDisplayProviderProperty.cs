using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class OnScreenDisplayProviderProperty : HelperProperty<int, SettingsManager>
    {
        public OnScreenDisplayProviderProperty(SettingsManager inManager) : base(0, null, Function.Settings_OnScreenDisplayProvider, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (Value >= 0 && Value < Program.onScreenDisplayProviders.Count)
            {
                var newOnScreenDisplayProvider = Program.onScreenDisplayProviders[Value];
                Logger.Info($"Change On-Screen Display provider to item {Value}: {newOnScreenDisplayProvider.GetType().Name}");
                Program.onScreenDisplay.ChangeManager(newOnScreenDisplayProvider);
            }
        }
    }
}
