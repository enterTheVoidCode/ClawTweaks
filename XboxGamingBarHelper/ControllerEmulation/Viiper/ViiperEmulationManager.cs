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

        public ViiperEmulationManager(SettingsManager inSettingsManager, ControllerEmulationManager inLegacyManager, LegionManager inLegionManager)
        {
            settingsManager = inSettingsManager;
            legacyManager = inLegacyManager;
            forwarder = new ViiperInputForwarder(service, inLegionManager);
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
                // Apply initial state.
                if (settingsManager.EmulationBackend != null)
                {
                    ApplyBackend(settingsManager.EmulationBackend.Value);
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

            if (useViiper)
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
            forwarder.Start(xinputIdx, activeBusId, activeDeviceId, activeDeviceType);

            isRunning = true;
            Logger.Info($"VIIPER emulation manager started (bus={activeBusId}, dev={activeDeviceId}, type={activeDeviceType}, xinput={xinputIdx})");
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

        private void OnDeviceConfigChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!isRunning) return; // Will be picked up on next Start().
            try
            {
                string newType;
                ushort vid, pid;
                ResolveDeviceTargets(out newType, out vid, out pid);
                if (newType == activeDeviceType && vid == 0 && pid == 0)
                {
                    return;
                }
                Logger.Info($"VIIPER hot-swap: {activeDeviceType} -> {newType} (vid=0x{vid:X4}, pid=0x{pid:X4})");
                var swap = service.SwitchDeviceType(activeBusId, activeDeviceId, newType, vid, pid);
                if (!swap.Success)
                {
                    Logger.Warn("VIIPER hot-swap failed; previous device left in place.");
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
        }

        public void Stop()
        {
            if (!isRunning) return;

            try { forwarder.Stop(); }
            catch (Exception ex) { Logger.Warn($"forwarder.Stop threw: {ex.Message}"); }

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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
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
                }
                Stop();
                try { forwarder?.Dispose(); }
                catch (Exception ex) { Logger.Warn($"forwarder.Dispose threw: {ex.Message}"); }
            }
            base.Dispose(disposing);
        }
    }
}
