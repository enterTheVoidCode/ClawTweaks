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
        }

        private void FanCurvePresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isFanCurvePresetLoading) return;

            if (FanCurvePresetComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string presetName)
            {
                if (presetName == "Custom") return; // User manually selected Custom, no action needed

                if (FanCurvePresets.TryGetValue(presetName, out int[] presetValues))
                {
                    currentFanCurvePreset = presetName;
                    currentFanCurveValues = (int[])presetValues.Clone();
                    UpdateFanCurveGraph();

                    // Send to helper
                    legionFanCurveGraph?.SetCurveValuesDebounced(currentFanCurveValues);

                    // Save preset selection
                    SaveFanCurvePresetSetting(presetName);
                }
            }
        }

        private void SwitchToCustomPreset()
        {
            if (currentFanCurvePreset != "Custom")
            {
                currentFanCurvePreset = "Custom";
                isFanCurvePresetLoading = true;
                SelectPresetInComboBox("Custom");
                isFanCurvePresetLoading = false;
                SaveFanCurvePresetSetting("Custom");
            }
        }

        private void SelectPresetInComboBox(string presetName)
        {
            if (FanCurvePresetComboBox == null) return;
            foreach (ComboBoxItem item in FanCurvePresetComboBox.Items)
            {
                if (item.Tag is string tag && tag == presetName)
                {
                    FanCurvePresetComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void SaveFanCurvePresetSetting(string presetName)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["FanCurvePreset"] = presetName;
            }
            catch { }
        }

        private void LoadFanCurvePresetSetting()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("FanCurvePreset", out object saved) && saved is string presetName)
                {
                    currentFanCurvePreset = presetName;
                    isFanCurvePresetLoading = true;
                    SelectPresetInComboBox(presetName);
                    isFanCurvePresetLoading = false;
                }
            }
            catch { }
        }

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

            currentFanCurveValues = values;
            UpdateFanCurveGraph();
        }

        private void OnCPUTempUpdated(int tempC)
        {
            // CPU temp is shown as reference only, fan sensor temp is used for graph indicator
            // (CPU temp is typically 10-17°C higher than fan sensor temp)
        }

        private void OnFanSensorTempUpdated(int tempC)
        {
            // Update temperature label (this is the temp the EC uses for fan curve)
            if (CurrentTempLabel != null)
            {
                CurrentTempLabel.Text = $"{tempC}°C";
            }
            // Update temperature indicator on graph (fan sensor temp matches the curve's X-axis)
            UpdateTemperatureIndicator(tempC);
        }

        private void OnFanRPMUpdated(int rpm)
        {
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

                // Re-update temp indicator if we have a value (fan sensor temp is used for graph)
                if (legionFanSensorTemp != null && legionFanSensorTemp.Value > 0)
                {
                    UpdateTemperatureIndicator(legionFanSensorTemp.Value);
                }
            }
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

                // Switch to Custom preset when manually dragging
                SwitchToCustomPreset();

                // Send the updated values to the helper (debounced)
                legionFanCurveGraph.SetCurveValuesDebounced(currentFanCurveValues);
            }

            draggedPointIndex = -1;
            isDraggingPoint = false;
            e.Handled = true;
        }

    }
}
