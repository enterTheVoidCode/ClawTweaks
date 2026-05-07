using NLog;
using Shared.Data;
using Shared.Enums;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Devices;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.Devices.Libraries.Legion
{
    internal partial class LegionManager
    {
        // Periodic poll for b0:01 device status. Legion Space polls on demand when its
        // UI is open; we just refresh every few seconds so the Info card stays current
        // without burning HID bandwidth.
        private Timer _deviceStatusPollTimer;
        private const int DeviceStatusPollIntervalMs = 5000;

        /// <summary>
        /// Starts battery monitoring for the controllers using the existing controllerService connection.
        /// </summary>
        private void StartBatteryMonitoring()
        {
            try
            {
                if (controllerService != null && isControllerConnected)
                {
                    controllerService.BatteryUpdated += OnControllerBatteryUpdated;
                    controllerService.DeviceStatusUpdated += OnControllerDeviceStatusUpdated;
                    controllerService.StickLightDriftDetected += OnStickLightDriftDetected;
                    controllerService.StartBatteryMonitoring();
                    StartDeviceStatusPolling();
                    Logger.Info("Controller battery monitoring started");
                }
                else
                {
                    Logger.Info("Controller not connected, battery monitoring not started");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error starting battery monitoring: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops battery monitoring.
        /// </summary>
        private void StopBatteryMonitoring()
        {
            if (controllerService != null)
            {
                try
                {
                    StopDeviceStatusPolling();
                    controllerService.BatteryUpdated -= OnControllerBatteryUpdated;
                    controllerService.DeviceStatusUpdated -= OnControllerDeviceStatusUpdated;
                    controllerService.StickLightDriftDetected -= OnStickLightDriftDetected;
                    controllerService.StopBatteryMonitoring();
                }
                catch { }
                Logger.Info("Controller battery monitoring stopped");
            }
        }

        private void StartDeviceStatusPolling()
        {
            try
            {
                _deviceStatusPollTimer?.Dispose();
                _deviceStatusPollTimer = new Timer(_ => PollDeviceStatus(), null, 250, DeviceStatusPollIntervalMs);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to start device status polling: {ex.Message}");
            }
        }

        private void StopDeviceStatusPolling()
        {
            try
            {
                _deviceStatusPollTimer?.Dispose();
                _deviceStatusPollTimer = null;
            }
            catch { }
        }

        private void PollDeviceStatus()
        {
            try
            {
                if (controllerService == null || !isControllerConnected) return;
                var result = controllerService.ReadDeviceStatus();
                if (result == null)
                {
                    Logger.Info("PollDeviceStatus: no b0:01 response (timeout or monitor inactive)");
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"PollDeviceStatus error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handler for stick-light drift events. Logs WARN on a new mismatch and INFO
        /// on recovery. Source distinguishes the debounced post-write verifier from
        /// the passive 5s poll re-check.
        /// </summary>
        private void OnStickLightDriftDetected(object sender, StickLightDriftEventArgs e)
        {
            try
            {
                if (e.IsMismatch)
                    Logger.Warn($"Stick light drift ({e.Source}): {e.Description}");
                else
                    Logger.Info($"Stick light matches expectation ({e.Source})");
            }
            catch { }
        }

        /// <summary>
        /// Handler for b0:01 device status responses. Serializes the snapshot to JSON
        /// and ships it to the widget via the ControllerDeviceStatus property.
        /// </summary>
        private void OnControllerDeviceStatusUpdated(object sender, LegionGoStatus status)
        {
            if (status == null) return;
            try
            {
                // Diagnostic — log every readback so the values can be compared
                // to what the widget last sent. Demote to Debug once stable.
                Logger.Info(
                    $"b0:01 readback: fw={status.FirmwareVersion} lightEnabled={status.LightEnabled} " +
                    $"mode={status.LightModeRaw} R={status.Red} G={status.Green} B={status.Blue} " +
                    $"brightness={status.Brightness} speed={status.Speed} " +
                    $"vibration={status.VibrationRaw} touchpad={status.TouchpadEnabled} " +
                    $"battery L={status.LeftBattery} R={status.RightBattery}");

                string json = JsonSerializer.Serialize(new
                {
                    fw = status.FirmwareVersion,
                    le = status.LightEnabled,
                    lm = status.LightModeRaw,
                    r = status.Red,
                    g = status.Green,
                    b = status.Blue,
                    br = status.Brightness,
                    sp = status.Speed,
                    vb = status.VibrationRaw,
                    tp = status.TouchpadEnabled,
                    bl = status.LeftBattery,
                    brt = status.RightBattery,
                });
                ControllerDeviceStatus.SetValueAndSync(json);

                // Reconcile the LegionTouchpadEnabled property with the hardware
                // state reported by the readback. Touchpad state is firmware-side
                // persistent — it survives helper restarts — so when the property
                // disagrees with hardware (typically after a fresh helper boot
                // before LocalSettings has caught up, or when LegionSpace / OS
                // gestures toggle it behind our back), trust the hardware. Use
                // SuppressHardwareApply so the resulting widget pipe sync doesn't
                // round-trip back to a redundant SetTouchpadEnabled call.
                if (LegionTouchpadEnabled != null && LegionTouchpadEnabled.Value != status.TouchpadEnabled)
                {
                    Logger.Info($"LegionTouchpadEnabled hardware reconcile: property={LegionTouchpadEnabled.Value} → readback={status.TouchpadEnabled}");
                    touchpadEnabled = status.TouchpadEnabled;
                    try { Settings.LocalSettingsHelper.SetValue("LegionTouchpadEnabled", status.TouchpadEnabled); }
                    catch (Exception persistEx) { Logger.Debug($"Failed to persist LegionTouchpadEnabled during reconcile: {persistEx.Message}"); }
                    LegionTouchpadEnabled.SuppressHardwareApply = true;
                    try { LegionTouchpadEnabled.SetValueAndSync(status.TouchpadEnabled); }
                    finally { LegionTouchpadEnabled.SuppressHardwareApply = false; }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to sync device status to widget: {ex.Message}");
            }
        }

        /// <summary>
        /// Public method to start battery monitoring if controller is connected.
        /// Called from Program.cs after widget connection is established.
        /// </summary>
        public void StartBatteryMonitoringIfConnected()
        {
            if (isControllerConnected)
            {
                StartBatteryMonitoring();
            }
        }

        /// <summary>
        /// Handler for battery updates from the controller.
        /// </summary>
        private void OnControllerBatteryUpdated(object sender, ControllerServiceBatteryEventArgs e)
        {
            Logger.Debug($"Battery update received: L={e.LeftBattery}% ({(e.LeftCharging ? "charging" : "discharging")}), R={e.RightBattery}% ({(e.RightCharging ? "charging" : "discharging")})");

            // Update cached values
            leftControllerBattery = e.LeftBattery;
            rightControllerBattery = e.RightBattery;
            leftControllerCharging = e.LeftCharging;
            rightControllerCharging = e.RightCharging;

            // Update properties and sync to widget
            // Wrap in try/catch because SyncToRemote is async void and can crash the process
            // if the AppService connection is broken (e.g., widget closed or stale connection)
            try
            {
                ControllerBatteryLeft.SetValueAndSync(e.LeftBattery);
                ControllerBatteryRight.SetValueAndSync(e.RightBattery);
                ControllerChargingLeft.SetValueAndSync(e.LeftCharging);
                ControllerChargingRight.SetValueAndSync(e.RightCharging);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to sync battery status to widget: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the left controller battery percentage (1-100), or -1 if unavailable.
        /// </summary>
        public int GetLeftControllerBattery() => leftControllerBattery;

        /// <summary>
        /// Gets the right controller battery percentage (1-100), or -1 if unavailable.
        /// </summary>
        public int GetRightControllerBattery() => rightControllerBattery;

        /// <summary>
        /// Gets whether the left controller is charging.
        /// </summary>
        public bool IsLeftControllerCharging() => leftControllerCharging;

        /// <summary>
        /// Gets whether the right controller is charging.
        /// </summary>
        public bool IsRightControllerCharging() => rightControllerCharging;

        /// <summary>
        /// Updates controller battery values from the LegionButtonMonitor.
        /// This is used when the button monitor is active and reading HID reports
        /// that contain battery data (same interface as button data).
        /// </summary>
        public void UpdateControllerBatteryFromButtonMonitor(int leftBattery, bool leftCharging, bool leftConnected,
                                                              int rightBattery, bool rightCharging, bool rightConnected)
        {
            try
            {
                bool batteryChanged = leftControllerBattery != leftBattery ||
                                     rightControllerBattery != rightBattery ||
                                     leftControllerCharging != leftCharging ||
                                     rightControllerCharging != rightCharging;

                bool connectionChanged = leftControllerConnected != leftConnected ||
                                        rightControllerConnected != rightConnected;

                leftControllerBattery = leftBattery;
                leftControllerCharging = leftCharging;
                leftControllerConnected = leftConnected;
                rightControllerBattery = rightBattery;
                rightControllerCharging = rightCharging;
                rightControllerConnected = rightConnected;

                if (batteryChanged)
                {
                    Logger.Info($"Controller battery from button monitor: L={leftBattery}% R={rightBattery}%");
                    try
                    {
                        ControllerBatteryLeft.SetValueAndSync(leftBattery);
                        ControllerBatteryRight.SetValueAndSync(rightBattery);
                        ControllerChargingLeft.SetValueAndSync(leftCharging);
                        ControllerChargingRight.SetValueAndSync(rightCharging);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to sync battery status from button monitor: {ex.Message}");
                    }
                }

                if (connectionChanged)
                {
                    Logger.Info($"Controller connection changed: L={leftConnected} R={rightConnected}");
                    try
                    {
                        ControllerConnectedLeft.SetValueAndSync(leftConnected);
                        ControllerConnectedRight.SetValueAndSync(rightConnected);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to sync connection status from button monitor: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"UpdateControllerBatteryFromButtonMonitor exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Updates the controller VID:PID from LegionButtonMonitor.
        /// Called from Program.cs when the button monitor successfully connects.
        /// </summary>
        public void UpdateControllerVidPid(string vidPid)
        {
            try
            {
                if (!string.IsNullOrEmpty(vidPid))
                {
                    // Always update the internal value
                    bool wasEmpty = string.IsNullOrEmpty(ControllerVidPid.Value);
                    bool changed = vidPid != ControllerVidPid.Value;

                    if (changed || wasEmpty)
                    {
                        Logger.Info($"Controller VID:PID set to {vidPid}");
                    }

                    // Always try to sync (the property will handle deduplication)
                    ControllerVidPid.SetValueAndSync(vidPid);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"UpdateControllerVidPid exception: {ex.Message}");
            }
        }

    }
}
