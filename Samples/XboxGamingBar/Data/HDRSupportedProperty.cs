using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class HDRSupportedProperty : WidgetControlProperty<bool, ToggleSwitch>
    {
        public HDRSupportedProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.HDRSupported, inUI, inOwner)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"HDR supported: {Value}");
        }
    }
}
