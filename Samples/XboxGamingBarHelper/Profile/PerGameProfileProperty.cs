using Shared.Data;
using Shared.Enums;
using System.Runtime.CompilerServices;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Profile
{
    internal class PerGameProfileProperty : HelperProperty<bool, ProfileManager>
    {
        public PerGameProfileProperty(IProperty inParentProperty, ProfileManager inManager) : base(false, inParentProperty, Function.PerGameProfile, inManager)
        {
        }

        protected override void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            //Logger.Info($"Use {(Value ? "per-game" : "global")} profile.");
        }
    }
}
