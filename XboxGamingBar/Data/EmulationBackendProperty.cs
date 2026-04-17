using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget property for controller emulation backend selection.
    /// Backed by a ToggleSwitch: Off = Legacy ViGEm, On = VIIPER.
    /// </summary>
    internal class EmulationBackendProperty : WidgetToggleProperty
    {
        public EmulationBackendProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.Settings_EmulationBackend, inUI, inOwner)
        {
        }

        /// <summary>
        /// Convenience: translate the bool to/from the enum.
        /// </summary>
        public EmulationBackend Backend
        {
            get => Value ? EmulationBackend.Viiper : EmulationBackend.Legacy;
        }
    }
}
