using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class LosslessScalingAnime4KVRSProperty : WidgetToggleProperty
    {
        public LosslessScalingAnime4KVRSProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.LosslessScalingAnime4KVRS, inUI, inOwner)
        {
        }
    }
}
