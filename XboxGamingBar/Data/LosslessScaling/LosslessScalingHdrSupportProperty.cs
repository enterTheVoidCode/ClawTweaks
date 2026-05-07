using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingHdrSupportProperty : WidgetToggleProperty
    {
        public LosslessScalingHdrSupportProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.LosslessScalingHdrSupport, inUI, inOwner)
        {
        }
    }
}
