using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class RTSSInstalledProperty : WidgetControlEnabledProperty<Slider>
    {
        public RTSSInstalledProperty(Slider inUI, Page inOwner) : base(Function.RTSSInstalled, inUI, inOwner)
        {
        }
    }
}
