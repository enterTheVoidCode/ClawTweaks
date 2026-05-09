using NLog;
using Shared.Enums;
using System;
using System.Management;
using System.Threading;
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
    ///
    /// HDR-active state is self-polled every <see cref="HdrPollIntervalMs"/> in addition
    /// to the external SetHdrEnabled trigger. Without the poll, we'd miss HDR toggles
    /// that happen outside our code (Windows Settings → System → HDR, F11 in some games,
    /// game-mode auto-HDR transitions). The brightness watcher starts/stops automatically
    /// based on the polled state so we don't burn WMI cycles when HDR is off.
    /// </summary>
    internal sealed class SdrWhiteLevelSyncManager : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const int PeakNitsAuto = 480;
        private const double GammaAuto = 2.2;
        private const int HdrPollIntervalMs = 2000;

        private ManagementEventWatcher watcher;
        private SdrWhiteLevelSyncMode currentMode = SdrWhiteLevelSyncMode.Off;
        private bool hdrEnabled;
        private Timer hdrPollTimer;

        public void SetMode(SdrWhiteLevelSyncMode mode)
        {
            currentMode = mode;
            if (mode == SdrWhiteLevelSyncMode.Off)
            {
                StopHdrPoll();
                StopWatcher();
                return;
            }
            StartHdrPoll();
            // Don't pre-start the brightness watcher — let the poll's first tick decide
            // based on the actual current HDR state. ApplyNow handles the HDR-on case.
            ApplyNow();
        }

        public void SetHdrEnabled(bool enabled)
        {
            if (hdrEnabled == enabled) return;
            hdrEnabled = enabled;
            if (currentMode == SdrWhiteLevelSyncMode.Off) return;
            if (enabled)
            {
                StartWatcher();
                ApplyNow();
            }
            else
            {
                StopWatcher();
            }
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

        // Periodic HDR-active check. Bridges the gap when HDR is toggled outside our
        // process (Windows Settings, game auto-HDR, F11 in DXVK/HDR games). Calls into
        // SetHdrEnabled with the polled state so the existing watcher start/stop +
        // ApplyNow logic stays unified between external triggers and self-polling.
        private void StartHdrPoll()
        {
            if (hdrPollTimer != null) return;
            // Initial fire after 0 to seed state immediately, then repeat at the interval.
            hdrPollTimer = new Timer(OnHdrPollTick, null, 0, HdrPollIntervalMs);
        }

        private void StopHdrPoll()
        {
            var t = hdrPollTimer;
            hdrPollTimer = null;
            if (t == null) return;
            try { t.Dispose(); } catch { }
        }

        private void OnHdrPollTick(object state)
        {
            try
            {
                var (_, enabled) = User32.GetHDRStatus();
                SetHdrEnabled(enabled);
            }
            catch (Exception ex)
            {
                Logger.Debug($"SDR sync: HDR poll tick failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            StopHdrPoll();
            StopWatcher();
        }
    }
}
