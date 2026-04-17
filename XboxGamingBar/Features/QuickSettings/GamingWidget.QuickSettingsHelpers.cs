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
        /// <summary>
        /// Toggles the Legion Power Light on/off.
        /// </summary>
        private void ToggleLegionPowerLight()
        {
            if (legionPowerLight == null) return;

            // Toggle the current state
            bool newState = !legionPowerLight.Value;
            legionPowerLight.SetValue(newState);

            Logger.Info($"Power Light toggled: {(newState ? "On" : "Off")}");
        }

        /// <summary>
        /// Toggles Fan Full Speed mode on/off for Legion or GPD devices.
        /// </summary>
        private bool gpdFanMaxActive = false;

        // GPD Software Fan Curve graph state
        private bool isGPDFanCurveExpanded = false;
        private bool gpdFanCurveGraphInitialized = false;
        private readonly Windows.UI.Xaml.Shapes.Ellipse[] gpdFanCurvePoints = new Windows.UI.Xaml.Shapes.Ellipse[10];
        private int[] currentGPDFanCurveValues = new int[10];
        private int gpdDraggedPointIndex = -1;
        private bool isGPDDraggingPoint = false;
        private static readonly int[] GPDFanCurveTemps = { 30, 38, 46, 54, 62, 70, 78, 86, 94, 100 };
        private static readonly Dictionary<string, int[]> GPDFanCurvePresets = new Dictionary<string, int[]>
        {
            { "Silent",      new int[] { 0, 0, 0, 30, 35, 45, 55, 65, 80, 100 } },
            { "Balanced",    new int[] { 0, 30, 35, 45, 55, 65, 75, 85, 95, 100 } },
            { "Performance", new int[] { 30, 40, 50, 55, 60, 70, 80, 90, 95, 100 } },
            { "MaxCooling",  new int[] { 40, 50, 60, 65, 70, 80, 85, 95, 100, 100 } }
        };
        private string currentGPDFanCurvePreset = "Custom";
        private bool isGPDFanCurvePresetLoading = false;

        private void ToggleLegionFanFullSpeed()
        {
            if (legionGoDetected?.Value == true && legionFanFullSpeed != null)
            {
                bool newState = !legionFanFullSpeed.Value;
                legionFanFullSpeed.SetValue(newState);

                if (LegionFanFullSpeedToggle != null)
                {
                    LegionFanFullSpeedToggle.IsOn = newState;
                }

                Logger.Info($"Fan Full Speed toggled (Legion): {(newState ? "On" : "Off")}");
            }
            else if (gpdDetected?.Value == true && gpdFanMode != null && gpdFanSpeed != null)
            {
                gpdFanMaxActive = !gpdFanMaxActive;
                if (gpdFanMaxActive)
                {
                    gpdFanMode.SetMode(1); // Manual
                    gpdFanSpeed.SetSpeed(100); // 100%
                    if (GPDFanModeToggle != null)
                    {
                        GPDFanModeToggle.Toggled -= GPDFanModeToggle_Toggled;
                        GPDFanModeToggle.IsOn = true;
                        GPDFanModeToggle.Toggled += GPDFanModeToggle_Toggled;
                    }
                    if (GPDFanSpeedSlider != null) GPDFanSpeedSlider.Value = 100;
                }
                else
                {
                    gpdFanMode.SetMode(0); // Auto
                    gpdFanSpeed.SetSpeed(0);
                    if (GPDFanModeToggle != null)
                    {
                        GPDFanModeToggle.Toggled -= GPDFanModeToggle_Toggled;
                        GPDFanModeToggle.IsOn = false;
                        GPDFanModeToggle.Toggled += GPDFanModeToggle_Toggled;
                    }
                }

                Logger.Info($"Fan Full Speed toggled (GPD): {(gpdFanMaxActive ? "On" : "Off")}");
            }
        }

        private async void ToggleScreenSaver()
        {
            screenSaverEnabled = !screenSaverEnabled;

            // Persist setting
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[ScreenSaverEnabledKey] = screenSaverEnabled;

            // Start or stop countdown timer
            if (screenSaverEnabled)
            {
                StartScreenSaverCountdown();
            }
            else
            {
                StopScreenSaverCountdown();
            }

            // Send to helper
            try
            {
                if (App.IsConnected)
                {
                    var request = new Windows.Foundation.Collections.ValueSet
                    {
                        { "Command", (int)Shared.Enums.Command.Set },
                        { "Function", (int)Shared.Enums.Function.ScreenSaverEnabled },
                        { "Content", screenSaverEnabled }
                    };
                    await App.SendMessageAsync(request);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending Screen Saver state: {ex.Message}");
            }

            Logger.Info($"Screen Saver toggled: {(screenSaverEnabled ? "On" : "Off")}");
        }

        private void StartScreenSaverCountdown()
        {
            if (screenSaverCountdownTimer == null)
            {
                screenSaverCountdownTimer = new DispatcherTimer();
                screenSaverCountdownTimer.Interval = TimeSpan.FromSeconds(1);
                screenSaverCountdownTimer.Tick += ScreenSaverCountdownTimer_Tick;
            }
            screenSaverCountdownTimer.Start();
            UpdateScreenSaverTileCountdown();
        }

        private void StopScreenSaverCountdown()
        {
            screenSaverCountdownTimer?.Stop();
            UpdateQuickSettingsTileStates();
        }

        private void ScreenSaverCountdownTimer_Tick(object sender, object e)
        {
            if (!screenSaverEnabled)
            {
                screenSaverCountdownTimer?.Stop();
                return;
            }
            UpdateScreenSaverTileCountdown();
        }

        private void UpdateScreenSaverTileCountdown()
        {
            if (qsTileMap == null || !qsTileMap.TryGetValue("ScreenSaver", out var tile) || tile.StateText == null)
                return;

            try
            {
                var lastInput = new LASTINPUTINFO();
                lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);

                if (GetLastInputInfo(ref lastInput))
                {
                    uint idleMs = (uint)Environment.TickCount - lastInput.dwTime;
                    int remaining = Math.Max(0, ScreenSaverTimeoutSeconds - (int)(idleMs / 1000));
                    tile.StateText.Text = $"{remaining}s";
                }
            }
            catch
            {
                tile.StateText.Text = $"{ScreenSaverTimeoutSeconds}s";
            }
        }

        /// <summary>
        /// Puts the system into hibernation via helper.
        /// </summary>
        private async void ExecuteHibernate()
        {
            Logger.Info("Hibernate action triggered");

            try
            {
                if (App.IsConnected)
                {
                    // Send hibernate request to helper (UWP can't execute shutdown directly)
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("Hibernate", true);
                    await App.SendMessageAsync(message);
                    Logger.Info("Hibernate request sent to helper");
                }
                else
                {
                    Logger.Warn("Cannot hibernate - helper not connected");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to hibernate: {ex.Message}");
            }
        }

    }
}
