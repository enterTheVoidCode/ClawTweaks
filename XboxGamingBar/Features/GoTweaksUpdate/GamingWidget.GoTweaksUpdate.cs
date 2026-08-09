using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // Historical key name retained — meaning flipped from "Install on
        // start" (auto-install the downloaded msixbundle) to "Check on
        // start" (allow the helper's startup probe to run at all) so the
        // stored preference carries over without a migration.
        private const string GoTweaksCheckOnStartKey = "GoTweaksUpdate_UpdateOnStart";
        private const string GoTweaksHideBannerKey   = "GoTweaksUpdate_HideBanner";

        // Cached latest-release info for the banner. No download URL any more — tapping the banner
        // opens CTW Center, which does the install (see GamingWidget.AppUpdate.cs for the full reason).
        private string _goTweaksLatestVersion;
        private string _goTweaksReleasePageUrl;

        // Set while SyncUpdatePreferenceCheckboxesFromLocalSettings programmatically
        // assigns IsChecked on the five update-preference checkboxes (GoTweaks +
        // Lenovo driver updates). Their Checked/Unchecked handlers early-return
        // when this is true so the init-time restore doesn't write back to
        // LocalSettings or pipe a redundant Set*OnStart message to the helper.
        private bool _isLoadingUpdatePreferenceCheckboxes;

        /// <summary>
        /// Restores the five update-preference checkboxes (two GoTweaks self-update
        /// + three Lenovo driver-update) from LocalSettings during widget init.
        /// The push-driven sync paths (HandleGoTweaksUpdatePush, UpdateDriverUpdatesTile,
        /// RenderDriverUpdateResult) only run when the helper actually delivers a
        /// result, so without this call the XAML defaults win on every cold start
        /// where no update is available — making toggles look unsaved even though
        /// LocalSettings holds the correct value.
        /// </summary>
        private void SyncUpdatePreferenceCheckboxesFromLocalSettings()
        {
            _isLoadingUpdatePreferenceCheckboxes = true;
            try
            {
                if (GoTweaksUpdateOnStartCheckbox != null)
                    GoTweaksUpdateOnStartCheckbox.IsChecked = GoTweaksCheckOnStart;
                if (GoTweaksHideBannerCheckbox != null)
                    GoTweaksHideBannerCheckbox.IsChecked = GoTweaksHideBanner;
                if (DriverUpdatesUpdateOnStartCheckbox != null)
                    DriverUpdatesUpdateOnStartCheckbox.IsChecked = DriverUpdatesCheckOnStart;
                if (DriverUpdatesModdedWifiCheckbox != null)
                    DriverUpdatesModdedWifiCheckbox.IsChecked = DriverUpdatesUseModdedWifi;
            }
            finally
            {
                _isLoadingUpdatePreferenceCheckboxes = false;
            }
        }

        private bool GoTweaksCheckOnStart
        {
            // Default true: users who haven't opted out expect the banner
            // to appear on launch when an update exists.
            get => GetBoolSetting(GoTweaksCheckOnStartKey, true);
            set => SetBoolSetting(GoTweaksCheckOnStartKey, value);
        }
        private bool GoTweaksHideBanner
        {
            get => GetBoolSetting(GoTweaksHideBannerKey, false);
            set => SetBoolSetting(GoTweaksHideBannerKey, value);
        }

        private async void GoTweaksUpdateOnStartCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUpdatePreferenceCheckboxes) return;
            if (GoTweaksUpdateOnStartCheckbox == null) return;
            bool on = GoTweaksUpdateOnStartCheckbox.IsChecked == true;
            GoTweaksCheckOnStart = on;
            // Forward to helper so its next startup honours the toggle. Mirrors
            // the DriverCheckOnStart path; helper persists it via
            // LocalSettingsHelper and reads synchronously before scheduling
            // its GitHub probe.
            try
            {
                if (App.IsConnected)
                {
                    var req = new ValueSet();
                    req.Add("SetGoTweaksCheckOnStart", on);
                    await App.SendMessageAsync(req);
                }
            }
            catch (Exception ex) { Logger.Warn($"SetGoTweaksCheckOnStart forward failed: {ex.Message}"); }
        }

        private void GoTweaksHideBannerCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUpdatePreferenceCheckboxes) return;
            if (GoTweaksHideBannerCheckbox == null) return;
            GoTweaksHideBanner = GoTweaksHideBannerCheckbox.IsChecked == true;
            if (QuickGoTweaksUpdateTile != null && GoTweaksHideBanner)
            {
                QuickGoTweaksUpdateTile.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Called from the pipe-message handler when the helper pushes a
        /// startup or on-demand self-update result. Keeps the Quick-tab
        /// banner in sync and leaves the System tab's existing "Check for
        /// Update" flow unchanged — it has its own manual fetch and
        /// UpdateStatusText/UpdateButton for install.
        /// </summary>
        internal async void HandleGoTweaksUpdatePush(string payload)
        {
            try
            {
                if (!JsonObject.TryParse(payload, out var root)) return;

                bool isUpdate = root.TryGetValue("isUpdateAvailable", out var uv)
                                && uv.ValueType == JsonValueType.Boolean && uv.GetBoolean();
                string latest = JsonString(root, "latestVersion");
                string pageUrl = JsonString(root, "releasePageUrl");

                _goTweaksLatestVersion = latest;
                _goTweaksReleasePageUrl = pageUrl;

                bool hideBanner = GoTweaksHideBanner;
                bool checkOnStart = GoTweaksCheckOnStart;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (GoTweaksUpdateOnStartCheckbox != null &&
                        GoTweaksUpdateOnStartCheckbox.IsChecked != checkOnStart)
                        GoTweaksUpdateOnStartCheckbox.IsChecked = checkOnStart;
                    if (GoTweaksHideBannerCheckbox != null &&
                        GoTweaksHideBannerCheckbox.IsChecked != hideBanner)
                        GoTweaksHideBannerCheckbox.IsChecked = hideBanner;

                    // No longer gated on a download URL — there isn't one. A newer stable release is
                    // enough to show the notice; the tile opens Center rather than installing.
                    bool showBanner = isUpdate && !hideBanner;
                    if (QuickGoTweaksUpdateTile != null)
                        QuickGoTweaksUpdateTile.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
                    if (QuickGoTweaksTitleText != null && isUpdate)
                        QuickGoTweaksTitleText.Text = $"ClawTweaks {latest} available";
                    if (QuickGoTweaksSubtitleText != null)
                        QuickGoTweaksSubtitleText.Text = "Tap to open CTW Center";
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"HandleGoTweaksUpdatePush failed: {ex.Message}");
            }
        }

        private static string JsonString(JsonObject obj, string key)
        {
            if (obj.TryGetValue(key, out var v) && v.ValueType == JsonValueType.String)
                return v.GetString();
            return "";
        }

        /// <summary>
        /// Opens CTW Center, which is where updates are installed \u2014 or, if Center isn't installed,
        /// offers the download page. This used to send an "InstallGoTweaksUpdate" pipe message that had
        /// the helper download the package and get it launched; that whole path is deleted. See
        /// OpenCenterOrOfferInstallAsync in GamingWidget.AppUpdate.cs for both the flow and the reason.
        /// </summary>
        private async void QuickGoTweaksUpdateTile_Click(object sender, RoutedEventArgs e)
        {
            await OpenCenterOrOfferInstallAsync();
        }
    }
}
