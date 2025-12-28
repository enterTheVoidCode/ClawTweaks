using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Gamepad Button Mapping (JSON string).
    /// Stores the mapping configuration for all 24 gamepad buttons.
    /// </summary>
    internal class LegionGamepadMappingProperty : WidgetProperty<string>
    {
        public LegionGamepadMappingProperty() : base("", null, Function.LegionGamepadButtonMapping)
        {
        }
    }
}
