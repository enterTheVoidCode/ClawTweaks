using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Helper-side "External Gamepad Mode" property.
    ///
    /// When the widget writes a new value (Quick Settings tile tap), NotifyPropertyChanged calls
    /// ExternalGamepadModeManager.Apply() which dispatches to Program.MSIClaw.cs via OnChanged.
    ///
    /// Default false; NOT loaded from settings — the tile always starts OFF after a helper start.
    /// </summary>
    internal class ExternalGamepadModeProperty : HelperProperty<bool, ExternalGamepadModeManager>
    {
        public ExternalGamepadModeProperty(ExternalGamepadModeManager manager)
            : base(false, null, Function.ExternalGamepadMode, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.Apply(Value);
        }
    }
}
