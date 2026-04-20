using System;
using NLog;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.Settings;

namespace XboxGamingBarHelper.ControllerEmulation.Viiper
{
    /// <summary>
    /// Owns the VIIPER runtime. Enabled/disabled in response to the
    /// <see cref="EmulationBackendProperty"/> toggle. Currently only brings up the
    /// USBIP server and a single virtual bus — device creation and input forwarding
    /// land in subsequent phases.
    /// </summary>
    internal sealed class ViiperEmulationManager : Manager
    {
        private new static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const uint DefaultBusId = 1;
        private const string DefaultDeviceType = "xbox360";

        private readonly SettingsManager settingsManager;
        private readonly ControllerEmulationManager legacyManager;
        private readonly ViiperService service = new ViiperService();
        private readonly ViiperInputForwarder forwarder;

        private bool isRunning;
        private uint activeBusId;
        private uint activeDeviceId;
        private string activeDeviceType = DefaultDeviceType;
        private bool viiperOwnsSuppression;

        public ViiperEmulationManager(SettingsManager inSettingsManager, ControllerEmulationManager inLegacyManager, LegionManager inLegionManager)
        {
            settingsManager = inSettingsManager;
            legacyManager = inLegacyManager;
            forwarder = new ViiperInputForwarder(service, inLegionManager);
            if (legacyManager != null)
            {
                // VIIPER respects the same "Enable Controller Emulation" toggle the legacy
                // backend observes — it is the master on/off switch for whichever backend
                // is selected. Flipping the Debug-panel backend selector alone should not
                // auto-start emulation.
                legacyManager.EmulationEnabledChanged += OnEmulationEnabledChanged;
            }
            if (settingsManager != null)
            {
                if (settingsManager.EmulationBackend != null)
                {
                    settingsManager.EmulationBackend.PropertyChanged += OnBackendChanged;
                }
                if (settingsManager.ViiperDeviceType != null)
                {
                    settingsManager.ViiperDeviceType.PropertyChanged += OnDeviceConfigChanged;
                }
                if (settingsManager.ViiperSteamSubDevice != null)
                {
                    settingsManager.ViiperSteamSubDevice.PropertyChanged += OnDeviceConfigChanged;
                }
                if (settingsManager.ViiperInputSource != null)
                {
                    settingsManager.ViiperInputSource.PropertyChanged += OnInputSourceChanged;
                }
                if (settingsManager.ViiperGyroSource != null)
                {
                    settingsManager.ViiperGyroSource.PropertyChanged += OnGyroSourceChanged;
                }
                if (settingsManager.ViiperGuideButtonMode != null)
                {
                    settingsManager.ViiperGuideButtonMode.PropertyChanged += OnGuideModeChanged;
                }
                if (settingsManager.ViiperSwapRumbleMotors != null)
                {
                    settingsManager.ViiperSwapRumbleMotors.PropertyChanged += OnSwapRumbleMotorsChanged;
                }
                if (settingsManager.ViiperRumbleIntensity != null)
                {
                    settingsManager.ViiperRumbleIntensity.PropertyChanged += OnRumbleIntensityChanged;
                }
                if (settingsManager.ViiperGyroAxisMapX != null)
                {
                    settingsManager.ViiperGyroAxisMapX.PropertyChanged += OnGyroAxisMapChanged;
                }
                if (settingsManager.ViiperGyroAxisMapY != null)
                {
                    settingsManager.ViiperGyroAxisMapY.PropertyChanged += OnGyroAxisMapChanged;
                }
                if (settingsManager.ViiperGyroAxisMapZ != null)
                {
                    settingsManager.ViiperGyroAxisMapZ.PropertyChanged += OnGyroAxisMapChanged;
                }
                // Apply initial state — deferred.
                //
                // ApplyBackend(true) walks through Start() which enables
                // HidHide suppression (Nefarius SetVariable + PnP cycle-port)
                // and creates the VIIPER USBIP bus / waits for Windows to
                // enumerate the virtual device. On Legion Go 2 that chain
                // costs ~6s of wall-clock time and used to block helper
                // Initialize() — keeping _managersReady=false, which makes
                // the widget show "Not Connected" spinners for ~12 s on
                // launch. Nothing downstream on the main init path needs
                // VIIPER running (no property reads from it), so the
                // startup apply runs on the thread pool and the bus stands
                // itself up in the background.
                if (settingsManager.EmulationBackend != null)
                {
                    var backendValue = settingsManager.EmulationBackend.Value;
                    _ = System.Threading.Tasks.Task.Run(() =>
                    {
                        try { ApplyBackend(backendValue); }
                        catch (Exception ex) { Logger.Warn($"ViiperEmulationManager deferred startup ApplyBackend failed: {ex.Message}"); }
                    });
                }
            }
        }

        /// <summary>True once VIIPER is initialized and a bus is attached.</summary>
        public bool IsRunning { get { return isRunning; } }

        /// <summary>Backing service — exposed for future device/feedback wiring.</summary>
        public ViiperService Service { get { return service; } }

        private void OnBackendChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (settingsManager?.EmulationBackend == null) return;
            ApplyBackend(settingsManager.EmulationBackend.Value);
        }

        private void ApplyBackend(bool useViiper)
        {
            // Mutual exclusion: legacy ControllerEmulationManager is suppressed whenever
            // VIIPER is the active backend. Clearing the suppression re-applies the user's
            // saved legacy configuration.
            try { legacyManager?.SetSuppressedByViiper(useViiper); }
            catch (Exception ex) { Logger.Warn($"legacyManager.SetSuppressedByViiper threw: {ex.Message}"); }

            // VIIPER only runs when BOTH the backend selector is on AND the master
            // "Enable Controller Emulation" switch is on.
            bool emulationEnabled = legacyManager?.EmulationEnabled ?? true;
            if (useViiper && emulationEnabled)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        private void OnEmulationEnabledChanged(bool emulationEnabled)
        {
            bool backendOn = settingsManager?.EmulationBackend?.Value ?? false;
            if (!backendOn) return; // legacy path handles its own state
            if (emulationEnabled)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        /// <summary>
        /// Idempotent start. Returns true if VIIPER is (or became) running.
        /// Safe to call when usbip-win2 is missing — returns false with a logged error.
        /// </summary>
        public bool Start()
        {
            if (isRunning) return true;

            if (settingsManager?.UsbipInstalled != null && !settingsManager.UsbipInstalled.Value)
            {
                Logger.Warn("VIIPER start requested but usbip-win2 is not installed; leaving service offline.");
                return false;
            }

            if (!service.Initialize())
            {
                Logger.Warn("VIIPER init failed; emulation backend will fall back to legacy at runtime.");
                return false;
            }

            if (!service.CreateBus(DefaultBusId))
            {
                Logger.Warn($"VIIPER failed to create bus {DefaultBusId}; shutting down service.");
                service.Dispose();
                return false;
            }
            activeBusId = DefaultBusId;

            // Enable HidHide so the stock physical controller is hidden from games while
            // VIIPER is active. Reuses the existing ControllerSuppressionManager instance
            // owned by the legacy manager — sharing the same underlying hide/unhide state
            // prevents both backends from fighting over the same device IDs.
            try
            {
                var suppression = legacyManager?.SuppressionManager;
                if (suppression != null)
                {
                    bool ok = suppression.Enable(
                        legacyManager.HandheldDeviceType,
                        legacyManager.HideTarget);
                    Logger.Info($"VIIPER: HidHide suppression enable => {ok}");
                    viiperOwnsSuppression = ok;
                }
            }
            catch (Exception ex) { Logger.Warn($"VIIPER HidHide Enable threw: {ex.Message}"); }

            // Create the initial virtual device using current settings.
            string targetType;
            ushort vid, pid;
            ResolveDeviceTargets(out targetType, out vid, out pid);
            var addResult = service.AddDevice(activeBusId, targetType, vid, pid);
            if (!addResult.Success)
            {
                Logger.Warn($"VIIPER failed to add {targetType} device; tearing down.");
                service.RemoveBus(activeBusId);
                service.Dispose();
                return false;
            }
            activeDeviceId = addResult.DeviceId;
            activeDeviceType = targetType;

            // Start forwarding physical input -> virtual device.
            uint xinputIdx = ViiperInputForwarder.DetectPhysicalXInputIndex();
            forwarder.SetInputSource(ResolveInputSource());
            forwarder.SetGyroSource(ResolveGyroSource());
            forwarder.SetGuideButtonMode(ResolveGuideMode());
            forwarder.SetSwapRumbleMotors(settingsManager?.ViiperSwapRumbleMotors?.Value ?? false);
            forwarder.SetRumbleIntensity(settingsManager?.ViiperRumbleIntensity?.Value ?? 100);
            forwarder.SetGyroAxisMapping(
                settingsManager?.ViiperGyroAxisMapX?.Value ?? "X",
                settingsManager?.ViiperGyroAxisMapY?.Value ?? "Y",
                settingsManager?.ViiperGyroAxisMapZ?.Value ?? "Z");
            forwarder.Start(xinputIdx, activeBusId, activeDeviceId, activeDeviceType);

            isRunning = true;
            Logger.Info($"VIIPER emulation manager started (bus={activeBusId}, dev={activeDeviceId}, type={activeDeviceType}, xinput={xinputIdx})");

            // Tell Labs/LegionButtonMonitor to tear down the dedicated Guide-only ViGEm pad
            // now that VIIPER will deliver the Guide press through its emulated device.
            Program.NotifyGuideRouteChanged();
            return true;
        }

        /// <summary>
        /// Determines the (deviceType, vid, pid) tuple to create based on current settings.
        /// </summary>
        private void ResolveDeviceTargets(out string targetType, out ushort vid, out ushort pid)
        {
            targetType = DefaultDeviceType;
            vid = 0;
            pid = 0;

            if (settingsManager?.ViiperDeviceType != null && !string.IsNullOrEmpty(settingsManager.ViiperDeviceType.Value))
            {
                targetType = settingsManager.ViiperDeviceType.Value;
            }

            // Steam controller family: need to resolve sub-device PID.
            bool isSteam = targetType == "steam-generic"
                || targetType == "steam-controller"
                || targetType == "steamdeck-generic";
            if (isSteam && settingsManager?.ViiperSteamSubDevice != null)
            {
                ViiperSteamSubDeviceProperty.TryGetSteamVidPid(settingsManager.ViiperSteamSubDevice.Value, out vid, out pid);
            }
        }

        private ViiperInputSourceKind ResolveInputSource()
        {
            var value = settingsManager?.ViiperInputSource?.Value ?? "XInput";
            return string.Equals(value, "LegionHid", StringComparison.OrdinalIgnoreCase)
                ? ViiperInputSourceKind.LegionHid
                : ViiperInputSourceKind.XInput;
        }

        private void OnInputSourceChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try { forwarder.SetInputSource(ResolveInputSource()); }
            catch (Exception ex) { Logger.Warn($"OnInputSourceChanged threw: {ex.Message}"); }
        }

        private ViiperGyroSourceKind ResolveGyroSource()
        {
            var value = settingsManager?.ViiperGyroSource?.Value ?? "None";
            switch (value)
            {
                case "Left":     return ViiperGyroSourceKind.Left;
                case "Right":    return ViiperGyroSourceKind.Right;
                case "Handheld": return ViiperGyroSourceKind.Handheld;
                default:         return ViiperGyroSourceKind.None;
            }
        }

        private void OnGyroSourceChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try { forwarder.SetGyroSource(ResolveGyroSource()); }
            catch (Exception ex) { Logger.Warn($"OnGyroSourceChanged threw: {ex.Message}"); }
        }

        private ViiperGuideButtonMode ResolveGuideMode()
        {
            var value = settingsManager?.ViiperGuideButtonMode?.Value ?? "Native";
            return string.Equals(value, "GameBar", StringComparison.OrdinalIgnoreCase)
                ? ViiperGuideButtonMode.GameBar
                : ViiperGuideButtonMode.Native;
        }

        private void OnGuideModeChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try { forwarder.SetGuideButtonMode(ResolveGuideMode()); }
            catch (Exception ex) { Logger.Warn($"OnGuideModeChanged threw: {ex.Message}"); }
        }

        private void OnSwapRumbleMotorsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try { forwarder.SetSwapRumbleMotors(settingsManager?.ViiperSwapRumbleMotors?.Value ?? false); }
            catch (Exception ex) { Logger.Warn($"OnSwapRumbleMotorsChanged threw: {ex.Message}"); }
        }

        private void OnRumbleIntensityChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try { forwarder.SetRumbleIntensity(settingsManager?.ViiperRumbleIntensity?.Value ?? 100); }
            catch (Exception ex) { Logger.Warn($"OnRumbleIntensityChanged threw: {ex.Message}"); }
        }

        private void OnGyroAxisMapChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                forwarder.SetGyroAxisMapping(
                    settingsManager?.ViiperGyroAxisMapX?.Value ?? "X",
                    settingsManager?.ViiperGyroAxisMapY?.Value ?? "Y",
                    settingsManager?.ViiperGyroAxisMapZ?.Value ?? "Z");
            }
            catch (Exception ex) { Logger.Warn($"OnGyroAxisMapChanged threw: {ex.Message}"); }
        }

        private void OnDeviceConfigChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!isRunning) return; // Will be picked up on next Start().

            string oldType = activeDeviceType;
            string newType;
            ushort vid, pid;
            ResolveDeviceTargets(out newType, out vid, out pid);
            if (newType == activeDeviceType && vid == 0 && pid == 0)
            {
                return;
            }

            Logger.Info($"VIIPER hot-swap: {activeDeviceType} -> {newType} (vid=0x{vid:X4}, pid=0x{pid:X4})");

            // Pause the forwarder for the whole swap: RemoveDevice is instant but AddDevice
            // can take 1-2 seconds (USBIP round-trip). Without this gate the poll loop
            // floods the log with "invalid input size" warnings and may push a wrong-format
            // packet at the new device before we can flip targetType.
            forwarder.SetPaused(true);
            try
            {
                var swap = service.SwitchDeviceType(activeBusId, activeDeviceId, newType, vid, pid);
                if (!swap.Success)
                {
                    Logger.Warn($"VIIPER hot-swap failed ({oldType} -> {newType}); attempting to re-add old device to recover.");
                    // RemoveDevice already succeeded inside SwitchDeviceType, so the old
                    // pad is gone. Re-add the old type with fresh vid/pid so the user
                    // isn't left with no virtual controller.
                    ushort oldVid = 0, oldPid = 0;
                    if (oldType == "steam-generic" || oldType == "steam-controller" || oldType == "steamdeck-generic")
                    {
                        ViiperSteamSubDeviceProperty.TryGetSteamVidPid(
                            settingsManager?.ViiperSteamSubDevice?.Value ?? "generic",
                            out oldVid, out oldPid);
                    }
                    var recover = service.AddDevice(activeBusId, oldType, oldVid, oldPid);
                    if (recover.Success)
                    {
                        activeDeviceId = recover.DeviceId;
                        activeDeviceType = oldType;
                        forwarder.UpdateTarget(activeBusId, activeDeviceId, activeDeviceType);
                        Logger.Info($"VIIPER hot-swap recovery: restored {oldType} device (dev={activeDeviceId})");
                    }
                    else
                    {
                        Logger.Warn($"VIIPER hot-swap recovery failed — no virtual device active.");
                        activeDeviceId = 0;
                    }
                    return;
                }
                activeDeviceId = swap.DeviceId;
                activeDeviceType = newType;
                forwarder.UpdateTarget(activeBusId, activeDeviceId, activeDeviceType);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "VIIPER hot-swap threw");
            }
            finally
            {
                forwarder.SetPaused(false);
            }
        }

        public void Stop()
        {
            if (!isRunning) return;

            try { forwarder.Stop(); }
            catch (Exception ex) { Logger.Warn($"forwarder.Stop threw: {ex.Message}"); }

            if (viiperOwnsSuppression)
            {
                try
                {
                    legacyManager?.SuppressionManager?.Disable();
                    Logger.Info("VIIPER: HidHide suppression disabled");
                }
                catch (Exception ex) { Logger.Warn($"VIIPER HidHide Disable threw: {ex.Message}"); }
                viiperOwnsSuppression = false;
            }

            try
            {
                if (activeDeviceId != 0)
                {
                    service.RemoveDevice(activeBusId, activeDeviceId);
                }
            }
            catch (Exception ex) { Logger.Warn($"RemoveDevice threw: {ex.Message}"); }

            try { service.RemoveBus(activeBusId); }
            catch (Exception ex) { Logger.Warn($"RemoveBus threw: {ex.Message}"); }

            try { service.Dispose(); }
            catch (Exception ex) { Logger.Warn($"VIIPER Dispose threw: {ex.Message}"); }

            activeDeviceId = 0;
            isRunning = false;
            Logger.Info("VIIPER emulation manager stopped");

            // Emulation just went away — tell Labs to re-spin the dedicated Guide-only
            // ViGEm pad if a Guide action is still mapped.
            Program.NotifyGuideRouteChanged();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (legacyManager != null)
                {
                    legacyManager.EmulationEnabledChanged -= OnEmulationEnabledChanged;
                }
                if (settingsManager != null)
                {
                    if (settingsManager.EmulationBackend != null)
                    {
                        settingsManager.EmulationBackend.PropertyChanged -= OnBackendChanged;
                    }
                    if (settingsManager.ViiperDeviceType != null)
                    {
                        settingsManager.ViiperDeviceType.PropertyChanged -= OnDeviceConfigChanged;
                    }
                    if (settingsManager.ViiperSteamSubDevice != null)
                    {
                        settingsManager.ViiperSteamSubDevice.PropertyChanged -= OnDeviceConfigChanged;
                    }
                    if (settingsManager.ViiperInputSource != null)
                    {
                        settingsManager.ViiperInputSource.PropertyChanged -= OnInputSourceChanged;
                    }
                    if (settingsManager.ViiperGyroSource != null)
                    {
                        settingsManager.ViiperGyroSource.PropertyChanged -= OnGyroSourceChanged;
                    }
                    if (settingsManager.ViiperGuideButtonMode != null)
                    {
                        settingsManager.ViiperGuideButtonMode.PropertyChanged -= OnGuideModeChanged;
                    }
                }
                Stop();
                try { forwarder?.Dispose(); }
                catch (Exception ex) { Logger.Warn($"forwarder.Dispose threw: {ex.Message}"); }
            }
            base.Dispose(disposing);
        }
    }
}
