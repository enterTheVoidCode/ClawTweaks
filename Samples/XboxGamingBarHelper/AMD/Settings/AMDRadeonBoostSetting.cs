using System;

namespace XboxGamingBarHelper.AMD.Settings
{
    internal class AMDRadeonBoostSetting : AMDSetting<IADLX3DBoost>
    {
        public AMDRadeonBoostSetting(IADLX3DBoost setting) : base(setting)
        {
            
        }

        public Tuple<int, int> GetResolutionRange()
        {
            return AMDUtilities.GetIntRangeValue(adlxSetting.GetResolutionRange);
        }

        public int GetResolution()
        {
            return AMDUtilities.GetIntValue(adlxSetting.GetResolution);
        }

        public void SetResolution(int resolution)
        {
            adlxSetting.SetResolution(resolution);
        }
    }
}
