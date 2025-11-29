using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Windows;

namespace XboxGamingBarHelper.Systems
{
    internal class ResolutionProperty : HelperProperty<string, SystemManager>
    {
        public ResolutionProperty(string inValue, SystemManager inManager) : base(inValue, null, Function.Resolution, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            User32.SetResolutionTo(Value);
        }
    }
}
