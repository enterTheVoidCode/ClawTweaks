using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property to control whether to use manufacturer WMI for TDP instead of RyzenAdj.
    /// </summary>
    internal class UseManufacturerWMIProperty : WidgetToggleProperty
    {
        public UseManufacturerWMIProperty(ToggleSwitch inUI, Page inOwner) : base(true, Function.Settings_UseManufacturerWMI, inUI, inOwner)
        {
        }
    }
}
