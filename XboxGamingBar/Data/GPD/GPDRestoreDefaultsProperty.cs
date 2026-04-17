using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Trigger property used by the widget to ask helper to restore GPD Win 5 defaults.
    /// </summary>
    internal class GPDRestoreDefaultsProperty : WidgetProperty<bool>
    {
        public GPDRestoreDefaultsProperty() : base(false, null, Function.GPDRestoreDefaults) { }

        public void Trigger()
        {
            SetValue(true);
        }
    }
}
