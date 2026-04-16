using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Trigger property used by the widget to ask helper to apply staged GPD Win 5 mappings.
    /// </summary>
    internal class GPDApplyMappingsProperty : WidgetProperty<bool>
    {
        public GPDApplyMappingsProperty() : base(false, null, Function.GPDApplyMappings) { }

        public void Trigger()
        {
            SetValue(true);
        }
    }
}
