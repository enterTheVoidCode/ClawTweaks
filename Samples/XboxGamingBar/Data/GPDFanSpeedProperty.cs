using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Fan speed property for GPD Win 5.
    /// Widget sends fan speed percentage (0 = auto, 30-100 = manual) to helper.
    /// </summary>
    internal class GPDFanSpeedProperty : WidgetProperty<int>
    {
        private readonly Page owner;

        public GPDFanSpeedProperty(Page inOwner) : base(0, null, Function.GPDFanSpeed)
        {
            owner = inOwner;
        }

        /// <summary>
        /// Sets the fan speed and sends to helper.
        /// </summary>
        /// <param name="percent">Fan speed percentage (0 = auto, 30-100 = manual).</param>
        public void SetSpeed(int percent)
        {
            SetValue(percent);
        }
    }
}
