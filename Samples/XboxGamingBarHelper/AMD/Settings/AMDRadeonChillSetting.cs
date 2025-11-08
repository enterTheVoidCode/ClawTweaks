using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XboxGamingBarHelper.AMD.Settings
{
    internal class AMDRadeonChillSetting : AMDSetting<IADLX3DChill>
    {
        public AMDRadeonChillSetting(IADLX3DChill setting) : base(setting)
        {
            
        }

        public Tuple<int, int> GetFPSRange()
        {
            return AMDUtilities.GetIntRangeValue(adlxSetting.GetFPSRange);
        }

        public int GetMinFPS()
        {
            return AMDUtilities.GetIntValue(adlxSetting.GetMinFPS);
        }

        public int GetMaxFPS()
        {
            return AMDUtilities.GetIntValue(adlxSetting.GetMaxFPS);
        }

        public void SetMinFPS(int minFPS)
        {
            adlxSetting.SetMinFPS(minFPS);
        }

        public void SetMaxFPS(int maxFPS)
        {
            adlxSetting.SetMaxFPS(maxFPS);
        }
    }
}
