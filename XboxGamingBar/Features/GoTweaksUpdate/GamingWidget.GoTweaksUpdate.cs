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
        private const string GoTweaksUpdateOnStartKey = "GoTweaksUpdate_UpdateOnStart";
        private const string GoTweaksHideBannerKey    = "GoTweaksUpdate_HideBanner";

        // Cached latest update payload so the Install button + banner can
        // act without re-asking the helper (helper also caches, but this
        // saves a pipe round-trip).
        private string _goTweaksLatestVersion;
        private string _goTweaksDownloadUrl;
        private string _goTweaksReleasePageUrl;
        private bool _goTweaksAutoUpdateFiredThisSession;

        private bool GoTweaksUpdateOnStart
        {
            get => GetBoolSetting(GoTweaksUpdateOnStartKey, false);
            set => SetBoolSetting(GoTweaksUpdateOnStartKey, value);
        }
        private bool GoTweaksHideBanner
        {
            get => GetBoolSetting(GoTweaksHideBannerKey, false);
            set => SetBoolSetting(GoTweaksHideBannerKey, value);
        }

        private void GoTweaksUpdateOnStartCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (GoTweaksUpdateOnStartCheckbox == null) return;
            GoTweaksUpdateOnStart = GoTweaksUpdateOnStartCheckbox.IsChecked == true;
        }

        private void GoTweaksHideBannerCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (GoTweaksHideBannerCheckbox == null) return;
            GoTweaksHideBanner = GoTweaksHideBannerCheckbox.IsChecked == true;
            if (QuickGoTweaksUpdateTile != null && GoTweaksHideBanner)
            {
                QuickGoTweaksUpdateTile.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Entry point from the pipe handler when helper pushes
        /// GoTweaksUpdate payload (startup probe + explicit check). Parses
        /// the JSON blob, caches DownloadUrl, updates the Quick-tab tile
        /// and the GoTweaks card, auto-installs if the user opted in.
        /// </summary>
        internal async void HandleGoTweaksUpdatePush(string payload)
        {
            try
            {
                if (!JsonObject.TryParse(payload, out var root)) return;

                bool isUpdate = root.TryGetValue("isUpdateAvailable", out var uv)
                                && uv.ValueType == JsonValueType.Boolean && uv.GetBoolean();
                string current = JsonString(root, "currentVersion");
                string latest = JsonString(root, "latestVersion");
                string url = JsonString(root, "downloadUrl");
                string pageUrl = JsonString(root, "releasePageUrl");
                string releaseName = JsonString(root, "releaseName");

                _goTweaksLatestVersion = latest;
                _goTweaksDownloadUrl = url;
                _goTweaksReleasePageUrl = pageUrl;

                bool hideBanner = GoTweaksHideBanner;
                bool updateOnStart = GoTweaksUpdateOnStart;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (GoTweaksUpdateOnStartCheckbox != null &&
                        GoTweaksUpdateOnStartCheckbox.IsChecked != updateOnStart)
                        GoTweaksUpdateOnStartCheckbox.IsChecked = updateOnStart;
                    if (GoTweaksHideBannerCheckbox != null &&
                        GoTweaksHideBannerCheckbox.IsChecked != hideBanner)
                        GoTweaksHideBannerCheckbox.IsChecked = hideBanner;

                    if (GoTweaksInstalledVersionText != null)
                        GoTweaksInstalledVersionText.Text = string.IsNullOrWhiteSpace(current) ? "—" : current;
                    if (GoTweaksLatestVersionText != null)
                        GoTweaksLatestVersionText.Text = string.IsNullOrWhiteSpace(latest) ? "—" : latest;

                    if (GoTweaksStatusText != null)
                    {
                        GoTweaksStatusText.Text = isUpdate
                            ? (string.IsNullOrWhiteSpace(url)
                                ? "Update available, but no msixbundle asset was published for this release."
                                : $"{releaseName ?? latest} is available.")
                            : "GoTweaks is up to date.";
                    }

                    // Banner: only when update exists, has a usable download URL, and user hasn't hidden it.
                    bool showBanner = isUpdate && !string.IsNullOrWhiteSpace(url) && !hideBanner;
                    if (QuickGoTweaksUpdateTile != null)
                        QuickGoTweaksUpdateTile.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
                    if (QuickGoTweaksTitleText != null && isUpdate)
                        QuickGoTweaksTitleText.Text = $"GoTweaks {latest} available";
                    if (QuickGoTweaksSubtitleText != null)
                        QuickGoTweaksSubtitleText.Text = "Tap to install";

                    // Install button in the GoTweaks card.
                    if (GoTweaksInstallButton != null)
                        GoTweaksInstallButton.Visibility = (isUpdate && !string.IsNullOrWhiteSpace(url))
                            ? Visibility.Visible : Visibility.Collapsed;
                });

                // Auto-install when opted in. One-shot per session.
                if (isUpdate && updateOnStart && !string.IsNullOrWhiteSpace(url)
                    && !_goTweaksAutoUpdateFiredThisSession)
                {
                    _goTweaksAutoUpdateFiredThisSession = true;
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
                    {
                        await TriggerGoTweaksInstallAsync();
                    });
                }
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
        /// Manual check button click — sends a pipe request with
        /// ForceRefresh=true so the helper skips its cache and hits GitHub.
        /// Same render path as the unsolicited push.
        /// </summary>
        private async void GoTweaksCheckButton_Click(object sender, RoutedEventArgs e)
        {
            if (GoTweaksCheckButton == null) return;
            string original = GoTweaksCheckButton.Content?.ToString() ?? "Check for update";
            GoTweaksCheckButton.IsEnabled = false;
            GoTweaksCheckButton.Content = "Checking\u2026";
            try
            {
                if (!App.IsConnected)
                {
                    if (GoTweaksStatusText != null)
                        GoTweaksStatusText.Text = "Helper not connected.";
                    return;
                }
                var request = new ValueSet();
                request.Add("CheckGoTweaksUpdate", true);
                request.Add("ForceRefresh", true);
                var response = await App.SendMessageAsync(request);
                if (response != null && response.TryGetValue("GoTweaksUpdate", out var payloadObj)
                    && payloadObj is string payload)
                {
                    HandleGoTweaksUpdatePush(payload);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"GoTweaksCheckButton_Click failed: {ex.Message}");
                if (GoTweaksStatusText != null)
                    GoTweaksStatusText.Text = $"Check failed: {ex.Message}";
            }
            finally
            {
                GoTweaksCheckButton.Content = original;
                GoTweaksCheckButton.IsEnabled = true;
            }
        }

        private async void GoTweaksInstallButton_Click(object sender, RoutedEventArgs e)
        {
            await TriggerGoTweaksInstallAsync();
        }

        private async void QuickGoTweaksUpdateTile_Click(object sender, RoutedEventArgs e)
        {
            await TriggerGoTweaksInstallAsync();
        }

        /// <summary>
        /// Sends the install-url pipe message. Helper downloads the
        /// signed .msixbundle and runs Add-AppxPackage via PowerShell. The
        /// widget updates its own status label during the round-trip.
        /// </summary>
        private async Task TriggerGoTweaksInstallAsync()
        {
            if (string.IsNullOrWhiteSpace(_goTweaksDownloadUrl))
            {
                if (GoTweaksStatusText != null)
                    GoTweaksStatusText.Text = "No download URL cached — click Check for update first.";
                return;
            }
            if (GoTweaksInstallButton != null) GoTweaksInstallButton.IsEnabled = false;
            if (GoTweaksStatusText != null) GoTweaksStatusText.Text = "Downloading GoTweaks update\u2026";
            try
            {
                if (!App.IsConnected)
                {
                    if (GoTweaksStatusText != null) GoTweaksStatusText.Text = "Helper not connected.";
                    return;
                }
                var request = new ValueSet();
                request.Add("InstallGoTweaksUpdate", _goTweaksDownloadUrl);
                var response = await App.SendMessageAsync(request);
                string message = "Install started.";
                if (response != null && response.TryGetValue("GoTweaksUpdateInstallResult", out var payloadObj)
                    && payloadObj is string payload)
                {
                    if (JsonObject.TryParse(payload, out var root))
                    {
                        string msg = JsonString(root, "message");
                        if (!string.IsNullOrWhiteSpace(msg)) message = msg;
                    }
                }
                if (GoTweaksStatusText != null) GoTweaksStatusText.Text = message;
            }
            catch (Exception ex)
            {
                Logger.Warn($"TriggerGoTweaksInstallAsync failed: {ex.Message}");
                if (GoTweaksStatusText != null) GoTweaksStatusText.Text = $"Install failed: {ex.Message}";
            }
            finally
            {
                if (GoTweaksInstallButton != null) GoTweaksInstallButton.IsEnabled = true;
            }
        }
    }
}
