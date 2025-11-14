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

            Logger.Info($"Change On-Screen Display provider to item {Value} in list of {Program.onScreenDisplayProviders.Count} items: {(Value == 0 ? "Rivatuner Statistics Server" : "AMD Software: Adrenaline Edition")}");
            Program.onScreenDisplay.ChangeManager(Program.onScreenDisplayProviders[Value]);
        }
    }
}
