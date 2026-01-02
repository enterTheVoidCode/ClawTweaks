using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Property to control which TDP method to use.
    /// </summary>
    internal class TdpMethodProperty : HelperProperty<int, SettingsManager>
    {
        public TdpMethodProperty(SettingsManager inManager) : base((int)TdpMethod.PawnIO, null, Function.Settings_TdpMethod, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Logger.Info($"TDP Method changed to {(TdpMethod)Value}");
        }

        /// <summary>
        /// Gets the current TDP method as the enum type
        /// </summary>
        public TdpMethod Method => (TdpMethod)Value;
    }
}
