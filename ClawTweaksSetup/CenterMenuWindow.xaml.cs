using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// Selects, stages, installs, and monitors remote builds for onboarded devices.
    /// Release folders use <see cref="MainWindow"/> for first-time setup.
    /// </summary>
    public partial class CenterMenuWindow : Window
    {
        private readonly List<BuildSource> _flat = new List<BuildSource>();
        private readonly Dictionary<BuildSource, Border> _rowElements = new Dictionary<BuildSource, Border>();
        private readonly List<WrapPanel> _browseCardPanels = new List<WrapPanel>();
        private readonly Dictionary<PadButton, Action> _liveActions = new Dictionary<PadButton, Action>();

        private List<BuildSource> _releases;
        private List<BuildSource> _testBuilds;
        private List<BuildSource> _nightlies;
        private string _releasesError;
        private string _testBuildsError;
        private string _nightliesError;

        /// <summary>Identifies the persistent view beneath transient install screens.</summary>
        private enum View { Home, Browse, Onboarding }
        private View _view = View.Home;

        private DeviceDetect.Model _deviceModel = DeviceDetect.Model.Unknown;
        private Version _installedVersion;
        private bool _installedVersionChecked;
        private SetupVersionCheck.Result _setupVersionCheck;
        private WindowsChannelDetect.Result _windowsChannel;
        private int _selectedIndex = -1;
        private bool _busy;
        private bool _confirming;
        private bool _blockedForDevice;
        private bool _installFinished;
        private BuildSource _pendingBuild;
        private XInputNavigator _nav;

        private readonly OnboardingRunner _onboarding = new OnboardingRunner();
        private readonly bool _startOnboardingOnLoad;
        private readonly bool _previewInstallOnLoad;

        public CenterMenuWindow(bool startOnboarding = false, bool previewInstall = false)
        {
            _startOnboardingOnLoad = startOnboarding;
            _previewInstallOnLoad = previewInstall;
            InitializeComponent();
            ModernWindow.Apply(this, edgeMargin: 12);

            _onboarding.StepsChanged += () => Dispatcher.Invoke(RenderOnboardingIfActive);

            SetupVersionLabel.Text = "CTW Center v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?");
            SizeChanged += (_, __) => UpdateShellLayout();
            ContentHost.SizeChanged += (_, __) =>
            {
                if (_view == View.Browse) UpdateBrowseCardWidths();
            };
            UpdateShellLayout();
            RenderDeviceBanner(null);
            RenderHome();
            RefreshActionBar();

            Loaded += async (_, __) =>
            {
                _nav = new XInputNavigator(this);
                _nav.ButtonPressed += b => Dispatcher.Invoke(() => Invoke(b));
                _nav.RightStickScrollRequested += d => Dispatcher.Invoke(() =>
                {
                    // Gamepad polling must tolerate transient layout failures.
                    try { ContentScroller.ScrollToVerticalOffset(ContentScroller.VerticalOffset + d); }
                    catch { }
                });
                _nav.Start();

#if DEBUG
                // Debug-only install preview without machine changes.
                if (_previewInstallOnLoad)
                {
                    RenderDeviceBanner(await Task.Run(() => DeviceDetect.Detect()));
                    await InstallSelectedAsync(new BuildSource
                    {
                        Origin = "Test build",
                        Version = "0.1.8.52",
                        Title = "0.1.9 Preview 1 — EX Gyro, KB5101684 install fix, customizable horizontal OSD",
                    }, previewOnly: true);
                    return;
                }
#endif

                var deviceTask = Task.Run(() => DeviceDetect.Detect());
                var sourcesTask = RefreshSourcesAsync();
                var setupVersionTask = SetupVersionCheck.CheckAsync();
                var windowsChannelTask = Task.Run(() => WindowsChannelDetect.Detect());
                RenderDeviceBanner(await deviceTask);
                await sourcesTask;

                _setupVersionCheck = await setupVersionTask;
                _windowsChannel = await windowsChannelTask;
                RenderCurrentView(); // Refresh warnings after source discovery.

                // The completed release-folder wizard can enter onboarding directly.
                if (_startOnboardingOnLoad) OpenOnboarding();
            };
            Closed += (_, __) => _nav?.Dispose();

            // Keyboard fallbacks for desktop testing.
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

        /// <summary>Stacks header content below the compact-width breakpoint.</summary>
        private void UpdateShellLayout()
        {
            if (ShellHeaderGrid == null || BrandBlock == null || DeviceBanner == null) return;

            bool narrow = ActualWidth > 0 && ActualWidth < 720;
            ShellHeaderGrid.RowDefinitions.Clear();
            ShellHeaderGrid.ColumnDefinitions.Clear();

            if (narrow)
            {
                ShellHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition
                    { Width = new GridLength(1, GridUnitType.Star) });
                ShellHeaderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                ShellHeaderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(BrandBlock, 0);
                Grid.SetColumn(BrandBlock, 0);
                Grid.SetRow(DeviceBanner, 1);
                Grid.SetColumn(DeviceBanner, 0);
                DeviceBanner.Margin = new Thickness(0, 12, 0, 0);
            }
            else
            {
                ShellHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition
                    { Width = new GridLength(1, GridUnitType.Star) });
                ShellHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition
                    { Width = new GridLength(1, GridUnitType.Star) });
                ShellHeaderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(BrandBlock, 0);
                Grid.SetColumn(BrandBlock, 0);
                Grid.SetRow(DeviceBanner, 0);
                Grid.SetColumn(DeviceBanner, 1);
                DeviceBanner.Margin = new Thickness(16, 0, 0, 0);
            }
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
                DeviceBanner.Content = BuildCompactDeviceBanner(
                    StatusKind.Working, "Detecting device…", "");
                return;
            }

            var d = device.Value;
            _deviceModel = d.Model;
            RenderCurrentView(); // Refresh device-specific build gates.

            var kind = d.Supported ? StatusKind.Ok : StatusKind.Warning;
            string detail = d.Supported ? "Supported." : "Not a recognized MSI Claw — installing here is untested.";

            var icon = DeviceIcons.For(d.Model);
            if (icon == null)
            {
                DeviceBanner.Content = BuildCompactDeviceBanner(kind, d.DisplayName, detail);
                return;
            }

            var image = new Image
            {
                Source = icon, Height = 48, Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = d.DisplayName, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = UiHelpers.Text,
                TextWrapping = TextWrapping.Wrap,
            });
            textStack.Children.Add(new TextBlock
            {
                Text = detail, FontSize = 13, Foreground = UiHelpers.BrushFor(kind), Margin = new Thickness(0, 1, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });

            // Grid constrains long device-status text so wrapping works.
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(image, 0);
            Grid.SetColumn(textStack, 1);
            content.Children.Add(image);
            content.Children.Add(textStack);

            DeviceBanner.Content = new Border
            {
                Background = UiHelpers.Card,
                BorderBrush = (Brush)Application.Current.Resources["StrokeBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 14, 8),
                Child = content,
            };
        }

        /// <summary>Builds compact header status for detecting or unknown hardware.</summary>
        private static Border BuildCompactDeviceBanner(StatusKind kind, string title, string detail)
        {
            var badge = UiHelpers.Badge(kind, 24);
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.Margin = new Thickness(0, 0, 10, 0);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiHelpers.Text,
                TextWrapping = TextWrapping.Wrap,
            });
            if (!string.IsNullOrEmpty(detail))
                text.Children.Add(new TextBlock
                {
                    Text = detail,
                    FontSize = 13,
                    Foreground = UiHelpers.BrushFor(kind),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 0),
                });

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(badge, 0);
            Grid.SetColumn(text, 1);
            content.Children.Add(badge);
            content.Children.Add(text);

            return new Border
            {
                Background = UiHelpers.Card,
                BorderBrush = UiHelpers.BrushFor(kind),
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 14, 8),
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
            _installedVersionChecked = true;
            RenderCurrentView(); // Refresh version-dependent status.

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

        /// <summary>Refreshes the active idle view without replacing transient content.</summary>
        private void RenderCurrentView()
        {
            if (_confirming) return;
            switch (_view)
            {
                case View.Home: RenderHome(); break;
                case View.Onboarding: RenderOnboarding(); break;
                default: RenderBrowse(); break;
            }
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

            if (_windowsChannel?.IsInsider == true)
                ContentHost.Children.Add(UiHelpers.StatusRow(StatusKind.Warning, "Windows Insider Preview detected",
                    $"You're on the \"{_windowsChannel.ChannelName}\" channel — the install routine is currently known not to work correctly on Insider builds."));

            var versionStack = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            if (!_installedVersionChecked)
            {
                // Avoid a false "not installed" state while PowerShell is still querying.
                var checkingRow = new StackPanel { Orientation = Orientation.Horizontal };
                checkingRow.Children.Add(new ContentControl
                {
                    Width = 22, Height = 22, Focusable = false,
                    VerticalAlignment = VerticalAlignment.Center,
                    Content = UiHelpers.Badge(StatusKind.Working, 22),
                });
                checkingRow.Children.Add(new TextBlock
                {
                    Text = "Checking installed ClawTweaks version…", FontSize = 18,
                    FontWeight = FontWeights.SemiBold, Foreground = UiHelpers.Subtle,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
                });
                versionStack.Children.Add(checkingRow);
            }
            else
            {
                bool setupOutdated = _setupVersionCheck?.Outdated == true;
                var runningCenterVersion = _setupVersionCheck?.RunningVersion
                    ?? Assembly.GetExecutingAssembly().GetName().Version;
                string setupDetail = setupOutdated
                    ? $"This Setup build is outdated. {_setupVersionCheck.Message} " +
                      $"Running CTW Center {runningCenterVersion}; requires {_setupVersionCheck.MinimumVersion}+."
                    : _setupVersionCheck != null
                        ? $"CTW Center {runningCenterVersion} is up to date."
                        : $"CTW Center {runningCenterVersion}.";

                // Keep installed-app and Center-version states in one card.
                versionStack.Children.Add(UiHelpers.StatusRow(
                    setupOutdated ? StatusKind.Warning : (_installedVersion != null ? StatusKind.Ok : StatusKind.Info),
                    _installedVersion != null
                        ? $"Currently installed: ClawTweaks {_installedVersion}"
                        : "ClawTweaks is not installed yet.",
                    setupDetail));
            }
            var update = FindNewestGithubUpdate();
            if (update != null)
                versionStack.Children.Add(new TextBlock
                {
                    Text = $"▲ Update available on GitHub: {update.Version} ({update.Origin})",
                    FontSize = 15, Foreground = UiHelpers.Ok, Margin = new Thickness(0, 4, 0, 0),
                });
            ContentHost.Children.Add(versionStack);

            var mainRow = new Grid();
            mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var releaseTile = BuildHomeTile(
                "Update & Release", "Browse GitHub releases, test builds, and Drive nightlies to install.",
                clickable: true, onClick: OpenBrowse);
            Grid.SetColumn(releaseTile, 0);
            releaseTile.Margin = new Thickness(0, 0, 7, 10);
            mainRow.Children.Add(releaseTile);

            var onboardingTile = BuildHomeTile(
                "Onboarding", "Center M, virtual controller, Game Bar auto-jump.",
                clickable: true, onClick: OpenOnboarding);
            Grid.SetColumn(onboardingTile, 1);
            onboardingTile.Margin = new Thickness(7, 0, 0, 10);
            mainRow.Children.Add(onboardingTile);

            ContentHost.Children.Add(mainRow);

            var placeholders = new UniformGrid { Columns = 3, Margin = new Thickness(0, 14, 0, 0) };
            placeholders.Children.Add(BuildHomeTile("FAQ", "Common questions and troubleshooting.", clickable: false));
            placeholders.Children.Add(BuildHomeTile("Controller Diagnostics", "Run the controller/helper health checks on demand.", clickable: false));
            placeholders.Children.Add(BuildHomeTile("ClawTweaks News", "Announcements from the project.", clickable: false));
            ContentHost.Children.Add(placeholders);
        }

        /// <summary>Returns the newest GitHub build newer than the installed version.</summary>
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

        /// <summary>Opens onboarding and refreshes helper-reported step state.</summary>
        private void OpenOnboarding()
        {
            _view = View.Onboarding;
            RenderOnboarding();
            RefreshActionBar();
            Dispatcher.BeginInvoke(new Action(ContentScroller.ScrollToTop));
            _ = _onboarding.RefreshStatusAsync(msg => Dispatcher.Invoke(RenderOnboardingIfActive));
        }

        /// <summary>Returns whether an onboarding response still targets the active view.</summary>
        private void RenderOnboardingIfActive()
        {
            if (_view != View.Onboarding || _confirming || _busy) return;
            RenderOnboarding();
            RefreshActionBar();
        }

        /// <summary>Renders helper-reported onboarding steps and their actions.</summary>
        private void RenderOnboarding()
        {
            ContentHost.Children.Clear();
            var page = new StackPanel
            {
                MaxWidth = 820,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ContentHost.Children.Add(page);

            var pageHeader = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            pageHeader.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            pageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var heading = new StackPanel();
            heading.Children.Add(new TextBlock
            {
                Text = "Onboarding",
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiHelpers.Text,
            });
            heading.Children.Add(new TextBlock
            {
                Text = "Set up the essential ClawTweaks features.",
                FontSize = 14,
                Foreground = UiHelpers.Subtle,
                Margin = new Thickness(0, 3, 0, 0),
            });
            Grid.SetColumn(heading, 0);
            pageHeader.Children.Add(heading);

            if (_onboarding.IsConnecting)
            {
                var connectingRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                connectingRow.Children.Add(new ContentControl
                {
                    Width = 16, Height = 16, Focusable = false, VerticalAlignment = VerticalAlignment.Center,
                    Content = UiHelpers.Badge(StatusKind.Working, 16),
                });
                connectingRow.Children.Add(new TextBlock
                {
                    Text = "Connecting…", FontSize = 12, Foreground = UiHelpers.Subtle,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0),
                });
                var connectingPill = new Border
                {
                    Background = UiHelpers.Card,
                    BorderBrush = (Brush)Application.Current.Resources["StrokeBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(16, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = connectingRow,
                };
                Grid.SetColumn(connectingPill, 1);
                pageHeader.Children.Add(connectingPill);
            }
            page.Children.Add(pageHeader);

            for (int i = 0; i < _onboarding.Steps.Count; i++)
            {
                int index = i; // Capture the loop value for callbacks.
                var step = _onboarding.Steps[i];
                bool working = step.State == OnboardingStepState.Working;

                // Mark satisfied steps complete only after helper state is available.
                bool doneNoAction = !working && !_onboarding.IsConnecting
                    && step.State != OnboardingStepState.Error
                    && (step.State == OnboardingStepState.Ok || !step.Actionable);
                string glyph = step.State == OnboardingStepState.Error ? "✕" : (doneNoAction ? "✓" : "○");
                Brush glyphBrush = step.State == OnboardingStepState.Error ? UiHelpers.Error
                    : (doneNoAction ? UiHelpers.Ok : UiHelpers.Subtle);

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                FrameworkElement statusEl = working
                    ? UiHelpers.Badge(StatusKind.Working, 20)
                    : new TextBlock
                    {
                        Text = glyph, FontSize = 18, FontWeight = FontWeights.Bold,
                        Foreground = glyphBrush,
                    };
                statusEl.Width = 24;
                statusEl.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(statusEl, 0);
                row.Children.Add(statusEl);

                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };
                textStack.Children.Add(new TextBlock
                {
                    Text = step.Title, FontSize = 15, FontWeight = FontWeights.SemiBold,
                    Foreground = UiHelpers.Text, TextWrapping = TextWrapping.Wrap,
                });
                if (!string.IsNullOrEmpty(step.Detail))
                    textStack.Children.Add(new TextBlock
                    {
                        Text = step.Detail, FontSize = 12, Foreground = UiHelpers.Subtle,
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0),
                    });
                Grid.SetColumn(textStack, 1);
                row.Children.Add(textStack);

                bool enabled = step.Actionable && !working && !_onboarding.IsConnecting;
                var runBtn = new Button
                {
                    Content = working ? "Working…" : "Run",
                    Style = (Style)Application.Current.Resources["SetupButton"],
                    IsEnabled = enabled,
                    Opacity = enabled ? 1.0 : 0.4,
                    MinWidth = 76,
                    MinHeight = 32,
                    Padding = new Thickness(12, 6, 12, 6),
                };
                runBtn.Click += (_, __) => _ = _onboarding.RunStepAsync(
                    index, msg => Dispatcher.Invoke(RenderOnboardingIfActive));
                Grid.SetColumn(runBtn, 2);
                row.Children.Add(runBtn);

                var card = new Border
                {
                    Background = UiHelpers.Card,
                    BorderBrush = (Brush)Application.Current.Resources["StrokeBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 9, 14, 9),
                    Margin = new Thickness(0, 0, 0, 6),
                    Child = row,
                };
                page.Children.Add(card);
            }
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
            _browseCardPanels.Clear();

            ContentHost.Children.Add(BuildBrowsePageHeader());
            AddSection("Stable releases", "Recommended production builds", UiHelpers.Ok, _releases, _releasesError);
            AddSection("Test releases", "Preview builds for validating upcoming changes", UiHelpers.Warn, _testBuilds, _testBuildsError);
            AddSection("Nightly releases", "Experimental snapshots with the newest changes", UiHelpers.Error, _nightlies, _nightliesError);

            Dispatcher.BeginInvoke(new Action(UpdateBrowseCardWidths));
        }

        private FrameworkElement BuildBrowsePageHeader()
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var copy = new StackPanel();
            copy.Children.Add(new TextBlock
            {
                Text = "Update & Release",
                FontSize = 30,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiHelpers.Text,
            });
            copy.Children.Add(new TextBlock
            {
                Text = "Choose a channel and install the build that fits your device.",
                FontSize = 15,
                Foreground = UiHelpers.Subtle,
                Margin = new Thickness(0, 5, 0, 0),
            });
            Grid.SetColumn(copy, 0);
            header.Children.Add(copy);

            var installedPill = new Border
            {
                Background = (Brush)Application.Current.Resources["FooterBrush"],
                BorderBrush = _installedVersion != null ? UiHelpers.Ok : UiHelpers.Subtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 7, 12, 7),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = _installedVersion != null
                        ? $"Installed  {_installedVersion}"
                        : (_installedVersionChecked ? "Not installed" : "Checking installed version…"),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = _installedVersion != null ? UiHelpers.Ok : UiHelpers.Subtle,
                },
            };
            Grid.SetColumn(installedPill, 1);
            header.Children.Add(installedPill);
            return header;
        }

        private void AddSection(string header, string description, Brush titleColor, List<BuildSource> items, string error)
        {
            var sectionContent = new StackPanel();

            var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var accent = new Border
            {
                Width = 4,
                Height = 30,
                CornerRadius = new CornerRadius(2),
                Background = titleColor,
                Margin = new Thickness(0, 1, 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetColumn(accent, 0);
            headerRow.Children.Add(accent);

            var labels = new StackPanel();
            labels.Children.Add(new TextBlock
            {
                Text = header,
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiHelpers.Text,
            });
            labels.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 13,
                Foreground = UiHelpers.Subtle,
                Margin = new Thickness(0, 2, 0, 0),
            });
            Grid.SetColumn(labels, 1);
            headerRow.Children.Add(labels);

            if (items != null)
            {
                var countPill = new Border
                {
                    Background = (Brush)Application.Current.Resources["BgBrush"],
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 5, 10, 5),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = items.Count == 1 ? "1 build" : $"{items.Count} builds",
                        FontSize = 12,
                        Foreground = UiHelpers.Subtle,
                    },
                };
                Grid.SetColumn(countPill, 2);
                headerRow.Children.Add(countPill);
            }
            sectionContent.Children.Add(headerRow);

            if (error != null)
            {
                sectionContent.Children.Add(BuildBrowseSectionMessage(StatusKind.Error, "Couldn't load", error));
            }
            else if (items == null)
            {
                sectionContent.Children.Add(BuildBrowseSectionMessage(StatusKind.Working, "Loading builds…", ""));
            }
            else if (items.Count == 0)
            {
                sectionContent.Children.Add(BuildBrowseSectionMessage(StatusKind.Info, "No builds found", ""));
            }
            else
            {
                bool haveSelection = _selectedIndex >= 0 && _selectedIndex < _flat.Count;
                var selected = haveSelection ? _flat[_selectedIndex] : null;
                var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
                _browseCardPanels.Add(cards);

                foreach (var b in items)
                {
                    bool isNewest = ReferenceEquals(b, items[0]);
                    var row = BuildRow(b, ReferenceEquals(b, selected), isNewest);
                    _rowElements[b] = row;
                    cards.Children.Add(row);
                }
                sectionContent.Children.Add(cards);
            }

            ContentHost.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["FooterBrush"],
                BorderBrush = (Brush)Application.Current.Resources["StrokeBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 13, 16, 3),
                Margin = new Thickness(0, 0, 0, 12),
                Child = sectionContent,
            });
        }

        private static Border BuildBrowseSectionMessage(StatusKind kind, string title, string detail)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var badge = UiHelpers.Badge(kind, 22);
            badge.Margin = new Thickness(0, 0, 10, 0);
            content.Children.Add(badge);

            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiHelpers.Text,
            });
            if (!string.IsNullOrEmpty(detail))
                copy.Children.Add(new TextBlock
                {
                    Text = detail,
                    FontSize = 12,
                    Foreground = UiHelpers.Subtle,
                    TextWrapping = TextWrapping.Wrap,
                });
            content.Children.Add(copy);

            return new Border
            {
                Background = (Brush)Application.Current.Resources["BgBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10),
                Child = content,
            };
        }

        private void UpdateBrowseCardWidths()
        {
            if (_browseCardPanels.Count == 0) return;
            const double gutter = 10;
            foreach (var panel in _browseCardPanels)
            {
                double available = panel.ActualWidth > 0
                    ? panel.ActualWidth
                    : Math.Max(0, ContentHost.ActualWidth - 36); // Exclude section padding.
                if (available <= 0) continue;

                // Single builds fill the row; larger sets use at most two readable columns.
                int maxColumns = available >= 720 ? 2 : 1;
                int columns = Math.Min(maxColumns, Math.Max(1, panel.Children.Count));
                double cardWidth = Math.Max(280, Math.Floor((available - (columns * gutter)) / columns));
                foreach (FrameworkElement card in panel.Children)
                    card.Width = cardWidth;
            }
        }

        private Border BuildRow(BuildSource b, bool selected, bool isNewest)
        {
            var stack = new StackPanel();

            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var versionText = new TextBlock
            {
                Text = b.Version,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = selected ? UiHelpers.Accent : UiHelpers.Text,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(versionText, 0);
            heading.Children.Add(versionText);
            if (isNewest)
            {
                var latest = new Border
                {
                    Background = UiHelpers.Accent,
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "Latest",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Black,
                    },
                };
                Grid.SetColumn(latest, 1);
                heading.Children.Add(latest);
            }
            stack.Children.Add(heading);

            if (!string.IsNullOrWhiteSpace(b.Title))
                stack.Children.Add(new TextBlock
                {
                    Text = b.Title,
                    FontSize = 13,
                    Foreground = UiHelpers.Subtle,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });

            string detail = b.When != default ? b.When.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
            if (!string.IsNullOrEmpty(b.SizeLabel)) detail += (detail.Length > 0 ? "  ·  " : "") + b.SizeLabel;
            if (!string.IsNullOrEmpty(detail))
                stack.Children.Add(new TextBlock
                {
                    Text = detail, FontSize = 11, Foreground = UiHelpers.Subtle,
                    Opacity = 0.85, Margin = new Thickness(0, 6, 0, 0),
                });

            string tag = VersionTag(b, out var tagBrush);
            if (tag != null)
                stack.Children.Add(new Border
                {
                    Background = (Brush)Application.Current.Resources["FooterBrush"],
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock
                    {
                        Text = tag,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = tagBrush,
                    },
                });

            if (IsBlockedForDevice(b, out string blockReason))
                stack.Children.Add(new TextBlock
                {
                    Text = "⛔ " + blockReason, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = UiHelpers.Error, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
                });

            // Deemphasize older builds without implying they are disabled.
            double baseOpacity = (isNewest || selected) ? 1.0 : 0.78;
            Brush normalBackground = selected
                ? UiHelpers.Card
                : (Brush)Application.Current.Resources["BgBrush"];

            var border = new Border
            {
                Background = normalBackground,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 10, 10),
                MinHeight = 96,
                BorderBrush = selected
                    ? UiHelpers.Accent
                    : (Brush)Application.Current.Resources["StrokeBrush"],
                BorderThickness = new Thickness(selected ? 2 : 1),
                Child = stack,
                Cursor = Cursors.Hand,
                Opacity = _busy ? baseOpacity * 0.5 : baseOpacity,
            };
            border.MouseEnter += (_, __) =>
            {
                if (_busy) return;
                border.Background = UiHelpers.Card;
                border.Opacity = 1.0;
            };
            border.MouseLeave += (_, __) =>
            {
                border.Background = normalBackground;
                border.Opacity = _busy ? baseOpacity * 0.5 : baseOpacity;
            };
            border.MouseLeftButtonUp += (_, __) =>
            {
                if (_busy) return;
                _selectedIndex = _flat.IndexOf(b);
                ShowConfirm(b);
            };
            return border;
        }

        /// <summary>Compares a parseable build version with the installed version.</summary>
        private string VersionTag(BuildSource b, out Brush tagBrush)
        {
            tagBrush = UiHelpers.Subtle;
            if (_installedVersion == null || !TryParseVersion(b.Version, out var v)) return null;

            if (v > _installedVersion) { tagBrush = UiHelpers.Ok; return "▲ Newer than installed"; }
            if (v < _installedVersion) { tagBrush = UiHelpers.Subtle; return "▼ Older than installed"; }
            tagBrush = UiHelpers.Accent;
            return "● Currently installed";
        }

        /// <summary>Returns whether a build predates the detected device's support floor.</summary>
        private bool IsBlockedForDevice(BuildSource b, out string reason)
        {
            reason = null;
            var min = DeviceDetect.MinimumSupportedVersion(_deviceModel);
            if (min == null || !TryParseVersion(b.Version, out var v) || v >= min) return false;
            reason = $"Not supported on this device — needs {min}+";
            return true;
        }

        /// <summary>Moves through all build sections as one two-column controller grid.</summary>
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
                AddScrollHint(); // Release notes can overflow.
                return;
            }

            // Hide actions while download or installation is active.
            if (_busy) return;

            // Completed installs close instead of returning silently to the picker.
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

            if (_view == View.Onboarding)
            {
                AddAction(PadButton.Y, "Refresh status", !_onboarding.IsConnecting, () =>
                    _ = _onboarding.RefreshStatusAsync(msg => Dispatcher.Invoke(RenderOnboardingIfActive)));
                AddAction(PadButton.B, "Back", true, GoHome);
                return;
            }

            AddAction(PadButton.A, "Install this build", _flat.Count > 0, () =>
            {
                if (_selectedIndex >= 0 && _selectedIndex < _flat.Count) ShowConfirm(_flat[_selectedIndex]);
            });
            AddAction(PadButton.Y, "Refresh", true, () => _ = RefreshSourcesAsync());
            AddAction(PadButton.B, "Back", true, GoHome);
            AddScrollHint();
        }

        /// <summary>Adds the right-stick scroll hint to overflow-prone views.</summary>
        private void AddScrollHint()
        {
            var glyph = new BitmapImage(new Uri(
                "pack://application:,,,/Assets/xbox/xbox_stick_r_vertical.png",
                UriKind.Absolute));
            ActionBar.Children.Add(ActionBarBuilder.BuildHint(glyph, "Scroll"));
        }

        private void AddAction(PadButton b, string label, bool enabled, Action action)
        {
            if (enabled) _liveActions[b] = action;
            ActionBar.Children.Add(ActionBarBuilder.BuildChip(b, label, enabled, action));
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

            // Only GitHub builds include release notes.
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
        private async Task InstallSelectedAsync(BuildSource build, bool previewOnly = false)
        {
            if (_busy) return;
            _busy = true;
            _installFinished = false;
            RefreshActionBar();

            // Snapshot helper PIDs because package shutdown does not stop standalone helpers.
            int[] priorHelperPids = HelperControl.GetHelperPids();
            Version previousVersion = _installedVersion; // Cached by RefreshSourcesAsync.

            // A bounded single column stays readable and contracts for handheld displays.
            var layout = new Grid { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch };
            layout.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var statusSection = new StackPanel();
            Grid.SetRow(statusSection, 0);
            layout.Children.Add(statusSection);

            var progressSection = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            Grid.SetRow(progressSection, 1);
            layout.Children.Add(progressSection);

            ContentHost.Children.Clear();
            ContentHost.Children.Add(layout);

            var progressBar = new ProgressBar
            {
                Height = 4, Minimum = 0, Maximum = 100, Value = 0,
                Foreground = UiHelpers.Accent,
                Background = (Brush)Application.Current.Resources["StrokeBrush"],
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 10),
                IsIndeterminate = true,
            };
            // Bound and scroll the log while keeping current status visible.
            var logPanel = new StackPanel { Margin = new Thickness(2, 4, 0, 0) };
            var logScroller = new ScrollViewer
            {
                MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = logPanel,
            };
            progressSection.Children.Add(progressBar);
            progressSection.Children.Add(logScroller);

            var statusPanel = new ContentControl
            {
                Focusable = false,
                Tag = ($"Installing {build.Version}", $"{build.Origin} — {build.Title}"),
            };
            var statusCard = new Border
            {
                Background = UiHelpers.Card,
                BorderBrush = UiHelpers.Accent,
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12),
                Child = statusPanel,
            };
            SetInstallStatus(statusPanel, statusCard, StatusKind.Working, "",
                "Download and package install in progress.");
            var historyPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            statusSection.Children.Add(statusCard);
            progressSection.Children.Add(historyPanel);

            // Show each step as complete or active in its own row.
            ContentControl currentLogBadge = null;
            StackPanel currentLogDetail = null;

            UIElement BuildLogRow(string text, out ContentControl badge, out StackPanel detail)
            {
                badge = new ContentControl
                {
                    Width = 18, Height = 18, Focusable = false,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
                    Content = UiHelpers.Badge(StatusKind.Working, 18),
                };

                // Grid width constraints keep long status text wrapping.
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(badge, 0);
                var textBlock = new TextBlock
                {
                    Text = text, FontSize = 14, Foreground = UiHelpers.Subtle,
                    Margin = new Thickness(8, 0, 0, 0), TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(textBlock, 1);
                header.Children.Add(badge);
                header.Children.Add(textBlock);

                detail = new StackPanel { Margin = new Thickness(26, 2, 0, 0) };

                var wrapper = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
                wrapper.Children.Add(header);
                wrapper.Children.Add(detail);
                return wrapper;
            }

            void FinishLogRow(ContentControl badge, bool ok)
            {
                if (badge == null) return;
                badge.Content = UiHelpers.Badge(ok ? StatusKind.Ok : StatusKind.Error, 18);
            }

            // Package installation can report synchronously from a worker thread.
            void Log(string s) => Dispatcher.Invoke(() =>
            {
                FinishLogRow(currentLogBadge, true);
                logPanel.Children.Add(BuildLogRow(s, out currentLogBadge, out currentLogDetail));
                logScroller.ScrollToBottom();
            });

            // Group internal substeps beneath the current top-level row.
            void LogDetail(string s) => Dispatcher.Invoke(() =>
            {
                currentLogDetail?.Children.Add(new TextBlock
                {
                    Text = s, FontSize = 12, Foreground = UiHelpers.Subtle,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1),
                });
                logScroller.ScrollToBottom();
            });
            var progress = new Progress<int>(p =>
            {
                progressBar.IsIndeterminate = false;
                progressBar.Value = p;
            });

            if (previewOnly)
            {
                Log($"Downloading installer ({build.Version})…");
                return;
            }

            try
            {
                bool certTrusted = await Task.Run(() => CertInstaller.IsKnownCertAlreadyTrusted());
                string staged = await BuildDownloader.DownloadAndStageAsync(build, certTrusted, Log, progress);
                SetupContext.AssetRoot = staged;

                // Fast install mirrors ToolsPhase and InstallPhase without the guided wizard.
                // Intentional: each tool handles UAC independently; do not add shared elevation here.
                progressBar.IsIndeterminate = true;
                bool ok = true;

                var hidhide = await Task.Run(() => ToolDetect.HidHide());
                var rtss = await Task.Run(() => ToolDetect.Rtss());
                var usbip = await Task.Run(() => ToolDetect.Usbip());
                var pawnio = await Task.Run(() => ToolDetect.PawnIO());
                if (!hidhide.Installed || !rtss.Installed || !usbip.Installed || !pawnio.Installed)
                {
                    // Accumulate each result and finalize its row before starting another step.
                    // Deferring would assign a cumulative failure to the wrong row.
                    if (!hidhide.Installed)
                    {
                        bool r = await Task.Run(() => ToolInstaller.InstallHidHide(Log));
                        FinishLogRow(currentLogBadge, r);
                        ok &= r;
                    }
                    if (!rtss.Installed)
                    {
                        bool r = await Task.Run(() => ToolInstaller.InstallRtss(Log));
                        FinishLogRow(currentLogBadge, r);
                        ok &= r;
                    }
                    if (!pawnio.Installed)
                    {
                        // Group PawnIO's silent substeps in one row.
                        Log("Installing PawnIO (driver)…");
                        bool r = await Task.Run(() => PawnIoSetup.Run(LogDetail)) == PawnIoSetup.Result.Success;
                        FinishLogRow(currentLogBadge, r);
                        ok &= r;
                    }
                    if (!await Task.Run(() => ToolDetect.Usbip().Installed))
                    {
                        // Group usbip's download, verification, launch, and result in one row.
                        Log("Installing usbip (driver) — not silent, a separate installer window will open.");
                        LogDetail("Confirm the driver-install prompt when it appears.");
                        var usbipResult = await Task.Run(() => UsbipSetup.Run(LogDetail));
                        if (usbipResult == UsbipSetup.Result.RebootRequired || usbipResult == UsbipSetup.Result.Success)
                        {
                            LogDetail("Reboot required for the driver to activate.");
                            SetInstallStatus(statusPanel, statusCard, StatusKind.Warning, "Reboot required",
                                "Virtual controller support won't work until you reboot — usbip's driver was just installed and needs it to activate.");
                            AppendHistory(historyPanel, false, "Reboot required",
                                "usbip's driver needs a restart before virtual controller mode works.");
                        }
                    }
                }
                else Log("Required tools (HidHide, RTSS, usbip, PawnIO) already installed.");

                // Required-tool failures must stop before certificate and package installation.
                if (!ok)
                {
                    SetInstallStatus(statusPanel, statusCard, StatusKind.Error, "Installation stopped",
                        "One or more required tools failed to install — see the log above for details. " +
                        "Fix the issue (e.g. check the system clock for the winget/certificate error) and try again.");
                    FinishLogRow(currentLogBadge, false);
                    _busy = false;
                    _installFinished = true;
                    RefreshActionBar();
                    return;
                }

                string cer = CertInstaller.FindSiblingCer();
                if (cer != null)
                {
                    string thumb = CertInstaller.ThumbprintOf(cer);
                    if (!CertInstaller.IsTrusted(thumb))
                    {
                        Log("Trusting signing certificate…");
                        bool r = await Task.Run(() => CertInstaller.Install(cer));
                        FinishLogRow(currentLogBadge, r);
                        ok &= r;
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

                    // Same-version installs do not restart the helper, so skip the fresh-PID check.
                    bool sameVersionReinstall = previousVersion != null
                        && TryParseVersion(build.Version, out var selVerForReinstall) && selVerForReinstall == previousVersion;

                    bool up = await RunPostInstallMonitorAsync(
                        priorHelperPids, previousVersion != null, sameVersionReinstall, helperProgress,
                        statusPanel, statusCard, historyPanel);
                    progressBar.Value = 100;

                    Log(up
                        ? $"{DescribeTransition(previousVersion, build.Version)} — helper is up and running."
                        : "Installed, but the helper did not appear in time — open the Game Bar (Win+G) manually.");
                }

                FinishLogRow(currentLogBadge, ok);
                _busy = false;
                _installFinished = true;
                RefreshActionBar();

                // Enter onboarding only after the successful install view has settled.
                if (ok) OpenOnboarding();
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

        /// <summary>Formats the installed or updated version transition.</summary>
        private static string DescribeTransition(Version previous, string selectedVersion)
        {
            if (previous == null) return $"Installed {selectedVersion}";
            if (!TryParseVersion(selectedVersion, out var selected)) return $"Installed {selectedVersion}";
            if (selected > previous) return $"Updated {previous} → {selected}";
            if (selected < previous) return $"Downgraded {previous} → {selected}";
            return $"Reinstalled {selected}";
        }

        /// <summary>
        /// Starts the widget, validates a fresh elevated helper, removes stale helpers, and probes
        /// controller state. Reports live status and permanent history before settling completion.
        /// </summary>
        private static async Task<bool> RunPostInstallMonitorAsync(
            int[] priorHelperPids, bool isUpdate, bool sameVersionReinstall, IProgress<int> progress,
            ContentControl statusPanel, Border statusCard, StackPanel historyPanel)
        {
            void AddHistory(bool ok, string title, string detail) => AppendHistory(historyPanel, ok, title, detail);

            var totalSw = System.Diagnostics.Stopwatch.StartNew();

            HelperControl.OpenGameBar();
            await Task.Delay(5000);
            HelperControl.CloseGameBarBestEffort(); // The UAC status remains visible if closing fails.

            // A new PID is insufficient; token elevation confirms UAC completed.
            bool FreshHelperUp() => HelperControl.GetHelperPids()
                .Any(pid => !priorHelperPids.Contains(pid) && HelperControl.IsProcessElevated(pid));

            SetInstallStatus(statusPanel, statusCard, StatusKind.Working, "Starting…",
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
                    if (uacShowing)
                        SetInstallStatus(statusPanel, statusCard, StatusKind.Warning, "Waiting for UAC…",
                            "A confirmation prompt appeared — please confirm it to continue.");
                    else
                        SetInstallStatus(statusPanel, statusCard, StatusKind.Working, "Starting…",
                            "Waiting for the ClawTweaks helper to start.");
                    lastUacShowing = uacShowing;
                }

                progress?.Report((int)Math.Min(70, sw.ElapsedMilliseconds * 70 / 60000));
                await Task.Delay(300);
            }

            if (!up)
            {
                SetInstallStatus(statusPanel, statusCard, StatusKind.Warning, "Timed out",
                    sameVersionReinstall
                        ? "Expected for a same-version reinstall — the helper doesn't restart or show a UAC prompt when nothing changed. Open the Game Bar (Win+G) to check it's still running."
                        : "Open the Game Bar manually (Win+G).");
                return false;
            }

            AddHistory(true, isUpdate ? "New update — background helper started" : "Installed — background helper started", "");
            progress?.Report(70);

            // Give stale helpers time to exit, then remove remaining duplicates.
            SetInstallStatus(statusPanel, statusCard, StatusKind.Working, "Checking for duplicate helpers…", "");
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
                    SetInstallStatus(statusPanel, statusCard, StatusKind.Warning, "Removing leftover helper…",
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

            // Reuse the setup controller probe and allow time for helper mounting.
            SetInstallStatus(statusPanel, statusCard, StatusKind.Working, "Checking controller mode…", "");
            var (controllerOk, ctrlTitle, ctrlDetail, ctrlCause) = await ProbeControllerModeAsync();
            if (controllerOk) AddHistory(true, ctrlTitle, ctrlDetail);
            else AddHistory(false, ctrlTitle, ctrlCause);
            progress?.Report(95);

            // Allow roughly 20 seconds total for post-install state to settle.
            int remainingMs = 20000 - (int)totalSw.ElapsedMilliseconds;
            if (remainingMs > 0) await Task.Delay(remainingMs);

            SetInstallStatus(statusPanel, statusCard, StatusKind.Ok, "Installation complete", "No restart necessary.");
            return true;
        }

        /// <summary>Retries the shared controller probe while the helper mounts its device.</summary>
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

        /// <summary>Appends a persistent history item with optional detail.</summary>
        private static void AppendHistory(StackPanel historyPanel, bool ok, string title, string detail)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = UiHelpers.Text,
            });
            if (!string.IsNullOrEmpty(detail))
                stack.Children.Add(new TextBlock
                {
                    Text = detail, FontSize = 12, Foreground = UiHelpers.Subtle,
                    Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap,
                });

            var row = new Grid { Margin = new Thickness(2, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = UiHelpers.Badge(ok ? StatusKind.Ok : StatusKind.Warning, 16);
            badge.Margin = new Thickness(0, 2, 8, 0);
            badge.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(badge, 0);
            Grid.SetColumn(stack, 1);
            row.Children.Add(badge);
            row.Children.Add(stack);
            historyPanel.Children.Add(row);
        }

        /// <summary>Updates live install status without replacing the build heading.</summary>
        private static void SetInstallStatus(
            ContentControl statusPanel, Border statusCard, StatusKind kind, string title, string detail)
        {
            var accent = kind == StatusKind.Working ? UiHelpers.Accent : UiHelpers.BrushFor(kind);
            statusCard.BorderBrush = accent;
            var context = ((string installLabel, string buildDetail))statusPanel.Tag;
            statusPanel.Content = BuildInstallStatusContent(
                kind, context.installLabel, context.buildDetail, title, detail);
        }

        private static Grid BuildInstallStatusContent(
            StatusKind kind, string installLabel, string buildDetail, string title, string detail)
        {
            bool showBadge = kind != StatusKind.Working;
            var text = new StackPanel { Margin = showBadge ? new Thickness(12, 0, 0, 0) : new Thickness(0) };
            var heading = new TextBlock
            {
                FontSize = 18,
                Foreground = UiHelpers.Text,
                TextWrapping = TextWrapping.Wrap,
            };
            heading.Inlines.Add(new System.Windows.Documents.Run
                { Text = installLabel, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(title))
            {
                heading.Inlines.Add(new System.Windows.Documents.Run
                    { Text = "  ·  ", Foreground = UiHelpers.Subtle });
                heading.Inlines.Add(new System.Windows.Documents.Run
                    { Text = title, FontWeight = FontWeights.SemiBold });
            }
            text.Children.Add(heading);
            if (!string.IsNullOrEmpty(buildDetail))
                text.Children.Add(new TextBlock
                {
                    Text = buildDetail,
                    FontSize = 12,
                    LineHeight = 17,
                    Foreground = UiHelpers.Subtle,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            if (!string.IsNullOrEmpty(detail))
                text.Children.Add(new TextBlock
                {
                    Text = detail, FontSize = 13, LineHeight = 18, Foreground = UiHelpers.Subtle,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
                });

            // Grid width constraints keep long details wrapping.
            var row = new Grid();
            if (showBadge)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (showBadge)
            {
                var badge = UiHelpers.Badge(kind, 28);
                badge.VerticalAlignment = VerticalAlignment.Top;
                badge.Margin = new Thickness(0, 1, 0, 0);
                Grid.SetColumn(badge, 0);
                row.Children.Add(badge);
            }
            Grid.SetColumn(text, showBadge ? 1 : 0);
            row.Children.Add(text);
            return row;
        }
        #endregion
    }
}
