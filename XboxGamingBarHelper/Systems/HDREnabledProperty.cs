using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Windows;

namespace XboxGamingBarHelper.Systems
{
    internal class HDREnabledProperty : HelperProperty<bool, SystemManager>
    {
        public HDREnabledProperty(bool inValue, SystemManager inManager) : base(inValue, null, Function.HDREnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            User32.SetHDREnabled(Value);
            Manager?.OnHdrEnabledChanged(Value);
        }
    }
}
