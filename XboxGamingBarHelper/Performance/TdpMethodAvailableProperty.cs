using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Performance
{
    /// <summary>
    /// Property to expose TDP method availability to the widget.
    /// </summary>
    internal class TdpMethodAvailableProperty : HelperProperty<bool, PerformanceManager>
    {
        public TdpMethodAvailableProperty(bool initialValue, Function function, PerformanceManager inManager)
            : base(initialValue, null, function, inManager)
        {
        }

        /// <summary>
        /// Updates the availability value and notifies the widget.
        /// </summary>
        public void SetAvailable(bool available)
        {
            if (Value != available)
            {
                SetValue(available);
            }
        }
    }
}
