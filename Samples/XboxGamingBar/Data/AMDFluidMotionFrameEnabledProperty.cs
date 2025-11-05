using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AMDFluidMotionFrameEnabledProperty : WidgetToggleProperty
    {
        public AMDFluidMotionFrameEnabledProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.AMDFluidMotionFrameEnabled, inUI, inOwner)
        {
        }
    }
}
