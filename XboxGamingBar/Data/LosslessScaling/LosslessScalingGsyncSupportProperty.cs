using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingGsyncSupportProperty : WidgetToggleProperty
    {
        public LosslessScalingGsyncSupportProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.LosslessScalingGsyncSupport, inUI, inOwner)
        {
        }
    }
}
