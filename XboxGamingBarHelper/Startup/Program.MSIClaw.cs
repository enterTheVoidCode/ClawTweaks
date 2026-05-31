using NLog;
using Shared.Enums;
using System;
using System.Threading.Tasks;
using XboxGamingBarHelper.Devices.MSIClaw;
using XboxGamingBarHelper.MSI;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// MSI Claw controller emulation startup / shutdown logic.
    ///
    /// Uses ClawButtonMonitor (DInput path, like HC's DClawController) as the sole
    /// emulation path for MSI Claw:
    ///   - Switches the physical controller to DInput mode (PID 0x1902)
    ///   - Hides ONLY PID 0x1902 via HidHide (PID 0x1901 keyboard stays visible → Win+G works)
    ///   - Creates a ViGEm virtual Xbox 360 controller
    ///   - Forwards all inputs (buttons, sticks, triggers, d-pad, M1/M2) to ViGEm
    ///
    /// Why DInput instead of XInput:
    ///   In XInput mode (PID 0x1901) the keyboard HID interface shares the same PID.
    ///   Hiding 0x1901 via HidHide also hides the keyboard → Win+G stops working →
    ///   Game Bar becomes uncontrollable. In DInput mode (PID 0x1902) the keyboard
    ///   has its own PID (not 0x1902) and is not affected by HidHide.
    ///
    /// MSIClawViGEmForwarder (XInput path) is kept in the codebase but is no longer
    /// started here — ClawButtonMonitor (DInput path) replaces it entirely.
    ///
    /// Wires ClawButtonMonitor into the helper lifecycle:
    ///   - Suppresses the legacy ControllerEmulationManager
    ///   - Subscribes to the master "Enable Controller Emulation" toggle
    ///   - Starts/stops ClawButtonMonitor dynamically as the toggle changes
    ///
    /// Adapted from HC fork ClawA1M + DClawController.
    /// </summary>
    internal partial class Program
    {
        /// <summary>
        /// True when this startup code owns the active HidHide suppression for MSI Claw.
        /// Tracks ownership so Disable() is only called when Enable() succeeded.
        /// </summary>
        private static bool _msiClawOwnsSuppression = false;

        /// <summary>
        /// Software mouse forwarder for MSI Claw "Mouse mode".
        /// Active when controller emulation is disabled (mode tile shows "Mouse").
        /// Null when not running.
        /// </summary>
        private static MSIClawDesktopModeForwarder _msiClawMouseForwarder;
        private static readonly object _msiClawMouseForwarderLock = new object();

        /// <summary>
        /// Start the MSI Claw DInput-based virtual controller emulation.
        ///
        /// Adapted from HC DClawController + ClawA1M:
        ///  - Switches physical controller to DInput mode (PID 0x1902)
        ///  - Hides only PID 0x1902 via HidHide (keyboard at PID 0x1901 stays visible)
        ///  - Creates a ViGEm virtual Xbox 360 controller
        ///  - Reads HID input reports at DInput interface (UsagePage 0xFFF0)
        ///  - Forwards all buttons, sticks, triggers, d-pad + M1/M2 to ViGEm
        ///
        /// Runs on a background thread (mode switch + HidHide settle ~2 s).
        ///
        /// Respects the master "Enable Controller Emulation" toggle
        /// (ControllerEmulationManager.EmulationEnabled).
        /// Subscribes to EmulationEnabledChanged for dynamic start/stop.
        /// </summary>
        // ── Left CLAW button WMI listener ────────────────────────────────────────
        // The left front button (CLAW logo, OEM1) fires a WMI event MSI_Event with
        // EventCode=41 (LaunchMcxMainUI). Ported from HC ClawA1M.cs specialKeyWatcher.
        // Started once at startup regardless of emulation state.
        private static System.Management.ManagementEventWatcher _clawButtonWatcher;
        private static readonly object _clawButtonWatcherLock = new object();

        private static void StartClawButtonWmiListener()
        {
            var deviceInfo = Devices.DeviceDetector.DetectDevice();
            if (deviceInfo.DeviceType != DeviceType.MSIClaw) return;

            lock (_clawButtonWatcherLock)
            {
                if (_clawButtonWatcher != null) return;
                try
                {
                    var scope = new System.Management.ManagementScope(@"\\.\root\WMI");
                    var query = new System.Management.WqlEventQuery("SELECT * FROM MSI_Event");
                    _clawButtonWatcher = new System.Management.ManagementEventWatcher(scope, query);
                    _clawButtonWatcher.EventArrived += OnClawWmiEvent;
                    _clawButtonWatcher.Start();
                    Logger.Info("MSIClaw: WMI MSI_Event listener started (left CLAW button)");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"MSIClaw: WMI listener failed to start: {ex.Message}");
                }
            }
        }

        private static void StopClawButtonWmiListener()
        {
            lock (_clawButtonWatcherLock)
            {
                try { _clawButtonWatcher?.Stop(); _clawButtonWatcher?.Dispose(); } catch { }
                _clawButtonWatcher = null;
            }
        }

        private static void OnClawWmiEvent(object sender, System.Management.EventArrivedEventArgs e)
        {
            try
            {
                // EventCode field carries the WMI event code (1 byte, value 41 = LaunchMcxMainUI)
                var props = e.NewEvent.Properties;
                int code = 0;
                foreach (System.Management.PropertyData p in props)
                {
                    if (p.Name.Equals("EventCode", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals("WMIEvent", StringComparison.OrdinalIgnoreCase))
                    {
                        code = Convert.ToInt32(p.Value) & 0xFF;
                        break;
                    }
                }
                if (code != 41) return; // 41 = LaunchMcxMainUI = left CLAW button

                Logger.Info("MSIClaw: Left CLAW button pressed (WMI LaunchMcxMainUI)");

                int actionType = legionManager?.DesktopButtonTileAction ?? -1;
                if (actionType < 0)
                {
                    Logger.Debug("MSIClaw: Left CLAW button — no Action mapping configured");
                    return;
                }

                string actionName = $"Action{actionType}";
                try
                {
                    var dispName = XboxGamingBarHelper.TileActionNames.GetDisplayName(actionType);
                    if (!string.IsNullOrEmpty(dispName)) actionName = dispName;
                }
                catch { }

                FireTileHotkeyToWidget($"__action__{actionType}", actionName);
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: OnClawWmiEvent threw: {ex.Message}");
            }
        }

        private static void StartMSIClawControllerEmulation()
        {
            try
            {
                var deviceInfo = Devices.DeviceDetector.DetectDevice();
                if (deviceInfo.DeviceType != DeviceType.MSIClaw)
                {
                    Logger.Debug("MSIClaw: DInput path skipped (not an MSI Claw)");
                    return;
                }

                // ── Step 0: Register MsiClawControllerMode callback ──────────────
                // MsiClawControllerModeManager.OnModeChanged is a static delegate.
                // Registering here (not at field-init time) ensures Program.MSIClaw.cs
                // is the authoritative owner; any previous value is safely overwritten.
                MsiClawControllerModeManager.OnModeChanged = OnMsiClawControllerModeChanged;

                // ── Step 1: Suppress legacy ControllerEmulationManager ─────────────
                // Reuse the same SetSuppressedByViiper() flag that VIIPER uses — identical
                // intent: tell the legacy backend that a higher-priority backend owns the
                // virtual controller for this session.
                if (controllerEmulationManager != null)
                {
                    controllerEmulationManager.SetSuppressedByViiper(true);
                    Logger.Info("MSIClaw: Legacy ControllerEmulationManager suppressed (ClawButtonMonitor DInput path takes over)");
                }

                // ── Step 1.5: Wire XInput button remap callback ───────────────────────
                // LegionManager.SetButtonMappingAdvanced() will invoke this callback instead
                // of connecting to Legion hardware (which doesn't exist on MSI Claw).
                // Pre-wired here so any stored button mapping loaded later (e.g. from profile
                // restore) is immediately routed to ClawButtonMonitor.ConfigureXInputRemap().
                WireClawXInputRemapCallback();

                // ── Step 2: Subscribe to master "Enable Controller Emulation" toggle ─
                // Do NOT unsubscribe inside the handler — if we did, toggling off would
                // remove the listener and re-enabling would silently do nothing.
                // Subscription lifetime = helper lifetime; only removed in the final
                // DisposeMSIClawControllerEmulation() called during shutdown.
                if (controllerEmulationManager != null)
                {
                    controllerEmulationManager.EmulationEnabledChanged += OnMSIClawEmulationEnabledChanged;
                }

                // ── Step 3: Guard on master "Enable Controller Emulation" toggle ──
                // The widget sends the persisted value on connect. Until the widget
                // connects, EmulationEnabled defaults to false — meaning emulation must
                // NOT start automatically. OnMSIClawEmulationEnabledChanged is already
                // subscribed (Step 2) and will start the correct path when the toggle
                // value arrives from the widget.
                bool emulationEnabled = controllerEmulationManager?.EmulationEnabled ?? false;
                if (!emulationEnabled)
                {
                    Logger.Info("MSIClaw: ControllerEmulationEnabled=false at startup — waiting for widget to send toggle state");
                    return;
                }

                // ── Step 4: Honour the current mode state ─────────────────────────
                // Use MsiClawControllerMode (default true = Controller).
                bool controllerModeOn = msiClawControllerModeManager?.MsiClawControllerMode?.Value ?? true;
                if (!controllerModeOn)
                {
                    Logger.Info("MSIClaw: MsiClawControllerMode=Mouse at startup — starting mouse forwarder");
                    StartMSIClawMouseForwarderBackground();
                    return;
                }

                StartClawButtonMonitorBackground();
            }
            catch (Exception ex)
            {
                Logger.Error($"MSIClaw: Failed to start ClawButtonMonitor: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when the master "Enable Controller Emulation" toggle changes.
        /// Mirrors ViiperEmulationManager.OnEmulationEnabledChanged.
        ///
        /// Toggle semantics for MSI Claw mode tile:
        ///   emulationEnabled = true  → "Controller" mode: ClawButtonMonitor running (ViGEm Xbox 360)
        ///   emulationEnabled = false → "Mouse" mode: ClawButtonMonitor stopped, MSIClawDesktopModeForwarder active
        ///
        /// NOTE: Does NOT remove the event subscription — the subscription must remain
        /// alive so subsequent toggles (off → on → off) all work correctly.
        /// </summary>
        private static void OnMSIClawEmulationEnabledChanged(bool emulationEnabled)
        {
            try
            {
                if (emulationEnabled)
                {
                    // Switching to Controller mode:
                    //   1. Stop mouse forwarder (switches controller back to XInput transiently;
                    //      ClawButtonMonitor.Start() will switch to DInput immediately after)
                    //   2. Start ClawButtonMonitor (DInput path, ViGEm virtual Xbox 360)
                    Logger.Info("MSIClaw: EmulationEnabled → true (Controller mode) — stopping mouse forwarder, starting ClawButtonMonitor");
                    StopMSIClawMouseForwarder();

                    lock (clawButtonMonitorLock)
                    {
                        if (clawButtonMonitor == null || !clawButtonMonitor.IsRunning)
                            StartClawButtonMonitorBackground();
                    }
                }
                else
                {
                    // Switching to Mouse mode:
                    //   1. Stop ClawButtonMonitor (unhides physical DInput controller via ForceUnhideAll)
                    //   2. Start mouse forwarder (switches to XInput mode, then polls XInput)
                    Logger.Info("MSIClaw: EmulationEnabled → false (Mouse mode) — stopping ClawButtonMonitor, starting mouse forwarder");
                    StopMSIClawButtonMonitor();
                    StartMSIClawMouseForwarderBackground();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: OnMSIClawEmulationEnabledChanged threw: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when the MsiClawControllerMode Quick Settings tile is tapped.
        /// Routes to OnMSIClawEmulationEnabledChanged() which handles the full
        /// ClawButtonMonitor ↔ MSIClawDesktopModeForwarder lifecycle.
        ///
        /// true  = Controller mode (ClawButtonMonitor + ViGEm)
        /// false = Mouse mode (MSIClawDesktopModeForwarder)
        /// </summary>
        private static void OnMsiClawControllerModeChanged(bool controllerOn)
        {
            Logger.Info($"MSIClaw: MsiClawControllerMode changed → {(controllerOn ? "Controller" : "Mouse")}");
            OnMSIClawEmulationEnabledChanged(controllerOn);
        }

        /// <summary>
        /// Starts MSIClawDesktopModeForwarder on a background thread.
        /// The mode switch + settle (~600 ms) must not block the toggle handler.
        /// </summary>
        private static void StartMSIClawMouseForwarderBackground()
        {
            Task.Run(() =>
            {
                try
                {
                    lock (_msiClawMouseForwarderLock)
                    {
                        if (_msiClawMouseForwarder == null)
                            _msiClawMouseForwarder = new MSIClawDesktopModeForwarder();

                        if (!_msiClawMouseForwarder.IsRunning)
                        {
                            bool ok = _msiClawMouseForwarder.Start();
                            if (!ok)
                                Logger.Warn("MSIClaw: MSIClawDesktopModeForwarder Start() failed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"MSIClaw: StartMSIClawMouseForwarderBackground threw: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Stops MSIClawDesktopModeForwarder synchronously.
        /// Releases any held mouse buttons before stopping.
        /// </summary>
        private static void StopMSIClawMouseForwarder()
        {
            try
            {
                lock (_msiClawMouseForwarderLock)
                {
                    _msiClawMouseForwarder?.Stop();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: StopMSIClawMouseForwarder threw: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops ClawButtonMonitor and performs a full HidHide reset.
        ///
        /// Uses ForceUnhideAll() instead of Disable() to guarantee physical controllers
        /// reappear even if the in-memory hiddenDeviceIds tracking was lost (e.g. helper
        /// restart, failed Enable(), or crash recovery):
        ///   1. Stop ClawButtonMonitor (disposes ViGEm virtual controller)
        ///   2. Read ALL blocked instance IDs live from the HidHide driver and remove them
        ///   3. Disable HidHide cloaking (IsActive = false)
        ///   4. Cycle USB ports for ALL MSI Claw devices (VID_0DB0 PID_1901 + PID_1902)
        ///      so they re-enumerate and reappear in Joy.cpl / Steam / games.
        ///
        /// Does NOT null out clawButtonMonitor — instance stays alive for re-enable.
        /// </summary>
        private static void StopMSIClawButtonMonitor()
        {
            // Stop ViGEm virtual controller
            try
            {
                lock (clawButtonMonitorLock)
                {
                    clawButtonMonitor?.Stop();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: Error stopping ClawButtonMonitor: {ex.Message}");
            }

            // Full HidHide reset — ForceUnhideAll() bypasses internal tracking and
            // reads the live blocked list directly from the driver. No ownership guard
            // (_msiClawOwnsSuppression) needed: we always want to clean up HidHide
            // on stop, regardless of whether Enable() completed successfully.
            if (controllerEmulationManager?.SuppressionManager != null)
            {
                try
                {
                    controllerEmulationManager.SuppressionManager.ForceUnhideAll();
                    Logger.Info("MSIClaw: ForceUnhideAll complete — physical MSI Claw controllers restored");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"MSIClaw: ForceUnhideAll threw: {ex.Message}");
                }
            }

            _msiClawOwnsSuppression = false;
        }

        /// <summary>
        /// Spins up ClawButtonMonitor (DInput path) on a background thread.
        ///
        /// Ordering matches HC DClawController:
        ///   1. Enable HidHide for PID 0x1902 only (keyboard at 0x1901 stays visible)
        ///   2. Start ClawButtonMonitor — switches to DInput mode, creates ViGEm, begins polling
        ///
        /// Must not block the caller — DInput mode switch + HidHide settle takes ~2 s.
        /// </summary>
        private static void StartClawButtonMonitorBackground()
        {
            Task.Run(() =>
            {
                try
                {
                    // ── Step 0: MSI Center guard ──────────────────────────────────
                    // Controller emulation requires MSI Center M to be fully stopped.
                    // MSI software re-registers controllers and fights HidHide suppression.
                    if (msiCenterManager?.DetectActive() == true)
                    {
                        Logger.Warn("[MSIClaw] MSI Center M is active — controller emulation blocked. " +
                                    "Stop MSI Center M via the Quick Tab tile first.");
                        return; // Hard block: do not start ClawButtonMonitor / ViGEm
                    }

                    // ── Step 0.5: Proactive XInput mode switch ────────────────────
                    // Mirrors HC ClawA1M.Open() → SwitchMode(gamepadMode):
                    // If the controller is in hardware Desktop mode (e.g. left by a previous
                    // HC session or after ClawTweaks shutdown), the user would have no gamepad
                    // input during the ~2.5 s DInput settle in ClawButtonMonitor.Start().
                    // Switching to XInput first gives immediate gamepad behaviour.
                    // No settle needed here — ClawButtonMonitor.Start() includes its own
                    // 2.5 s settle after the XInput → DInput transition.
                    MSIClawHidController.TrySwitchToXInput();

                    // ── Step 1: Start ClawButtonMonitor ───────────────────────────
                    // MUST happen before HidHide Enable.
                    // Start() is synchronous for the DInput mode switch: OpenClawInterfaces()
                    // sends SwitchMode(DInput) and waits ~2.5 s for PID_1902 to enumerate
                    // before returning. HidHide Enable must query PID_1902 — if called before
                    // Start() the device is still in XInput mode (PID_1901) and Enable() finds
                    // zero matching device IDs → suppression silently fails.
                    var monitor = EnsureClawButtonMonitor();

                    // Apply current gyro settings from profile BEFORE Start().
                    // LegionControllerSetting_PropertyChanged null-guards on clawButtonMonitor,
                    // so any gyro settings applied during profile load (when the monitor was null)
                    // were silently dropped. New instances default to _gyroActivationButton=0
                    // (always active) which caused the "always-on regardless of LT" bug.
                    // This block is the 1:1 equivalent of RestoreGlobalProfileSettings() for gyro,
                    // using the same properties that LegionControllerSetting_PropertyChanged routes.
                    if (legionManager != null)
                    {
                        monitor.SetGyroTarget(legionManager.LegionGyroTarget.Value);
                        monitor.SetGyroActivationButton(legionManager.LegionGyroActivationButton.Value);
                        monitor.SetGyroActivationMode(legionManager.LegionGyroActivationMode.Value);
                        monitor.SetGyroSensitivityX(legionManager.LegionGyroSensitivityX.Value);
                        monitor.SetGyroSensitivityY(legionManager.LegionGyroSensitivityY.Value);
                        monitor.SetGyroDeadzone(legionManager.LegionGyroDeadzone.Value);
                        monitor.SetGyroInvertX(legionManager.LegionGyroInvertX.Value);
                        monitor.SetGyroInvertY(legionManager.LegionGyroInvertY.Value);
                        Logger.Info($"MSIClaw: Gyro settings applied before Start() — target={legionManager.LegionGyroTarget.Value}, activationButton={legionManager.LegionGyroActivationButton.Value}, activationMode={legionManager.LegionGyroActivationMode.Value}");
                    }

                    bool ok = monitor.Start();

                    if (!ok)
                    {
                        Logger.Warn("MSIClaw: ClawButtonMonitor Start() failed — ViGEmBus not installed or Claw DInput interface unavailable");
                        return;
                    }

                    Logger.Info("MSIClaw: ClawButtonMonitor running (DInput path)");

                    // ── Step 2: HidHide — hide physical DInput controller ──────────
                    // PID_1902 is now enumerated (Start() completed the DInput mode switch).
                    // Hides PID 0x1902 only (configured in ControllerSuppressionManager).
                    // PID 0x1901 (command interface + keyboard HID) is NOT hidden so
                    // Win+G keeps working and Game Bar remains fully controllable.
                    var suppression = controllerEmulationManager?.SuppressionManager;
                    if (suppression != null)
                    {
                        try
                        {
                            _msiClawOwnsSuppression = suppression.Enable(
                                DeviceType.MSIClaw,
                                controllerEmulationManager?.HideTarget ?? 0);
                            Logger.Info($"[MSIClaw] HidHide suppression => {_msiClawOwnsSuppression}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"[MSIClaw] HidHide Enable threw: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"MSIClaw: ClawButtonMonitor startup threw: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Final shutdown: unsubscribes the emulation toggle event, stops ClawButtonMonitor,
        /// and disposes the mouse forwarder.
        /// Called ONLY during helper shutdown — NOT from the toggle handler.
        /// Full disposal of the ClawButtonMonitor instance is handled by DisposeClawButtonMonitor()
        /// in Program.Labs.cs which also nulls out the field.
        /// </summary>
        private static void DisposeMSIClawControllerEmulation()
        {
            // Remove event subscription (shutdown only — not called from toggle handler)
            try
            {
                if (controllerEmulationManager != null)
                    controllerEmulationManager.EmulationEnabledChanged -= OnMSIClawEmulationEnabledChanged;
            }
            catch { }

            StopMSIClawButtonMonitor();

            // Dispose mouse forwarder (releases stuck mouse buttons if active)
            try
            {
                lock (_msiClawMouseForwarderLock)
                {
                    _msiClawMouseForwarder?.Dispose();
                    _msiClawMouseForwarder = null;
                }
            }
            catch { }

            // Switch controller back to Desktop mode on shutdown —
            // mirrors HC ClawA1M.Close() → SwitchMode(GamepadMode.Desktop).
            // Ensures the controller is in a known, clean hardware state when
            // no companion app is running (MSI firmware handles basic cursor movement).
            try { MSIClawHidController.TrySwitchToDesktop(); } catch { }
        }

        /// <summary>
        /// Wire LegionManager.OnButtonMappingChanged to route M1/M2 gamepad button mappings
        /// (LS Click, A, B, etc.) to ClawButtonMonitor software remapping.
        ///
        /// Called once during StartMSIClawControllerEmulation(). The callback is a simple
        /// field assignment, not a C# event — no unsubscription needed on shutdown.
        ///
        /// Pre-wired before ClawButtonMonitor.Start() so any stored mapping loaded from
        /// profile restore (RestoreGlobalProfileSettings()) is applied immediately via
        /// LegionButtonM1Property.NotifyPropertyChanged() → SetButtonMappingAdvanced() →
        /// MSI Claw branch → OnButtonMappingChanged → ConfigureXInputRemap().
        /// </summary>
        /// <summary>
        /// Called when MSI Center M active state changes (poll-detected or user-toggled).
        ///
        /// active = true  → MSI Center M just started  → stop ClawButtonMonitor + mouse forwarder
        /// active = false → MSI Center M just stopped  → restart the appropriate emulation mode
        ///
        /// Wired in Program.cs after msiCenterManager is created.
        /// </summary>
        internal static void OnMsiCenterStateChanged(bool active)
        {
            var deviceInfo = Devices.DeviceDetector.DetectDevice();
            if (deviceInfo.DeviceType != DeviceType.MSIClaw) return;

            if (active)
            {
                // MSI Center M became active → shut down our controller emulation immediately
                Logger.Info("[Program.MSIClaw] MSI Center M active → stopping ClawButtonMonitor and mouse forwarder");
                StopMSIClawButtonMonitor();
                StopMSIClawMouseForwarder();
            }
            else
            {
                // MSI Center M deactivated → restart controller emulation if the master toggle is on
                Logger.Info("[Program.MSIClaw] MSI Center M deactivated → restarting controller emulation");
                bool controllerModeOn = msiClawControllerModeManager?.MsiClawControllerMode?.Value ?? true;

                if (controllerModeOn)
                {
                    bool emulationEnabled = controllerEmulationManager?.EmulationEnabled ?? true;
                    if (emulationEnabled)
                        StartClawButtonMonitorBackground();
                }
                else
                {
                    StartMSIClawMouseForwarderBackground();
                }
            }
        }

        private static void WireClawXInputRemapCallback()
        {
            if (legionManager == null) return;
            legionManager.OnButtonMappingChanged = OnMSIClawButtonMappingChanged;
            Logger.Info("MSIClaw: LegionManager.OnButtonMappingChanged wired → ClawButtonMonitor.ConfigureXInputRemap");
        }

        /// <summary>
        /// Invoked when the user sets a button mapping in the Legion tab (M1/M2 → gamepad button).
        /// Routes the mapping to ClawButtonMonitor.ConfigureXInputRemap() for software remapping.
        ///
        /// Mirrors HC LayoutManager.MapController() path for OEM3/OEM4 buttons:
        ///   buttonIndex 3 = M1 (OEM3), buttonIndex 4 = M2 (OEM4).
        ///   mappingType 0 = Gamepad → values[0] is the RemapAction index.
        ///   mappingType != 0 = Keyboard/Mouse → no XInput remap; clears any prior action.
        ///
        /// EnsureClawButtonMonitor() pre-creates the monitor if not yet running so the remap
        /// is ready when Start() is called from StartClawButtonMonitorBackground().
        /// </summary>
        private static void OnMSIClawButtonMappingChanged(int buttonIndex, int mappingType, int[] values)
        {
            string button = buttonIndex == 3 ? "M1" : buttonIndex == 4 ? "M2" : null;
            if (button == null) return; // M3 and Y1/Y2/Y3 not applicable on MSI Claw

            // Only Gamepad (type=0) actions map to XInput; Keyboard/Mouse → clear XInput remap.
            int actionIndex = (mappingType == 0 && values?.Length > 0) ? values[0] : 0;

            try
            {
                // EnsureClawButtonMonitor() creates the instance if not yet present.
                // ConfigureXInputRemap() hot-applies if the monitor is running, or
                // pre-configures the instance so the remap is ready when Start() is called.
                EnsureClawButtonMonitor().ConfigureXInputRemap(button, actionIndex);
                Logger.Info($"MSIClaw: {button} XInput remap applied — actionIndex={actionIndex}, mappingType={mappingType}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: OnMSIClawButtonMappingChanged threw: {ex.Message}");
            }
        }
    }
}
