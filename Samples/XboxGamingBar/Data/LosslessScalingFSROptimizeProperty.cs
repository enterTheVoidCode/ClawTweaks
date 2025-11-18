using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingFSROptimizeProperty : WidgetToggleProperty
    {
        public LosslessScalingFSROptimizeProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.LosslessScalingFSROptimize, inUI, inOwner)
        {
        }
    }
}
