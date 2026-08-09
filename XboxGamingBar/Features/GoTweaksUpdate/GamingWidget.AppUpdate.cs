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
    /// "What's new" UI (Onboarding tab, top collapsible section). Lists recent STABLE releases as
    /// cards with their changelog, and a badge on the Setup tab when a newer stable build exists.
    ///
    /// ── Information only ─────────────────────────────────────────────────────────────────────────
    /// There used to be a "Download &amp; install" button per card, which had the helper fetch the
    /// release asset and get the package launched. That path is deleted end to end (widget, pipe verbs,
    /// helper service). Downloading a payload and arranging for it to run is what antivirus heuristics
    /// score, and this project has been bitten by exactly that before. Installing is CTW Center's job —
    /// the cards now carry a button that opens Center via the existing "@ClawTweaksCenter" launch path.
    ///
    /// Prereleases never reach here: the helper filters them out (see GoTweaksUpdateService), so an
    /// experimental tag can no longer turn into an update prompt for every user.
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

        // Throttled "is a newer release available?" probe that drives the green dot on the Setup tab.
        // It runs only when the user opens Game Bar and navigates (from NavRadioButton_Checked) — never
        // at helper start — and at most once per AppUpdateCheckInterval (timestamp persisted to survive
        // restarts). No auto-download, no banner; it just flips the badge.
        private const string LastAppUpdateCheckKey = "AppUpdate_LastCheckUtc";
        // Releases are infrequent, so a 12h throttle is plenty — the update LIST (expander) always
        // fetches fresh; this only paces the background "is a newer build out?" badge probe.
        private static readonly TimeSpan AppUpdateCheckInterval = TimeSpan.FromHours(12);
        private bool _appUpdateCheckInFlight;
        private bool _appUpdateAvailable;

        /// <summary>
        /// Lightweight, throttled update-availability check. Compares the newest GitHub release against
        /// the installed version and shows the green Setup-tab badge when a newer build exists. Called
        /// from tab navigation so it never fires on helper start; rate-limited by a persisted timestamp.
        /// </summary>
        internal async Task MaybeCheckForAppUpdateAsync()
        {
            if (_appUpdateCheckInFlight || !App.IsConnected) return;

            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (ls.Values.TryGetValue(LastAppUpdateCheckKey, out var lastObj) && lastObj is string lastStr
                    && DateTime.TryParse(lastStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last)
                    && DateTime.UtcNow - last < AppUpdateCheckInterval)
                {
                    // Checked recently — just re-assert the cached result so the badge survives nav rebuilds.
                    double remainingSec = (AppUpdateCheckInterval - (DateTime.UtcNow - last)).TotalSeconds;
                    Logger.Info($"App update check skipped (throttled) — last={last:o}, next due in ~{remainingSec:F0}s, cachedUpdateAvailable={_appUpdateAvailable}");
                    SetUpdateAvailableBadge(_appUpdateAvailable);
                    return;
                }
            }
            catch { }

            _appUpdateCheckInFlight = true;
            try
            {
                await appReleases.Sync();
                string json = appReleases?.Value ?? "[]";
                bool available = false;
                if (JsonArray.TryParse(json, out var arr) && arr.Count > 0)
                {
                    int[] installed = GetInstalledVersionParts();
                    for (uint i = 0; i < arr.Count; i++)
                    {
                        var rel = arr.GetObjectAt(i);
                        string relVer = JsonStr(rel, "version");
                        if (string.IsNullOrWhiteSpace(relVer)) relVer = JsonStr(rel, "tag");
                        if (CompareVerParts(ParseVerParts(relVer), installed) > 0) { available = true; break; }
                    }
                }
                try { Windows.Storage.ApplicationData.Current.LocalSettings.Values[LastAppUpdateCheckKey] = DateTime.UtcNow.ToString("o"); } catch { }
                SetUpdateAvailableBadge(available);
                Logger.Info($"App update check (nav-triggered): updateAvailable={available}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"MaybeCheckForAppUpdateAsync failed: {ex.Message}");
            }
            finally
            {
                _appUpdateCheckInFlight = false;
            }
        }

        /// <summary>
        /// The single entry point behind every "open CTW Center" button in the widget. If Center is
        /// installed it is launched (helper-side, via the "@ClawTweaksCenter" token). If it is NOT, the
        /// user gets an explanation and — on confirmation — the download page in their browser.
        ///
        /// Without this the button was a silent dead end: the helper's LaunchClawTweaksCenter() logs
        /// "not installed" and returns, so from the user's side nothing at all happened.
        ///
        /// ── Why opening a browser is safe here ───────────────────────────────────────────────────
        /// Nothing is fetched, written or executed by us, and the destination is the release PAGE, not
        /// a direct .exe link — so nothing downloads on its own either. The user picks the file, the
        /// browser applies its own reputation checks, and the user runs it deliberately. That is a
        /// categorically different shape from the download-and-launch path this file used to contain,
        /// which is the one antivirus heuristics score. Same approach Center takes for driver tools.
        ///
        /// ── The URL is opened by the HELPER, not by Launcher.LaunchUriAsync ──────────────────────
        /// This used to call Launcher.LaunchUriAsync directly. It returns false from the Game Bar
        /// AppContainer and nothing opens — measured on 2026-08-09, three clicks in two sessions, every
        /// one of them "opened download page … (ok=False)". The button was a dead end again, this time
        /// after the dialog instead of before it.
        ///
        /// An older comment here claimed the helper route "produced nothing at all" because of an
        /// explorer.exe hand-off that exits when the caller is elevated. That hand-off is gone: the
        /// helper's OpenExternalUrl handler runs `cmd /c start`, which does reach the user's default
        /// browser from an elevated process. Every driver/Intel "Open page" button in the widget uses
        /// it. OpenExternalUrlAsync keeps the Launcher as its own fallback, so the previous behaviour
        /// is still the last resort rather than the first attempt.
        /// </summary>
        internal async Task OpenCenterOrOfferInstallAsync()
        {
            bool installed = true;              // assume present: launching is harmless if it isn't
            string downloadUrl = null;
            try
            {
                if (App.IsConnected)
                {
                    await clawTweaksCenterStatus.Sync();
                    string payload = clawTweaksCenterStatus?.Value ?? "";
                    // "installed=0|1;url=…" — the URL may itself contain '=' and ';' is not legal in a
                    // query-less GitHub release URL, so split the key off once rather than on every ';'.
                    foreach (var part in payload.Split(';'))
                    {
                        if (part.StartsWith("installed=", StringComparison.OrdinalIgnoreCase))
                            installed = part.Substring("installed=".Length).Trim() == "1";
                        else if (part.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
                            downloadUrl = part.Substring("url=".Length).Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                // Helper unreachable / malformed answer: fall through and just try to launch. Showing a
                // "Center is missing" dialog to someone who has it installed would be worse than a
                // launch attempt that quietly does nothing.
                Logger.Warn($"OpenCenterOrOfferInstallAsync: status query failed: {ex.Message}");
            }

            if (installed)
            {
                await LaunchProgramViaHelper("@ClawTweaksCenter", closeGameBar: true);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "CTW Center is not installed",
                Content = "CTW Center is the desktop app that installs and updates ClawTweaks. "
                        + "It also provides backups and restore, onboarding, and the driver and "
                        + "prerequisite checks.\n\nIt's recommended, but ClawTweaks works without it.\n\n"
                        + "Open the download page in your browser?",
                PrimaryButtonText = "Open download page",
                CloseButtonText = "Not now",
                DefaultButton = ContentDialogButton.Primary,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            if (string.IsNullOrWhiteSpace(downloadUrl))
                downloadUrl = "https://github.com/enterTheVoidCode/ClawTweaks/releases";

            try
            {
                bool opened = await OpenExternalUrlAsync(downloadUrl);
                // The Game Bar is deliberately NOT closed first. Sending Win+G can suspend this widget
                // before the launch runs, and the browser taking focus dismisses the overlay anyway.
                if (opened)
                    Logger.Info($"CTW Center not installed — opened download page {downloadUrl}");
                else
                    Logger.Warn($"CTW Center not installed — could not open {downloadUrl}: neither the "
                              + "helper nor Launcher.LaunchUriAsync reached a browser.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Opening the Center download page failed: {ex.Message}");
            }
        }

        private void SetUpdateAvailableBadge(bool available)
        {
            _appUpdateAvailable = available;
            if (OnboardingUpdateBadge != null)
                OnboardingUpdateBadge.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        }

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
                    ShowAppUpdateProgress(true, "Helper not connected — open ClawTweaks once so the helper starts, then retry.", spinning: false);
                    return;
                }

                // Get-query the helper for the latest releases (Content = JSON array).
                await appReleases.Sync();
                string json = appReleases?.Value ?? "[]";

                if (!JsonArray.TryParse(json, out var arr) || arr.Count == 0)
                {
                    ShowAppUpdateProgress(true, "Couldn't load releases right now. Check your connection and tap 'Reload releases'.", spinning: false);
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
                        $"You're on the latest version ({installed[0]}.{installed[1]}.{installed[2]}.{installed[3]}). Newer releases appear here automatically.", spinning: false);
                }
                _appReleasesLoaded = true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"LoadAppReleasesAsync failed: {ex.Message}");
                ShowAppUpdateProgress(true, "Couldn't load releases right now. Tap 'Reload releases' to try again.", spinning: false);
            }
            finally
            {
                _appReleasesLoading = false;
                if (AppUpdateRefreshBtn != null) AppUpdateRefreshBtn.IsEnabled = true;
            }
        }

        // spinning: whether the progress ring should animate. Active work (loading, downloading,
        // installing) spins; terminal/idle messages (done, failed, "you're on the latest") do not.
        private void ShowAppUpdateProgress(bool show, string text, bool spinning = true)
        {
            if (AppUpdateProgressPanel != null)
                AppUpdateProgressPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (AppUpdateSpinner != null)
            {
                bool spin = show && spinning;
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
            string pageUrl = JsonStr(rel, "releasePageUrl");

            var panel = new StackPanel();

            // No prerelease badge: the helper only ever sends stable releases now.

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
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = AppUpdAccentBrush,
                Margin = new Thickness(0, 8, 0, 2),
            });

            var bodyPanel = new StackPanel();
            RenderReleaseBody(bodyPanel, body);
            var bodyScroll = new ScrollViewer
            {
                Content = bodyPanel,
                MaxHeight = 420,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            panel.Children.Add(bodyScroll);

            // Installing happens in CTW Center. Only the newest card carries the button — repeating it
            // on every card would suggest each one installs "its" version, which Center decides.
            if (isLatest)
            {
                var btn = new Button
                {
                    Content = "Install with CTW Center",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    FontSize = 12,
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 8, 0, 0),
                };
                TryApplyModernButtonStyle(btn);
                btn.Click += async (s, e) => await OpenCenterOrOfferInstallAsync();
                panel.Children.Add(btn);
            }

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

        // Renders the GitHub release body into the given panel as readable "What's new" content:
        // only the "## What's new" section is shown (install instructions / warnings above it are
        // dropped), headings and bullets are styled, inline markdown is stripped, and image tags
        // (<img src> and ![](...)) are loaded as actual images. Keeps it lightweight — no full
        // markdown engine, just the constructs our release notes actually use.
        private void RenderReleaseBody(StackPanel parent, string body)
        {
            if (parent == null) return;
            if (string.IsNullOrWhiteSpace(body))
            {
                parent.Children.Add(MakeBodyText("No release notes."));
                return;
            }

            string whatsNew = ExtractWhatsNew(body);
            var lines = whatsNew.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            bool any = false;
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                // Section divider / front-matter markers we don't want to show.
                if (line == "---" || line.StartsWith("> [!") || line.StartsWith("```")) continue;

                // Image (HTML <img src="..."> or markdown ![alt](url)).
                if (TryExtractImageUrl(line, out string imgUrl))
                {
                    var img = MakeImage(imgUrl);
                    if (img != null) { parent.Children.Add(img); any = true; }
                    continue;
                }

                // Heading (### / ## / #).
                if (line.StartsWith("#"))
                {
                    string h = line.TrimStart('#').Trim();
                    h = StripInline(h);
                    if (h.Length == 0) continue;
                    parent.Children.Add(new TextBlock
                    {
                        Text = h,
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = AppUpdTitleBrush,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 2),
                    });
                    any = true;
                    continue;
                }

                // Blockquote → muted note.
                if (line.StartsWith(">"))
                {
                    string q = StripInline(line.TrimStart('>').Trim());
                    if (q.Length == 0) continue;
                    var note = MakeBodyText(q);
                    note.Foreground = AppUpdSubBrush;
                    parent.Children.Add(note);
                    any = true;
                    continue;
                }

                // Bullet.
                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    parent.Children.Add(MakeBodyText("•  " + StripInline(line.Substring(2).Trim())));
                    any = true;
                    continue;
                }

                // Plain paragraph.
                parent.Children.Add(MakeBodyText(StripInline(line)));
                any = true;
            }

            if (!any) parent.Children.Add(MakeBodyText("No release notes."));
        }

        private TextBlock MakeBodyText(string text) => new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = AppUpdBodyBrush,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0, 1, 0, 1),
        };

        private Image MakeImage(string url)
        {
            try
            {
                var uri = SafeUri(url);
                if (uri == null) return null;
                return new Image
                {
                    Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(uri),
                    Stretch = Stretch.Uniform,
                    MaxWidth = 300,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 6, 0, 6),
                };
            }
            catch { return null; }
        }

        // Returns the text from the "## What's new" heading onward (heading excluded). If no such
        // heading exists, returns the whole body so nothing is lost.
        private static string ExtractWhatsNew(string body)
        {
            var lines = body.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("#") && t.TrimStart('#').Trim().StartsWith("What's new", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Join("\n", lines, i + 1, lines.Length - (i + 1));
                }
            }
            return body;
        }

        // Extracts an image URL from an HTML <img ... src="..."> tag or a markdown ![alt](url).
        private static bool TryExtractImageUrl(string line, out string url)
        {
            url = "";
            // HTML: <img ... src="URL" ...>
            int img = line.IndexOf("<img", StringComparison.OrdinalIgnoreCase);
            if (img >= 0)
            {
                int s = line.IndexOf("src", img, StringComparison.OrdinalIgnoreCase);
                if (s >= 0)
                {
                    int q = line.IndexOfAny(new[] { '"', '\'' }, s);
                    if (q >= 0)
                    {
                        char quote = line[q];
                        int end = line.IndexOf(quote, q + 1);
                        if (end > q) { url = line.Substring(q + 1, end - q - 1); return url.Length > 0; }
                    }
                }
            }
            // Markdown: ![alt](URL)
            int bang = line.IndexOf("![", StringComparison.Ordinal);
            if (bang >= 0)
            {
                int open = line.IndexOf('(', bang);
                int close = open >= 0 ? line.IndexOf(')', open) : -1;
                if (open >= 0 && close > open)
                {
                    url = line.Substring(open + 1, close - open - 1).Trim();
                    // a URL may be "url "title" — keep just the url part
                    int sp = url.IndexOf(' ');
                    if (sp > 0) url = url.Substring(0, sp);
                    return url.Length > 0;
                }
            }
            return false;
        }

        // Strips the inline markdown our notes use: **bold**, `code`, and [text](url) → text.
        private static string StripInline(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("**", "").Replace("`", "");
            // [text](url) → text
            int b;
            while ((b = s.IndexOf('[')) >= 0)
            {
                int mid = s.IndexOf("](", b, StringComparison.Ordinal);
                if (mid < 0) break;
                int end = s.IndexOf(')', mid + 2);
                if (end < 0) break;
                string text = s.Substring(b + 1, mid - b - 1);
                s = s.Substring(0, b) + text + s.Substring(end + 1);
            }
            return s.Trim();
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
