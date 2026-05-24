using NLog;
using Shared.Enums;
using System;
using System.Threading.Tasks;

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

                // ── Step 3: Honour the current enabled state ──────────────────────
                bool emulationEnabled = controllerEmulationManager?.EmulationEnabled ?? true;
                if (!emulationEnabled)
                {
                    Logger.Info("MSIClaw: ClawButtonMonitor deferred — controller emulation is currently disabled");
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
        /// NOTE: Does NOT remove the event subscription — the subscription must remain
        /// alive so subsequent toggles (off → on → off) all work correctly.
        /// </summary>
        private static void OnMSIClawEmulationEnabledChanged(bool emulationEnabled)
        {
            try
            {
                if (emulationEnabled)
                {
                    lock (clawButtonMonitorLock)
                    {
                        if (clawButtonMonitor == null || !clawButtonMonitor.IsRunning)
                        {
                            Logger.Info("MSIClaw: EmulationEnabled → true — starting ClawButtonMonitor");
                            StartClawButtonMonitorBackground();
                        }
                    }
                }
                else
                {
                    Logger.Info("MSIClaw: EmulationEnabled → false — stopping ClawButtonMonitor");
                    // Stop-only: keep the event subscription alive for the next re-enable.
                    StopMSIClawButtonMonitor();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"MSIClaw: OnMSIClawEmulationEnabledChanged threw: {ex.Message}");
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
                    // Controller emulation requires MSI Center M to be stopped first.
                    // If MSI processes/service are still active, HidHide suppression
                    // can be undermined (MSI software re-shows or re-registers controllers).
                    if (msiCenterManager?.DetectActive() == true)
                    {
                        Logger.Warn("[MSIClaw] MSI Center M is still active (processes, service, or tasks running). " +
                                    "Controller emulation may be unreliable. " +
                                    "Use the MSI Center M toggle in ClawTweaks to stop it first.");
                        // Do not hard-block — user may have partially disabled it.
                        // The warning is surfaced in logs and the MSI tile reflects the state.
                    }

                    // ── Step 1: Start ClawButtonMonitor ───────────────────────────
                    // MUST happen before HidHide Enable.
                    // Start() is synchronous for the DInput mode switch: OpenClawInterfaces()
                    // sends SwitchMode(DInput) and waits ~2.5 s for PID_1902 to enumerate
                    // before returning. HidHide Enable must query PID_1902 — if called before
                    // Start() the device is still in XInput mode (PID_1901) and Enable() finds
                    // zero matching device IDs → suppression silently fails.
                    var monitor = EnsureClawButtonMonitor();
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
        /// Final shutdown: unsubscribes the emulation toggle event and stops ClawButtonMonitor.
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
