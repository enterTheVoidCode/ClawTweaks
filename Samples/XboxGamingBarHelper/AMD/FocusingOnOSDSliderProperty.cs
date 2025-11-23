using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD
{
    internal class FocusingOnOSDSliderProperty : HelperProperty<bool, AMDManager>
    {
        public FocusingOnOSDSliderProperty(AMDManager inManager) : base(false, null, Function.FocusingOnOSDSlider, inManager)
        {
        }
    }
}
