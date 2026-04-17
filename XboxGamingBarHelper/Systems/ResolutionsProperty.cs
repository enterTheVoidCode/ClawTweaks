using Shared.Enums;
using System.Collections.Generic;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class ResolutionsProperty : HelperProperty<List<string>, SystemManager>
    {
        public ResolutionsProperty(List<string> inValue, SystemManager inManager) : base(inValue, null, Function.Resolutions, inManager)
        {
        }
    }
}
