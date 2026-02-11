using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Reusable widget property for GPD Win 5 button remapping.
    /// Sends USB HID keycode to helper for the specified button function.
    /// </summary>
    internal class GPDButtonProperty : WidgetProperty<int>
    {
        private readonly Page owner;

        public GPDButtonProperty(Page inOwner, Function function) : base(0, null, function)
        {
            owner = inOwner;
        }

        /// <summary>
        /// Sets the button keycode and sends to helper.
        /// </summary>
        /// <param name="keycode">USB HID keycode.</param>
        public void SetKeycode(ushort keycode)
        {
            SetValue((int)keycode);
        }
    }
}
