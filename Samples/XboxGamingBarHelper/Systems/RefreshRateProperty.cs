using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Windows;

namespace XboxGamingBarHelper.Systems
{
    internal class RefreshRateProperty : HelperProperty<int, SystemManager>
    {
        public RefreshRateProperty(int inValue, SystemManager inManager) : base(inValue, null, Function.RefreshRate, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            User32.SetRefreshRateTo(Value);
        }
    }
}
