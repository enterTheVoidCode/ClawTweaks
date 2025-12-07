using System;

namespace XboxGamingBarHelper.AMD.Settings
{
    internal class AMDImageSharpeningSetting : AMDSetting<IADLX3DImageSharpening>
    {
        public AMDImageSharpeningSetting(IADLX3DImageSharpening setting) : base(setting)
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
