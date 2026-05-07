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
        // Local cache of every saved per-mode fan curve and EC-override unlock state.
        // Helper pushes all 4 modes on connect via LegionFanCurvePerMode /
        // LegionUnlockFanCurvePerMode; the user-selected mode in the dropdown picks
        // which slot the graph + toggle reflect, decoupled from the actual running
        // power mode. Outbound edits target whichever mode is selected, not active.
        private readonly Dictionary<int, int[]> fanCurveCache = new Dictionary<int, int[]>();
        private readonly Dictionary<int, bool> unlockCache = new Dictionary<int, bool>();
        // Default to Balanced (the XAML dropdown default). Overwritten as soon as the
        // helper syncs LegionPerformanceMode and we auto-jump to the active mode.
        private int selectedFanCurveMode = 2;
        private bool isApplyingFanCurveCacheLoad = false; // suppress UI→helper echo while loading the selected slot

        private void InitializeFanCurveGraph()
        {
            if (FanCurveCanvas == null || fanCurveGraphInitialized)
                return;

            // Initialize with current values from property
            currentFanCurveValues = legionFanCurveGraph.GetCurveValues();

            // Create 10 control point ellipses
            for (int i = 0; i < 10; i++)
            {
                var ellipse = new Windows.UI.Xaml.Shapes.Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 0, 170, 255)),
                    Stroke = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White),
                    StrokeThickness = 2,
                    Tag = i
                };
                fanCurvePoints[i] = ellipse;
                FanCurveCanvas.Children.Add(ellipse);
            }

            fanCurveGraphInitialized = true;

            // Load saved preset selection
            LoadFanCurvePresetSetting();

            // Draw the graph
            DrawGridLines();
            UpdateFanCurveGraph();

            // Sync prefix label + EC floor legend with the persisted unlock state so the
            // first render matches reality (avoids flicker on first toggle).
            RefreshFanCurveGraphForUnlockState();

            // Pick up active mode from the helper-synced LegionPerformanceMode so the
            // dropdown starts on the running mode rather than the XAML default. After
            // this, the cache contents (when the per-mode push lands) repaint the graph.
            JumpFanCurveDropdownToActiveMode();
            UpdateActiveModeLabel();
        }

        // The fan-curve dropdown is a *view selector* — it picks which mode's saved
        // curve and unlock state the user is editing. It does NOT change the running
        // power mode. The "Active: <mode>" label next to it shows what's actually
        // running; that label only appears when the selected mode differs from the
        // active mode.
        private void FanCurvePresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isFanCurvePresetLoading) return;

            if (FanCurvePresetComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string modeStr)
            {
                if (!int.TryParse(modeStr, out int mode)) return;
                if (mode == selectedFanCurveMode) return;

                Logger.Info($"Fan curve view switched to mode {mode} (was {selectedFanCurveMode}); active mode is {legionPerformanceMode?.Value}");
                selectedFanCurveMode = mode;

                // Repaint graph + sync toggle from the cache for the newly selected mode.
                ApplySelectedFanCurveModeFromCache();
                UpdateActiveModeLabel();
                RefreshFanCurveGraphForUnlockState();
            }
        }

        // Stub kept so per-edit code paths still compile. With per-mode storage, manual
        // curve edits stick in whatever mode is selected in the dropdown — no separate
        // "Custom preset" concept anymore.
        private void SwitchToCustomPreset() { }

        // Auto-jump: when the running power mode changes externally (Lenovo button,
        // TDP card, helper push), jump the dropdown to the new active mode so the
        // user is editing the curve that's actually being applied.
        private void JumpFanCurveDropdownToActiveMode()
        {
            if (FanCurvePresetComboBox == null || legionPerformanceMode == null) return;
            int targetMode = legionPerformanceMode.Value;
            string targetTag = targetMode.ToString();
            foreach (ComboBoxItem item in FanCurvePresetComboBox.Items)
            {
                if (item.Tag is string tag && tag == targetTag)
                {
                    if (FanCurvePresetComboBox.SelectedItem != item)
                    {
                        isFanCurvePresetLoading = true;
                        try { FanCurvePresetComboBox.SelectedItem = item; }
                        finally { isFanCurvePresetLoading = false; }
                        selectedFanCurveMode = targetMode;
                        ApplySelectedFanCurveModeFromCache();
                        RefreshFanCurveGraphForUnlockState();
                    }
                    else if (selectedFanCurveMode != targetMode)
                    {
                        selectedFanCurveMode = targetMode;
                        ApplySelectedFanCurveModeFromCache();
                        RefreshFanCurveGraphForUnlockState();
                    }
                    break;
                }
            }
        }

        // Legacy entry point kept for callers that still invoke it from other partials.
        // Same behavior as JumpFanCurveDropdownToActiveMode now (dropdown was a power-
        // mode selector before; it's a view selector now).
        private void SyncFanCurvePresetComboToActiveMode()
        {
            JumpFanCurveDropdownToActiveMode();
            UpdateActiveModeLabel();
        }

        // Show "Active: <mode>" only when the user is viewing a non-active mode, so
        // there's a visual hint that edits to the current view will only kick in
        // once that mode is selected (or via Lenovo button etc).
        private void UpdateActiveModeLabel()
        {
            if (FanCurveActiveModeLabel == null || legionPerformanceMode == null) return;
            int active = legionPerformanceMode.Value;
            if (active == selectedFanCurveMode)
            {
                FanCurveActiveModeLabel.Visibility = Visibility.Collapsed;
            }
            else
            {
                FanCurveActiveModeLabel.Text = $"Active: {LegionModeShortName(active)}";
                FanCurveActiveModeLabel.Visibility = Visibility.Visible;
            }
        }

        private static string LegionModeShortName(int mode)
        {
            switch (mode)
            {
                case 1: return "Quiet";
                case 2: return "Balanced";
                case 3: return "Performance";
                case 255: return "Custom";
                default: return mode.ToString();
            }
        }

        // Loads the cached curve + unlock state for whichever mode is selected into
        // the graph + toggle, suppressing the UI→helper echo so swapping views never
        // triggers a phantom save. If the cache for that mode isn't populated yet
        // (helper hasn't pushed), fall back to the legacy active-mode value so the
        // graph isn't blank.
        private void ApplySelectedFanCurveModeFromCache()
        {
            if (!fanCurveGraphInitialized) return;
            isApplyingFanCurveCacheLoad = true;
            try
            {
                if (fanCurveCache.TryGetValue(selectedFanCurveMode, out int[] cached) && cached != null && cached.Length == 10)
                {
                    currentFanCurveValues = (int[])cached.Clone();
                }
                else if (legionFanCurveGraph != null)
                {
                    currentFanCurveValues = legionFanCurveGraph.GetCurveValues();
                }
                UpdateFanCurveGraph();

                if (LegionUnlockFanCurveToggle != null)
                {
                    bool desired = unlockCache.TryGetValue(selectedFanCurveMode, out bool u) && u;
                    if (LegionUnlockFanCurveToggle.IsOn != desired)
                    {
                        LegionUnlockFanCurveToggle.IsOn = desired;
                    }
                }
            }
            finally
            {
                isApplyingFanCurveCacheLoad = false;
            }
        }

        // Inbound from helper: per-mode fan curve push. Update the cache; if the user
        // is currently viewing this mode, repaint the graph with the new values.
        private void OnFanCurvePerModeReceived(int mode, int[] values)
        {
            if (values == null || values.Length != 10) return;
            fanCurveCache[mode] = (int[])values.Clone();
            if (mode == selectedFanCurveMode && fanCurveGraphInitialized)
            {
                isApplyingFanCurveCacheLoad = true;
                try
                {
                    currentFanCurveValues = (int[])values.Clone();
                    UpdateFanCurveGraph();
                }
                finally { isApplyingFanCurveCacheLoad = false; }
            }
        }

        // Inbound from helper: per-mode unlock push. Update the cache; if the user
        // is currently viewing this mode, sync the toggle without firing the Toggled
        // handler (which would echo back to the helper).
        private void OnUnlockFanCurvePerModeReceived(int mode, bool unlocked)
        {
            unlockCache[mode] = unlocked;
            if (mode == selectedFanCurveMode && LegionUnlockFanCurveToggle != null)
            {
                if (LegionUnlockFanCurveToggle.IsOn != unlocked)
                {
                    isApplyingFanCurveCacheLoad = true;
                    try { LegionUnlockFanCurveToggle.IsOn = unlocked; }
                    finally { isApplyingFanCurveCacheLoad = false; }
                }
                RefreshFanCurveGraphForUnlockState();
            }
        }

        private bool IsViewingActiveFanCurveMode()
            => legionPerformanceMode != null && legionPerformanceMode.Value == selectedFanCurveMode;

        // Legacy preset-name persistence is obsolete now (we don't store a "preset name"
        // separately from power mode). Kept as a no-op so older code paths don't crash.
        private void SaveFanCurvePresetSetting(string presetName) { }

        // Legacy: persisted preset name was used by the old preset dropdown
        // (Silent/Balanced/Performance/MaxCooling/Custom). The dropdown is now keyed
        // off TdpMode and the helper is the source of truth — SyncFanCurvePresetComboToActiveMode
        // drives the selection from legionPerformanceMode.Value. Kept as a no-op so we
        // don't read the now-meaningless LocalSettings value.
        private void LoadFanCurvePresetSetting() { }

        private void DrawGridLines()
        {
            if (FanCurveCanvas == null) return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Draw horizontal grid lines (at 25%, 50%, 75%)
            for (int i = 1; i <= 3; i++)
            {
                double y = height - (height * i * 0.25);
                var line = new Windows.UI.Xaml.Shapes.Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.ColorHelper.FromArgb(50, 255, 255, 255)),
                    StrokeThickness = 1
                };
                Canvas.SetZIndex(line, -1);
                FanCurveCanvas.Children.Add(line);
            }

            // Draw vertical grid lines (at 20%, 40%, 60%, 80%)
            for (int i = 1; i <= 4; i++)
            {
                double x = width * i * 0.2;
                var line = new Windows.UI.Xaml.Shapes.Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = height,
                    Stroke = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.ColorHelper.FromArgb(50, 255, 255, 255)),
                    StrokeThickness = 1
                };
                Canvas.SetZIndex(line, -1);
                FanCurveCanvas.Children.Add(line);
            }

            // Draw EC floor line after grid lines
            DrawECFloorLine();
        }

        private void DrawECFloorLine()
        {
            if (ECFloorPolyline == null || FanCurveCanvas == null) return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            var points = new Windows.UI.Xaml.Media.PointCollection();

            foreach (var (temp, floor) in ECFloorPoints)
            {
                // Map temperature to X position (10-100°C range)
                double x = (temp - 10.0) / 90.0 * width;
                // Map fan % to Y position (inverted)
                double y = height - (floor / 100.0 * height);
                points.Add(new Windows.Foundation.Point(x, y));
            }

            ECFloorPolyline.Points = points;
        }

        private void UpdateFanCurveGraph()
        {
            if (FanCurveCanvas == null || FanCurvePolyline == null || FanCurveFill == null)
                return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            var points = new Windows.UI.Xaml.Media.PointCollection();
            var fillPoints = new Windows.UI.Xaml.Media.PointCollection();

            // Legion Go temperature thresholds: 10, 20, 30, 40, 50, 60, 70, 80, 90, 100°C (FIXED by EC)
            // Map to 0-100% of width (10-100°C range = 90°C)
            for (int i = 0; i < 10; i++)
            {
                int temp = FanCurveTemperatures[i];
                double x = (temp - 10.0) / 90.0 * width; // Normalize 10-100 to 0-width
                double y = height - (currentFanCurveValues[i] / 100.0 * height);

                points.Add(new Windows.Foundation.Point(x, y));
                fillPoints.Add(new Windows.Foundation.Point(x, y));

                // Position control point
                if (fanCurvePoints[i] != null)
                {
                    Canvas.SetLeft(fanCurvePoints[i], x - 8); // Center the 16px ellipse
                    Canvas.SetTop(fanCurvePoints[i], y - 8);
                }
            }

            FanCurvePolyline.Points = points;

            // Add bottom corners for fill polygon
            fillPoints.Add(new Windows.Foundation.Point(width, height));
            fillPoints.Add(new Windows.Foundation.Point(0, height));
            FanCurveFill.Points = fillPoints;
        }

        private void UpdateTemperatureIndicator(int tempC)
        {
            if (TempIndicatorLine == null || FanCurveCanvas == null)
                return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Clamp temp to 10-100 range (Legion Go fan curve range, FIXED by EC)
            tempC = Math.Max(10, Math.Min(100, tempC));

            // Calculate X position (10-100°C range = 90°C span)
            double x = (tempC - 10.0) / 90.0 * width;

            TempIndicatorLine.X1 = x;
            TempIndicatorLine.X2 = x;
            TempIndicatorLine.Y1 = 0;
            TempIndicatorLine.Y2 = height;
            TempIndicatorLine.Visibility = Visibility.Visible;
        }

        private void OnFanCurveUpdated(int[] values)
        {
            if (values == null || values.Length != 10) return;

            // Cache is owned by the per-mode channel (LegionFanCurvePerMode), where the
            // mode is explicit in the payload — race-free. The legacy LegionFanCurveData
            // channel doesn't carry a mode tag, so reading legionPerformanceMode.Value
            // here is racy at startup / mode change. Don't write to the cache from this
            // path; just repaint the graph if the user is viewing the (presumed) active
            // mode and the per-mode push hasn't yet landed.
            if (legionPerformanceMode != null && legionPerformanceMode.Value == selectedFanCurveMode)
            {
                currentFanCurveValues = values;
                UpdateFanCurveGraph();
            }
        }

        // Route the graph's temperature display + indicator based on unlock state.
        // When unlocked, the EC override loop drives the fan from CPU/Tctl (k10temp),
        // matching what HWiNFO/Rodpad show; the displayed temp must match what's
        // actually being used to evaluate the curve. When locked, firmware drives
        // the fan from its own 0x01 sensor, so we show that.
        private void OnCPUTempUpdated(int tempC)
        {
            // Live indicators only meaningful when the user is viewing the curve that's
            // actually running. When viewing a non-active mode, hide the live temp dot
            // since it can't honestly map onto an inactive curve.
            if (!IsViewingActiveFanCurveMode()) { HideLiveIndicators(); return; }
            if (!IsFanCurveOverrideUnlocked()) return; // ignore — fan-sensor path owns the display
            UpdateFanCurveGraphTemp(tempC);
        }

        private void OnFanSensorTempUpdated(int tempC)
        {
            if (!IsViewingActiveFanCurveMode()) { HideLiveIndicators(); return; }
            if (IsFanCurveOverrideUnlocked()) return; // ignore — CPU temp path owns the display
            UpdateFanCurveGraphTemp(tempC);
        }

        // Hide the live temp/RPM dots + label values when viewing a non-active mode.
        private void HideLiveIndicators()
        {
            if (TempIndicatorLine != null) TempIndicatorLine.Visibility = Visibility.Collapsed;
            if (RPMIndicatorLine != null) RPMIndicatorLine.Visibility = Visibility.Collapsed;
            if (CurrentTempLabel != null) CurrentTempLabel.Text = "--";
            if (FanRPMLabel != null) FanRPMLabel.Text = "-- RPM";
        }

        private bool IsFanCurveOverrideUnlocked()
            => legionUnlockFanCurve != null && legionUnlockFanCurve.Value;

        private void UpdateFanCurveGraphTemp(int tempC)
        {
            if (CurrentTempLabel != null)
            {
                CurrentTempLabel.Text = $"{tempC}°C";
            }
            UpdateTemperatureIndicator(tempC);
        }

        private void OnFanRPMUpdated(int rpm)
        {
            if (!IsViewingActiveFanCurveMode())
            {
                HideLiveIndicators();
                return;
            }
            if (FanRPMLabel != null)
            {
                FanRPMLabel.Text = $"{rpm} RPM";
            }

            // Update RPM indicator line on graph
            UpdateRPMIndicator(rpm);
        }

        private void UpdateRPMIndicator(int rpm)
        {
            if (RPMIndicatorLine == null || FanCurveCanvas == null)
                return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Convert RPM to percentage (max 7500 RPM for Legion Go EC scale)
            const int MAX_RPM = 7500;
            double percent = Math.Max(0, Math.Min(100, (double)rpm / MAX_RPM * 100));

            // Calculate Y position (inverted - 0% at bottom, 100% at top)
            double y = height - (percent / 100.0 * height);

            RPMIndicatorLine.X1 = 0;
            RPMIndicatorLine.X2 = width;
            RPMIndicatorLine.Y1 = y;
            RPMIndicatorLine.Y2 = y;
            RPMIndicatorLine.Visibility = Windows.UI.Xaml.Visibility.Visible;
        }

        private void FanCurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (fanCurveGraphInitialized)
            {
                // Clear old grid lines
                var toRemove = new System.Collections.Generic.List<Windows.UI.Xaml.UIElement>();
                foreach (var child in FanCurveCanvas.Children)
                {
                    if (child is Windows.UI.Xaml.Shapes.Line line && line != TempIndicatorLine && line != RPMIndicatorLine)
                    {
                        toRemove.Add(child);
                    }
                }
                foreach (var item in toRemove)
                {
                    FanCurveCanvas.Children.Remove(item);
                }

                DrawGridLines();
                UpdateFanCurveGraph();

                // Re-update temp indicator using whichever sensor matches the current unlock state.
                int? activeTemp = IsFanCurveOverrideUnlocked()
                    ? (legionCPUTemp != null && legionCPUTemp.Value > 0 ? legionCPUTemp.Value : (int?)null)
                    : (legionFanSensorTemp != null && legionFanSensorTemp.Value > 0 ? legionFanSensorTemp.Value : (int?)null);
                if (activeTemp.HasValue)
                {
                    UpdateTemperatureIndicator(activeTemp.Value);
                }
            }
        }

        // Refreshes the graph's temp-display strip and the EC floor visibility whenever
        // the Unlock Fan Curve Override toggle changes state. Called from the toggle's
        // Toggled handler — that event fires for both user clicks and helper-pushed
        // value changes, so we cover both paths.
        private void RefreshFanCurveGraphForUnlockState()
        {
            // The "active mode unlock state" governs the live sensor routing (CPU vs
            // fan sensor) and EC floor visibility — those describe what's running
            // right now. The "selected mode unlock state" governs the X-axis label
            // mode, since the labels describe the curve the user is editing.
            bool activeUnlocked = IsFanCurveOverrideUnlocked();
            bool selectedUnlocked = unlockCache.TryGetValue(selectedFanCurveMode, out bool su) && su;
            bool viewingActive = IsViewingActiveFanCurveMode();

            if (CurrentTempPrefixLabel != null)
                CurrentTempPrefixLabel.Text = activeUnlocked ? "CPU Temp: " : "Fan Sensor Temp: ";

            // EC floor line + legend make no sense once we're bypassing the firmware
            // (active unlock on). Also hide them while editing a non-active mode since
            // they describe the running curve, not the one being edited.
            if (ECFloorPolyline != null)
                ECFloorPolyline.Visibility = (activeUnlocked || !viewingActive) ? Visibility.Collapsed : Visibility.Visible;
            if (ECFloorLegendPanel != null)
                ECFloorLegendPanel.Visibility = (activeUnlocked || !viewingActive) ? Visibility.Collapsed : Visibility.Visible;

            // Temperature axis labels are always visible now — they describe the
            // curve's breakpoint storage (10°C…100°C) regardless of which path
            // applies the curve. Under the locked/firmware path Lenovo may map
            // sensor temp to curve point differently; the Info expander notes
            // that. Showing the labels gives the user a temperature anchor while
            // editing in any state.
            if (FanCurveTempAxisGrid != null)
                FanCurveTempAxisGrid.Visibility = Visibility.Visible;
            if (FanCurveLockedAxisHint != null)
                FanCurveLockedAxisHint.Visibility = Visibility.Collapsed;

            // Hide live temp/RPM indicators when viewing a non-active mode — they
            // can't honestly map onto a curve that isn't currently driving the fan.
            if (!viewingActive)
            {
                HideLiveIndicators();
                return;
            }

            // Push the temp value from whichever sensor owns the display now so the label
            // and indicator switch immediately rather than waiting for the next sensor tick.
            int? newTemp = activeUnlocked
                ? (legionCPUTemp != null && legionCPUTemp.Value > 0 ? legionCPUTemp.Value : (int?)null)
                : (legionFanSensorTemp != null && legionFanSensorTemp.Value > 0 ? legionFanSensorTemp.Value : (int?)null);
            if (newTemp.HasValue)
            {
                UpdateFanCurveGraphTemp(newTemp.Value);
            }
        }

        private void LegionUnlockFanCurveToggle_Toggled(object sender, RoutedEventArgs e)
        {
            try { RefreshFanCurveGraphForUnlockState(); }
            catch (Exception ex) { Logger.Debug($"RefreshFanCurveGraphForUnlockState failed: {ex.Message}"); }

            // Skip outbound while loading the toggle from any non-user source:
            //   1. ApplySelectedFanCurveModeFromCache (view switch) — `isApplyingFanCurveCacheLoad`.
            //   2. OnUnlockFanCurvePerModeReceived (per-mode push) — same flag.
            //   3. WidgetToggleProperty syncing legacy LegionUnlockFanCurve from helper
            //      (mode-change push) — `legionUnlockFanCurve.IsUpdatingUI`. Without
            //      this, an external mode change would route the active-mode unlock
            //      flip into whichever mode the user happened to have in the dropdown.
            if (isApplyingFanCurveCacheLoad) return;
            if (legionUnlockFanCurve != null && legionUnlockFanCurve.IsUpdatingUI) return;
            if (LegionUnlockFanCurveToggle == null) return;

            bool unlocked = LegionUnlockFanCurveToggle.IsOn;
            unlockCache[selectedFanCurveMode] = unlocked;
            // Send to helper keyed by the dropdown-selected mode (NOT necessarily the
            // active mode). Helper persists in the right slot and only flips the EC
            // override loop if the toggled mode happens to be the running power mode.
            legionUnlockFanCurvePerMode?.SendForMode(selectedFanCurveMode, unlocked);
            Logger.Info($"Unlock toggle set to {unlocked} for mode {selectedFanCurveMode} (active={legionPerformanceMode?.Value})");
        }

        private void FanCurveCanvas_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (FanCurveCanvas == null) return;

            var point = e.GetCurrentPoint(FanCurveCanvas).Position;

            // Find the closest control point
            double minDist = double.MaxValue;
            int closestIndex = -1;

            for (int i = 0; i < 10; i++)
            {
                if (fanCurvePoints[i] == null) continue;

                double px = Canvas.GetLeft(fanCurvePoints[i]) + 8;
                double py = Canvas.GetTop(fanCurvePoints[i]) + 8;

                double dist = Math.Sqrt(Math.Pow(point.X - px, 2) + Math.Pow(point.Y - py, 2));
                if (dist < minDist && dist < 30) // 30px hit area
                {
                    minDist = dist;
                    closestIndex = i;
                }
            }

            if (closestIndex >= 0)
            {
                draggedPointIndex = closestIndex;
                isDraggingPoint = true;
                FanCurveCanvas.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void FanCurveCanvas_PointerMoved(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!isDraggingPoint || draggedPointIndex < 0 || FanCurveCanvas == null)
                return;

            var point = e.GetCurrentPoint(FanCurveCanvas).Position;
            double height = FanCurveCanvas.ActualHeight;

            // Calculate new fan speed (invert Y since 0 is at top)
            double fanSpeed = (1.0 - point.Y / height) * 100.0;

            // Enforce minimum fan speed for this temperature threshold
            int minSpeed = FanCurveMinSpeeds[draggedPointIndex];
            fanSpeed = Math.Max(minSpeed, Math.Min(100, fanSpeed));

            // Update the value
            currentFanCurveValues[draggedPointIndex] = (int)Math.Round(fanSpeed);

            // Redraw the graph
            UpdateFanCurveGraph();

            e.Handled = true;
        }

        private void FanCurveCanvas_PointerReleased(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (isDraggingPoint && FanCurveCanvas != null)
            {
                FanCurveCanvas.ReleasePointerCapture(e.Pointer);

                // Update the cache for whichever mode the dropdown is showing — that's
                // the slot the edit targets (NOT necessarily the active mode).
                fanCurveCache[selectedFanCurveMode] = (int[])currentFanCurveValues.Clone();

                // Push to helper via the per-mode channel so the helper persists this
                // edit in the right slot. Helper will only write to hardware if the
                // edited mode happens to be the running power mode.
                if (legionFanCurvePerMode != null)
                {
                    legionFanCurvePerMode.SendForMode(selectedFanCurveMode, currentFanCurveValues);
                }
            }

            draggedPointIndex = -1;
            isDraggingPoint = false;
            e.Handled = true;
        }

    }
}
