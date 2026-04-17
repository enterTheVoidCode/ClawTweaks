using NLog;
using Shared.Data;
using Shared.Enums;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Devices;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.Devices.Libraries.Legion
{
    internal partial class LegionManager
    {
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
                    controllerService.StartBatteryMonitoring();
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
                    controllerService.BatteryUpdated -= OnControllerBatteryUpdated;
                    controllerService.StopBatteryMonitoring();
                }
                catch { }
                Logger.Info("Controller battery monitoring stopped");
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
