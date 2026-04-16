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
        private void GPDFanCurveToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (GPDFanCurveToggle == null) return;

            bool enabled = GPDFanCurveToggle.IsOn;
            Logger.Info($"GPD fan curve toggled: {enabled}");

            // Send enabled state to helper
            gpdFanCurveEnabled?.SetEnabled(enabled);

            // Toggle visibility of manual vs curve content
            if (GPDManualFanContent != null)
            {
                GPDManualFanContent.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            }
            if (GPDFanCurveContent != null)
            {
                GPDFanCurveContent.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            }

            // Save enabled state
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["GPDFanCurveEnabled"] = enabled;
            }
            catch { }
        }

        private void GPDFanCurveExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isGPDFanCurveExpanded = !isGPDFanCurveExpanded;

            if (GPDFanCurveGraphContent != null)
            {
                GPDFanCurveGraphContent.Visibility = isGPDFanCurveExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (GPDFanCurveExpandIcon != null)
            {
                GPDFanCurveExpandIcon.Glyph = isGPDFanCurveExpanded ? "\uE70E" : "\uE70D";
            }

            // Initialize graph on first expand
            if (isGPDFanCurveExpanded && !gpdFanCurveGraphInitialized)
            {
                InitializeGPDFanCurveGraph();
            }

            // Tell helper whether to push CPU temp updates
            gpdFanCurveVisible?.SetVisible(isGPDFanCurveExpanded);
        }

        private void InitializeGPDFanCurveGraph()
        {
            if (GPDFanCurveCanvas == null || gpdFanCurveGraphInitialized)
                return;

            // Initialize with current values from property
            currentGPDFanCurveValues = gpdFanCurveGraph.GetCurveValues();

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
                gpdFanCurvePoints[i] = ellipse;
                GPDFanCurveCanvas.Children.Add(ellipse);
            }

            gpdFanCurveGraphInitialized = true;

            // Load saved preset selection
            LoadGPDFanCurvePresetSetting();

            // Draw the graph
            DrawGPDGridLines();
            UpdateGPDFanCurveGraph();
        }

        private void DrawGPDGridLines()
        {
            if (GPDFanCurveCanvas == null) return;

            double width = GPDFanCurveCanvas.ActualWidth;
            double height = GPDFanCurveCanvas.ActualHeight;

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
                GPDFanCurveCanvas.Children.Add(line);
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
                GPDFanCurveCanvas.Children.Add(line);
            }
        }

        private void UpdateGPDFanCurveGraph()
        {
            if (GPDFanCurveCanvas == null || GPDFanCurvePolyline == null || GPDFanCurveFill == null)
                return;

            double width = GPDFanCurveCanvas.ActualWidth;
            double height = GPDFanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            var points = new Windows.UI.Xaml.Media.PointCollection();
            var fillPoints = new Windows.UI.Xaml.Media.PointCollection();

            // GPD temperature thresholds: 30-100°C (70°C range)
            for (int i = 0; i < 10; i++)
            {
                int temp = GPDFanCurveTemps[i];
                double x = (temp - 30.0) / 70.0 * width; // Normalize 30-100 to 0-width
                double y = height - (currentGPDFanCurveValues[i] / 100.0 * height);

                points.Add(new Windows.Foundation.Point(x, y));
                fillPoints.Add(new Windows.Foundation.Point(x, y));

                // Position control point
                if (gpdFanCurvePoints[i] != null)
                {
                    Canvas.SetLeft(gpdFanCurvePoints[i], x - 8); // Center the 16px ellipse
                    Canvas.SetTop(gpdFanCurvePoints[i], y - 8);
                }
            }

            GPDFanCurvePolyline.Points = points;

            // Add bottom corners for fill polygon
            fillPoints.Add(new Windows.Foundation.Point(width, height));
            fillPoints.Add(new Windows.Foundation.Point(0, height));
            GPDFanCurveFill.Points = fillPoints;
        }

        private void UpdateGPDTemperatureIndicator(int tempC)
        {
            if (GPDTempIndicatorLine == null || GPDFanCurveCanvas == null)
                return;

            double width = GPDFanCurveCanvas.ActualWidth;
            double height = GPDFanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Clamp temp to 30-100 range
            tempC = Math.Max(30, Math.Min(100, tempC));

            // Calculate X position (30-100°C range = 70°C span)
            double x = (tempC - 30.0) / 70.0 * width;

            GPDTempIndicatorLine.X1 = x;
            GPDTempIndicatorLine.X2 = x;
            GPDTempIndicatorLine.Y1 = 0;
            GPDTempIndicatorLine.Y2 = height;
            GPDTempIndicatorLine.Visibility = Visibility.Visible;
        }

        private void OnGPDFanCurveUpdated(int[] values)
        {
            if (values == null || values.Length != 10) return;

            currentGPDFanCurveValues = values;
            UpdateGPDFanCurveGraph();
        }

        private void OnGPDCPUTempUpdated(int tempC)
        {
            // Update temperature label
            if (GPDCurrentTempLabel != null)
            {
                GPDCurrentTempLabel.Text = $"{tempC}°C";
            }
            // Update temperature indicator on graph
            UpdateGPDTemperatureIndicator(tempC);
        }

        private void GPDFanCurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (gpdFanCurveGraphInitialized)
            {
                // Clear old grid lines
                var toRemove = new System.Collections.Generic.List<Windows.UI.Xaml.UIElement>();
                foreach (var child in GPDFanCurveCanvas.Children)
                {
                    if (child is Windows.UI.Xaml.Shapes.Line line && line != GPDTempIndicatorLine)
                    {
                        toRemove.Add(child);
                    }
                }
                foreach (var item in toRemove)
                {
                    GPDFanCurveCanvas.Children.Remove(item);
                }

                DrawGPDGridLines();
                UpdateGPDFanCurveGraph();

                // Re-update temp indicator if we have a value
                if (gpdCPUTemp != null && gpdCPUTemp.Value > 0)
                {
                    UpdateGPDTemperatureIndicator(gpdCPUTemp.Value);
                }
            }
        }

        private void GPDFanCurveCanvas_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (GPDFanCurveCanvas == null) return;

            var point = e.GetCurrentPoint(GPDFanCurveCanvas).Position;

            // Find the closest control point
            double minDist = double.MaxValue;
            int closestIndex = -1;

            for (int i = 0; i < 10; i++)
            {
                if (gpdFanCurvePoints[i] == null) continue;

                double px = Canvas.GetLeft(gpdFanCurvePoints[i]) + 8;
                double py = Canvas.GetTop(gpdFanCurvePoints[i]) + 8;

                double dist = Math.Sqrt(Math.Pow(point.X - px, 2) + Math.Pow(point.Y - py, 2));
                if (dist < minDist && dist < 30) // 30px hit area
                {
                    minDist = dist;
                    closestIndex = i;
                }
            }

            if (closestIndex >= 0)
            {
                gpdDraggedPointIndex = closestIndex;
                isGPDDraggingPoint = true;
                GPDFanCurveCanvas.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void GPDFanCurveCanvas_PointerMoved(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!isGPDDraggingPoint || gpdDraggedPointIndex < 0 || GPDFanCurveCanvas == null)
                return;

            var point = e.GetCurrentPoint(GPDFanCurveCanvas).Position;
            double height = GPDFanCurveCanvas.ActualHeight;

            // Calculate new fan speed (invert Y since 0 is at top)
            double fanSpeed = (1.0 - point.Y / height) * 100.0;
            fanSpeed = Math.Max(0, Math.Min(100, fanSpeed));

            // Update the value
            currentGPDFanCurveValues[gpdDraggedPointIndex] = (int)Math.Round(fanSpeed);

            // Redraw the graph
            UpdateGPDFanCurveGraph();

            e.Handled = true;
        }

        private void GPDFanCurveCanvas_PointerReleased(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (isGPDDraggingPoint && GPDFanCurveCanvas != null)
            {
                GPDFanCurveCanvas.ReleasePointerCapture(e.Pointer);

                // Switch to Custom preset when manually dragging
                GPDSwitchToCustomPreset();

                // Send the updated values to the helper (debounced)
                gpdFanCurveGraph.SetCurveValuesDebounced(currentGPDFanCurveValues);
            }

            gpdDraggedPointIndex = -1;
            isGPDDraggingPoint = false;
            e.Handled = true;
        }

        private void GPDFanCurvePresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isGPDFanCurvePresetLoading) return;

            if (GPDFanCurvePresetComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string presetName)
            {
                if (presetName == "Custom") return;

                if (GPDFanCurvePresets.TryGetValue(presetName, out int[] presetValues))
                {
                    currentGPDFanCurvePreset = presetName;
                    currentGPDFanCurveValues = (int[])presetValues.Clone();
                    UpdateGPDFanCurveGraph();

                    // Send to helper
                    gpdFanCurveGraph?.SetCurveValuesDebounced(currentGPDFanCurveValues);

                    // Save preset selection
                    SaveGPDFanCurvePresetSetting(presetName);
                }
            }
        }

        private void GPDSwitchToCustomPreset()
        {
            if (currentGPDFanCurvePreset != "Custom")
            {
                currentGPDFanCurvePreset = "Custom";
                isGPDFanCurvePresetLoading = true;
                GPDSelectPresetInComboBox("Custom");
                isGPDFanCurvePresetLoading = false;
                SaveGPDFanCurvePresetSetting("Custom");
            }
        }

        private void GPDSelectPresetInComboBox(string presetName)
        {
            if (GPDFanCurvePresetComboBox == null) return;
            foreach (ComboBoxItem item in GPDFanCurvePresetComboBox.Items)
            {
                if (item.Tag is string tag && tag == presetName)
                {
                    GPDFanCurvePresetComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void SaveGPDFanCurvePresetSetting(string presetName)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["GPDFanCurvePreset"] = presetName;
            }
            catch { }
        }

        private void LoadGPDFanCurvePresetSetting()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("GPDFanCurvePreset", out object saved) && saved is string presetName)
                {
                    currentGPDFanCurvePreset = presetName;
                    isGPDFanCurvePresetLoading = true;
                    GPDSelectPresetInComboBox(presetName);
                    isGPDFanCurvePresetLoading = false;
                }
            }
            catch { }
        }

    }
}
