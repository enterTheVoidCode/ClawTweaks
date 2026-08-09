using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for the gyro hold-boost factor: gyro sensitivity in percent of normal while the boost
    /// button is held. 100 = unchanged, which is also the default, so the feature stays inert until the
    /// user both picks a button and moves this off 100. Range 10-300.
    /// </summary>
    internal class LegionGyroBoostFactorProperty : WidgetSliderProperty
    {
        public LegionGyroBoostFactorProperty(Slider inUI, Page inOwner) : base(100, Function.LegionGyroBoostFactor, inUI, inOwner)
        {
        }
    }
}
