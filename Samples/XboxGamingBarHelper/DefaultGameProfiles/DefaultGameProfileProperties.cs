using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.DefaultGameProfiles
{
    /// <summary>
    /// Property indicating whether a default game profile is available for the current game.
    /// </summary>
    internal class DefaultGameProfileAvailableProperty : HelperProperty<bool, DefaultGameProfileManager>
    {
        public DefaultGameProfileAvailableProperty(DefaultGameProfileManager manager)
            : base(false, null, Function.DefaultGameProfileAvailable, manager)
        {
        }
    }

    /// <summary>
    /// Property containing the serialized default game profile data.
    /// Format: XML serialized DefaultGameProfile struct.
    /// </summary>
    internal class DefaultGameProfileDataProperty : HelperProperty<string, DefaultGameProfileManager>
    {
        public DefaultGameProfileDataProperty(DefaultGameProfileManager manager)
            : base("", null, Function.DefaultGameProfileData, manager)
        {
        }
    }

    /// <summary>
    /// Property indicating whether the default profile is currently enabled for this game.
    /// Widget can toggle this to enable/disable applying the default profile.
    /// </summary>
    internal class DefaultGameProfileEnabledProperty : HelperProperty<bool, DefaultGameProfileManager>
    {
        public DefaultGameProfileEnabledProperty(DefaultGameProfileManager manager)
            : base(false, null, Function.DefaultGameProfileEnabled, manager)
        {
        }
    }
}
