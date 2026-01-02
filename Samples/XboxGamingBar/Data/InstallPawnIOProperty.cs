using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property to trigger PawnIO installation.
    /// Write "install" to trigger the installation on the helper side.
    /// </summary>
    internal class InstallPawnIOProperty : WidgetProperty<string>
    {
        private readonly Page owner;

        public InstallPawnIOProperty(Page inOwner) : base("", null, Function.TdpMethod_InstallPawnIO)
        {
            owner = inOwner;
        }

        /// <summary>
        /// Triggers PawnIO installation via the helper.
        /// </summary>
        public void TriggerInstall()
        {
            Logger.Info("Triggering PawnIO installation...");
            SetValue("install");
        }
    }
}
