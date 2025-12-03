using Shared.Enums;
using System;
using System.Diagnostics;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class CoreParkingPercentProperty : HelperProperty<int, SystemManager>
    {
        public CoreParkingPercentProperty(SystemManager inManager) : base(100, null, Function.CoreParkingPercent, inManager)
        {
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            var result = base.SetValue(newValue, updatedTime);
            if (result && manager != null && newValue is int percent)
            {
                manager.ApplyCoreParkingPercent(percent);
            }
            return result;
        }
    }
}
