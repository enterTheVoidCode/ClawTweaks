using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Windows;

namespace XboxGamingBarHelper.Systems
{
    /// <summary>
    /// Property for display orientation.
    /// Values: 0=Landscape, 1=Portrait (90°), 2=Landscape flipped (180°), 3=Portrait flipped (270°)
    /// </summary>
    internal class DisplayOrientationProperty : HelperProperty<int, SystemManager>
    {
        public DisplayOrientationProperty(int inValue, SystemManager inManager) : base(inValue, null, Function.DisplayOrientation, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            User32.SetDisplayOrientation(Value);
        }
    }
}
