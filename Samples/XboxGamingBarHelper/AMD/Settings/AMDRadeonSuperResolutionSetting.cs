using System;

namespace XboxGamingBarHelper.AMD.Settings
{
    internal class AMDRadeonSuperResolutionSetting : AMDSetting<IADLX3DRadeonSuperResolution>
    {
        public AMDRadeonSuperResolutionSetting(IADLX3DRadeonSuperResolution setting) : base(setting)
        {
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
