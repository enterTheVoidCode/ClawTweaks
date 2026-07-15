using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClawTweaksSetup.Core;
using ClawTweaksSetup.Core.Sources;
using ClawTweaksSetup.Navigation;
using ClawTweaksSetup.Ui;

namespace ClawTweaksSetup
{
    /// <summary>
    /// Standalone entry menu shown when the exe is run with nothing next to it (no sibling
    /// .msix/.cer — see App.xaml.cs). Lets the user pick a build from GitHub releases, GitHub test
    /// builds, or Google Drive nightlies, downloads/stages it, then triggers and monitors the actual
    /// install (cert trust → Add-AppxPackage → Game Bar → helper) by repointing
    /// <see cref="SetupContext.AssetRoot"/> — this is the fast-iteration path for an already-onboarded
    /// dev device, not the full first-time wizard (that's still <see cref="MainWindow"/>, reached via
    /// a real release folder, unchanged).
    /// </summary>
    public partial class CenterMenuWindow : Window
    {
        private readonly List<BuildSource> _flat = new List<BuildSource>();
        private readonly Dictionary<BuildSource, Border> _rowElements = new Dictionary<BuildSource, Border>();
        private readonly Dictionary<PadButton, Action> _liveActions = new Dictionary<PadButton, Action>();

        private List<BuildSource> _releases;
        private List<BuildSource> _testBuilds;
        private List<BuildSource> _nightlies;
        private string _releasesError;
        private string _testBuildsError;
        private string _nightliesError;

        /// <summary>Which idle screen ContentHost shows — Confirm/Install are transient overlays
        /// triggered from Browse and don't need their own value here.</summary>
        private enum View { Home, Browse }
        private View _view = View.Home;

        private DeviceDetect.Model _deviceModel = DeviceDetect.Model.Unknown;
        private Version _installedVersion;
        private int _selectedIndex = -1;
        private bool _busy;
        private bool _confirming;
        private bool _blockedForDevice;
        private bool _installFinished;
        private BuildSource _pendingBuild;
        private XInputNavigator _nav;

        public CenterMenuWindow()
        {
            InitializeComponent();
            RenderDeviceBanner(null);
            RenderHome();
            RefreshActionBar();

            Loaded += async (_, __) =>
            {
                _nav = new XInputNavigator(this);
                _nav.ButtonPressed += b => Dispatcher.Invoke(() => Invoke(b));
                _nav.RightStickScrollRequested += d => Dispatcher.Invoke(() =>
                {
                    // Defensive: this fires at up to ~25Hz straight off a live gamepad reading, so any
                    // transient WPF layout hiccup here must never take the whole app down with it.
                    try { ContentScroller.ScrollToVerticalOffset(ContentScroller.VerticalOffset + d); }
                    catch { }
                });
                _nav.Start();

                var deviceTask = Task.Run(() => DeviceDetect.Detect());
                var sourcesTask = RefreshSourcesAsync();
                RenderDeviceBanner(await deviceTask);
                await sourcesTask;
            };
            Closed += (_, __) => _nav?.Dispose();

            // Keyboard fallbacks for desk testing.
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { Invoke(PadButton.B); e.Handled = true; }
                else if (e.Key == Key.Enter) { Invoke(PadButton.A); e.Handled = true; }
                else if (e.Key == Key.Tab) { Invoke(PadButton.X); e.Handled = true; }
                else if (e.Key == Key.F5) { Invoke(PadButton.Y); e.Handled = true; }
                else if (e.Key == Key.Up) { Invoke(PadButton.Up); e.Handled = true; }
                else if (e.Key == Key.Down) { Invoke(PadButton.Down); e.Handled = true; }
                else if (e.Key == Key.Left) { Invoke(PadButton.Left); e.Handled = true; }
                else if (e.Key == Key.Right) { Invoke(PadButton.Right); e.Handled = true; }
            };
        }

        private void Invoke(PadButton b)
        {
            if (_liveActions.TryGetValue(b, out var action)) { action(); return; }
            if (b == PadButton.Up || b == PadButton.Down || b == PadButton.Left || b == PadButton.Right)
                MoveSelection(b);
        }

        #region Device banner
        private void RenderDeviceBanner(DeviceDetect.Result? device)
        {
            if (device == null)
            {
                DeviceBanner.Content = UiHelpers.StatusRow(StatusKind.Working, "Detecting device…", "");
                return;
            }

            var d = device.Value;
            _deviceModel = d.Model;
            RenderCurrentView(); // the build list's per-device gating tags depend on this

            var kind = d.Supported ? StatusKind.Ok : StatusKind.Warning;
            string detail = d.Supported ? "Supported." : "Not a recognized MSI Claw — installing here is untested.";

            var icon = DeviceIcons.For(d.Model);
            if (icon == null)
            {
                DeviceBanner.Content = UiHelpers.StatusRow(kind, d.DisplayName, detail);
                return;
            }

            var image = new Image
            {
                Source = icon, Height = 72, Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = d.DisplayName, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = UiHelpers.Text,
            });
            textStack.Children.Add(new TextBlock
            {
                Text = detail, FontSize = 15, Foreground = UiHelpers.BrushFor(kind), Margin = new Thickness(0, 2, 0, 0),
            });

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(image);
            content.Children.Add(textStack);

            DeviceBanner.Content = new Border
            {
                Background = UiHelpers.Card,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 18, 10),
                Child = content,
            };
        }
        #endregion

        #region Source fetching
        private async Task RefreshSourcesAsync()
        {
            if (_busy) return;
            _busy = true;
            RefreshActionBar();

            _releasesError = _testBuildsError = _nightliesError = null;
            _releases = _testBuilds = _nightlies = null;
            RebuildFlat();
            RenderCurrentView();

            var versionTask = Task.Run(() => PackageInstaller.GetInstalledVersion());
            var ghTask = FetchGitHubAsync();
            var driveTask = FetchDriveAsync();
            await Task.WhenAll(versionTask, ghTask, driveTask);

            _installedVersion = versionTask.Result;
            RenderCurrentView(); // installed version is now known — Home's update banner + Browse's tags show up

            _busy = false;
            RefreshActionBar();
        }

        private async Task FetchGitHubAsync()
        {
            try
            {
                var (releases, testBuilds) = await GitHubReleaseSource.FetchAsync();
                _releases = releases;
                _testBuilds = testBuilds;
            }
            catch (Exception ex)
            {
                _releasesError = _testBuildsError = ex.Message;
            }
            RebuildFlat();
            RenderCurrentView();
        }

        private async Task FetchDriveAsync()
        {
            try { _nightlies = await GoogleDriveSource.FetchAsync(); }
            catch (Exception ex) { _nightliesError = ex.Message; }
            RebuildFlat();
            RenderCurrentView();
        }

        /// <summary>Re-renders whichever idle screen is currently showing — used by the background
        /// fetches so Home's update banner and Browse's list both stay live as data arrives. Skipped
        /// while the Confirm screen is up so a background refresh can't clobber it (Install has its
        /// own separate ContentHost takeover and never runs concurrently with a source refresh).</summary>
        private void RenderCurrentView()
        {
            if (_confirming) return;
            if (_view == View.Home) RenderHome(); else RenderBrowse();
        }

        private void RebuildFlat()
        {
            _flat.Clear();
            if (_releases != null) _flat.AddRange(_releases);
            if (_testBuilds != null) _flat.AddRange(_testBuilds);
            if (_nightlies != null) _flat.AddRange(_nightlies);

            if (_selectedIndex >= _flat.Count) _selectedIndex = _flat.Count - 1;
            if (_selectedIndex < 0 && _flat.Count > 0) _selectedIndex = 0;
        }
        #endregion

        #region Home
        private void GoHome()
        {
            _view = View.Home;
            RenderHome();
            RefreshActionBar();
        }

        private void OpenBrowse()
        {
            _view = View.Browse;
            RenderBrowse();
            RefreshActionBar();
        }

        private void RenderHome()
        {
            ContentHost.Children.Clear();

            var versionStack = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            versionStack.Children.Add(new TextBlock
            {
                Text = _installedVersion != null ? $"Currently installed: {_installedVersion}" : "ClawTweaks is not installed yet.",
                FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = UiHelpers.Text,
            });
            var update = FindNewestGithubUpdate();
            if (update != null)
                versionStack.Children.Add(new TextBlock
                {
                    Text = $"▲ Update available on GitHub: {update.Version} ({update.Origin})",
                    FontSize = 15, Foreground = UiHelpers.Ok, Margin = new Thickness(0, 4, 0, 0),
                });
            ContentHost.Children.Add(versionStack);

            ContentHost.Children.Add(BuildHomeTile(
                "Update & Release", "Browse GitHub releases, test builds, and Drive nightlies to install.",
                clickable: true, onClick: OpenBrowse));

            var placeholders = new UniformGrid { Columns = 3, Margin = new Thickness(0, 14, 0, 0) };
            placeholders.Children.Add(BuildHomeTile("FAQ", "Common questions and troubleshooting.", clickable: false));
            placeholders.Children.Add(BuildHomeTile("Controller Diagnostics", "Run the controller/helper health checks on demand.", clickable: false));
            placeholders.Children.Add(BuildHomeTile("ClawTweaks News", "Announcements from the project.", clickable: false));
            ContentHost.Children.Add(placeholders);
        }

        /// <summary>Highest GitHub release/test-build version above what's currently installed, or
        /// null. Drive nightlies aren't considered — the ask was specifically "available on GitHub".</summary>
        private BuildSource FindNewestGithubUpdate()
        {
            if (_installedVersion == null) return null;

            BuildSource best = null; Version bestVer = null;
            foreach (var b in (_releases ?? Enumerable.Empty<BuildSource>()).Concat(_testBuilds ?? Enumerable.Empty<BuildSource>()))
            {
                if (!TryParseVersion(b.Version, out var v) || v <= _installedVersion) continue;
                if (bestVer == null || v > bestVer) { bestVer = v; best = b; }
            }
            return best;
        }

        private Border BuildHomeTile(string title, string detail, bool clickable, Action onClick = null)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = UiHelpers.Text,
            });
            stack.Children.Add(new TextBlock
            {
                Text = detail, FontSize = 14, Foreground = UiHelpers.Subtle,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            });
            if (!clickable)
                stack.Children.Add(new TextBlock
                {
                    Text = "Coming soon", FontSize = 12, FontWeight = FontWeights.Bold,
                    Foreground = UiHelpers.Accent, Margin = new Thickness(0, 10, 0, 0),
                });

            var border = new Border
            {
                Background = UiHelpers.Card,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20, 18, 20, 18),
                Margin = new Thickness(0, 0, 10, 10),
                BorderBrush = clickable ? UiHelpers.Accent : Brushes.Transparent,
                BorderThickness = new Thickness(clickable ? 2 : 0),
                Opacity = clickable ? 1.0 : 0.55,
                Child = stack,
                Cursor = clickable ? Cursors.Hand : Cursors.Arrow,
            };
            if (clickable) border.MouseLeftButtonUp += (_, __) => onClick?.Invoke();
            return border;
        }
        #endregion

        #region Build list rendering + grid navigation
        private void RenderBrowse()
        {
            ContentHost.Children.Clear();
            _rowElements.Clear();
            AddSection("Releases", _releases, _releasesError);
            AddSection("Test builds", _testBuilds, _testBuildsError);
            AddSection("Nightly builds", _nightlies, _nightliesError);
        }

        private void AddSection(string header, List<BuildSource> items, string error)
        {
            ContentHost.Children.Add(new TextBlock
            {
                Text = header,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = UiHelpers.Text,
                Margin = new Thickness(0, 14, 0, 8),
            });

            if (error != null)
            {
                ContentHost.Children.Add(UiHelpers.StatusRow(StatusKind.Error, "Couldn't load", error));
                return;
            }
            if (items == null)
            {
                ContentHost.Children.Add(UiHelpers.StatusRow(StatusKind.Working, "Loading…", ""));
                return;
            }
            if (items.Count == 0)
            {
                ContentHost.Children.Add(UiHelpers.StatusRow(StatusKind.Info, "Nothing found", ""));
                return;
            }

            bool haveSelection = _selectedIndex >= 0 && _selectedIndex < _flat.Count;
            var selected = haveSelection ? _flat[_selectedIndex] : null;

            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var b in items)
            {
                // items are already sorted newest-first (GitHubReleaseSource/GoogleDriveSource), so
                // only the first card per section gets full contrast — the rest are dimmed.
                bool isNewest = ReferenceEquals(b, items[0]);
                var row = BuildRow(b, ReferenceEquals(b, selected), isNewest);
                _rowElements[b] = row;
                grid.Children.Add(row);
            }
            ContentHost.Children.Add(grid);
        }

        private Border BuildRow(BuildSource b, bool selected, bool isNewest)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = $"{b.Version}  —  {b.Title}",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiHelpers.Text,
                TextWrapping = TextWrapping.Wrap,
            });

            string detail = b.When != default ? b.When.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
            if (!string.IsNullOrEmpty(b.SizeLabel)) detail += (detail.Length > 0 ? "  ·  " : "") + b.SizeLabel;
            if (!string.IsNullOrEmpty(detail))
                stack.Children.Add(new TextBlock
                {
                    Text = detail, FontSize = 14, Foreground = UiHelpers.Subtle,
                    Margin = new Thickness(0, 3, 0, 0),
                });

            string tag = VersionTag(b, out var tagBrush);
            if (tag != null)
                stack.Children.Add(new TextBlock
                {
                    Text = tag, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = tagBrush,
                    Margin = new Thickness(0, 4, 0, 0),
                });

            if (IsBlockedForDevice(b, out string blockReason))
                stack.Children.Add(new TextBlock
                {
                    Text = "⛔ " + blockReason, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = UiHelpers.Error, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
                });

            // Only the newest card per section reads at full contrast; older ones are dimmed so the
            // latest is the obvious pick at a glance. A selected (controller-highlighted) card always
            // shows at full strength regardless, so the highlight itself is never hard to see.
            double baseOpacity = (isNewest || selected) ? 1.0 : 0.55;

            var border = new Border
            {
                Background = UiHelpers.Card,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 10, 10),
                BorderBrush = selected ? UiHelpers.Accent : Brushes.Transparent,
                BorderThickness = new Thickness(selected ? 3 : 0, 0, 0, 0),
                Child = stack,
                Cursor = Cursors.Hand,
                Opacity = _busy ? baseOpacity * 0.5 : baseOpacity,
            };
            border.MouseLeftButtonUp += (_, __) =>
            {
                if (_busy) return;
                _selectedIndex = _flat.IndexOf(b);
                ShowConfirm(b);
            };
            return border;
        }

        /// <summary>Compares a listed build against the currently installed version. Null (no tag)
        /// if nothing's installed yet or the version string doesn't parse.</summary>
        private string VersionTag(BuildSource b, out Brush tagBrush)
        {
            tagBrush = UiHelpers.Subtle;
            if (_installedVersion == null || !TryParseVersion(b.Version, out var v)) return null;

            if (v > _installedVersion) { tagBrush = UiHelpers.Ok; return "▲ Newer than installed"; }
            if (v < _installedVersion) { tagBrush = UiHelpers.Subtle; return "▼ Older than installed"; }
            tagBrush = UiHelpers.Accent;
            return "● Currently installed";
        }

        /// <summary>True if this build predates the detected device's minimum supported version (e.g.
        /// the Claw 8 EX only landed proper support in 0.1.7.63) — that device may only download and
        /// install versions at or above that floor.</summary>
        private bool IsBlockedForDevice(BuildSource b, out string reason)
        {
            reason = null;
            var min = DeviceDetect.MinimumSupportedVersion(_deviceModel);
            if (min == null || !TryParseVersion(b.Version, out var v) || v >= min) return false;
            reason = $"Not supported on this device — needs {min}+";
            return true;
        }

        /// <summary>
        /// D-Pad grid navigation: Left/Right move by one card, Up/Down by a row (stride 2, matching
        /// the 2-column layout above). Treated as one global 2-col grid across all three sections —
        /// a small simplification at section boundaries, but predictable and simple.
        /// </summary>
        private void MoveSelection(PadButton dir)
        {
            if (_view != View.Browse || _busy || _confirming || _flat.Count == 0) return;

            int delta = dir switch
            {
                PadButton.Left => -1,
                PadButton.Right => 1,
                PadButton.Up => -2,
                PadButton.Down => 2,
                _ => 0,
            };
            if (delta == 0) return;

            int next = _selectedIndex < 0 ? 0 : _selectedIndex + delta;
            if (next < 0) next = 0;
            if (next >= _flat.Count) next = _flat.Count - 1;
            if (next == _selectedIndex) return;

            _selectedIndex = next;
            RenderBrowse();
            if (_rowElements.TryGetValue(_flat[_selectedIndex], out var el)) el.BringIntoView();
        }
        #endregion

        #region Footer action bar
        private void RefreshActionBar()
        {
            _liveActions.Clear();
            ActionBar.Children.Clear();

            if (_confirming)
            {
                if (_blockedForDevice) { AddAction(PadButton.B, "Back", true, CancelConfirm); AddScrollHint(); return; }
                AddAction(PadButton.A, "Yes, install", true, ConfirmInstall);
                AddAction(PadButton.B, "Cancel", true, CancelConfirm);
                AddScrollHint(); // the "What's new" section can run long
                return;
            }

            // Nothing is actionable mid-download/install — an empty bar beats four dead-looking chips.
            if (_busy) return;

            // Once an install has run to completion (success or failure), the only thing left to do
            // is close — re-launch the Center for another round rather than silently falling back
            // into the same picker.
            if (_installFinished)
            {
                AddAction(PadButton.B, "Exit", true, () => Application.Current.Shutdown());
                AddScrollHint();
                return;
            }

            if (_view == View.Home)
            {
                AddAction(PadButton.A, "Open Update & Release", true, OpenBrowse);
                AddAction(PadButton.B, "Exit", true, () => Application.Current.Shutdown());
                return;
            }

            AddAction(PadButton.X, "Next", _flat.Count > 1, CycleNext);
            AddAction(PadButton.A, "Install this build", _flat.Count > 0, () =>
            {
                if (_selectedIndex >= 0 && _selectedIndex < _flat.Count) ShowConfirm(_flat[_selectedIndex]);
            });
            AddAction(PadButton.Y, "Refresh", true, () => _ = RefreshSourcesAsync());
            AddAction(PadButton.B, "Back", true, GoHome);
            AddScrollHint();
        }

        /// <summary>Non-interactive footer hint: right stick scrolls the content — added wherever the
        /// current screen can realistically overflow the viewport (the "What's new" section on Confirm
        /// in particular, but Browse's list and the install history can run long too).</summary>
        private void AddScrollHint()
        {
            var glyph = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/xbox/xbox_stick_r_vertical.png", UriKind.Absolute)),
                Width = 44, Height = 44,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            RenderOptions.SetBitmapScalingMode(glyph, BitmapScalingMode.HighQuality);

            var label = new TextBlock
            {
                Text = "Scroll", FontSize = 22, VerticalAlignment = VerticalAlignment.Center,
                Foreground = UiHelpers.Subtle,
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(glyph);
            content.Children.Add(label);
            ActionBar.Children.Add(new Border { Padding = new Thickness(10, 0, 10, 0), Child = content });
        }

        private void AddAction(PadButton b, string label, bool enabled, Action action)
        {
            if (enabled) _liveActions[b] = action;
            ActionBar.Children.Add(ActionBarBuilder.BuildChip(b, label, enabled, action));
        }

        private void CycleNext()
        {
            if (_flat.Count == 0) return;
            _selectedIndex = (_selectedIndex + 1) % _flat.Count;
            RenderBrowse();
            if (_rowElements.TryGetValue(_flat[_selectedIndex], out var el)) el.BringIntoView();
        }
        #endregion

        #region Confirm
        private void ShowConfirm(BuildSource build)
        {
            if (_busy || build == null) return;
            _pendingBuild = build;
            _confirming = true;

            ContentHost.Children.Clear();

            if (IsBlockedForDevice(build, out string blockReason))
            {
                _blockedForDevice = true;
                ContentHost.Children.Add(UiHelpers.Title("Not supported on this device"));
                ContentHost.Children.Add(UiHelpers.Body($"{build.Version} — {build.Origin} — {build.Title}"));
                ContentHost.Children.Add(UiHelpers.StatusRow(StatusKind.Error, "Blocked", blockReason));
                RefreshActionBar();
                return;
            }
            _blockedForDevice = false;

            ContentHost.Children.Add(UiHelpers.Title($"Install {build.Version}?"));
            ContentHost.Children.Add(UiHelpers.Body($"{build.Origin} — {build.Title}"));

            if (_installedVersion != null && TryParseVersion(build.Version, out var selVer) && selVer < _installedVersion)
            {
                ContentHost.Children.Add(UiHelpers.StatusRow(StatusKind.Warning, "Downgrade",
                    $"Currently installed: {_installedVersion} — this installs an OLDER version ({selVer})."));
            }

            // "What's new" — only Releases/Test builds carry a GitHub release body; nightlies don't.
            if (!string.IsNullOrWhiteSpace(build.Body))
            {
                ContentHost.Children.Add(new TextBlock
                {
                    Text = "What's new", FontSize = 18, FontWeight = FontWeights.Bold,
                    Foreground = UiHelpers.Text, Margin = new Thickness(0, 16, 0, 6),
                });
                var notes = new StackPanel();
                ReleaseNotes.RenderInto(notes, build.Body);
                ContentHost.Children.Add(notes);
            }

            RefreshActionBar();
        }

        private void CancelConfirm()
        {
            _confirming = false;
            _blockedForDevice = false;
            _pendingBuild = null;
            RenderBrowse();
            RefreshActionBar();
        }

        private void ConfirmInstall()
        {
            var build = _pendingBuild;
            _confirming = false;
            _pendingBuild = null;
            if (build == null) { RenderBrowse(); RefreshActionBar(); return; }
            _ = InstallSelectedAsync(build);
        }

        private static bool TryParseVersion(string s, out Version v)
        {
            v = null;
            if (string.IsNullOrEmpty(s)) return false;
            return Version.TryParse(s.TrimStart('v', 'V'), out v);
        }
        #endregion

        #region Install
        private async Task InstallSelectedAsync(BuildSource build)
        {
            if (_busy) return;
            _busy = true;
            _installFinished = false;
            RefreshActionBar();

            // A stale helper from a previous run can still be alive here (Add-AppxPackage's
            // -ForceApplicationShutdown doesn't reach it — it's a plain exe, not an app-lifecycle
            // process). Snapshot its PID(s) now so "the fresh helper came up" later means a PID
            // outside this set, not just "some helper process exists" — and so any that are still
            // hanging around once the new one is confirmed up can be cleaned up.
            int[] priorHelperPids = HelperControl.GetHelperPids();
            Version previousVersion = _installedVersion; // cached from the last RefreshSourcesAsync

            // 2-column layout: left = progress/log, right = live status (used for the UAC-wait card
            // below — visible next to the Game Bar overlay when it opens).
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();
            Grid.SetColumn(left, 0);
            layout.Children.Add(left);

            var right = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
            Grid.SetColumn(right, 1);
            layout.Children.Add(right);

            ContentHost.Children.Clear();
            ContentHost.Children.Add(layout);

            left.Children.Add(UiHelpers.Title($"Installing {build.Version}"));
            left.Children.Add(UiHelpers.Body($"{build.Origin} — {build.Title}"));

            var progressBar = new ProgressBar
            {
                Height = 14, Minimum = 0, Maximum = 100, Value = 0,
                Foreground = UiHelpers.Accent,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x38)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 8, 0, 8),
                IsIndeterminate = true,
            };
            var logPanel = new StackPanel { Margin = new Thickness(2, 4, 0, 0) };
            left.Children.Add(progressBar);
            left.Children.Add(logPanel);

            var statusPanel = new ContentControl
            {
                Focusable = false,
                Content = BuildBigStatusCard(StatusKind.Working, "Preparing…", "Download and package install in progress."),
            };
            var historyPanel = new StackPanel();
            right.Children.Add(new TextBlock
            {
                Text = "Status", FontSize = 15, Foreground = UiHelpers.Subtle, Margin = new Thickness(0, 0, 0, 8),
            });
            right.Children.Add(statusPanel);
            right.Children.Add(historyPanel);

            // Each step gets its own row: a ✓ once it's done, a pulsing "…" badge while it's the
            // current one — so the user can tell at a glance exactly what's finished vs. still running,
            // instead of a flat scroll of text.
            ContentControl currentLogBadge = null;

            UIElement BuildLogRow(string text, out ContentControl badge)
            {
                badge = new ContentControl
                {
                    Width = 20, Height = 20, Focusable = false,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
                    Content = UiHelpers.Badge(StatusKind.Working, 20),
                };
                var pulse = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0, To = 0.35, Duration = TimeSpan.FromMilliseconds(700),
                    AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                };
                badge.BeginAnimation(OpacityProperty, pulse);

                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                row.Children.Add(badge);
                row.Children.Add(new TextBlock
                {
                    Text = text, FontSize = 15, Foreground = UiHelpers.Subtle,
                    Margin = new Thickness(8, 0, 0, 0), TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                return row;
            }

            void FinishLogRow(ContentControl badge, bool ok)
            {
                if (badge == null) return;
                badge.BeginAnimation(OpacityProperty, null);
                badge.Opacity = 1.0;
                badge.Content = UiHelpers.Badge(ok ? StatusKind.Ok : StatusKind.Error, 20);
            }

            // Dispatcher.Invoke matters here: PackageInstaller.Install runs inside Task.Run further
            // down and calls this synchronously from a thread-pool thread, not just via awaited
            // continuations — same guard InstallPhase.Log already uses for the same reason.
            void Log(string s) => Dispatcher.Invoke(() =>
            {
                FinishLogRow(currentLogBadge, true);
                logPanel.Children.Add(BuildLogRow(s, out currentLogBadge));
            });
            var progress = new Progress<int>(p =>
            {
                progressBar.IsIndeterminate = false;
                progressBar.Value = p;
            });

            try
            {
                bool certTrusted = await Task.Run(() => CertInstaller.IsKnownCertAlreadyTrusted());
                string staged = await BuildDownloader.DownloadAndStageAsync(build, certTrusted, Log, progress);
                SetupContext.AssetRoot = staged;

                // Straight into the actual install from here — no manual wizard walk-through. The
                // Center menu exists for fast iteration on an already-onboarded dev device: pick a
                // build, the tool triggers the install and watches it succeed. Ported 1:1 from
                // InstallPhase.InstallAsync (cert trust → Add-AppxPackage → Game Bar → wait for helper);
                // the release-folder path still gets the full guided wizard via MainWindow, unchanged.
                progressBar.IsIndeterminate = true;
                bool ok = true;

                string cer = CertInstaller.FindSiblingCer();
                if (cer != null)
                {
                    string thumb = CertInstaller.ThumbprintOf(cer);
                    if (!CertInstaller.IsTrusted(thumb))
                    {
                        Log("Trusting signing certificate…");
                        ok &= await Task.Run(() => CertInstaller.Install(cer));
                    }
                    else Log("Certificate already trusted.");
                }

                string pkg = PackageInstaller.FindPackage();
                if (pkg == null)
                {
                    Log("No installable package found after staging.");
                    ok = false;
                }
                else if (ok)
                {
                    var deps = PackageInstaller.FindDependencies(pkg);
                    ok &= await Task.Run(() => PackageInstaller.Install(pkg, deps, Log));
                }

                if (ok)
                {
                    Log("Opening Game Bar — the ClawTweaks widget will start the helper…");
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = 0;
                    var helperProgress = new Progress<int>(p => progressBar.Value = p);

                    bool up = await RunPostInstallMonitorAsync(
                        priorHelperPids, previousVersion != null, helperProgress, statusPanel, historyPanel);
                    progressBar.Value = 100;

                    Log(up
                        ? $"{DescribeTransition(previousVersion, build.Version)} — helper is up and running."
                        : "Installed, but the helper did not appear in time — open the Game Bar (Win+G) manually.");
                }

                FinishLogRow(currentLogBadge, ok);
                _busy = false;
                _installFinished = true;
                RefreshActionBar();
            }
            catch (Exception ex)
            {
                Log("Error: " + ex.Message);
                FinishLogRow(currentLogBadge, false);
                _busy = false;
                _installFinished = true;
                RefreshActionBar();
            }
        }

        /// <summary>Human-readable version transition for the final status ("Updated X → Y", not just "Installed Y").</summary>
        private static string DescribeTransition(Version previous, string selectedVersion)
        {
            if (previous == null) return $"Installed {selectedVersion}";
            if (!TryParseVersion(selectedVersion, out var selected)) return $"Installed {selectedVersion}";
            if (selected > previous) return $"Updated {previous} → {selected}";
            if (selected < previous) return $"Downgraded {previous} → {selected}";
            return $"Reinstalled {selected}";
        }

        /// <summary>
        /// Everything that happens after Add-AppxPackage succeeds: open the Game Bar (auto-closes
        /// itself after ~1s so the user sees this panel, not just the overlay), wait for the FRESH
        /// helper (surfacing the UAC prompt prominently if the helper's own first-run --setup needs
        /// one), check for and remove any stale helper left over from before the update, then run the
        /// controller diagnostic (HW vs. virtual mode). Settles for a fixed ~20s total before
        /// declaring the install done, so nothing flaky shows up right after. Every step is shown live
        /// in <paramref name="statusPanel"/> (the big current-step card, the only place that keeps the
        /// checkmark-in-circle look) and appended to <paramref name="historyPanel"/> as plain, flush
        /// text lines (a permanent log of what happened, deliberately no badge/circle of its own).
        /// </summary>
        private static async Task<bool> RunPostInstallMonitorAsync(
            int[] priorHelperPids, bool isUpdate, IProgress<int> progress, ContentControl statusPanel, StackPanel historyPanel)
        {
            void AddHistory(bool ok, string title, string detail)
            {
                var stack = new StackPanel { Margin = new Thickness(2, 8, 0, 0) };
                stack.Children.Add(new TextBlock
                {
                    Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold,
                    Foreground = ok ? UiHelpers.Ok : UiHelpers.Warn,
                });
                if (!string.IsNullOrEmpty(detail))
                    stack.Children.Add(new TextBlock
                    {
                        Text = detail, FontSize = 13, Foreground = UiHelpers.Subtle,
                        Margin = new Thickness(14, 2, 0, 0), TextWrapping = TextWrapping.Wrap,
                    });
                historyPanel.Children.Add(stack);
            }

            var totalSw = System.Diagnostics.Stopwatch.StartNew();

            HelperControl.OpenGameBar();
            await Task.Delay(1000);
            HelperControl.CloseGameBarBestEffort(); // best-effort — the big UAC card below is the fallback if this doesn't land

            // 1) Wait for the FRESH helper, running ELEVATED — surfacing the UAC prompt prominently
            // while we wait. A new PID can appear before its own elevation request is even shown (an
            // initial unelevated instance that then requests --setup elevation itself), so "PID
            // exists" alone isn't proof the UAC was confirmed. Checking TokenElevation is the actual,
            // verifiable signal instead of guessing from timing.
            bool FreshHelperUp() => HelperControl.GetHelperPids()
                .Any(pid => !priorHelperPids.Contains(pid) && HelperControl.IsProcessElevated(pid));

            statusPanel.Content = BuildBigStatusCard(StatusKind.Working, "Starting…",
                "Waiting for the ClawTweaks helper to start.");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool? lastUacShowing = false;
            bool up = false;
            while (sw.ElapsedMilliseconds < 60000)
            {
                if (FreshHelperUp()) { up = true; break; }

                bool uacShowing = HelperControl.IsUacPromptShowing();
                if (uacShowing != lastUacShowing)
                {
                    statusPanel.Content = uacShowing
                        ? BuildBigStatusCard(StatusKind.Warning, "Waiting for UAC…",
                            "A confirmation prompt appeared — please confirm it to continue.")
                        : BuildBigStatusCard(StatusKind.Working, "Starting…",
                            "Waiting for the ClawTweaks helper to start.");
                    lastUacShowing = uacShowing;
                }

                progress?.Report((int)Math.Min(70, sw.ElapsedMilliseconds * 70 / 60000));
                await Task.Delay(300);
            }

            if (!up)
            {
                statusPanel.Content = BuildBigStatusCard(StatusKind.Warning, "Timed out",
                    "Open the Game Bar manually (Win+G).");
                return false;
            }

            AddHistory(true, isUpdate ? "New update — background helper started" : "Installed — background helper started", "");
            progress?.Report(70);

            // 2) Duplicate-helper check: a stale instance from before the update often exits on its
            // own within a few seconds once it notices the fresh one; give it that grace period, then
            // forcibly remove anything still left over so only the new helper keeps running.
            statusPanel.Content = BuildBigStatusCard(StatusKind.Working, "Checking for duplicate helpers…", "");
            bool AnyStaleAlive() => priorHelperPids.Any(IsProcessAlive);

            if (priorHelperPids.Length == 0 || !AnyStaleAlive())
            {
                AddHistory(true, "No duplicate helper detected", "");
            }
            else
            {
                for (int i = 0; i < 5 && AnyStaleAlive(); i++)
                    await Task.Delay(1000);

                if (AnyStaleAlive())
                {
                    statusPanel.Content = BuildBigStatusCard(StatusKind.Warning, "Removing leftover helper…",
                        "A helper from before the update is still running.");
                    int killed = 0;
                    foreach (var pid in priorHelperPids)
                    {
                        if (!IsProcessAlive(pid)) continue;
                        try { System.Diagnostics.Process.GetProcessById(pid).Kill(); killed++; }
                        catch { }
                    }
                    AddHistory(true, "No duplicate helper detected", $"Removed {killed} leftover helper process(es).");
                }
                else
                {
                    AddHistory(true, "No duplicate helper detected", "The old helper exited on its own.");
                }
            }
            progress?.Report(82);

            // 3) Controller diagnostic — same probe ControllerPhase/FinalizePhase already use during
            // first-time setup, reused here rather than reinvented. Retries a few times with a short
            // delay since the helper can take a moment after starting to actually mount the controller.
            statusPanel.Content = BuildBigStatusCard(StatusKind.Working, "Checking controller mode…", "");
            var (controllerOk, ctrlTitle, ctrlDetail, ctrlCause) = await ProbeControllerModeAsync();
            if (controllerOk) AddHistory(true, ctrlTitle, ctrlDetail);
            else AddHistory(false, ctrlTitle, ctrlCause);
            progress?.Report(95);

            // 4) Settle: give everything ~20s total (from opening the Game Bar) before declaring victory.
            int remainingMs = 20000 - (int)totalSw.ElapsedMilliseconds;
            if (remainingMs > 0) await Task.Delay(remainingMs);

            statusPanel.Content = BuildBigStatusCard(StatusKind.Ok, "Installation complete", "No restart necessary.");
            return true;
        }

        /// <summary>
        /// Reuses ControllerHealth.Probe() (the same PnP/XInput probe ControllerPhase/FinalizePhase run
        /// during first-time setup) to report whether the Claw is running in HW controller mode (native
        /// XInput surface, no overlay) or virtual controller mode (a VIIPER/ViGEm pad is mounted). The
        /// helper can take a moment after starting to actually mount the controller, so this retries a
        /// few times with a short delay before giving up and reporting why.
        /// </summary>
        private static async Task<(bool ok, string title, string detail, string cause)> ProbeControllerModeAsync()
        {
            HealthResult result = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                result = await Task.Run(() => ControllerHealth.Probe());
                if (result.ClawPresent)
                {
                    if (result.VirtualPadCount > 0)
                    {
                        string name = result.VirtualPadName ?? "Virtual pad";
                        return (true, "Virtual controller mode detected", $"{name} active and running.", null);
                    }
                    return (true, "HW controller mode detected", "MSI HW Controller active and running.", null);
                }
                if (attempt < 3) await Task.Delay(1500);
            }

            string cause = result.Problems.Count > 0 ? result.Problems[0]
                : (result.Warnings.Count > 0 ? result.Warnings[0] : "Claw controller not detected.");
            return (false, "Controller mode unknown", null, cause);
        }

        private static bool IsProcessAlive(int pid)
        {
            try { return !System.Diagnostics.Process.GetProcessById(pid).HasExited; }
            catch { return false; }
        }

        /// <summary>
        /// Large, colour-highlighted "what's happening right now" card for the install's right column
        /// — deliberately much bigger than the regular <see cref="UiHelpers.StatusRow"/> rows, since
        /// this is the one thing the user needs to notice even glancing over from behind the Game Bar
        /// overlay (the UAC prompt in particular). Pulses the badge while <see cref="StatusKind.Working"/>
        /// as a lightweight stand-in for a spinner.
        /// </summary>
        private static Border BuildBigStatusCard(StatusKind kind, string title, string detail)
        {
            var accent = UiHelpers.BrushFor(kind);
            var badge = UiHelpers.Badge(kind, 56);
            if (kind == StatusKind.Working)
            {
                var pulse = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0, To = 0.35, Duration = TimeSpan.FromMilliseconds(700),
                    AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                };
                badge.BeginAnimation(OpacityProperty, pulse);
            }

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            text.Children.Add(new TextBlock
            {
                Text = title, FontSize = 26, FontWeight = FontWeights.Bold,
                Foreground = UiHelpers.Text, TextWrapping = TextWrapping.Wrap,
            });
            if (!string.IsNullOrEmpty(detail))
                text.Children.Add(new TextBlock
                {
                    Text = detail, FontSize = 16, Foreground = UiHelpers.Subtle,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
                });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(badge);
            row.Children.Add(text);

            var accentColor = ((SolidColorBrush)accent).Color;
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B)),
                BorderBrush = accent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(22, 20, 22, 20),
                Margin = new Thickness(0, 0, 0, 12),
                Child = row,
            };
        }
        #endregion
    }
}
