using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property to trigger HidHide installation.
    /// Write "install" to trigger the installation on the helper side.
    /// </summary>
    internal class InstallHidHideProperty : WidgetProperty<string>
    {
        private readonly Page owner;

        public InstallHidHideProperty(Page inOwner) : base("", null, Function.InstallHidHide)
        {
            owner = inOwner;
        }

        /// <summary>
        /// Triggers HidHide installation via the helper.
        /// </summary>
        public void TriggerInstall()
        {
            Logger.Info("Triggering HidHide installation...");
            SetValue("install");
        }
    }
}
