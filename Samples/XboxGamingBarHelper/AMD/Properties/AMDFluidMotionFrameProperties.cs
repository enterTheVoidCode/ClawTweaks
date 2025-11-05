using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDFluidMotionFrameSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDFluidMotionFrameSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDFluidMotionFrameSupported, inManager)
        {
        }
    }

    internal class AMDFluidMotionFrameEnabledProperty : HelperProperty<bool, AMDManager>
    {
        public AMDFluidMotionFrameEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDFluidMotionFrameEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.AMDFluidMotionFrameSetting.SetEnabled(Value);
        }
    }
}
