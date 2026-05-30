using NLog;
using System;
using System.Linq;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async void FactoryResetButton_Click(object sender, RoutedEventArgs e)
        {
            // Confirmation dialog before wiping everything
            var dialog = new ContentDialog
            {
                Title = "Factory Reset",
                Content = "This will delete ALL profiles, controller settings, TDP presets, custom tiles, Quick Settings and all other user data.\n\nThe app will restart with factory defaults.\n\nThis cannot be undone.",
                PrimaryButtonText = "Reset everything",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            try
            {
                Logger.Info("FactoryReset: starting — clearing all LocalSettings");

                var settings = ApplicationData.Current.LocalSettings;

                // 1. Delete all profile containers (Global, AC, DC, Game_*)
                var containerKeys = settings.Containers.Keys.ToList();
                foreach (var key in containerKeys)
                {
                    settings.DeleteContainer(key);
                    Logger.Info($"FactoryReset: deleted container '{key}'");
                }

                // 2. Clear all flat LocalSettings values (QS_*, Hotkey_*, TdpPresets_*, etc.)
                settings.Values.Clear();
                Logger.Info("FactoryReset: cleared all LocalSettings values");

                // 3. Delete LocalFolder files (logs are kept, only data files cleared)
                try
                {
                    var localFolder = ApplicationData.Current.LocalFolder;
                    var files = await localFolder.GetFilesAsync();
                    foreach (var file in files)
                    {
                        // Keep log files so the user can still diagnose after reset
                        if (file.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                            continue;
                        await file.DeleteAsync();
                        Logger.Info($"FactoryReset: deleted file '{file.Name}'");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"FactoryReset: could not clear local folder files: {ex.Message}");
                }

                Logger.Info("FactoryReset: complete — requesting app restart");

                // Show brief success notice then exit (Xbox Game Bar will relaunch on next open)
                var doneDialog = new ContentDialog
                {
                    Title = "Reset complete",
                    Content = "All settings have been cleared. The app will now close.\n\nReopen Game Bar to start fresh.",
                    CloseButtonText = "Close"
                };
                await doneDialog.ShowAsync();

                // Exit the app — Game Bar will reopen the widget on next invocation
                Application.Current.Exit();
            }
            catch (Exception ex)
            {
                Logger.Error($"FactoryReset: unexpected error: {ex.Message}");

                var errDialog = new ContentDialog
                {
                    Title = "Reset failed",
                    Content = $"An error occurred during reset:\n{ex.Message}",
                    CloseButtonText = "OK"
                };
                await errDialog.ShowAsync();
            }
        }
    }
}
