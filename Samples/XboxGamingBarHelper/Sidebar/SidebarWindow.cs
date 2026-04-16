using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NLog;

namespace XboxGamingBarHelper.Sidebar
{
    internal enum FocusLayer
    {
        TabBar,
        Content,
        ContentAdjust,
    }

    internal class SidebarWindow : Window
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // P/Invoke for topmost enforcement
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // Layout
        private const double SidebarWidth = 320;
        private readonly TranslateTransform _slideTransform;

        // Theme colors
        private static readonly Color BgColor = (Color)ColorConverter.ConvertFromString("#25282C");
        private static readonly Color AccentColor = (Color)ColorConverter.ConvertFromString("#0078D4");
        private static readonly Color TextColor = Colors.White;
        private static readonly Color SubtextColor = (Color)ColorConverter.ConvertFromString("#888888");
        private static readonly Color CardBorderColor = (Color)ColorConverter.ConvertFromString("#50555C");

        // Tab system
        private const int TabCount = 5;
        private readonly SidebarTab[] _tabs;
        internal readonly QuickTab QuickTab;
        internal readonly PerformanceTab PerformanceTab;
        internal readonly DisplayTab DisplayTab;
        internal readonly LegionTab LegionTab;
        internal readonly ProfilesTab ProfilesTab;
        private int _activeTabIndex;

        // Tab bar UI
        private readonly Border[] _tabBorders;
        private readonly Rectangle[] _tabUnderlines;
        private readonly TextBlock[] _tabLabels;
        private static readonly string[] TabNames = { "Quick", "Perf.", "Display", "Legion", "Profiles" };

        // Content area
        private readonly ScrollViewer _scrollViewer;
        private readonly Border _contentHost;

        // Focus state machine
        private FocusLayer _focusLayer;
        private int _focusIndex;

        // Header
        private readonly TextBlock _profileText;
        private readonly TextBlock _batteryHeaderText;
        private readonly TextBlock _batteryHeaderIcon;

        // Footer text
        private readonly TextBlock _footerText;

        internal SidebarWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;

            // Position on right edge of primary screen (WPF units, DPI-aware)
            var workArea = SystemParameters.WorkArea;
            Width = SidebarWidth;
            Height = workArea.Height;
            Left = workArea.Right - SidebarWidth;
            Top = workArea.Top;

            // Slide transform (starts off-screen)
            _slideTransform = new TranslateTransform(SidebarWidth, 0);

            // Create tabs
            QuickTab = new QuickTab();
            PerformanceTab = new PerformanceTab();
            DisplayTab = new DisplayTab();
            LegionTab = new LegionTab();
            ProfilesTab = new ProfilesTab();
            _tabs = new SidebarTab[] { QuickTab, PerformanceTab, DisplayTab, LegionTab, ProfilesTab };

            // Build UI
            var rootBorder = new Border
            {
                Background = new SolidColorBrush(BgColor),
                CornerRadius = new CornerRadius(12, 0, 0, 12),
                RenderTransform = _slideTransform,
            };

            var rootDock = new DockPanel { LastChildFill = true };

            // ── Header ──
            var headerPanel = new StackPanel { Margin = new Thickness(16, 12, 16, 0) };
            DockPanel.SetDock(headerPanel, Dock.Top);

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = "GoTweaks",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(TextColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(titleText, 0);
            headerGrid.Children.Add(titleText);

            // Battery in header
            var batteryHeaderPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            _batteryHeaderIcon = new TextBlock
            {
                Text = "\uE83F",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            };
            batteryHeaderPanel.Children.Add(_batteryHeaderIcon);
            _batteryHeaderText = new TextBlock
            {
                Text = "--%",
                FontSize = 12,
                Foreground = new SolidColorBrush(SubtextColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            batteryHeaderPanel.Children.Add(_batteryHeaderText);
            Grid.SetColumn(batteryHeaderPanel, 1);
            headerGrid.Children.Add(batteryHeaderPanel);

            _profileText = new TextBlock
            {
                Text = "Global",
                FontSize = 12,
                Foreground = new SolidColorBrush(SubtextColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(_profileText, 2);
            headerGrid.Children.Add(_profileText);
            headerPanel.Children.Add(headerGrid);
            rootDock.Children.Add(headerPanel);

            // ── Tab Bar ──
            var tabBarPanel = new Grid { Margin = new Thickness(16, 8, 16, 0) };
            DockPanel.SetDock(tabBarPanel, Dock.Top);

            _tabBorders = new Border[TabCount];
            _tabUnderlines = new Rectangle[TabCount];
            _tabLabels = new TextBlock[TabCount];

            for (int i = 0; i < TabCount; i++)
            {
                tabBarPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (int i = 0; i < TabCount; i++)
            {
                var tabStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

                _tabLabels[i] = new TextBlock
                {
                    Text = TabNames[i],
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(i == 0 ? TextColor : SubtextColor),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 4),
                };
                tabStack.Children.Add(_tabLabels[i]);

                _tabUnderlines[i] = new Rectangle
                {
                    Height = 3,
                    Fill = new SolidColorBrush(i == 0 ? AccentColor : Colors.Transparent),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                };
                tabStack.Children.Add(_tabUnderlines[i]);

                _tabBorders[i] = new Border
                {
                    Child = tabStack,
                    Padding = new Thickness(4, 0, 4, 0),
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                };
                Grid.SetColumn(_tabBorders[i], i);
                tabBarPanel.Children.Add(_tabBorders[i]);
            }

            rootDock.Children.Add(tabBarPanel);

            // ── Footer ──
            _footerText = new TextBlock
            {
                Text = "LT/RT: Tab | Up/Down: Navigate | A: Select | B: Close",
                FontSize = 11,
                Foreground = new SolidColorBrush(SubtextColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 12),
            };
            DockPanel.SetDock(_footerText, Dock.Bottom);
            rootDock.Children.Add(_footerText);

            // ── Separator above footer ──
            var footerSep = new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(AccentColor),
                Margin = new Thickness(16, 4, 16, 0),
            };
            DockPanel.SetDock(footerSep, Dock.Bottom);
            rootDock.Children.Add(footerSep);

            // ── Content Area (fills remaining space) ──
            _scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.VerticalOnly,
                Margin = new Thickness(16, 8, 16, 0),
            };
            _scrollViewer.ManipulationBoundaryFeedback += (s, e) => e.Handled = true;

            _contentHost = new Border();
            _contentHost.Child = QuickTab.ContentPanel; // default tab
            _scrollViewer.Content = _contentHost;
            rootDock.Children.Add(_scrollViewer);

            rootBorder.Child = rootDock;
            Content = rootBorder;

            // Initialize focus
            _activeTabIndex = 0;
            _focusLayer = FocusLayer.Content;
            _focusIndex = 0;
            UpdateFocusHighlight();

            WirePointerEvents();
        }

        #region Tab Switching

        private void SwitchTab(int index)
        {
            if (index < 0 || index >= _tabs.Length || index == _activeTabIndex) return;

            // Update tab bar visuals
            _tabLabels[_activeTabIndex].Foreground = new SolidColorBrush(SubtextColor);
            _tabUnderlines[_activeTabIndex].Fill = new SolidColorBrush(Colors.Transparent);

            _activeTabIndex = index;

            _tabLabels[_activeTabIndex].Foreground = new SolidColorBrush(TextColor);
            _tabUnderlines[_activeTabIndex].Fill = new SolidColorBrush(AccentColor);

            // Swap content
            _contentHost.Child = _tabs[_activeTabIndex].ContentPanel;
            _scrollViewer.ScrollToTop();

            // Reset focus to first item in new tab
            _focusIndex = 0;
            UpdateFocusHighlight();

            Logger.Info($"Sidebar: Switched to tab {TabNames[_activeTabIndex]}");
        }

        internal void TabLeft()
        {
            if (_activeTabIndex > 0)
                SwitchTab(_activeTabIndex - 1);
        }

        internal void TabRight()
        {
            if (_activeTabIndex < _tabs.Length - 1)
                SwitchTab(_activeTabIndex + 1);
        }

        private SidebarTab ActiveTab => _tabs[_activeTabIndex];

        #endregion

        #region Focus Highlight

        private void UpdateFocusHighlight()
        {
            // Update tab bar highlight
            for (int i = 0; i < _tabBorders.Length; i++)
            {
                _tabBorders[i].Background = _focusLayer == FocusLayer.TabBar && i == _activeTabIndex
                    ? new SolidColorBrush(AccentColor)
                    : Brushes.Transparent;
            }

            // Update content focus
            var controls = ActiveTab.FocusableControls;
            for (int i = 0; i < controls.Length; i++)
            {
                if (_focusLayer == FocusLayer.Content && i == _focusIndex)
                {
                    controls[i].BorderBrush = new SolidColorBrush(TextColor);
                    controls[i].BorderThickness = new Thickness(2);
                }
                else if (_focusLayer == FocusLayer.ContentAdjust && i == _focusIndex)
                {
                    controls[i].BorderBrush = new SolidColorBrush(AccentColor);
                    controls[i].BorderThickness = new Thickness(2);
                }
                else
                {
                    controls[i].BorderBrush = new SolidColorBrush(CardBorderColor);
                    controls[i].BorderThickness = new Thickness(1);
                }
            }

            // Update footer hint text
            switch (_focusLayer)
            {
                case FocusLayer.TabBar:
                    _footerText.Text = "LT/RT: Tab | D-Pad: Navigate | A: Enter | B: Close";
                    break;
                case FocusLayer.Content:
                    _footerText.Text = "LT/RT: Tab | Up/Down: Navigate | A: Select | B: Close";
                    break;
                case FocusLayer.ContentAdjust:
                    _footerText.Text = "L/R: Adjust | A: Confirm | B: Cancel";
                    break;
            }
        }

        #endregion

        #region Show/Hide

        internal void ShowSidebar()
        {
            _focusLayer = FocusLayer.Content;
            _focusIndex = 0;
            UpdateFocusHighlight();
            _scrollViewer.ScrollToTop();

            Show();

            // Enforce topmost via P/Invoke
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }

            // Slide in animation
            var anim = new DoubleAnimation(SidebarWidth, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            _slideTransform.BeginAnimation(TranslateTransform.XProperty, anim);

            Logger.Info("Sidebar: Shown");
        }

        internal void HideSidebar()
        {
            _focusLayer = FocusLayer.Content;

            var anim = new DoubleAnimation(0, SidebarWidth, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            anim.Completed += (s, e) =>
            {
                Hide();
            };
            _slideTransform.BeginAnimation(TranslateTransform.XProperty, anim);

            Logger.Info("Sidebar: Hidden");
        }

        internal new bool IsVisible => Visibility == Visibility.Visible || base.IsVisible;

        #endregion

        #region Navigation (Two-Layer Focus State Machine)

        internal void NavigateUp()
        {
            switch (_focusLayer)
            {
                case FocusLayer.TabBar:
                    // Already at top, do nothing
                    break;
                case FocusLayer.Content:
                    if (_focusIndex > 0)
                    {
                        _focusIndex--;
                        UpdateFocusHighlight();
                        ActiveTab.FocusableControls[_focusIndex].BringIntoView();
                    }
                    else
                    {
                        // Up from first item → go to tab bar
                        _focusLayer = FocusLayer.TabBar;
                        UpdateFocusHighlight();
                    }
                    break;
                case FocusLayer.ContentAdjust:
                    // Don't navigate while adjusting
                    break;
            }
        }

        internal void NavigateDown()
        {
            switch (_focusLayer)
            {
                case FocusLayer.TabBar:
                    // Enter content area
                    _focusLayer = FocusLayer.Content;
                    _focusIndex = 0;
                    UpdateFocusHighlight();
                    if (ActiveTab.FocusableControls.Length > 0)
                        ActiveTab.FocusableControls[0].BringIntoView();
                    break;
                case FocusLayer.Content:
                    if (_focusIndex < ActiveTab.FocusableControls.Length - 1)
                    {
                        _focusIndex++;
                        UpdateFocusHighlight();
                        ActiveTab.FocusableControls[_focusIndex].BringIntoView();
                    }
                    break;
                case FocusLayer.ContentAdjust:
                    // Don't navigate while adjusting
                    break;
            }
        }

        internal void NavigateLeft()
        {
            switch (_focusLayer)
            {
                case FocusLayer.TabBar:
                    if (_activeTabIndex > 0)
                        SwitchTab(_activeTabIndex - 1);
                    break;
                case FocusLayer.Content:
                    // D-pad L/R no longer switches tabs in Content layer
                    break;
                case FocusLayer.ContentAdjust:
                    ActiveTab.AdjustLeft(_focusIndex);
                    UpdateFocusHighlight();
                    break;
            }
        }

        internal void NavigateRight()
        {
            switch (_focusLayer)
            {
                case FocusLayer.TabBar:
                    if (_activeTabIndex < _tabs.Length - 1)
                        SwitchTab(_activeTabIndex + 1);
                    break;
                case FocusLayer.Content:
                    // D-pad L/R no longer switches tabs in Content layer
                    break;
                case FocusLayer.ContentAdjust:
                    ActiveTab.AdjustRight(_focusIndex);
                    UpdateFocusHighlight();
                    break;
            }
        }

        internal void Activate()
        {
            switch (_focusLayer)
            {
                case FocusLayer.TabBar:
                    // Enter content from tab bar
                    _focusLayer = FocusLayer.Content;
                    _focusIndex = 0;
                    UpdateFocusHighlight();
                    if (ActiveTab.FocusableControls.Length > 0)
                        ActiveTab.FocusableControls[0].BringIntoView();
                    break;
                case FocusLayer.Content:
                    {
                        bool isAdjusting = false;
                        ActiveTab.Activate(_focusIndex, ref isAdjusting);
                        if (isAdjusting)
                            _focusLayer = FocusLayer.ContentAdjust;
                        UpdateFocusHighlight();
                    }
                    break;
                case FocusLayer.ContentAdjust:
                    {
                        bool isAdjusting = true;
                        ActiveTab.Activate(_focusIndex, ref isAdjusting);
                        if (!isAdjusting)
                            _focusLayer = FocusLayer.Content;
                        UpdateFocusHighlight();
                    }
                    break;
            }
        }

        internal void Dismiss()
        {
            switch (_focusLayer)
            {
                case FocusLayer.ContentAdjust:
                    _focusLayer = FocusLayer.Content;
                    UpdateFocusHighlight();
                    break;
                default:
                    HideSidebar();
                    break;
            }
        }

        #endregion

        #region External Updates

        internal void UpdateProfile(string profileName)
        {
            _profileText.Text = string.IsNullOrEmpty(profileName) || profileName == "global"
                ? "Global"
                : profileName;
        }

        internal void UpdateHeaderBattery(string text, Color color)
        {
            _batteryHeaderText.Text = text;
            _batteryHeaderIcon.Foreground = new SolidColorBrush(color);
        }

        #endregion

        #region Pointer (Mouse/Touch) Support

        private void WirePointerEvents()
        {
            // Tab borders: click to switch tab
            for (int i = 0; i < _tabBorders.Length; i++)
            {
                int tabIndex = i;
                _tabBorders[i].PreviewMouseLeftButtonDown += (s, e) =>
                {
                    AutoCommitIfAdjusting();
                    SwitchTab(tabIndex);
                    e.Handled = true;
                };
            }

            // Focusable controls in each tab
            for (int t = 0; t < _tabs.Length; t++)
            {
                var tab = _tabs[t];
                var controls = tab.FocusableControls;
                for (int i = 0; i < controls.Length; i++)
                {
                    int controlIndex = i;
                    controls[i].PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        HandlePointerClick(tab, controlIndex, e);
                    };

                    var slider = tab.GetSlider(i);
                    if (slider != null)
                    {
                        int sliderIndex = i;
                        slider.PreviewMouseLeftButtonUp += (s, e) =>
                        {
                            tab.CommitSliderValue(sliderIndex);
                        };
                    }
                }
            }
        }

        private void HandlePointerClick(SidebarTab tab, int controlIndex, MouseButtonEventArgs e)
        {
            AutoCommitIfAdjusting();

            _focusLayer = FocusLayer.Content;
            _focusIndex = controlIndex;
            UpdateFocusHighlight();

            var controlType = tab.GetControlType(controlIndex);
            switch (controlType)
            {
                case ControlType.Tile:
                    e.Handled = true;
                    break;
                case ControlType.Toggle:
                case ControlType.TileCycle:
                    {
                        bool isAdjusting = false;
                        tab.Activate(controlIndex, ref isAdjusting);
                        UpdateFocusHighlight();
                        e.Handled = true;
                    }
                    break;
                case ControlType.ModeSelector:
                    tab.PointerCycleForward(controlIndex);
                    e.Handled = true;
                    break;
                case ControlType.Slider:
                    // Let WPF Slider handle the mouse event for dragging
                    break;
            }
        }

        private void AutoCommitIfAdjusting()
        {
            if (_focusLayer == FocusLayer.ContentAdjust)
            {
                bool isAdjusting = true;
                ActiveTab.Activate(_focusIndex, ref isAdjusting);
                _focusLayer = FocusLayer.Content;
                UpdateFocusHighlight();
            }
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (_focusLayer == FocusLayer.Content || _focusLayer == FocusLayer.ContentAdjust)
            {
                var slider = ActiveTab.GetSlider(_focusIndex);
                if (slider != null)
                {
                    double newValue = slider.Value + (e.Delta > 0 ? 1 : -1);
                    if (newValue >= slider.Minimum && newValue <= slider.Maximum)
                        slider.Value = newValue;
                    e.Handled = true;
                    return;
                }
            }
            base.OnPreviewMouseWheel(e);
        }

        #endregion
    }
}
