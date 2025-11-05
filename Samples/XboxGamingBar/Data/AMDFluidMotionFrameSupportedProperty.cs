using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDFluidMotionFrameSupportedProperty : WidgetControlEnabledProperty<ToggleSwitch>
    {
        public AMDFluidMotionFrameSupportedProperty(ToggleSwitch inUI, Page inOwner) : base(Function.AMDFluidMotionFrameSupported, inUI, inOwner)
        {
        }
    }
}
