using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    /// <summary>
    /// In-app update UI (Onboarding tab, top collapsible section). Lists the latest and previous
    /// GitHub release as cards — each with its changelog and an install button — so the user can
    /// jump forward to the newest build or roll back to the prior one.
    ///
    /// The heavy lifting lives in the signed, elevated helper: it fetches the releases feed
    /// (Function.ListAppReleases) and installs a chosen .msixbundle via the WinRT PackageManager
    /// (Function.InstallAppRelease) — no PowerShell, no Process.Start, no UAC. This file only
    /// renders the result and forwards the chosen download URL.
    /// </summary>
    public sealed partial class GamingWidget
    {
        private static readonly SolidColorBrush AppUpdCardBrush   = new SolidColorBrush(Color.FromArgb(255, 26, 26, 46));   // #1A1A2E
        private static readonly SolidColorBrush AppUpdTitleBrush  = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush AppUpdSubBrush    = new SolidColorBrush(Color.FromArgb(255, 136, 136, 136)); // #888888
        private static readonly SolidColorBrush AppUpdBodyBrush   = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)); // #C8C8C8
        private static readonly SolidColorBrush AppUpdAccentBrush = new SolidColorBrush(Color.FromArgb(255, 91, 141, 239));  // #5B8DEF

        private bool _appReleasesLoaded;
        private bool _appReleasesLoading;

        // Lazy-load the releases the first time the section is expanded.
        private void AppUpdateExpander_Expanding(object sender, object e)
        {
            UpdateAppUpdateCurrentVersion();
            if (!_appReleasesLoaded && !_appReleasesLoading)
            {
                _ = LoadAppReleasesAsync();
            }
        }

        private void AppUpdateRefresh_Click(object sender, RoutedEventArgs e)
        {
            _appReleasesLoaded = false;
            _ = LoadAppReleasesAsync();
        }

        private void UpdateAppUpdateCurrentVersion()
        {
            if (AppUpdateCurrentVersionText == null) return;
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                AppUpdateCurrentVersionText.Text = $"Installed version: {v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            catch
            {
                AppUpdateCurrentVersionText.Text = "Installed version: unknown";
            }
        }

        private async Task LoadAppReleasesAsync()
        {
            if (_appReleasesLoading) return;
            _appReleasesLoading = true;

            ShowAppUpdateProgress(true, "Loading releases from GitHub…");
            if (AppUpdateRefreshBtn != null) AppUpdateRefreshBtn.IsEnabled = false;
            AppReleasesContainer?.Children.Clear();

            try
            {
                if (!App.IsConnected)
                {
                    ShowAppUpdateProgress(true, "Helper not connected — open ClawTweaks once so the helper starts, then retry.");
                    return;
                }

                // Get-query the helper for the latest releases (Content = JSON array).
                await appReleases.Sync();
                string json = appReleases?.Value ?? "[]";

                if (!JsonArray.TryParse(json, out var arr) || arr.Count == 0)
                {
                    ShowAppUpdateProgress(true, "Couldn't load releases right now. Check your connection and tap 'Reload releases'.");
                    return;
                }

                ShowAppUpdateProgress(false, "");

                // No downgrades: only surface releases whose version is >= the installed one.
                // To test the updater, publish an (experimental) release with a higher version.
                int[] installed = GetInstalledVersionParts();
                int shown = 0;
                for (uint i = 0; i < arr.Count; i++)
                {
                    var rel = arr.GetObjectAt(i);
                    string relVer = JsonStr(rel, "version");
                    if (string.IsNullOrWhiteSpace(relVer)) relVer = JsonStr(rel, "tag");
                    if (CompareVerParts(ParseVerParts(relVer), installed) <= 0)
                        continue; // older than OR same as installed → hidden (no downgrades, no reinstall of current)
                    BuildReleaseCard(rel, shown == 0);
                    shown++;
                }

                if (shown == 0)
                {
                    ShowAppUpdateProgress(true,
                        $"You're on the latest version ({installed[0]}.{installed[1]}.{installed[2]}.{installed[3]}). Newer releases appear here automatically.");
                }
                _appReleasesLoaded = true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"LoadAppReleasesAsync failed: {ex.Message}");
                ShowAppUpdateProgress(true, "Couldn't load releases right now. Tap 'Reload releases' to try again.");
            }
            finally
            {
                _appReleasesLoading = false;
                if (AppUpdateRefreshBtn != null) AppUpdateRefreshBtn.IsEnabled = true;
            }
        }

        private void ShowAppUpdateProgress(bool show, string text)
        {
            if (AppUpdateProgressPanel != null)
                AppUpdateProgressPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (AppUpdateSpinner != null)
            {
                bool spin = show && !string.IsNullOrEmpty(text) && text.EndsWith("…");
                AppUpdateSpinner.IsActive = spin;
                AppUpdateSpinner.Visibility = spin ? Visibility.Visible : Visibility.Collapsed;
            }
            if (AppUpdateStatusText != null) AppUpdateStatusText.Text = text ?? "";
        }

        private static string JsonStr(JsonObject obj, string key)
        {
            if (obj != null && obj.TryGetValue(key, out var v) && v.ValueType == JsonValueType.String)
                return v.GetString();
            return "";
        }

        private void BuildReleaseCard(JsonObject rel, bool isLatest)
        {
            if (AppReleasesContainer == null || rel == null) return;

            string version = JsonStr(rel, "version");
            if (string.IsNullOrWhiteSpace(version)) version = JsonStr(rel, "tag");
            string name = JsonStr(rel, "name");
            string body = JsonStr(rel, "body");
            string published = FormatDate(JsonStr(rel, "publishedAt"));
            string downloadUrl = JsonStr(rel, "downloadUrl");
            string pageUrl = JsonStr(rel, "releasePageUrl");
            bool prerelease = rel.TryGetValue("isPrerelease", out var pv)
                              && pv.ValueType == JsonValueType.Boolean && pv.GetBoolean();

            var panel = new StackPanel();

            // Experimental (GitHub pre-release) builds are clearly flagged as test builds.
            if (prerelease)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "EXPERIMENTAL BUILD",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = OnbAmberBrush,
                    Margin = new Thickness(0, 0, 0, 2),
                });
            }

            // Header: "Version 0.1.5  ·  latest" + date
            string tag = isLatest ? "  ·  latest" : "";
            var header = new TextBlock
            {
                Text = $"Version {version}{tag}",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = AppUpdTitleBrush,
                TextWrapping = TextWrapping.Wrap,
            };
            panel.Children.Add(header);

            if (!string.IsNullOrWhiteSpace(published))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = published,
                    FontSize = 10,
                    Foreground = AppUpdSubBrush,
                    Margin = new Thickness(0, 1, 0, 0),
                });
            }

            // What's new
            panel.Children.Add(new TextBlock
            {
                Text = "What's new",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = AppUpdAccentBrush,
                Margin = new Thickness(0, 8, 0, 2),
            });

            var bodyText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(body) ? "No release notes." : body,
                FontSize = 11,
                Foreground = AppUpdBodyBrush,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            var bodyScroll = new ScrollViewer
            {
                Content = bodyText,
                MaxHeight = 160,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            panel.Children.Add(bodyScroll);

            // Install button (or a note + link when no msixbundle asset is attached)
            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                var btn = new Button
                {
                    Content = prerelease ? "Download & install (experimental)" : "Download & install",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    FontSize = 12,
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 8, 0, 0),
                };
                TryApplyModernButtonStyle(btn);
                string urlCapture = downloadUrl;
                string verCapture = version;
                btn.Click += (s, e) => OnInstallReleaseClick(btn, verCapture, urlCapture);
                panel.Children.Add(btn);
            }
            else
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "No installable package attached to this release.",
                    FontSize = 10,
                    Foreground = AppUpdSubBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0),
                });
                if (!string.IsNullOrWhiteSpace(pageUrl))
                {
                    var link = new HyperlinkButton
                    {
                        Content = "Open release page",
                        NavigateUri = SafeUri(pageUrl),
                        FontSize = 11,
                        Padding = new Thickness(0, 2, 0, 0),
                    };
                    panel.Children.Add(link);
                }
            }

            var card = new Border
            {
                Background = AppUpdCardBrush,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                Child = panel,
            };
            AppReleasesContainer.Children.Add(card);
        }

        private void OnInstallReleaseClick(Button btn, string version, string downloadUrl)
        {
            try
            {
                if (!App.IsConnected)
                {
                    ShowAppUpdateProgress(true, "Helper not connected — can't install right now.");
                    return;
                }
                if (btn != null)
                {
                    btn.Content = "Installing…";
                    btn.IsEnabled = false;
                }
                ShowAppUpdateProgress(true,
                    $"Starting download of version {version}… ClawTweaks will close and reload when the install finishes (the package is ~80 MB, so this can take several minutes on a slow connection).");
                Logger.Info($"AppUpdate: installing release {version} from {downloadUrl}");
                installAppRelease?.Trigger(downloadUrl);
                _ = PollInstallStatusAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn($"OnInstallReleaseClick failed: {ex.Message}");
                if (btn != null) { btn.Content = "Download & install this version"; btn.IsEnabled = true; }
                ShowAppUpdateProgress(true, $"Install failed to start: {ex.Message}");
            }
        }

        // Polls the helper for live install progress (download %, phase) so the card shows
        // "Downloading 45%…" instead of a static "Installing…". The widget is force-closed by the
        // package install once it reaches the AddPackage step, so this loop simply ends there.
        private bool _installPolling;
        private async Task PollInstallStatusAsync()
        {
            if (_installPolling) return;
            _installPolling = true;
            try
            {
                for (int i = 0; i < 600; i++) // ~20 min ceiling at 2s cadence
                {
                    await Task.Delay(2000);
                    if (!App.IsConnected) continue;
                    try { await appInstallStatus.Sync(); } catch { continue; }

                    string json = appInstallStatus?.Value ?? "";
                    if (string.IsNullOrEmpty(json) || !JsonObject.TryParse(json, out var o)) continue;

                    string phase = JsonStr(o, "phase");
                    int percent = (int)o.GetNamedNumber("percent", -1);
                    string msg = JsonStr(o, "message");

                    switch (phase)
                    {
                        case "downloading":
                            ShowAppUpdateProgress(true, percent >= 0
                                ? $"Downloading update… {percent}%"
                                : "Downloading update…");
                            break;
                        case "installing":
                            ShowAppUpdateProgress(true, "Installing the package… ClawTweaks will close and reload shortly.");
                            break;
                        case "done":
                            ShowAppUpdateProgress(true, "Update installed — reloading…");
                            return;
                        case "failed":
                            ShowAppUpdateProgress(true, string.IsNullOrWhiteSpace(msg) ? "Install failed." : $"Install failed: {msg}");
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"PollInstallStatusAsync failed: {ex.Message}");
            }
            finally { _installPolling = false; }
        }

        // Installed package version as [Major, Minor, Build, Revision].
        private static int[] GetInstalledVersionParts()
        {
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return new int[] { v.Major, v.Minor, v.Build, v.Revision };
            }
            catch { return new int[] { 0, 0, 0, 0 }; }
        }

        // Parse a version/tag string ("v0.1.5", "0.1.5.412", "v.0.1.3") into 4 numeric parts.
        // Non-digit/garbage segments degrade to 0; missing trailing parts are 0.
        private static int[] ParseVerParts(string s)
        {
            var r = new int[4];
            if (string.IsNullOrWhiteSpace(s)) return r;
            int start = 0;
            while (start < s.Length && (s[start] == 'v' || s[start] == 'V' || s[start] == '.' || s[start] == ' ')) start++;
            var parts = s.Substring(start).Split('.');
            for (int i = 0; i < 4 && i < parts.Length; i++)
            {
                int val = 0;
                foreach (char c in parts[i])
                {
                    if (c < '0' || c > '9') break;
                    val = val * 10 + (c - '0');
                }
                r[i] = val;
            }
            return r;
        }

        private static int CompareVerParts(int[] a, int[] b)
        {
            for (int i = 0; i < 4; i++)
                if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
            return 0;
        }

        private void TryApplyModernButtonStyle(Button btn)
        {
            try
            {
                if (Application.Current.Resources.TryGetValue("ModernButtonStyle", out var styleObj)
                    && styleObj is Style style)
                {
                    btn.Style = style;
                }
            }
            catch { /* default style is fine */ }
        }

        private static Uri SafeUri(string url)
        {
            try { return new Uri(url); }
            catch { return null; }
        }

        // GitHub timestamps are ISO-8601 ("2026-06-10T18:22:00Z"). Show just the date.
        private static string FormatDate(string iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return "";
            if (DateTimeOffset.TryParse(iso, out var dt))
                return dt.ToLocalTime().ToString("d MMM yyyy");
            return iso;
        }
    }
}
