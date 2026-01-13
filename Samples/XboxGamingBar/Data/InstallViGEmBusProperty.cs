using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property to trigger ViGEmBus installation.
    /// Write "install" to trigger the installation on the helper side.
    /// </summary>
    internal class InstallViGEmBusProperty : WidgetProperty<string>
    {
        private readonly Page owner;

        public InstallViGEmBusProperty(Page inOwner) : base("", null, Function.InstallViGEmBus)
        {
            owner = inOwner;
        }

        /// <summary>
        /// Triggers ViGEmBus installation via the helper.
        /// </summary>
        public void TriggerInstall()
        {
            Logger.Info("Triggering ViGEmBus installation...");
            SetValue("install");
        }
    }
}
