using Shared.Enums;
using System.Collections.Generic;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class RefreshRatesProperty : HelperProperty<List<int>, SystemManager>
    {
        public RefreshRatesProperty(List<int> inValue, SystemManager inManager) : base(inValue, null, Function.RefreshRates, inManager)
        {
        }
    }
}
