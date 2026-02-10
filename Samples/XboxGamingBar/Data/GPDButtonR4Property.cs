using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// R4 back paddle property for GPD Win 5.
    /// Widget sends keycode to helper for remapping.
    /// </summary>
    internal class GPDButtonR4Property : WidgetProperty<int>
    {
        private readonly Page owner;

        public GPDButtonR4Property(Page inOwner) : base(0, null, Function.GPDButtonR4)
        {
            owner = inOwner;
        }

        /// <summary>
        /// Sets the R4 paddle keycode and sends to helper.
        /// </summary>
        /// <param name="keycode">USB HID keycode.</param>
        public void SetKeycode(ushort keycode)
        {
            SetValue((int)keycode);
        }
    }
}
