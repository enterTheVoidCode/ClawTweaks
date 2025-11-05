using System;

namespace XboxGamingBarHelper.AMD.Settings
{
    internal class AMDRadeonSuperResolutionSetting : AMDSetting<IADLX3DRadeonSuperResolution>
    {
        public AMDRadeonSuperResolutionSetting(IADLX3DRadeonSuperResolution setting) : base(setting)
        {
        }

        public override bool IsSupported()
        {
            return AMDUtilities.GetBoolValue(adlxSetting.IsSupported);
        }

        public override bool IsEnabled()
        {
            return AMDUtilities.GetBoolValue(adlxSetting.IsEnabled);
        }

        public override void SetEnabled(bool enabled)
        {
            adlxSetting.SetEnabled(enabled);
        }

        public Tuple<int, int> GetSharpnessRange()
        {
            return AMDUtilities.GetIntRangeValue(adlxSetting.GetSharpnessRange);
        }

        public int GetSharpness()
        {
            return AMDUtilities.GetIntValue(adlxSetting.GetSharpness);
        }

        public void SetSharpness(int sharpness)
        {
            adlxSetting.SetSharpness(sharpness);
        }
    }
}
