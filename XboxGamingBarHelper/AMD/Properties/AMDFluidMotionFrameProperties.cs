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

            // Notify the listener to start cooldown before we make the change
            // This prevents the listener from reading stale values when the driver callback fires
            Manager.AMD3DSettingsChangedListener?.NotifyAFMFChanged();

            Manager.AMDFluidMotionFrameSetting.SetEnabled(Value);
        }
    }

    internal class AMDFluidMotionFrameV1SupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDFluidMotionFrameV1SupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDFluidMotionFrameV1Supported, inManager)
        {
        }
    }

    internal class AMDFluidMotionFrameAlgorithmProperty : HelperProperty<int, AMDManager>
    {
        public AMDFluidMotionFrameAlgorithmProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDFluidMotionFrameAlgorithm, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.AMD3DSettingsChangedListener?.NotifyAFMFChanged();
            Manager.AMDFluidMotionFrameSettingV1?.SetAlgorithm((ADLX_AFMF_ALGORITHM)Value);
        }
    }

    internal class AMDFluidMotionFrameSearchModeProperty : HelperProperty<int, AMDManager>
    {
        public AMDFluidMotionFrameSearchModeProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDFluidMotionFrameSearchMode, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.AMD3DSettingsChangedListener?.NotifyAFMFChanged();
            Manager.AMDFluidMotionFrameSettingV1?.SetSearchMode((ADLX_AFMF_SEARCH_MODE_TYPE)Value);
        }
    }

    internal class AMDFluidMotionFramePerformanceModeProperty : HelperProperty<int, AMDManager>
    {
        public AMDFluidMotionFramePerformanceModeProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDFluidMotionFramePerformanceMode, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.AMD3DSettingsChangedListener?.NotifyAFMFChanged();
            Manager.AMDFluidMotionFrameSettingV1?.SetPerformanceMode((ADLX_AFMF_PERFORMANCE_MODE_TYPE)Value);
        }
    }

    internal class AMDFluidMotionFrameFastMotionResponseProperty : HelperProperty<int, AMDManager>
    {
        public AMDFluidMotionFrameFastMotionResponseProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDFluidMotionFrameFastMotionResponse, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.AMD3DSettingsChangedListener?.NotifyAFMFChanged();
            Manager.AMDFluidMotionFrameSettingV1?.SetFastMotionResponse((ADLX_AFMF_FAST_MOTION_RESP)Value);
        }
    }
}
