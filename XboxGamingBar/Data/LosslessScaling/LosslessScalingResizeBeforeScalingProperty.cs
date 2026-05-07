using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingResizeBeforeScalingProperty : WidgetToggleProperty
    {
        public LosslessScalingResizeBeforeScalingProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.LosslessScalingResizeBeforeScaling, inUI, inOwner)
        {
        }
    }
}
