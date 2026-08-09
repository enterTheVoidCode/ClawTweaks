using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {

        private void UpdateGameProfileCardVisibility()
        {
            bool hasGame = HasValidGame(currentGameName);
            // When the per-game profile toggle is off, the GLOBAL profile is what's
            // actually applied to hardware. Showing a per-game card with default values
            // (which the user never picked and which aren't being applied) is misleading
            // — hide the entire card and let the rest of the widget UI surface the
            // active global profile via the slider/toggle states it already reflects.
            bool perGameProfileInUse = hasGame && (PerGameProfileToggle?.IsOn ?? false);
            bool powerSourceEnabled = perGameProfileInUse && GetPerGamePowerSourceProfileEnabled(currentGameName);
            UpdatePowerSourceProfileScopeText();

            if (perGameProfileInUse)
            {
                GameProfileCard.Visibility = Visibility.Visible;

                if (powerSourceEnabled)
                {
                    GameProfileWithPowerSource.Visibility = Visibility.Visible;
                    GameProfileWithoutPowerSource.Visibility = Visibility.Collapsed;
                    GameProfileTitleWithPower.Text = currentGameName;
                }
                else
                {
                    GameProfileWithPowerSource.Visibility = Visibility.Collapsed;
                    GameProfileWithoutPowerSource.Visibility = Visibility.Visible;
                    GameProfileTitleNoPower.Text = currentGameName;
                }
            }
            else
            {
                GameProfileCard.Visibility = Visibility.Collapsed;
            }
        }

        private List<string> GetAllSavedGameProfiles()
        {
            var gameNames = new HashSet<string>();
            var settings = ApplicationData.Current.LocalSettings;

            // Enumerate all containers looking for game profiles
            foreach (var containerName in settings.Containers.Keys)
            {
                if (containerName.StartsWith("Profile_Game_"))
                {
                    // Extract game name from container key
                    string gameName = containerName.Substring("Profile_Game_".Length);

                    // Remove _AC or _DC suffix if present
                    if (gameName.EndsWith("_AC"))
                    {
                        gameName = gameName.Substring(0, gameName.Length - 3);
                    }
                    else if (gameName.EndsWith("_DC"))
                    {
                        gameName = gameName.Substring(0, gameName.Length - 3);
                    }

                    gameNames.Add(gameName);
                }
                // No log line for the non-matches. This enumeration walks EVERY container, so the old
                // two lines per miss produced 24 of the 77 lines a single slider step used to write —
                // pure noise that buried the real entries and cost file I/O on the UI thread.
            }

            return gameNames.OrderBy(name => name).ToList();
        }

        // Set true once we've restored the user's saved sort mode into the ComboBox.
        // Without this guard, the SelectionChanged handler would keep re-running on
        // each restoration attempt, causing redundant re-renders.
        private bool _profileSortModeRestored;

        private void UpdateAllGameProfilesDisplay()
        {
            if (AllGameProfilesContainer == null)
                return;

            // Restore the persisted sort mode on the first render. The XAML default is
            // "name"; on subsequent app starts we honor whatever the user picked last.
            if (!_profileSortModeRestored && ProfileSortComboBox != null)
            {
                _profileSortModeRestored = true;
                try
                {
                    if (ApplicationData.Current.LocalSettings.Values.TryGetValue("ProfileSortMode", out var modeObj)
                        && modeObj is string saved)
                    {
                        foreach (var item in ProfileSortComboBox.Items)
                        {
                            if (item is ComboBoxItem cbi && (cbi.Tag as string) == saved)
                            {
                                if (ProfileSortComboBox.SelectedItem != cbi)
                                {
                                    ProfileSortComboBox.SelectedItem = cbi;
                                    // SelectionChanged will fire and call us back; bail
                                    // here so we don't render twice with stale data.
                                    return;
                                }
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Restoring ProfileSortMode failed: {ex.Message}");
                }
            }

            // Clear existing game profile cards. The focus-routing list is rebuilt with them — a stale
            // entry here would hand the D-pad a control that is no longer in the tree.
            AllGameProfilesContainer.Children.Clear();
            _profileGroupExpanders.Clear();

            var savedGames = GetAllSavedGameProfiles();

            if (savedGames.Count == 0)
            {
                // Show "No saved game profiles" message
                var noProfilesText = new TextBlock
                {
                    Text = "No saved game profiles yet. Play a game with Per-Game Profiles enabled to create profiles.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                AllGameProfilesContainer.Children.Add(noProfilesText);
                return;
            }

            // Backfill GameExePath and LastModifiedUtc onto legacy widget profile
            // containers (created before we started stamping those keys at save time).
            // Without this step, profiles created in earlier builds never group, never
            // show their icon, and never show a "modified Xago" line — even though the
            // helper has all the info we need sitting in LocalState/profiles/*.xml.
            BackfillLegacyContainersFromHelperXmls();

            // Pull the title→exe-basename map from the helper's per-exe XML profiles
            // so legacy widget profiles (saved before we started stamping GameExePath
            // into LocalSettings containers) can still be grouped by their owning exe.
            var helperTitleMap = BuildTitleToExeBasenameMap();

            // Bucket every saved game profile by the exe it belongs to. Profiles for
            // the same exe (e.g. multiple titles played in Citron) collapse into one
            // parent card with an Expander; orphan profiles render flat.
            var groups = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var gameName in savedGames)
            {
                // Skip the active game — its card is rendered above this list.
                if (gameName == currentGameName && HasValidGame(currentGameName))
                    continue;

                string groupKey = ResolveGroupKeyForProfile(gameName, helperTitleMap) ?? gameName;
                if (!groups.TryGetValue(groupKey, out var list))
                {
                    list = new List<string>();
                    groups[groupKey] = list;
                }
                list.Add(gameName);
            }

            // Sort groups according to the user's choice in ProfileSortComboBox. Sorting
            // happens at two levels: across groups (group-key by name; max LastModified;
            // max TDP), and inside each group (always by name — within an exe, alphabetical
            // child order is the most predictable).
            string sortMode = (ProfileSortComboBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "name";

            IEnumerable<KeyValuePair<string, List<string>>> orderedGroups;
            switch (sortMode)
            {
                case "modified":
                    orderedGroups = groups.OrderByDescending(kv => kv.Value.Max(GetMostRecentLastModifiedTicks));
                    break;
                case "tdp":
                    orderedGroups = groups.OrderByDescending(kv => kv.Value.Max(GetProfileTopTdp));
                    break;
                default: // "name"
                    orderedGroups = groups; // SortedDictionary already alphabetical
                    break;
            }

            foreach (var kv in orderedGroups)
            {
                // Always wrap in an Expander, even for single-profile groups, so the
                // collapsed list is visually uniform (every entry the same height —
                // less eye-jumping when scanning, less scrolling overall).
                AllGameProfilesContainer.Children.Add(RenderProfileGroupExpander(kv.Key, kv.Value));
            }
        }

        private void ProfileSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var tag = (ProfileSortComboBox?.SelectedItem as ComboBoxItem)?.Tag as string;
                if (!string.IsNullOrEmpty(tag))
                {
                    ApplicationData.Current.LocalSettings.Values["ProfileSortMode"] = tag;
                }
                UpdateAllGameProfilesDisplay();
            }
            catch (Exception ex)
            {
                Logger.Debug($"ProfileSortComboBox_SelectionChanged failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the highest TDP across the profile's single/_AC/_DC containers, used
        /// for "TDP (high → low)" sort. Falls back to int.MinValue for missing/legacy
        /// profiles so they sort to the bottom.
        /// </summary>
        private int GetProfileTopTdp(string gameName)
        {
            int max = int.MinValue;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                foreach (var suffix in new[] { "", "_AC", "_DC" })
                {
                    var key = $"Profile_Game_{gameName}{suffix}";
                    if (settings.Containers.ContainsKey(key)
                        && settings.Containers[key].Values.TryGetValue("TDP", out var tdpObj))
                    {
                        int v = Convert.ToInt32(tdpObj);
                        if (v > max) max = v;
                    }
                }
            }
            catch { }
            return max;
        }

        /// <summary>
        /// Tries to map a widget profile (keyed by window title) back to its owning exe
        /// basename. Order: container-stored GameExePath (new profiles), then helper XML
        /// title→exe map (legacy profiles whose title still matches the helper's last
        /// recorded name for that exe). Returns null when no mapping is available — the
        /// caller will fall back to using the title as the group key (1-profile group).
        /// </summary>
        private string ResolveGroupKeyForProfile(string gameName, Dictionary<string, string> helperTitleMap)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                foreach (var suffix in new[] { "", "_AC", "_DC" })
                {
                    var key = $"Profile_Game_{gameName}{suffix}";
                    if (settings.Containers.ContainsKey(key)
                        && settings.Containers[key].Values.TryGetValue("GameExePath", out var pathObj)
                        && pathObj is string path
                        && !string.IsNullOrEmpty(path))
                    {
                        return Path.GetFileNameWithoutExtension(path);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"ResolveGroupKeyForProfile({gameName}) container read failed: {ex.Message}");
            }

            if (helperTitleMap != null && helperTitleMap.TryGetValue(gameName, out var helperBasename))
            {
                return helperBasename;
            }
            return null;
        }

        /// <summary>
        /// Walks every Profile_Game_* container in LocalSettings and stamps GameExePath
        /// + LastModifiedUtc on legacy ones using data from the helper's per-exe profile
        /// XMLs (LocalState/profiles/*.xml — same package, so widget can read them).
        ///
        /// Two matching strategies, in priority order:
        ///   1. Direct title match: helper XML's &lt;Name&gt; equals the widget's profile
        ///      key. Always safe — that's the most recent title the helper saw for that
        ///      exe.
        ///   2. Word-boundary substring match: the exe basename appears as a whole word
        ///      in the title (regex \b...\b, case-insensitive, basename ≥ 4 chars).
        ///      Catches the emulator pattern — Citron / Eden / Yuzu / RetroArch each
        ///      produce many distinct widget profiles (one per game played), but the
        ///      helper only retains the latest title in citron.xml etc. The substring
        ///      match recovers the rest.
        /// Substring match is restricted to UNAMBIGUOUS cases (only one helper basename
        /// matches) so generic exe basenames like "Code" don't grab unrelated titles
        /// like "Code Vein". Idempotent: containers that already have GameExePath are
        /// skipped, so this is cheap to call on every Profiles-tab render.
        /// </summary>
        private void BackfillLegacyContainersFromHelperXmls()
        {
            try
            {
                string profilesFolder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "profiles");
                if (!Directory.Exists(profilesFolder)) return;

                // Build helper-side lookup table once: per exe XML, the basename, full
                // exe path (from GameId/Path), file's last write time, and the latest
                // recorded title.
                var helperEntries = new List<(string basename, string fullPath, DateTime lastWrite, string title)>();
                foreach (var xmlPath in Directory.GetFiles(profilesFolder, "*.xml"))
                {
                    try
                    {
                        var doc = System.Xml.Linq.XDocument.Load(xmlPath);
                        var gameId = doc.Descendants("GameId").FirstOrDefault();
                        string title = gameId?.Element("Name")?.Value;
                        string fullPath = gameId?.Element("Path")?.Value;
                        if (string.IsNullOrEmpty(fullPath)) continue;

                        helperEntries.Add((
                            Path.GetFileNameWithoutExtension(xmlPath),
                            fullPath,
                            File.GetLastWriteTimeUtc(xmlPath),
                            title ?? string.Empty));
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Backfill: parse {xmlPath} failed: {ex.Message}");
                    }
                }
                if (helperEntries.Count == 0) return;

                var settings = ApplicationData.Current.LocalSettings;
                var containerNames = settings.Containers.Keys
                    .Where(k => k.StartsWith("Profile_Game_"))
                    .ToList();

                int filled = 0;
                foreach (var containerName in containerNames)
                {
                    var container = settings.Containers[containerName];
                    string existingExe = container.Values.ContainsKey("GameExePath")
                        ? container.Values["GameExePath"] as string
                        : null;
                    bool needsExe = string.IsNullOrEmpty(existingExe);
                    bool needsMod = !container.Values.ContainsKey("LastModifiedUtc");

                    string suffixed = containerName.Substring("Profile_Game_".Length);
                    string title = suffixed;
                    if (suffixed.EndsWith("_AC")) title = suffixed.Substring(0, suffixed.Length - 3);
                    else if (suffixed.EndsWith("_DC")) title = suffixed.Substring(0, suffixed.Length - 3);

                    // 1) Direct title match — authoritative (helper XML <Name> == profile title).
                    var direct = helperEntries.FirstOrDefault(e =>
                        string.Equals(e.title, title, StringComparison.OrdinalIgnoreCase));
                    bool hasDirect = !string.IsNullOrEmpty(direct.fullPath);

                    var match = direct;

                    // 2) Word-boundary substring match (emulators) — only when no direct match.
                    if (!hasDirect)
                    {
                        var candidates = helperEntries
                            .Where(e => e.basename.Length >= 4
                                && System.Text.RegularExpressions.Regex.IsMatch(
                                    title,
                                    $"\\b{System.Text.RegularExpressions.Regex.Escape(e.basename)}\\b",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            .ToList();
                        if (candidates.Count == 1)
                        {
                            match = candidates[0];
                        }
                    }

                    if (string.IsNullOrEmpty(match.fullPath))
                    {
                        if (needsMod && hasDirect)
                            container.Values["LastModifiedUtc"] = direct.lastWrite.Ticks;
                        continue;
                    }

                    // Stamp GameExePath when missing (any match), OR REPAIR it when a direct
                    // title match disagrees with the stored value. The repair undoes the
                    // start-up name/path race that cross-stamped a wrong exe (e.g. RE2's
                    // container holding Blasphemous 2's path → both collapsing into one group).
                    // Repair only on a direct title match — never on the fuzzy emulator match,
                    // which is intentionally not authoritative.
                    bool wrongDirect = hasDirect && !needsExe
                        && !string.Equals(existingExe, direct.fullPath, StringComparison.OrdinalIgnoreCase);
                    if (needsExe || wrongDirect)
                    {
                        container.Values["GameExePath"] = match.fullPath;
                        filled++;
                        if (wrongDirect)
                            Logger.Info($"Profiles repair: corrected GameExePath for '{title}' ('{existingExe}' -> '{match.fullPath}')");
                    }
                    if (needsMod)
                    {
                        container.Values["LastModifiedUtc"] = match.lastWrite.Ticks;
                    }
                }

                if (filled > 0)
                {
                    Logger.Info($"Profiles backfill: stamped GameExePath on {filled} legacy container(s) using helper XML data");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"BackfillLegacyContainersFromHelperXmls failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the helper's per-exe profile XMLs (LocalState/profiles/*.xml — same
        /// package, so the widget can read them) and returns a map of the most recent
        /// window title → exe basename for each exe. Used to retroactively group
        /// pre-existing widget profiles that don't have GameExePath stamped in their
        /// LocalSettings container.
        /// </summary>
        private Dictionary<string, string> BuildTitleToExeBasenameMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string profilesFolder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "profiles");
                if (!Directory.Exists(profilesFolder)) return map;

                foreach (var xmlPath in Directory.GetFiles(profilesFolder, "*.xml"))
                {
                    try
                    {
                        var doc = System.Xml.Linq.XDocument.Load(xmlPath);
                        var nameEl = doc.Descendants("Name").FirstOrDefault();
                        if (nameEl != null && !string.IsNullOrEmpty(nameEl.Value))
                        {
                            map[nameEl.Value] = Path.GetFileNameWithoutExtension(xmlPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"BuildTitleToExeBasenameMap parse {xmlPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"BuildTitleToExeBasenameMap enumerate failed: {ex.Message}");
            }
            return map;
        }

        /// <summary>
        /// Builds the parent Border for a multi-profile exe group. Header shows the exe
        /// name and a "N profiles" badge; the muxc:Expander collapses the children by
        /// default so users with lots of emulator-spawned per-title profiles don't
        /// scroll past everything to find what they want.
        /// </summary>
        private Border RenderProfileGroupExpander(string exeBasename, List<string> profileNames)
        {
            var inner = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var name in profileNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var child = RenderProfileCardInternal(name);
                if (child != null)
                {
                    inner.Children.Add(child);
                }
            }

            var headerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Every child profile in this group shares the same exe (that's what put
            // them in the same group), so any child's stored GameExePath is enough
            // to surface the helper-cached icon next to the group header. First child
            // with a stamped path wins; legacy profiles in the group quietly skip.
            string groupExePath = null;
            foreach (var name in profileNames)
            {
                var path = TryGetExePathForGame(name);
                if (!string.IsNullOrEmpty(path))
                {
                    groupExePath = path;
                    break;
                }
            }
            if (!string.IsNullOrEmpty(groupExePath))
            {
                var groupIcon = new Image
                {
                    Width = 24,
                    Height = 24,
                    Stretch = Stretch.UniformToFill,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed,
                };
                headerStack.Children.Add(groupIcon);
                FillGameIconAsync(groupIcon, groupExePath);
            }

            headerStack.Children.Add(new TextBlock
            {
                Text = exeBasename,
                FontSize = 14,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 100)),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 80)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 1, 8, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = profileNames.Count == 1 ? "1 profile" : $"{profileNames.Count} profiles",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White)
                }
            });

            var expander = new Microsoft.UI.Xaml.Controls.Expander
            {
                Header = headerStack,
                Content = inner,
                IsExpanded = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                // Controller reachability. These cards are built in code, so they were never part of
                // the tab's hand-written focus chain and could only be opened by touching the screen —
                // on a handheld whose whole point is the controller (user, 2026-08-02). Making the
                // Expander itself the tab stop is what lets the chain below address it; the D-pad
                // routing lives in ProfileGroupExpander_KeyDown because the surrounding cards use
                // explicit KeyDown routing too, and mixing that with XYFocus hints is what already
                // stranded the power-split toggle.
                IsTabStop = true,
                UseSystemFocusVisuals = true,
                XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled
            };
            expander.KeyDown += ProfileGroupExpander_KeyDown;
            _profileGroupExpanders.Add(expander);

            return new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Child = expander
            };
        }

        // The group expanders in display order, rebuilt with the list. Used only for D-pad routing —
        // the visual tree stays the source of truth for everything else.
        private readonly List<Microsoft.UI.Xaml.Controls.Expander> _profileGroupExpanders
            = new List<Microsoft.UI.Xaml.Controls.Expander>();

        /// <summary>First saved-profile group, for the sort dropdown's Down target. Null when the list
        /// is empty or collapsed, in which case the caller falls through to the next card.</summary>
        private Control FirstGameProfileFocusTarget()
        {
            if (PerfSavedProfilesContent?.Visibility != Visibility.Visible) return null;
            return _profileGroupExpanders.Count > 0 ? _profileGroupExpanders[0] : null;
        }

        /// <summary>
        /// D-pad routing inside the saved-profile list: Up/Down walk the groups, A/Space/Enter opens and
        /// closes one. Leaving the list at either end rejoins the tab's fixed chain (sort dropdown above,
        /// power-split toggle below).
        ///
        /// Expanding does NOT descend into the group's child cards — those are read-only summaries with
        /// nothing to activate, so stepping through them would just be a long corridor of dead stops.
        /// </summary>
        private void ProfileGroupExpander_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var self = sender as Microsoft.UI.Xaml.Controls.Expander;
            if (self == null) return;

            int idx = _profileGroupExpanders.IndexOf(self);
            if (idx < 0) return;

            switch (e.Key)
            {
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadLeftThumbstickUp:
                case VirtualKey.Up:
                    Control up = idx > 0 ? (Control)_profileGroupExpanders[idx - 1] : ProfileSortComboBox;
                    try { up?.Focus(FocusState.Keyboard); } catch { }
                    e.Handled = true;
                    break;

                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadLeftThumbstickDown:
                case VirtualKey.Down:
                    Control down = idx < _profileGroupExpanders.Count - 1
                        ? (Control)_profileGroupExpanders[idx + 1]
                        : PowerSourceProfileToggle;
                    try { down?.Focus(FocusState.Keyboard); } catch { }
                    e.Handled = true;
                    break;

                case VirtualKey.GamepadA:
                case VirtualKey.Space:
                case VirtualKey.Enter:
                    self.IsExpanded = !self.IsExpanded;
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// Renders a single per-game profile card (title row, AC/DC split badge, and
        /// either the AC/DC comparison grid or the single-profile grid). Returns the
        /// constructed Border so the caller decides whether to drop it directly into
        /// AllGameProfilesContainer (single-profile group) or wrap it inside a
        /// multi-profile group's Expander body.
        /// </summary>
        private Border RenderProfileCardInternal(string gameName)
        {
            try
            {
                // Load profiles
                var settings = ApplicationData.Current.LocalSettings;
                bool hasAC = settings.Containers.ContainsKey($"Profile_Game_{gameName}_AC");
                bool hasDC = settings.Containers.ContainsKey($"Profile_Game_{gameName}_DC");
                bool hasACDC = hasAC || hasDC;
                bool hasSingle = settings.Containers.ContainsKey($"Profile_Game_{gameName}");
                bool gamePowerSourceSplit = GetPerGamePowerSourceProfileEnabled(gameName);

                Border profileCard = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 58, 42, 26)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 58, 58, 58)),
                    BorderThickness = new Thickness(1)
                };

                var stackPanel = new StackPanel();
                profileCard.Child = stackPanel;

                // Title row: [optional icon] [title + "modified Xago"] [delete button]
                var titleGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // The exe icon next to the title, same source and same look as the saved CONTROLLER
                // profiles list - that one has shown it all along, this one never did.
                string exePathForIcon = TryGetExePathForGame(gameName);
                if (!string.IsNullOrEmpty(exePathForIcon))
                {
                    var icon = new Image
                    {
                        Width = 28,
                        Height = 28,
                        Margin = new Thickness(0, 0, 10, 0),
                        Stretch = Stretch.UniformToFill,
                        VerticalAlignment = VerticalAlignment.Center,
                        Visibility = Visibility.Collapsed,
                    };
                    Grid.SetColumn(icon, 0);
                    titleGrid.Children.Add(icon);
                    FillGameIconAsync(icon, exePathForIcon);
                }

                // Title + last-modified subtitle stacked vertically.
                var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                titleStack.Children.Add(new TextBlock
                {
                    Text = gameName,
                    FontSize = 13,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 165, 0)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                string modifiedText = GetMostRecentLastModifiedText(gameName);
                if (!string.IsNullOrEmpty(modifiedText))
                {
                    titleStack.Children.Add(new TextBlock
                    {
                        Text = modifiedText,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150))
                    });
                }
                Grid.SetColumn(titleStack, 1);
                titleGrid.Children.Add(titleStack);

                // Delete button
                var deleteButton = new Button
                {
                    Content = "🗑️",
                    FontSize = 12,
                    Width = 28,
                    Height = 28,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 255, 0, 0)),
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = gameName,  // Store game name for delete handler
                    BorderBrush = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    BorderThickness = new Thickness(2)
                };
                deleteButton.Click += DeleteProfileButton_Click;
                deleteButton.GotFocus += (s, args) =>
                {
                    deleteButton.BorderBrush = new SolidColorBrush(Windows.UI.Colors.White);
                    deleteButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 50, 50));
                };
                deleteButton.LostFocus += (s, args) =>
                {
                    deleteButton.BorderBrush = new SolidColorBrush(Windows.UI.Colors.Transparent);
                    deleteButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 255, 0, 0));
                };
                Grid.SetColumn(deleteButton, 2);
                titleGrid.Children.Add(deleteButton);

                stackPanel.Children.Add(titleGrid);
                stackPanel.Children.Add(new TextBlock
                {
                    Text = gamePowerSourceSplit
                        ? $"Separate values: plugged in {PluggedGlyph} / on battery {BatteryGlyph}"
                        : "One value for both power states",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                    Margin = new Thickness(0, 0, 0, 6)
                });

                // The helper's profile for this card — one profile feeds BOTH columns now, resolved per
                // power source via GameProfile.Effective* (plan §5.3/§7.1). The two widget containers
                // below still supply group-C values (the preset combo index).
                var snapCard = profileSnapshot?.GetByName(gameName);

                // Decided by the profile's own split flag alone. It used to also require two widget
                // containers to exist (hasACDC) — a second answer to the same question, and the wrong
                // one now: the two columns are rendered from ONE helper profile and its *_DC overrides,
                // so whether the widget ever created a _AC/_DC container says nothing about it.
                if (gamePowerSourceSplit)
                {
                    // Load AC/DC profiles
                    var gameAC = new PerformanceProfile();
                    var gameDC = new PerformanceProfile();
                    if (hasAC)
                    {
                        LoadProfileFromStorage($"Game_{gameName}_AC", gameAC);
                    }
                    else if (hasSingle)
                    {
                        LoadProfileFromStorage($"Game_{gameName}", gameAC);
                    }

                    if (hasDC)
                    {
                        LoadProfileFromStorage($"Game_{gameName}_DC", gameDC);
                    }
                    else if (hasSingle)
                    {
                        LoadProfileFromStorage($"Game_{gameName}", gameDC);
                    }

                    // Create AC/DC comparison grid
                    var acDcGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                    // Add rows dynamically based on enabled settings
                    for (int i = 0; i < 30; i++) // Max rows
                        acDcGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    acDcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    acDcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    acDcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    int rowIndex = 0;

                    // Headers
                    // Short forms: the card's value columns are narrow, and the scope line above the
                    // grid already spells the two states out.
                    AddTextBlock(acDcGrid, rowIndex, 1, $"Plugged {PluggedGlyph}", 10, "#FFD700", horizontalAlignment: HorizontalAlignment.Center);
                    AddTextBlock(acDcGrid, rowIndex, 2, $"Battery {BatteryGlyph}", 10, "#FF6B6B", horizontalAlignment: HorizontalAlignment.Center);
                    rowIndex++;

                    // TDP Mode (Legion only)
                    if (legionGoDetected?.Value == true && SaveTDP)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Mode", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, GetProfileTDPModeName(gameAC.TDPModeIndex, snapCard?.LegionPerformanceMode ?? 2), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, GetProfileTDPModeName(gameDC.TDPModeIndex, snapCard?.LegionPerformanceMode ?? 2), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // From here on the values come from the HELPER's profile (plan §5.3), resolved per
                    // power source with GameProfile.Effective*. There is ONE profile behind both
                    // columns — the AC/DC difference lives in its *_DC overrides, not in two separate
                    // stores. The widget containers loaded above stay for group C only (the preset combo
                    // index) and for the AMD block, which has no helper counterpart.
                    //
                    // Rows fed by the snapshot are skipped entirely while it is absent, rather than
                    // printed as zeros — plan §6.
                    if (snapCard != null)
                    {
                    // TDP
                    if (SaveTDP)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "TDP", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, $"{snapCard.EffectiveTDP(onBattery: false)}W", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, $"{snapCard.EffectiveTDP(onBattery: true)}W", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;

                        // Overboost and PL2 resolve per power state now, so the two columns can differ.
                        bool acBoost = snapCard.EffectiveTDPBoostEnabled(onBattery: false);
                        bool dcBoost = snapCard.EffectiveTDPBoostEnabled(onBattery: true);
                        AddTextBlock(acDcGrid, rowIndex, 0, "TDP Overboost", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, acBoost ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, dcBoost ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;

                        // PL2 target — sub-row whenever either side has Overboost on; "-" on the side
                        // that does not, so the column stays readable.
                        if (acBoost || dcBoost)
                        {
                            AddTextBlock(acDcGrid, rowIndex, 0, "PL2 Overboost", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                            AddTextBlock(acDcGrid, rowIndex, 1, acBoost ? $"{snapCard.EffectiveTDPBoostFPPTWatts(onBattery: false)}W" : "-", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            AddTextBlock(acDcGrid, rowIndex, 2, dcBoost ? $"{snapCard.EffectiveTDPBoostFPPTWatts(onBattery: true)}W" : "-", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            rowIndex++;
                        }
                    }

                    // Boost
                    if (SaveCPUBoost)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Boost", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, snapCard.EffectiveCPUBoost(onBattery: false) ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, snapCard.EffectiveCPUBoost(onBattery: true) ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;

                        // CPU advanced (ToothNClaw port) — no DC override, so one value in both columns.
                        string schedName = GetSchedulingPolicyName(snapCard.ProcessorSchedulingPolicy);
                        if (snapCard.ProcessorSchedulingPolicy >= 0 && schedName != null)
                        {
                            AddTextBlock(acDcGrid, rowIndex, 0, "Scheduling", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                            AddTextBlock(acDcGrid, rowIndex, 1, schedName, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            AddTextBlock(acDcGrid, rowIndex, 2, schedName, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            rowIndex++;
                        }
                        if (snapCard.MaxPCoreFreqMHz > 0)
                        {
                            AddTextBlock(acDcGrid, rowIndex, 0, "P-Core Max", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                            AddTextBlock(acDcGrid, rowIndex, 1, GetFreqLabel(snapCard.MaxPCoreFreqMHz), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            AddTextBlock(acDcGrid, rowIndex, 2, GetFreqLabel(snapCard.MaxPCoreFreqMHz), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            rowIndex++;
                        }
                        if (snapCard.MaxECoreFreqMHz > 0)
                        {
                            AddTextBlock(acDcGrid, rowIndex, 0, "E-Core Max", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                            AddTextBlock(acDcGrid, rowIndex, 1, GetFreqLabel(snapCard.MaxECoreFreqMHz), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            AddTextBlock(acDcGrid, rowIndex, 2, GetFreqLabel(snapCard.MaxECoreFreqMHz), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            rowIndex++;
                        }
                    }

                    // Intel Display (IGCL) — non-neutral channels only. No DC override exists for these,
                    // so both columns carry the one stored value. Nullable in the helper's store: null
                    // means "never captured", resolves to neutral and therefore prints nothing.
                    int acdcSaturation = snapCard.IntelColorSaturation ?? 50;
                    int acdcHue        = snapCard.IntelColorHue ?? 0;
                    int acdcContrast   = snapCard.IntelDisplayContrast ?? 50;
                    int acdcBrightness = snapCard.IntelDisplayBrightness ?? 50;
                    int acdcGammaX100  = snapCard.IntelDisplayGamma ?? 100;   // same ×100 encoding, different element name
                    int acdcSharpness  = snapCard.IntelAdaptiveSharpness ?? 0;

                    if (acdcSaturation != 50)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Saturation", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, acdcSaturation.ToString(), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, acdcSaturation.ToString(), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }
                    if (acdcHue != 0)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Hue", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, acdcHue.ToString(), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, acdcHue.ToString(), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }
                    if (acdcContrast != 50)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Contrast", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, acdcContrast.ToString(), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, acdcContrast.ToString(), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }
                    if (acdcBrightness != 50)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Brightness", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, acdcBrightness.ToString(), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, acdcBrightness.ToString(), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }
                    if (acdcGammaX100 != 100)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Gamma", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, (acdcGammaX100 / 100.0).ToString("0.00"), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, (acdcGammaX100 / 100.0).ToString("0.00"), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }
                    if (acdcSharpness > 0)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Sharpness", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, acdcSharpness.ToString(), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, acdcSharpness.ToString(), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // Frame generation and VRR — same "only when the profile sets something" rule as the
                    // colour rows above. Their neutral values differ: app choice (0) for frame generation,
                    // on (1) for VRR, so a card stays quiet unless the profile really overrides one.
                    int acdcFrameGen = snapCard.IntelFrameGeneration ?? 0;
                    if (acdcFrameGen > 0)
                    {
                        string fg = FormatFrameGeneration(acdcFrameGen);
                        AddTextBlock(acDcGrid, rowIndex, 0, "Frame Gen", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, fg, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, fg, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }
                    if ((snapCard.IntelVrr ?? 1) == 0)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "VRR", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // Fan name and curve. This one genuinely has a per-power-state override, so the two
                    // columns can differ — unlike the colour rows, which carry the single stored value twice.
                    string acFanCsv = snapCard.EffectiveMsiFanCurve(onBattery: false);
                    string dcFanCsv = snapCard.EffectiveMsiFanCurve(onBattery: true);
                    string acFanName = DescribeFanCurve(acFanCsv), dcFanName = DescribeFanCurve(dcFanCsv);
                    if (acFanName != null || dcFanName != null)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Fan", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, acFanName ?? "-", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, dcFanName ?? "-", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }
                    string acFan = FormatFanCurveShort(acFanCsv);
                    string dcFan = FormatFanCurveShort(dcFanCsv);
                    if (acFan != null || dcFan != null)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Curve", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, acFan ?? "-", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, dcFan ?? "-", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // EPP
                    //if (SaveCPUEPP)
                    //{
                    //    AddTextBlock(acDcGrid, rowIndex, 0, "EPP", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                    //    AddTextBlock(acDcGrid, rowIndex, 1, $"{gameAC.CPUEPP}", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                    //    AddTextBlock(acDcGrid, rowIndex, 2, $"{gameDC.CPUEPP}", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                    //    rowIndex++;
                    //}

                    // CPU State
                    //if (SaveCPUState)
                    //{
                    //    AddTextBlock(acDcGrid, rowIndex, 0, "CPU St", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                    //    AddTextBlock(acDcGrid, rowIndex, 1, $"{gameAC.MinCPUState}-{gameAC.MaxCPUState}%", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                    //    AddTextBlock(acDcGrid, rowIndex, 2, $"{gameDC.MinCPUState}-{gameDC.MaxCPUState}%", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                    //    rowIndex++;
                    //}

                    // FPS Limit (if enabled). The limiter is named in brackets after the number, and the
                    // separate "FPS Mode" row is gone — same shape as the single-profile card.
                    if (SaveFPSLimit)
                    {
                        string acFps = GetFpsValueLabel(snapCard, onBattery: false);
                        string dcFps = GetFpsValueLabel(snapCard, onBattery: true);
                        // The limiter is per power state too, so each column names its own.
                        if (acFps != "Off") acFps += (snapCard.EffectiveFpsCapMode(onBattery: false) == 1) ? " (Intel)" : " (RTSS)";
                        if (dcFps != "Off") dcFps += (snapCard.EffectiveFpsCapMode(onBattery: true) == 1) ? " (Intel)" : " (RTSS)";

                        AddTextBlock(acDcGrid, rowIndex, 0, "FPS Lim", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, acFps, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, dcFps, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // Power Mode — nullable per side ("profile configures no mode"), so the row is absent
                    // when neither side sets one, exactly like the single-profile card.
                    if (SaveOSPowerMode)
                    {
                        int? acMode = snapCard.EffectiveOSPowerMode(onBattery: false);
                        int? dcMode = snapCard.EffectiveOSPowerMode(onBattery: true);
                        if (acMode.HasValue || dcMode.HasValue)
                        {
                            AddTextBlock(acDcGrid, rowIndex, 0, "Power", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                            AddTextBlock(acDcGrid, rowIndex, 1, acMode.HasValue ? GetPowerModeShortName(acMode.Value) : "-", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            AddTextBlock(acDcGrid, rowIndex, 2, dcMode.HasValue ? GetPowerModeShortName(dcMode.Value) : "-", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            rowIndex++;
                        }
                    }
                    } // end of the snapshot-fed rows

                    // AMD Features (if enabled)
                    if (SaveAMDFeatures)
                    {
                        // Build AMD features string for AC profile
                        var acAmdFeatures = GetAMDFeaturesShortString(gameAC);
                        var dcAmdFeatures = GetAMDFeaturesShortString(gameDC);

                        if (!string.IsNullOrEmpty(acAmdFeatures) || !string.IsNullOrEmpty(dcAmdFeatures))
                        {
                            AddTextBlock(acDcGrid, rowIndex, 0, "AMD", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                            AddTextBlock(acDcGrid, rowIndex, 1, string.IsNullOrEmpty(acAmdFeatures) ? "Off" : acAmdFeatures, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            AddTextBlock(acDcGrid, rowIndex, 2, string.IsNullOrEmpty(dcAmdFeatures) ? "Off" : dcAmdFeatures, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            rowIndex++;
                        }
                    }

                    // Resolution (if enabled) — helper-owned since §5.5, no DC override, so one value in
                    // both columns. Short form, same as everywhere else the resolution is displayed.
                    string acdcResolution = GetResolutionShortLabel(snapCard?.Resolution);
                    if (SaveResolution && acdcResolution != null)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Res", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, acdcResolution, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, acdcResolution, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    stackPanel.Children.Add(acDcGrid);

                    // Same treatment as the live split cards: only the differences stay in the table,
                    // the agreed values go underneath as pairs. This grid is rebuilt from scratch on
                    // every render, so it needs no restore pass.
                    var sharedPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                    stackPanel.Children.Add(sharedPanel);
                    CollapseIdenticalSplitRows(acDcGrid, sharedPanel);
                }
                else
                {
                    // Load single profile
                    var game = new PerformanceProfile();
                    if (hasSingle)
                    {
                        LoadProfileFromStorage($"Game_{gameName}", game);
                    }
                    else if (hasAC)
                    {
                        LoadProfileFromStorage($"Game_{gameName}_AC", game);
                    }
                    else if (hasDC)
                    {
                        LoadProfileFromStorage($"Game_{gameName}_DC", game);
                    }
                    else
                    {
                        // No usable container for this profile — skip without rendering.
                        return null;
                    }

                    // Collect label/value pairs, then render across two columns (shared builder
                    // — same content/layout as the live Global & Now Playing cards).
                    stackPanel.Children.Add(RenderPairsGrid(BuildProfileCardPairs(
                        game, profileSnapshot?.GetByName(gameName), Data.ProfileSnapshotProperty.IsOnBattery)));
                }

                return profileCard;
            }
            catch (Exception ex)
            {
                Logger.Warn($"RenderProfileCardInternal({gameName}) failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves a profile's exe path from the most recently-stamped GameExePath
        /// container value. Used both for icon lookup and for grouping; null when the
        /// profile is legacy (no exe path was stamped). The helper-XML-driven fallback
        /// path is intentionally NOT consulted here — that map is built once per render
        /// in UpdateAllGameProfilesDisplay; this function is a faster per-card lookup.
        /// </summary>
        private string TryGetExePathForGame(string gameName)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                foreach (var suffix in new[] { "", "_AC", "_DC" })
                {
                    var key = $"Profile_Game_{gameName}{suffix}";
                    if (settings.Containers.ContainsKey(key)
                        && settings.Containers[key].Values.TryGetValue("GameExePath", out var pathObj)
                        && pathObj is string path
                        && !string.IsNullOrEmpty(path))
                    {
                        return path;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"TryGetExePathForGame({gameName}) failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Fills a card's icon slot from the same source the saved CONTROLLER profiles use, and shows
        /// the element only once a bitmap actually arrived.
        ///
        /// WHY THIS EXISTS AT ALL. These cards asked for the icon before and never showed one, for two
        /// independent reasons. First, they built the image as
        /// <c>new BitmapImage(new Uri(@"C:\…\LocalCache\icons\x.png"))</c> — a UWP BitmapImage does not
        /// load a plain filesystem path; it takes ms-appx / ms-appdata / http, and anything else fails
        /// silently, which is why the slot stayed empty rather than throwing. Second, they only ever
        /// consulted the helper's icon cache, so a game the helper had not extracted an icon for had no
        /// second chance. LoadSavedProfileIconAsync does both properly: cache first, Steam artwork
        /// after, loaded through a StorageFile stream.
        ///
        /// Fire and forget on purpose — the card renders now, the icon arrives when it arrives, and a
        /// missing one leaves a collapsed element rather than a hole in the layout.
        /// </summary>
        private async void FillGameIconAsync(Image target, string exePath)
        {
            if (target == null || string.IsNullOrEmpty(exePath)) return;
            try
            {
                var bitmap = await LoadSavedProfileIconAsync(exePath);
                if (bitmap == null) return;

                target.Source = bitmap;
                target.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                // An icon is decoration; a card that renders without one is still correct. This runs on
                // an async void, so an escaping exception would take the widget down with it.
                Logger.Debug($"FillGameIconAsync({exePath}) failed: {ex.Message}");
            }
        }

        // TryResolveCachedIconPath lived here and is gone: it was a second copy of
        // GetCachedIconPath (GamingWidget.SteamGameIcons.cs) that knew only the helper's cache and no
        // Steam fallback. Two implementations of "where is this game's icon" is how the cards came to
        // show a different answer than the controller list. One is enough.

        /// <summary>
        /// Returns the most recent LastModifiedUtc across the single/_AC/_DC containers
        /// for the given profile, formatted as a relative-time string ("3d ago"). Null
        /// when no container has the value yet (legacy profile, never re-saved since the
        /// 2074 storage upgrade).
        /// </summary>
        private string GetMostRecentLastModifiedText(string gameName)
        {
            DateTime? newest = null;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                foreach (var suffix in new[] { "", "_AC", "_DC" })
                {
                    var key = $"Profile_Game_{gameName}{suffix}";
                    if (settings.Containers.ContainsKey(key)
                        && settings.Containers[key].Values.TryGetValue("LastModifiedUtc", out var ts)
                        && ts is long ticks)
                    {
                        var dt = new DateTime(ticks, DateTimeKind.Utc);
                        if (newest == null || dt > newest.Value) newest = dt;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"GetMostRecentLastModifiedText({gameName}) failed: {ex.Message}");
            }
            return newest.HasValue ? "modified " + FormatRelativeTime(newest.Value) : null;
        }

        /// <summary>
        /// Returns the most recent LastModifiedUtc as ticks, for sorting. Falls back to
        /// long.MinValue when no value exists so legacy profiles sort to the bottom of
        /// "Last Modified" mode.
        /// </summary>
        private long GetMostRecentLastModifiedTicks(string gameName)
        {
            long max = long.MinValue;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                foreach (var suffix in new[] { "", "_AC", "_DC" })
                {
                    var key = $"Profile_Game_{gameName}{suffix}";
                    if (settings.Containers.ContainsKey(key)
                        && settings.Containers[key].Values.TryGetValue("LastModifiedUtc", out var ts)
                        && ts is long ticks
                        && ticks > max)
                    {
                        max = ticks;
                    }
                }
            }
            catch { }
            return max;
        }

        // ===== CPU advanced (ToothNClaw port) — card formatting helpers =====

        internal static string GetSchedulingPolicyName(int policy)
        {
            switch (policy)
            {
                case 0: return "Auto";
                case 1: return "Prefer P";
                case 2: return "Prefer E";
                case 3: return "Only P";
                case 4: return "Only E";
                default: return null;            // unset
            }
        }

        internal static string GetFreqLabel(int mhz)
        {
            return mhz <= 0 ? "Unlimited" : $"{mhz} MHz";
        }

        /// <summary>
        /// Builds the standard label/value pairs for a performance &amp; display profile card,
        /// honoring the Save* flags. Shared by the saved-profile cards and the live Global /
        /// Now Playing cards so they all use the same two-column layout and content.
        /// </summary>
        /// <param name="ui">
        /// The widget's own UI state. Only group-C values may be read from here — currently the preset
        /// ComboBox index and the AMD block (dead on the Claw). Everything else has moved to
        /// <paramref name="perf"/>.
        /// </param>
        /// <param name="perf">
        /// The helper's profile for this card (plan §5.3), or null when no snapshot has arrived yet or
        /// the game has no per-game profile. Null renders the group A/B rows as absent rather than as
        /// zeros (plan §6).
        /// </param>
        /// <param name="onBattery">Which power state to resolve — base value when true, plugged-in override when false. See GameProfile.Effective*.</param>
        /// <summary>Frame-generation override as the widget names it: 1 = 2X, 2 = 3X, 3 = 4X.</summary>
        private static string FormatFrameGeneration(int mode) => $"{mode + 1}X";

        /// <summary>
        /// Windowed-VRR mode, in the dropdown's own words. "Disabled" is not among them: it was dropped
        /// on 2026-08-06 because the driver never accepted it, and a stored 2 is folded to Fullscreen on
        /// apply (IgclDirect.NormalizeVrrMode) - so a card naming it would describe a state the machine
        /// is not in.
        /// </summary>
        private static string FormatVrrMode(int mode) => mode == 1 ? "Full + Windowed" : "Fullscreen";

        /// <summary>
        /// The saved scaling setting as one string ("GPU / Stretch"), or null when the profile carries
        /// none. Group and method are one setting split the way Intel splits it, so naming only one half
        /// says nothing: "Stretch" belongs to a different API under Retro than under GPU.
        /// </summary>
        private static string FormatScaling(int? mode, int? method)
        {
            if (!mode.HasValue && !method.HasValue) return null;

            int m = mode ?? 1;
            string group = m == 2 ? "Retro" : m == 0 ? "Display" : "GPU";

            string[] entries = ScalingMethodNames(m);
            int i = method ?? 0;
            return i >= 0 && i < entries.Length ? $"{group} / {entries[i]}" : group;
        }

        /// <summary>
        /// The Intel gaming rows both card builders share: frame generation, VRR, its sub-mode and
        /// scaling. One place, because the saved card and the split card drifted apart before - the
        /// split card is the one people compare against, and a row missing on one side reads as
        /// "this power state does not set it".
        ///
        /// Frame generation prints nothing at "app choice" and the sub-mode nothing at Fullscreen: those
        /// are what the driver does anyway. VRR is different and NOT treated as neutral-when-on - see
        /// below.
        /// </summary>
        private static void AddIntelGamingPairs(List<(string Label, string Value)> pairs, Shared.Data.GameProfile perf)
        {
            int frameGen = perf.IntelFrameGeneration ?? 0;
            if (frameGen > 0) pairs.Add(("Frame Gen", FormatFrameGeneration(frameGen)));

            // Shown whenever the profile STORES a value, in both directions - not only when off.
            // Treating "on" as neutral made a game that deliberately switches VRR back on against a
            // global that has it off show no VRR row at all (reported on Silksong, 2026-08-06): the one
            // card where the setting mattered most was the one that stayed silent about it. Null still
            // prints nothing, because null means the profile never captured VRR and overrides nothing.
            if (perf.IntelVrr.HasValue) pairs.Add(("VRR", perf.IntelVrr.Value == 0 ? "Off" : "On"));

            // Only meaningful while VRR is on - the sub-mode decides where VRR applies, so naming it
            // next to "VRR Off" would state a rule for something that is not running.
            int vrrMode = perf.IntelVrrMode ?? 0;
            if (vrrMode != 0 && (perf.IntelVrr ?? 1) != 0) pairs.Add(("VRR Mode", FormatVrrMode(vrrMode)));

            string scaling = FormatScaling(perf.IntelScalingMode, perf.IntelScalingMethod);
            if (scaling != null) pairs.Add(("Scaling", scaling));
        }

        /// <summary>
        /// The six duty values of a stored fan curve, or null when the profile carries no curve.
        ///
        /// Null and empty both mean "no override" here — a profile that never captured a curve must read
        /// as absent, not as a curve of zeros, because a fan row claiming 0% would look like an instruction
        /// to stop the fan. Only the CPU side is printed unless the two fans differ, in which case both are
        /// named: the card is a summary, and two identical rows would just be noise.
        ///
        /// Deliberately no preset name in front. The store keeps the curve and nothing else, because an
        /// earlier version kept a preset index where 0 ("MSI Default") could not be told apart from
        /// "nothing captured" — and every game without a fan setting then wrote the factory curve over the
        /// user's global one. There is no preset here to show.
        /// </summary>
        private static string FormatFanCurveShort(string curve)
        {
            if (string.IsNullOrWhiteSpace(curve)) return null;

            // "sync|cpuD0..D5|gpuD0..D5"
            string[] parts = curve.Split('|');
            if (parts.Length < 2) return null;

            string cpu = parts[1].Replace(",", "/");
            if (string.IsNullOrWhiteSpace(cpu)) return null;

            bool synced = parts[0] == "1";
            if (synced || parts.Length < 3 || string.IsNullOrWhiteSpace(parts[2])) return cpu;

            string gpu = parts[2].Replace(",", "/");
            return cpu == gpu ? cpu : $"{cpu} / {gpu}";
        }

        /// <summary>
        /// The two fan rows for a card: the curve's NAME ("Quiet Idle", "Custom", …) and the six duty
        /// values under it. Both or neither — a name without the numbers is the thing that made people ask
        /// what "Custom" actually is, and numbers without the name lose the preset the user picked.
        ///
        /// <paramref name="globalFanCurve"/> is the helper-published global curve and wins when set: the
        /// global fan lives in the helper's LocalSettings, so for the global card there is nothing in the
        /// profile to read (see GameProfile.MsiFanCurve). For a per-game card it stays null and the
        /// profile's own override is used — still behind the Fan save flag, because a stored curve that
        /// the flag stops the helper from applying must not be presented as if it were running.
        /// </summary>
        private void AddFanPairs(List<(string Label, string Value)> pairs, Shared.Data.GameProfile perf,
                                 bool onBattery, string globalFanCurve)
        {
            string csv = globalFanCurve;
            if (csv == null)
            {
                if (!SaveFan || perf == null) return;
                csv = perf.EffectiveMsiFanCurve(onBattery);
            }

            string name = DescribeFanCurve(csv);
            string values = FormatFanCurveShort(csv);
            if (name != null) pairs.Add(("Fan", name));
            if (values != null) pairs.Add(("Curve", values));
        }

        private List<(string Label, string Value)> BuildProfileCardPairs(
            PerformanceProfile ui, Shared.Data.GameProfile perf, bool onBattery, string globalFanCurve = null)
        {
            var pairs = new List<(string Label, string Value)>();

            // No snapshot / no per-game profile: show only what the widget legitimately owns. Adding
            // "0 W" here would be the exact bug plan §6 warns about.
            if (perf != null)
            {
                // "TDP Mode" and "TDP Overboost" were dropped from this card (user, 2026-08-01): every
                // TDP value is set with the slider, so the mode name adds nothing, and the Overboost
                // On/Off is already implied by the PL2 row next to it.
                if (SaveTDP)
                {
                    pairs.Add(("TDP", $"{perf.EffectiveTDP(onBattery)}W"));
                    // Resolved, like the TDP above it: the raw fields are the unplugged base values, so
                    // a card read plugged in was quoting the battery overboost next to plugged watts.
                    if (perf.EffectiveTDPBoostEnabled(onBattery))
                        pairs.Add(("PL2 Overboost", $"{perf.EffectiveTDPBoostFPPTWatts(onBattery)}W"));
                }

                if (SaveCPUBoost)
                {
                    pairs.Add(("CPU Boost", perf.EffectiveCPUBoost(onBattery) ? "On" : "Off"));
                    string schedName = GetSchedulingPolicyName(perf.ProcessorSchedulingPolicy);
                    if (perf.ProcessorSchedulingPolicy >= 0 && schedName != null)
                        pairs.Add(("Scheduling", schedName));
                    if (perf.MaxPCoreFreqMHz > 0)
                        pairs.Add(("P-Core Max", GetFreqLabel(perf.MaxPCoreFreqMHz)));
                    if (perf.MaxECoreFreqMHz > 0)
                        pairs.Add(("E-Core Max", GetFreqLabel(perf.MaxECoreFreqMHz)));
                }

                // Intel Display (IGCL) — only show non-neutral channels. Nullable in the helper's store:
                // null means "never captured", which resolves to neutral and therefore prints nothing.
                int saturation = perf.IntelColorSaturation ?? 50;
                int hue        = perf.IntelColorHue ?? 0;
                int contrast   = perf.IntelDisplayContrast ?? 50;
                int brightness = perf.IntelDisplayBrightness ?? 50;
                int gammaX100  = perf.IntelDisplayGamma ?? 100;   // same ×100 encoding, different element name
                int sharpness  = perf.IntelAdaptiveSharpness ?? 0;

                if (saturation != 50) pairs.Add(("Saturation", saturation.ToString()));
                if (hue != 0) pairs.Add(("Hue", hue.ToString()));
                if (contrast != 50) pairs.Add(("Contrast", contrast.ToString()));
                if (brightness != 50) pairs.Add(("Brightness", brightness.ToString()));
                if (gammaX100 != 100) pairs.Add(("Gamma", (gammaX100 / 100.0).ToString("0.00")));
                if (sharpness > 0) pairs.Add(("Sharpness", sharpness.ToString()));

                // Frame generation, VRR, VRR sub-mode and scaling — shown only when the profile
                // overrides the neutral value, same rule as the colour rows above.
                AddIntelGamingPairs(pairs, perf);

                // Fan name + curve. Both rows come from one place now — they used to sit at opposite ends
                // of this method, which is why the card showed "Fan Custom" and "Fan 0/0/40/…" as two rows
                // with the same label and the values above the name they belong to.
                AddFanPairs(pairs, perf, onBattery, globalFanCurve);

                if (SaveFPSLimit)
                {
                    // The separate "FPS Mode" row is gone (user, 2026-08-01): the limiter is named in
                    // brackets after the number instead. No suffix when there is no cap — "Off (Intel)"
                    // would name a limiter that is not limiting anything.
                    string fpsValue = GetFpsValueLabel(perf, onBattery);
                    if (fpsValue != "Off")
                        fpsValue += (perf.EffectiveFpsCapMode(onBattery) == 1) ? " (Intel)" : " (RTSS)";
                    pairs.Add(("FPS Limit", fpsValue));
                }

                // Power Mode from the HELPER's store. It used to read the widget's frozen copy, which
                // is why every card said "Balanced" no matter what the OS was actually set to — the
                // widget stopped writing that field, while OSPowerMode became a real helper-owned
                // int? with commit 35ee315. Absent when the profile configures no mode, like the
                // Intel rows above: the card states what the profile sets, and an unset profile sets
                // nothing (the mode from before the game simply carries on).
                if (SaveOSPowerMode)
                {
                    int? powerMode = perf.EffectiveOSPowerMode(onBattery);
                    if (powerMode.HasValue)
                        pairs.Add(("Power Mode", GetPowerModeShortName(powerMode.Value)));
                }

                // Resolution, short form ("1200p"). Full "WxH" would not fit the narrow card column.
                if (SaveResolution)
                {
                    string resolutionLabel = GetResolutionShortLabel(perf.Resolution);
                    if (resolutionLabel != null)
                        pairs.Add(("Resolution", resolutionLabel));
                }

            }

            // Group C: AMD is dead on the Claw and has no helper counterpart — stays in the widget.
            if (SaveAMDFeatures)
            {
                var amdFeatures = GetAMDFeaturesShortString(ui);
                pairs.Add(("AMD", string.IsNullOrEmpty(amdFeatures) ? "Off" : amdFeatures));
            }

            // The HDR row is gone (user, 2026-08-02): no Claw model has an HDR panel, so it stated a
            // capability the hardware does not have — and it was the last reason the widget's profile
            // copy carried a hardware field at all.

            return pairs;
        }

        // Marks the rows this code appended to a XAML-declared split grid, so a refresh can take exactly
        // those back out again and leave the fixed rows alone.
        private const string SplitExtraRowTag = "SplitExtraRow";

        // How many RowDefinitions each split grid was born with. Captured on first use — trimming back to
        // a hardcoded number would break the moment someone adds a row in the XAML.
        private readonly Dictionary<Grid, int> _splitGridBaseRowCount = new Dictionary<Grid, int>();

        /// <summary>
        /// The rows the two AC/DC grids do NOT already carry in XAML, for one power state. Kept in the same
        /// order as the saved-profile card so the live card and the saved one read alike — the point of the
        /// exercise is comparing them while tweaking (user, 2026-08-04).
        ///
        /// Every row is conditional on the profile actually setting something. A card that lists a value the
        /// profile does not set is worse than a missing row: it claims an override that is not there.
        /// </summary>
        private List<(string Label, string Value)> BuildSplitExtraPairs(
            Shared.Data.GameProfile perf, bool onBattery, string globalFanCurve)
        {
            var pairs = new List<(string Label, string Value)>();
            if (perf == null && globalFanCurve == null) return pairs;

            if (perf != null)
            {
                if (SaveCPUBoost)
                {
                    string schedName = GetSchedulingPolicyName(perf.ProcessorSchedulingPolicy);
                    if (perf.ProcessorSchedulingPolicy >= 0 && schedName != null)
                        pairs.Add(("Scheduling", schedName));
                    if (perf.MaxPCoreFreqMHz > 0) pairs.Add(("P-Core Max", GetFreqLabel(perf.MaxPCoreFreqMHz)));
                    if (perf.MaxECoreFreqMHz > 0) pairs.Add(("E-Core Max", GetFreqLabel(perf.MaxECoreFreqMHz)));
                }

                // Intel display channels — neutral values print nothing, same rule as the saved card.
                int saturation = perf.IntelColorSaturation ?? 50;
                int hue        = perf.IntelColorHue ?? 0;
                int contrast   = perf.IntelDisplayContrast ?? 50;
                int brightness = perf.IntelDisplayBrightness ?? 50;
                int gammaX100  = perf.IntelDisplayGamma ?? 100;
                int sharpness  = perf.IntelAdaptiveSharpness ?? 0;

                if (saturation != 50) pairs.Add(("Saturation", saturation.ToString()));
                if (hue != 0) pairs.Add(("Hue", hue.ToString()));
                if (contrast != 50) pairs.Add(("Contrast", contrast.ToString()));
                if (brightness != 50) pairs.Add(("Brightness", brightness.ToString()));
                if (gammaX100 != 100) pairs.Add(("Gamma", (gammaX100 / 100.0).ToString("0.00")));
                if (sharpness > 0) pairs.Add(("Sharpness", sharpness.ToString()));

                AddIntelGamingPairs(pairs, perf);
            }

            AddFanPairs(pairs, perf, onBattery, globalFanCurve);
            return pairs;
        }

        /// <summary>
        /// Appends the extra rows to a XAML-declared AC/DC grid, one row per label, plugged value in
        /// column 1 and battery value in column 2.
        ///
        /// They go INTO the existing grid rather than into a second one below it. A separate grid would
        /// have its own Auto-sized label column, so its centred value columns would sit a few pixels off
        /// from the rows above — visible straight away in a table that exists to be read column-wise.
        /// </summary>
        private void RenderSplitExtraRows(Grid grid,
                                          List<(string Label, string Value)> plugged,
                                          List<(string Label, string Value)> battery)
        {
            if (grid == null) return;

            if (!_splitGridBaseRowCount.TryGetValue(grid, out int baseRows))
            {
                baseRows = grid.RowDefinitions.Count;
                _splitGridBaseRowCount[grid] = baseRows;
            }

            for (int i = grid.Children.Count - 1; i >= 0; i--)
                if (grid.Children[i] is FrameworkElement fe && (fe.Tag as string) == SplitExtraRowTag)
                    grid.Children.RemoveAt(i);
            while (grid.RowDefinitions.Count > baseRows)
                grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);

            // Union of both states in plugged-first order: a value set on only one side still deserves a
            // row, with a dash opposite it rather than a silently missing line.
            var labels = new List<string>();
            foreach (var p in plugged) if (!labels.Contains(p.Label)) labels.Add(p.Label);
            foreach (var b in battery) if (!labels.Contains(b.Label)) labels.Add(b.Label);
            if (labels.Count == 0) return;

            int row = baseRows;
            foreach (string label in labels)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                string ac = plugged.FirstOrDefault(p => p.Label == label).Value ?? "-";
                string dc = battery.FirstOrDefault(b => b.Label == label).Value ?? "-";

                AddSplitExtraText(grid, row, 0, label, "#AAAAAA", HorizontalAlignment.Left);
                AddSplitExtraText(grid, row, 1, ac, "#FFFFFF", HorizontalAlignment.Center);
                AddSplitExtraText(grid, row, 2, dc, "#FFFFFF", HorizontalAlignment.Center);
                row++;
            }
        }

        // The cells CollapseIdenticalSplitRows hid, per grid, so RestoreCollapsedSplitRows can put them
        // back before the next refresh decides their visibility itself.
        private readonly Dictionary<Grid, List<UIElement>> _splitRowsWeHid = new Dictionary<Grid, List<UIElement>>();

        /// <summary>
        /// Un-hides everything the previous collapse pass hid. MUST run BEFORE a refresh assigns row
        /// visibilities, never after: the refresh is the authority on which rows exist at all, and
        /// restoring on top of its decisions would resurrect rows it had just switched off. Restoring
        /// first and letting it overwrite us costs nothing and cannot fight it.
        /// </summary>
        private void RestoreCollapsedSplitRows(Grid grid)
        {
            if (grid == null || !_splitRowsWeHid.TryGetValue(grid, out var hidden)) return;
            foreach (var element in hidden) element.Visibility = Visibility.Visible;
            hidden.Clear();
        }

        /// <summary>
        /// Takes every row whose two power states carry the SAME value out of the split table and
        /// re-renders it as a compact label/value pair below.
        ///
        /// WHY. A split card is read to answer one question: what does the split actually change? In a
        /// measured case (user, 2026-08-06) nine of fourteen rows held the identical value twice, so the
        /// table spent two thirds of its height restating what the two states agree on and the handful
        /// of real differences was buried among them. Now the table shows only the differences, and the
        /// agreed values sit underneath in the same two-pairs-per-line grid the unsplit card uses.
        ///
        /// Rows the refresh has hidden are left alone — an absent setting is not an agreement.
        /// </summary>
        private void CollapseIdenticalSplitRows(Grid grid, Panel sharedTarget)
        {
            if (grid == null || sharedTarget == null) return;

            try
            {
                sharedTarget.Children.Clear();
                if (!_splitRowsWeHid.TryGetValue(grid, out var hidden))
                    _splitRowsWeHid[grid] = hidden = new List<UIElement>();

                // Row 0 is the "Plugged in / On battery" header and has no label to move.
                var byRow = new Dictionary<int, TextBlock[]>();
                foreach (var child in grid.Children)
                {
                    if (!(child is TextBlock block)) continue;
                    int row = Grid.GetRow(block), column = Grid.GetColumn(block);
                    if (row <= 0 || column < 0 || column > 2) continue;

                    if (!byRow.TryGetValue(row, out var cells)) byRow[row] = cells = new TextBlock[3];
                    cells[column] = block;
                }

                var shared = new List<(string Label, string Value)>();
                foreach (var entry in byRow.OrderBy(e => e.Key))
                {
                    var cells = entry.Value;
                    TextBlock label = cells[0], plugged = cells[1], battery = cells[2];
                    if (label == null || plugged == null || battery == null) continue;

                    // All three must be on screen: a half-hidden row is one the refresh is in the middle
                    // of switching off, not one the two states agree on.
                    if (label.Visibility != Visibility.Visible
                        || plugged.Visibility != Visibility.Visible
                        || battery.Visibility != Visibility.Visible) continue;

                    if (string.IsNullOrWhiteSpace(label.Text)) continue;
                    if (!string.Equals(plugged.Text, battery.Text, StringComparison.Ordinal)) continue;
                    if (string.IsNullOrWhiteSpace(plugged.Text)) continue;

                    shared.Add((label.Text, plugged.Text));
                    foreach (var cell in cells)
                    {
                        if (cell == null) continue;
                        cell.Visibility = Visibility.Collapsed;
                        hidden.Add(cell);
                    }
                }

                if (shared.Count > 0) sharedTarget.Children.Add(RenderPairsGrid(shared));
            }
            catch (Exception ex)
            {
                // Purely presentational. A card that stays tall is still a correct card.
                Logger.Warn($"CollapseIdenticalSplitRows failed: {ex.Message}");
            }
        }

        /// <summary>One cell of an appended split row — same font, colour and margins as the XAML rows,
        /// plus the tag that lets the next refresh find it again.</summary>
        private void AddSplitExtraText(Grid grid, int row, int column, string text, string colorHex,
                                       HorizontalAlignment alignment)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(ParseColor(colorHex)),
                Margin = column == 0 ? new Thickness(0, 3, 8, 0) : new Thickness(0, 3, 0, 0),
                HorizontalAlignment = alignment,
                Tag = SplitExtraRowTag
            };
            Grid.SetRow(block, row);
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
        }

        /// <summary>
        /// Renders label/value pairs into a compact two-column grid (4 grid columns:
        /// label,value,label,value). Used so profile cards spread across the available width
        /// instead of one long single column.
        /// </summary>
        private Grid RenderPairsGrid(List<(string Label, string Value)> pairs)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int rows = (pairs.Count + 1) / 2;
            for (int i = 0; i < rows; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < pairs.Count; i++)
            {
                int row = i / 2;
                int colBase = (i % 2) * 2; // 0 or 2
                AddTextBlock(grid, row, colBase, pairs[i].Label, 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                AddTextBlock(grid, row, colBase + 1, pairs[i].Value, 10, "#FFFFFF", margin: new Thickness(0, 3, 12, 0));
            }
            return grid;
        }

        private static string FormatRelativeTime(DateTime utc)
        {
            var diff = DateTime.UtcNow - utc;
            if (diff.TotalSeconds < 60) return "just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)}w ago";
            if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}mo ago";
            return utc.ToLocalTime().ToString("yyyy-MM-dd");
        }

    }
}
