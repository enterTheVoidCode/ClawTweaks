using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class OnScreenDisplayProviderInstalledProperty : WidgetControlEnabledProperty<Slider>
    {
        public OnScreenDisplayProviderInstalledProperty(Slider inUI, Page inOwner) : base(Function.Settings_OnScreenDisplayProviderInstalled, inUI, inOwner)
        {
        }

        //protected override void NotifyPropertyChanged(string propertyName = "")
        //{
        //    Logger.Info($"Settings_OnScreenDisplayProviderInstalled to {value}???");
        //    base.NotifyPropertyChanged(propertyName);

            
        //}
    }
}
