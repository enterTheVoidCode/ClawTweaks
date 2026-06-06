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

        private void NavRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton selectedItem)
            {
                string tag = selectedItem.Tag?.ToString() ?? "";

                // Ensure the active-tab pill is themed (some tab templates aren't realised yet when
                // ApplyTheme runs at load, so they'd show the un-themed grey pill on first select).
                ApplyNavPillTheme(selectedItem);

                // Hide all sections
                QuickSettingsScrollViewer.Visibility = Visibility.Collapsed;
                PerformanceScrollViewer.Visibility = Visibility.Collapsed;
                GameScrollViewer.Visibility = Visibility.Collapsed;
                AMDScrollViewer.Visibility = Visibility.Collapsed;
                if (DisplayScrollViewer != null) DisplayScrollViewer.Visibility = Visibility.Collapsed;
                if (FanScrollViewer != null) FanScrollViewer.Visibility = Visibility.Collapsed;
                ScalingScrollViewer.Visibility = Visibility.Collapsed;
                LegionScrollViewer.Visibility = Visibility.Collapsed;
                GPDScrollViewer.Visibility = Visibility.Collapsed;
                SystemScrollViewer.Visibility = Visibility.Collapsed;
                // TriggerScrollViewer.Visibility = Visibility.Collapsed;  // Hotkeys tab hidden

                // Stop fan curve updates when leaving Legion tab (will be re-enabled if Legion is selected)
                legionFanCurveVisible?.SetVisible(false);

                // Stop DAService status polling when leaving Legion tab
                daServiceStatusTimer?.Stop();

                // Show selected section and scroll to top
                switch (tag)
                {
                    case "Quick":
                        QuickSettingsScrollViewer.Visibility = Visibility.Visible;
                        QuickSettingsScrollViewer.ChangeView(null, 0, null, true);
                        UpdateQuickSettingsTileStates();
                        break;
                    case "Performance":
                        PerformanceScrollViewer.Visibility = Visibility.Visible;
                        PerformanceScrollViewer.ChangeView(null, 0, null, true);
                        // Re-evaluate the MSI fan card here too: the device name may have
                        // arrived after Loaded, and this guarantees a fresh check on the
                        // instance the user is actually viewing.
                        InitializeMsiFanCard();
                        break;
                    case "Game":
                        GameScrollViewer.Visibility = Visibility.Visible;
                        GameScrollViewer.ChangeView(null, 0, null, true);
                        break;
                    case "AMD":
                        AMDScrollViewer.Visibility = Visibility.Visible;
                        AMDScrollViewer.ChangeView(null, 0, null, true);
                        break;
                    case "Display":
                        if (DisplayScrollViewer != null)
                        {
                            DisplayScrollViewer.Visibility = Visibility.Visible;
                            DisplayScrollViewer.ChangeView(null, 0, null, true);
                        }
                        break;
                    case "Fan":
                        // Reparent the card into the Fan tab on first open (stable moment;
                        // other tabs are collapsed). Then show + refresh.
                        EnsureFanCardInFanTab();
                        if (FanScrollViewer != null)
                        {
                            FanScrollViewer.Visibility = Visibility.Visible;
                            FanScrollViewer.ChangeView(null, 0, null, true);
                        }
                        InitializeMsiFanCard();
                        break;
                    // case "Trigger":  // Hotkeys tab hidden — redundant with Main > Customize keyboard hotkeys
                    //     TriggerScrollViewer.Visibility = Visibility.Visible;
                    //     TriggerScrollViewer.ChangeView(null, 0, null, true);
                    //     break;
                    case "Scaling":
                        ScalingScrollViewer.Visibility = Visibility.Visible;
                        ScalingScrollViewer.ChangeView(null, 0, null, true);
                        UpdateLosslessScalingStatus();
                        break;
                    case "Legion":
                        LegionScrollViewer.Visibility = Visibility.Visible;
                        LegionScrollViewer.ChangeView(null, 0, null, true);
                        // Update fan curve visibility when switching to Legion tab
                        legionFanCurveVisible?.SetVisible(isFanCurveExpanded);
                        // Start DAService status polling when on Legion tab
                        if (daServiceStatusTimer != null)
                        {
                            UpdateDAServiceStatus(); // Immediate update
                            daServiceStatusTimer.Start();
                        }
                        // Request ViGEmBus status for button remap section
                        RequestViGEmBusStatus();
                        // Force remap UI refresh when Legion tab becomes active.
                        RefreshLegionEnhancedRemapUi();
                        break;
                    case "GPD":
                        GPDScrollViewer.Visibility = Visibility.Visible;
                        GPDScrollViewer.ChangeView(null, 0, null, true);
                        break;
                    case "System":
                        SystemScrollViewer.Visibility = Visibility.Visible;
                        SystemScrollViewer.ChangeView(null, 0, null, true);
                        RequestControllerEmulationDriverStatus();
                        break;
                }

                // Re-apply theme to newly visible tab (StaticResources don't update dynamically)
                // Defer with delay to ensure visual tree is fully loaded
                if (currentThemeName != "Default")
                {
                    _ = ApplyThemeToCurrentTabAsync();
                }
            }
        }

        private async Task ApplyThemeToCurrentTabAsync()
        {
            // Wait for visual tree to fully load
            await Task.Delay(50);
            ApplyThemeToCurrentTab();
        }

        private void ApplyThemeToCurrentTab()
        {
            if (!WidgetThemes.TryGetValue(currentThemeName, out var theme)) return;

            var cardBgBrush = new SolidColorBrush(theme.CardBackground);
            var cardBorderBrush = new SolidColorBrush(theme.CardBorder);
            var accentBrush = new SolidColorBrush(theme.AccentColor);
            var textSecondaryBrush = new SolidColorBrush(theme.TextSecondary);

            // Apply to all scroll viewers (only visible ones will have loaded content)
            ApplyThemeToVisualTree(QuickSettingsScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(PerformanceScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(GameScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(AMDScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(ScalingScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(LegionScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(SystemScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
        }

        // Trigger-press edge tracking for tab navigation. Holding LT/RT would otherwise
        // auto-repeat (WasKeyDown=true) and cycle tabs continuously. We require the user
        // to release the trigger before accepting another press, and also apply a small
        // minimum interval as a belt-and-suspenders debounce.
        private bool ltTriggerHeld;
        private bool rtTriggerHeld;
        private bool yButtonHeld;
        private bool xButtonHeld;
        private DateTime lastTriggerNavigateUtc = DateTime.MinValue;
        private DateTime lastYxButtonUtc = DateTime.MinValue;
        private static readonly TimeSpan TriggerNavigateDebounce = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan YXButtonDebounce = TimeSpan.FromMilliseconds(200);

        private void GamingWidget_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // LT / RT — tab navigation
            if (e.Key == VirtualKey.GamepadLeftTrigger)
            {
                if (!ltTriggerHeld && !e.KeyStatus.WasKeyDown
                    && (DateTime.UtcNow - lastTriggerNavigateUtc) >= TriggerNavigateDebounce)
                {
                    ltTriggerHeld = true;
                    lastTriggerNavigateUtc = DateTime.UtcNow;
                    NavigateToPreviousTab();
                }
                e.Handled = true;
                return;
            }
            else if (e.Key == VirtualKey.GamepadRightTrigger)
            {
                if (!rtTriggerHeld && !e.KeyStatus.WasKeyDown
                    && (DateTime.UtcNow - lastTriggerNavigateUtc) >= TriggerNavigateDebounce)
                {
                    rtTriggerHeld = true;
                    lastTriggerNavigateUtc = DateTime.UtcNow;
                    NavigateToNextTab();
                }
                e.Handled = true;
                return;
            }
            // Y — jump focus to the active tab in the nav bar
            else if (e.Key == VirtualKey.GamepadY)
            {
                if (!yButtonHeld && !e.KeyStatus.WasKeyDown
                    && (DateTime.UtcNow - lastYxButtonUtc) >= YXButtonDebounce)
                {
                    yButtonHeld = true;
                    lastYxButtonUtc = DateTime.UtcNow;
                    FocusActiveTab();
                }
                e.Handled = true;
                return;
            }
            // X — collapse the expanded segment that currently contains focus
            else if (e.Key == VirtualKey.GamepadX)
            {
                if (!xButtonHeld && !e.KeyStatus.WasKeyDown
                    && (DateTime.UtcNow - lastYxButtonUtc) >= YXButtonDebounce)
                {
                    xButtonHeld = true;
                    lastYxButtonUtc = DateTime.UtcNow;
                    CollapseContainingSection();
                }
                e.Handled = true;
                return;
            }
            // D-pad down from nav area → enter content at the FIRST element of the active tab.
            else if (e.Key == VirtualKey.GamepadDPadDown)
            {
                var focusedElement = FocusManager.GetFocusedElement() as FrameworkElement;
                if (focusedElement != null && IsInNavigationArea(focusedElement))
                {
                    e.Handled = true;
                    FocusFirstTabContentElement();
                }
            }
            // D-pad up from content area → let UWP spatial navigation try first.
            // If TryMoveFocus(Up) fails — because all elements above the current focus are
            // disabled (IsEnabled=false) and therefore invisible to XY navigation — fall
            // back to the active nav tab. This is the ONLY reliable escape from a
            // "stuck below disabled controls" situation; XYFocusUp bindings on disabled
            // targets are silently ignored by UWP.
            // We ALWAYS mark e.Handled here to prevent per-control KeyDown handlers from
            // double-navigating: PreviewKeyDown (tunneling) fires before KeyDown (bubbling),
            // so consuming the event here stops the per-control handler from also acting.
            else if (e.Key == VirtualKey.GamepadDPadUp)
            {
                var focusedElement = FocusManager.GetFocusedElement() as FrameworkElement;
                if (focusedElement != null && !IsInNavigationArea(focusedElement))
                {
                    bool moved = FocusManager.TryMoveFocus(FocusNavigationDirection.Up);
                    if (!moved)
                    {
                        // Nothing enabled above — always let the user escape to the nav bar.
                        FocusActiveTab();
                    }
                    // Always consume — prevent per-control KeyDown from double-navigating
                    // (both TryMoveFocus success and fallback paths need this).
                    e.Handled = true;
                }
            }
        }

        private void GamingWidget_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.GamepadLeftTrigger)
                ltTriggerHeld = false;
            else if (e.Key == VirtualKey.GamepadRightTrigger)
                rtTriggerHeld = false;
            else if (e.Key == VirtualKey.GamepadY)
                yButtonHeld = false;
            else if (e.Key == VirtualKey.GamepadX)
                xButtonHeld = false;
        }

        /// <summary>
        /// Forcibly clears any held LT/RT press-edge state. Called when the widget gains
        /// focus or when VIIPER/controller emulation toggles. HidHide CyclePort on the
        /// physical pad during emulation setup can leave the OS believing RT/LT is
        /// stuck-down (no KeyUp arrives because the device disappeared between events),
        /// which would otherwise leave tab nav wedged until the user gets a fresh KeyUp.
        /// Resetting here lets the very next physical press act as a clean press-edge.
        /// </summary>
        internal void ResetTriggerTabNavState()
        {
            if (ltTriggerHeld || rtTriggerHeld)
            {
                Logger.Info("Clearing stuck LT/RT tab-nav state (focus/emulation transition)");
            }
            ltTriggerHeld = false;
            rtTriggerHeld = false;
            lastTriggerNavigateUtc = DateTime.MinValue;
        }

        private bool IsInNavigationArea(FrameworkElement element)
        {
            // Check if any of our nav items has focus
            // This works regardless of whether the item is in the main bar or overflow menu
            if (QuickNavItem.FocusState != FocusState.Unfocused) return true;
            if (PerformanceNavItem.FocusState != FocusState.Unfocused) return true;
            if (ProfilesNavItem.FocusState != FocusState.Unfocused) return true;
            if (GraphicsNavItem.FocusState != FocusState.Unfocused) return true;
            if (ScalingNavItem.FocusState != FocusState.Unfocused) return true;
            if (LegionNavItem.FocusState != FocusState.Unfocused) return true;
            if (GPDNavItem.FocusState != FocusState.Unfocused) return true;
            if (SystemNavItem.FocusState != FocusState.Unfocused) return true;

            // Fallback: walk visual tree for other nav-related elements
            var current = element;
            while (current != null)
            {
                // Check if we're in the nav panel
                if (current == MainNavPanel)
                    return true;
                current = VisualTreeHelper.GetParent(current) as FrameworkElement;
            }
            return false;
        }

        private void NavigateToPreviousTab()
        {
            var visibleItems = GetVisibleNavigationItems();
            if (visibleItems.Count == 0) return;

            var currentItem = visibleItems.FirstOrDefault(rb => rb.IsChecked == true);
            int currentIndex = currentItem != null ? visibleItems.IndexOf(currentItem) : 0;

            RadioButton nextTab;
            if (currentIndex > 0)
                nextTab = visibleItems[currentIndex - 1];
            else
                nextTab = visibleItems[visibleItems.Count - 1]; // wrap around

            nextTab.IsChecked = true;
            // Move visual focus to the newly-active tab so D-pad Down reliably
            // targets the correct tab's content (spatial nav depends on focus position).
            nextTab.Focus(FocusState.Programmatic);
        }

        private void NavigateToNextTab()
        {
            var visibleItems = GetVisibleNavigationItems();
            if (visibleItems.Count == 0) return;

            var currentItem = visibleItems.FirstOrDefault(rb => rb.IsChecked == true);
            int currentIndex = currentItem != null ? visibleItems.IndexOf(currentItem) : 0;

            RadioButton nextTab;
            if (currentIndex < visibleItems.Count - 1)
                nextTab = visibleItems[currentIndex + 1];
            else
                nextTab = visibleItems[0]; // wrap around

            nextTab.IsChecked = true;
            nextTab.Focus(FocusState.Programmatic);
        }

        private List<RadioButton> GetVisibleNavigationItems()
        {
            var visibleItems = new List<RadioButton>();
            foreach (var item in MainNavPanel.Children)
            {
                if (item is RadioButton radioButton && radioButton.Visibility == Visibility.Visible)
                {
                    visibleItems.Add(radioButton);
                }
            }
            return visibleItems;
        }

        /// <summary>
        /// Returns the ScrollViewer of the currently visible tab, or null if none found.
        /// Used to determine where D-pad Down should land when focus is in the nav bar.
        /// </summary>
        private ScrollViewer GetActiveScrollViewer()
        {
            if (QuickSettingsScrollViewer?.Visibility == Visibility.Visible) return QuickSettingsScrollViewer;
            if (PerformanceScrollViewer?.Visibility == Visibility.Visible) return PerformanceScrollViewer;
            if (GameScrollViewer?.Visibility == Visibility.Visible) return GameScrollViewer;
            if (AMDScrollViewer?.Visibility == Visibility.Visible) return AMDScrollViewer;
            if (DisplayScrollViewer?.Visibility == Visibility.Visible) return DisplayScrollViewer;
            if (FanScrollViewer?.Visibility == Visibility.Visible) return FanScrollViewer;
            if (ScalingScrollViewer?.Visibility == Visibility.Visible) return ScalingScrollViewer;
            if (LegionScrollViewer?.Visibility == Visibility.Visible) return LegionScrollViewer;
            if (GPDScrollViewer?.Visibility == Visibility.Visible) return GPDScrollViewer;
            if (SystemScrollViewer?.Visibility == Visibility.Visible) return SystemScrollViewer;
            return null;
        }

        /// <summary>
        /// D-pad Down from the nav bar: focus the known first interactive element for the
        /// active tab, scrolled to top. Uses explicit per-tab lookups rather than
        /// FindFirstFocusableElement(), which was unreliable with disabled controls and
        /// complex ToggleSwitch templates (jumped past the FPS card to TDP slider).
        /// </summary>
        private void FocusFirstTabContentElement()
        {
            var viewer = GetActiveScrollViewer();
            if (viewer == null) { FocusManager.TryMoveFocus(FocusNavigationDirection.Down); return; }

            // Scroll to top so the target element is on-screen.
            viewer.ChangeView(null, 0, null, true);

            // Per-tab explicit first-element lookup. The element must be enabled (IsEnabled=true)
            // because UWP can't focus a disabled control regardless of IsTabStop.
            Control target = null;

            if (PerformanceScrollViewer?.Visibility == Visibility.Visible)
            {
                // Performance tab: pick the topmost enabled spine element so D-pad Down
                // from the nav bar always lands at a focusable control.
                // FPSStateCycleButton is always enabled — use it as the guaranteed fallback.
                if (PerGameProfileToggle?.IsEnabled == true)
                    target = PerGameProfileToggle;
                else if (FPSStateCycleButton != null)
                    target = FPSStateCycleButton; // always enabled
                // else: fall through to FindFirstFocusableElement
            }
            else if (SystemScrollViewer?.Visibility == Visibility.Visible)
            {
                target = ThemeComboBox;
            }
            else if (ScalingScrollViewer?.Visibility == Visibility.Visible)
            {
                target = LosslessScalingEnabledToggle?.IsEnabled == true
                    ? (Control)LosslessScalingEnabledToggle
                    : LosslessScalingAutoScaleToggle;
            }

            if (target != null && target.IsEnabled)
            {
                target.Focus(FocusState.Programmatic);
                return;
            }

            // Generic fallback: FindFirstFocusableElement for tabs without an explicit mapping
            // (Quick, Display, Fan, Profiles, GPD, Scaling fallback)
            var first = FocusManager.FindFirstFocusableElement(viewer) as Control;
            if (first != null)
                first.Focus(FocusState.Programmatic);
            else
                FocusManager.TryMoveFocus(FocusNavigationDirection.Down);
        }

        /// <summary>
        /// Y button: move focus to the currently active (checked) tab in the nav bar.
        /// Falls back to the first visible tab if none is checked.
        /// </summary>
        private void FocusActiveTab()
        {
            var items = GetVisibleNavigationItems();
            if (items.Count == 0) return;
            var active = items.FirstOrDefault(rb => rb.IsChecked == true) ?? items[0];
            active.Focus(FocusState.Programmatic);
        }

        /// <summary>
        /// X button: walk up the visual tree from the focused element and collapse the
        /// first expandable section that contains it. Focus moves to the section's toggle
        /// button so the user can re-expand without leaving the card area.
        /// </summary>
        private void CollapseContainingSection()
        {
            var focused = FocusManager.GetFocusedElement() as DependencyObject;
            if (focused == null) return;

            var current = focused;
            while (current != null)
            {
                if (current is FrameworkElement fe && TryCollapse(fe))
                    return;
                current = VisualTreeHelper.GetParent(current);
            }
        }

        private bool TryCollapse(FrameworkElement fe)
        {
            // Each entry: content panel, is-expanded flag, collapse action, toggle button to re-focus.
            // Only collapse when expanded — never accidentally expand.
            if ((fe == ControllerEmulationContent || fe == ViiperEmulationContent) && isControllerEmulationExpanded)
            {
                isControllerEmulationExpanded = false;
                ControllerEmulationContent.Visibility = Visibility.Collapsed;
                if (ViiperEmulationContent != null) ViiperEmulationContent.Visibility = Visibility.Collapsed;
                if (ControllerEmulationExpandIcon != null) ControllerEmulationExpandIcon.Glyph = "";
                ControllerEmulationExpandButton?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == GyroActivationContent && isGyroActivationExpanded)
            {
                isGyroActivationExpanded = false;
                GyroActivationContent.Visibility = Visibility.Collapsed;
                if (GyroActivationExpandIcon != null) GyroActivationExpandIcon.Glyph = "";
                GyroActivationExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == FeaturesContent && isFeaturesExpanded)
            {
                isFeaturesExpanded = false;
                FeaturesContent.Visibility = Visibility.Collapsed;
                if (FeaturesExpandIcon != null) FeaturesExpandIcon.Glyph = "";
                FeaturesExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == JoystickOutputContent && isJoystickOutputExpanded)
            {
                isJoystickOutputExpanded = false;
                JoystickOutputContent.Visibility = Visibility.Collapsed;
                if (JoystickOutputExpandIcon != null) JoystickOutputExpandIcon.Glyph = "";
                JoystickOutputExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == OSDCustomizeContent && isOSDCustomizeExpanded)
            {
                isOSDCustomizeExpanded = false;
                OSDCustomizeContent.Visibility = Visibility.Collapsed;
                if (OSDCustomizeExpandIcon != null) OSDCustomizeExpandIcon.Glyph = "";
                OSDCustomizeExpandButton?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == TDPSettingsContent && isTDPSettingsExpanded)
            {
                isTDPSettingsExpanded = false;
                TDPSettingsContent.Visibility = Visibility.Collapsed;
                if (TDPSettingsExpandIcon != null) TDPSettingsExpandIcon.Glyph = "";
                TDPSettingsExpandButton?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == CPUExtrasContent && isCPUExtrasExpanded)
            {
                isCPUExtrasExpanded = false;
                CPUExtrasContent.Visibility = Visibility.Collapsed;
                CPUExtrasExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == TDPExtrasContent && isTDPExtrasExpanded)
            {
                isTDPExtrasExpanded = false;
                TDPExtrasContent.Visibility = Visibility.Collapsed;
                TDPExtrasExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == FanCurveContent && isFanCurveExpanded)
            {
                isFanCurveExpanded = false;
                FanCurveContent.Visibility = Visibility.Collapsed;
                if (FanCurveExpandIcon != null) FanCurveExpandIcon.Glyph = "";
                FanCurveExpandToggle?.Focus(FocusState.Programmatic);
                legionFanCurveVisible?.SetVisible(false);
                return true;
            }
            if (fe == ProfileDetectionContent && isProfileDetectionExpanded)
            {
                isProfileDetectionExpanded = false;
                ProfileDetectionContent.Visibility = Visibility.Collapsed;
                if (ProfileDetectionExpandIcon != null) ProfileDetectionExpandIcon.Glyph = "";
                ProfileDetectionExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == ProfileSettingsContent && isProfileSettingsExpanded)
            {
                isProfileSettingsExpanded = false;
                ProfileSettingsContent.Visibility = Visibility.Collapsed;
                if (ProfileSettingsExpandIcon != null) ProfileSettingsExpandIcon.Glyph = "";
                ProfileSettingsExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == TouchpadVibrationContent && isTouchpadVibrationExpanded)
            {
                isTouchpadVibrationExpanded = false;
                TouchpadVibrationContent.Visibility = Visibility.Collapsed;
                if (TouchpadVibrationExpandIcon != null) TouchpadVibrationExpandIcon.Glyph = "";
                TouchpadVibrationExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == GyroSettingsContent && isGyroSettingsExpanded)
            {
                isGyroSettingsExpanded = false;
                GyroSettingsContent.Visibility = Visibility.Collapsed;
                if (GyroSettingsExpandIcon != null) GyroSettingsExpandIcon.Glyph = "";
                GyroSettingsExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == ButtonRemappingContent && isButtonRemappingExpanded)
            {
                isButtonRemappingExpanded = false;
                ButtonRemappingContent.Visibility = Visibility.Collapsed;
                if (ButtonRemappingExpandIcon != null) ButtonRemappingExpandIcon.Glyph = "";
                ButtonRemappingExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == StickDeadzonesContent && isStickDeadzonesExpanded)
            {
                isStickDeadzonesExpanded = false;
                StickDeadzonesContent.Visibility = Visibility.Collapsed;
                if (StickDeadzonesExpandIcon != null) StickDeadzonesExpandIcon.Glyph = "";
                StickDeadzonesExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == SpecialRemappingContent && isSpecialRemappingExpanded)
            {
                isSpecialRemappingExpanded = false;
                SpecialRemappingContent.Visibility = Visibility.Collapsed;
                if (SpecialRemappingExpandIcon != null) SpecialRemappingExpandIcon.Glyph = "";
                SpecialRemappingExpandButton?.Focus(FocusState.Programmatic);
                return true;
            }
            if (fe == LightingContent && isLightingExpanded)
            {
                isLightingExpanded = false;
                LightingContent.Visibility = Visibility.Collapsed;
                if (LightingExpandIcon != null) LightingExpandIcon.Glyph = "";
                LightingExpandToggle?.Focus(FocusState.Programmatic);
                return true;
            }
            return false;
        }

    }
}
