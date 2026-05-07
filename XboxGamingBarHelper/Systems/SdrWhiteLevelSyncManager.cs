using NLog;
using Shared.Enums;
using System;
using System.Management;
using XboxGamingBarHelper.Sidebar;
using XboxGamingBarHelper.Windows;

namespace XboxGamingBarHelper.Systems
{
    /// <summary>
    /// Keeps the Windows SDR White Level (paper-white nits target) in sync with the
    /// hardware backlight slider while HDR is active. Windows itself never wires these
    /// two together — Settings exposes a static "SDR content brightness" slider only.
    ///
    /// Curve choices:
    ///   Auto             — nits = 80 + (brightness / 100) ^ gamma * (peakNits - 80)
    ///                      Generic parametric model. Defaults to peakNits=480, gamma=2.2.
    ///   LegionGo2Preset  — empirical Go2HDR curve calibrated for the Legion Go 2 panel
    ///                      (0–46 → 80 nits floor, then ramps to ~480 at slider=100).
    /// </summary>
    internal sealed class SdrWhiteLevelSyncManager : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const int PeakNitsAuto = 480;
        private const double GammaAuto = 2.2;

        private ManagementEventWatcher watcher;
        private SdrWhiteLevelSyncMode currentMode = SdrWhiteLevelSyncMode.Off;
        private bool hdrEnabled;

        public void SetMode(SdrWhiteLevelSyncMode mode)
        {
            currentMode = mode;
            if (mode == SdrWhiteLevelSyncMode.Off)
            {
                StopWatcher();
                return;
            }
            StartWatcher();
            ApplyNow();
        }

        public void SetHdrEnabled(bool enabled)
        {
            if (hdrEnabled == enabled) return;
            hdrEnabled = enabled;
            if (enabled && currentMode != SdrWhiteLevelSyncMode.Off) ApplyNow();
        }

        private void StartWatcher()
        {
            if (watcher != null) return;
            try
            {
                var query = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");
                watcher = new ManagementEventWatcher(@"root\wmi", query.QueryString);
                watcher.EventArrived += OnBrightnessEvent;
                watcher.Start();
                Logger.Info("SDR sync: WmiMonitorBrightnessEvent watcher started");
            }
            catch (Exception ex)
            {
                Logger.Error($"SDR sync: failed to start brightness watcher: {ex.Message}");
                watcher = null;
            }
        }

        private void StopWatcher()
        {
            if (watcher == null) return;
            try
            {
                watcher.EventArrived -= OnBrightnessEvent;
                watcher.Stop();
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn($"SDR sync: stop watcher: {ex.Message}");
            }
            watcher = null;
            Logger.Info("SDR sync: watcher stopped");
        }

        private void OnBrightnessEvent(object sender, EventArrivedEventArgs e)
        {
            if (currentMode == SdrWhiteLevelSyncMode.Off || !hdrEnabled) return;
            try
            {
                var brightness = Convert.ToInt32(e.NewEvent["Brightness"]);
                Apply(brightness);
            }
            catch (Exception ex)
            {
                Logger.Warn($"SDR sync: brightness event handler failed: {ex.Message}");
                ApplyNow();
            }
        }

        private void ApplyNow()
        {
            if (!hdrEnabled || currentMode == SdrWhiteLevelSyncMode.Off) return;
            Apply(BrightnessManager.GetBrightness());
        }

        private void Apply(int brightness)
        {
            if (brightness < 0) brightness = 0;
            if (brightness > 100) brightness = 100;

            int nits = currentMode switch
            {
                SdrWhiteLevelSyncMode.Auto => AutoCurveNits(brightness),
                SdrWhiteLevelSyncMode.LegionGo2Preset => LegionGo2CurveNits(brightness),
                _ => 0,
            };
            if (nits <= 0) return;
            User32.SetSdrWhiteLevelNits(nits);
        }

        private static int AutoCurveNits(int brightness)
        {
            double normalized = brightness / 100.0;
            double scaled = Math.Pow(normalized, GammaAuto);
            return (int)Math.Round(80 + scaled * (PeakNitsAuto - 80));
        }

        // Empirical mapping from Go2HDR — slider 0–46 floors at 80 nits (panel can't dim
        // SDR below that without becoming muddy), then ramps to ~480 at the top.
        private static int LegionGo2CurveNits(int brightness)
        {
            int sdrSlider = brightness <= 46 ? 0 : (int)Math.Round((brightness - 46) * (100.0 / 54.0));
            if (sdrSlider < 0) sdrSlider = 0;
            if (sdrSlider > 100) sdrSlider = 100;
            return 80 + sdrSlider * 4;
        }

        public void Dispose() => StopWatcher();
    }
}
