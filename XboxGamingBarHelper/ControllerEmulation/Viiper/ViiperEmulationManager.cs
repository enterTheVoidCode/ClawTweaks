using System;
using NLog;
using XboxGamingBarHelper.Core;
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

        private readonly SettingsManager settingsManager;
        private readonly ViiperService service = new ViiperService();

        private bool isRunning;
        private uint activeBusId;

        public ViiperEmulationManager(SettingsManager inSettingsManager)
        {
            settingsManager = inSettingsManager;
            if (settingsManager != null && settingsManager.EmulationBackend != null)
            {
                settingsManager.EmulationBackend.PropertyChanged += OnBackendChanged;
                // Apply initial state.
                ApplyBackend(settingsManager.EmulationBackend.Value);
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
            isRunning = true;
            Logger.Info($"VIIPER emulation manager started (bus={activeBusId})");
            return true;
        }

        public void Stop()
        {
            if (!isRunning) return;

            try { service.RemoveBus(activeBusId); }
            catch (Exception ex) { Logger.Warn($"RemoveBus threw: {ex.Message}"); }

            try { service.Dispose(); }
            catch (Exception ex) { Logger.Warn($"VIIPER Dispose threw: {ex.Message}"); }

            isRunning = false;
            Logger.Info("VIIPER emulation manager stopped");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (settingsManager != null && settingsManager.EmulationBackend != null)
                {
                    settingsManager.EmulationBackend.PropertyChanged -= OnBackendChanged;
                }
                Stop();
            }
            base.Dispose(disposing);
        }
    }
}
