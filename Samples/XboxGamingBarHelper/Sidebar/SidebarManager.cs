using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using NLog;
using XboxGamingBarHelper.AutoTDP;
using XboxGamingBarHelper.ControllerEmulation;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.Power;
using XboxGamingBarHelper.Profile;
using XboxGamingBarHelper.RTSS;
using XboxGamingBarHelper.Sidebar.Audio;
using XboxGamingBarHelper.Systems;

namespace XboxGamingBarHelper.Sidebar
{
    internal sealed class SidebarManager : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // WPF thread
        private Thread _wpfThread;
        private System.Windows.Threading.Dispatcher _dispatcher;
        private readonly ManualResetEventSlim _startupSignal = new ManualResetEventSlim(false);
        private Exception _startupException;
        private bool _disposed;

        // UI
        private SidebarWindow _window;
        private SidebarInputHandler _inputHandler;

        // Audio manager (created on WPF thread)
        private AudioManager _audioMgr;

        // Metrics timer
        private System.Timers.Timer _metricsTimer;

        // Manager references
        private PerformanceManager _perfMgr;
        private AutoTDPManager _autoTDPMgr;
        private ProfileManager _profileMgr;
        private LegionManager _legionMgr;
        private ControllerEmulationManager _ctrlEmuMgr;
        private PowerManager _powerMgr;
        private RTSSManager _rtssMgr;
        private SystemManager _sysMgr;

        internal void SetManagers(
            PerformanceManager perfMgr,
            AutoTDPManager autoTDPMgr,
            ProfileManager profileMgr,
            LegionManager legionMgr,
            ControllerEmulationManager ctrlEmuMgr,
            PowerManager powerMgr,
            RTSSManager rtssMgr,
            SystemManager sysMgr)
        {
            _perfMgr = perfMgr;
            _autoTDPMgr = autoTDPMgr;
            _profileMgr = profileMgr;
            _legionMgr = legionMgr;
            _ctrlEmuMgr = ctrlEmuMgr;
            _powerMgr = powerMgr;
            _rtssMgr = rtssMgr;
            _sysMgr = sysMgr;

            SubscribePropertyChanges();
        }

        internal bool Start()
        {
            _wpfThread = new Thread(WpfThreadMain)
            {
                IsBackground = true,
                Name = "GoTweaksSidebar",
            };
            _wpfThread.SetApartmentState(ApartmentState.STA);
            _wpfThread.Start();

            if (!_startupSignal.Wait(5000))
            {
                _startupException = new TimeoutException("Timed out waiting for sidebar WPF thread to initialize.");
            }

            if (_startupException != null)
            {
                Logger.Error($"Sidebar: Failed to start WPF thread: {_startupException.Message}");
                return false;
            }

            Logger.Info("Sidebar: WPF thread started successfully");
            return true;
        }

        private void WpfThreadMain()
        {
            try
            {
                _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

                _window = new SidebarWindow();
                _inputHandler = new SidebarInputHandler();
                _audioMgr = new AudioManager();

                // ══════════════════════════════════════════
                // Wire QuickTab events
                // ══════════════════════════════════════════
                _window.QuickTab.OnVolumeChanged += value =>
                {
                    Logger.Info($"Sidebar: Volume changed to {value}");
                    _audioMgr?.SetVolume(value);
                };

                _window.QuickTab.OnBrightnessChanged += value =>
                {
                    Logger.Info($"Sidebar: Brightness changed to {value}");
                    BrightnessManager.SetBrightness(value);
                };

                _window.QuickTab.OnAudioDeviceChanged += index =>
                {
                    var devices = _window.QuickTab.GetAudioDevices();
                    if (index >= 0 && index < devices.Count)
                    {
                        Logger.Info($"Sidebar: Audio device changed to {devices[index].FriendlyName}");
                        _audioMgr?.SetDefaultDevice(devices[index].Id);
                    }
                };

                _window.QuickTab.OnPerformanceModeChanged += mode =>
                {
                    Logger.Info($"Sidebar: Quick Performance mode changed to {mode}");
                    _legionMgr?.LegionPerformanceMode?.ForceSetValue(mode);
                };

                _window.QuickTab.OnTDPChanged += value =>
                {
                    Logger.Info($"Sidebar: Quick TDP changed to {value}");
                    _perfMgr?.TDP?.ForceSetValue(value);
                };

                _window.QuickTab.OnControllerEmulationChanged += enabled =>
                {
                    Logger.Info($"Sidebar: Quick Controller Emulation changed to {enabled}");
                    _ctrlEmuMgr?.ControllerEmulationEnabled?.ForceSetValue(enabled);
                };

                _window.QuickTab.OnCPUBoostChanged += enabled =>
                {
                    Logger.Info($"Sidebar: Quick CPU Boost changed to {enabled}");
                    _powerMgr?.CPUBoost?.ForceSetValue(enabled);
                };

                _window.QuickTab.OnAutoTDPChanged += enabled =>
                {
                    Logger.Info($"Sidebar: Quick AutoTDP changed to {enabled}");
                    _autoTDPMgr?.Enabled?.ForceSetValue(enabled);
                };

                _window.QuickTab.OnPerformanceOverlayChanged += level =>
                {
                    Logger.Info($"Sidebar: Quick Overlay changed to {level}");
                    Program.onScreenDisplay?.ForceSetValue(level);
                };

                _window.QuickTab.OnFanFullSpeedChanged += enabled =>
                {
                    Logger.Info($"Sidebar: Quick Fan Full Speed changed to {enabled}");
                    _legionMgr?.LegionFanFullSpeed?.ForceSetValue(enabled);
                };

                // ══════════════════════════════════════════
                // Wire PerformanceTab events
                // ══════════════════════════════════════════
                _window.PerformanceTab.OnPerformanceModeChanged += mode =>
                {
                    Logger.Info($"Sidebar: Performance mode changed to {mode}");
                    _legionMgr?.LegionPerformanceMode?.ForceSetValue(mode);
                };

                _window.PerformanceTab.OnTDPChanged += value =>
                {
                    Logger.Info($"Sidebar: TDP changed to {value}");
                    _perfMgr?.TDP?.ForceSetValue(value);
                };

                _window.PerformanceTab.OnTDPBoostChanged += enabled =>
                {
                    Logger.Info($"Sidebar: TDP Boost changed to {enabled}");
                    _perfMgr?.TDPBoostEnabled?.ForceSetValue(enabled);
                };

                _window.PerformanceTab.OnAutoTDPChanged += enabled =>
                {
                    Logger.Info($"Sidebar: AutoTDP changed to {enabled}");
                    _autoTDPMgr?.Enabled?.ForceSetValue(enabled);
                };

                _window.PerformanceTab.OnAutoTDPTargetFPSChanged += value =>
                {
                    Logger.Info($"Sidebar: AutoTDP Target FPS changed to {value}");
                    _autoTDPMgr?.TargetFPS?.ForceSetValue(value);
                };

                _window.PerformanceTab.OnAutoTDPMinTDPChanged += value =>
                {
                    Logger.Info($"Sidebar: AutoTDP Min TDP changed to {value}");
                    _autoTDPMgr?.MinTDP?.ForceSetValue(value);
                };

                _window.PerformanceTab.OnAutoTDPMaxTDPChanged += value =>
                {
                    Logger.Info($"Sidebar: AutoTDP Max TDP changed to {value}");
                    _autoTDPMgr?.MaxTDP?.ForceSetValue(value);
                };

                _window.PerformanceTab.OnAutoTDPControllerChanged += index =>
                {
                    Logger.Info($"Sidebar: AutoTDP Controller changed to {index}");
                    _autoTDPMgr?.ControllerType?.ForceSetValue(index);
                };

                _window.PerformanceTab.OnPerformanceOverlayChanged += level =>
                {
                    Logger.Info($"Sidebar: Performance Overlay changed to {level}");
                    Program.onScreenDisplay?.ForceSetValue(level);
                };

                _window.PerformanceTab.OnCPUBoostChanged += enabled =>
                {
                    Logger.Info($"Sidebar: CPU Boost changed to {enabled}");
                    _powerMgr?.CPUBoost?.ForceSetValue(enabled);
                };

                _window.PerformanceTab.OnCPUEPPChanged += value =>
                {
                    Logger.Info($"Sidebar: CPU EPP changed to {value}");
                    _powerMgr?.CPUEPP?.ForceSetValue(value);
                };

                _window.PerformanceTab.OnOSPowerModeChanged += index =>
                {
                    Logger.Info($"Sidebar: OS Power Mode changed to {index}");
                    _powerMgr?.OSPowerMode?.ForceSetValue(index);
                };

                _window.PerformanceTab.OnFPSLimitChanged += value =>
                {
                    Logger.Info($"Sidebar: FPS Limit changed to {value}");
                    _rtssMgr?.FPSLimit?.ForceSetValue(value);
                };

                _window.PerformanceTab.OnControllerEmulationChanged += enabled =>
                {
                    Logger.Info($"Sidebar: Controller Emulation changed to {enabled}");
                    _ctrlEmuMgr?.ControllerEmulationEnabled?.ForceSetValue(enabled);
                };

                // ══════════════════════════════════════════
                // Wire DisplayTab events
                // ══════════════════════════════════════════
                _window.DisplayTab.OnResolutionChanged += resolution =>
                {
                    Logger.Info($"Sidebar: Resolution changed to {resolution}");
                    _sysMgr?.Resolution?.ForceSetValue(resolution);
                };

                _window.DisplayTab.OnRefreshRateChanged += rate =>
                {
                    Logger.Info($"Sidebar: Refresh Rate changed to {rate}");
                    _sysMgr?.RefreshRate?.ForceSetValue(rate);
                };

                _window.DisplayTab.OnHDRChanged += enabled =>
                {
                    Logger.Info($"Sidebar: HDR changed to {enabled}");
                    _sysMgr?.HDREnabled?.ForceSetValue(enabled);
                };

                _window.DisplayTab.OnOrientationChanged += index =>
                {
                    Logger.Info($"Sidebar: Display Orientation changed to {index}");
                    _sysMgr?.DisplayOrientation?.ForceSetValue(index);
                };

                // ══════════════════════════════════════════
                // Wire LegionTab events
                // ══════════════════════════════════════════
                _window.LegionTab.OnFanFullSpeedChanged += enabled =>
                {
                    Logger.Info($"Sidebar: Legion Fan Full Speed changed to {enabled}");
                    _legionMgr?.LegionFanFullSpeed?.ForceSetValue(enabled);
                };

                _window.LegionTab.OnChargeLimitChanged += enabled =>
                {
                    Logger.Info($"Sidebar: Legion Charge Limit changed to {enabled}");
                    _legionMgr?.LegionChargeLimit?.ForceSetValue(enabled);
                };

                _window.LegionTab.OnTouchpadChanged += enabled =>
                {
                    Logger.Info($"Sidebar: Legion Touchpad changed to {enabled}");
                    _legionMgr?.LegionTouchpadEnabled?.ForceSetValue(enabled);
                };

                _window.LegionTab.OnPowerLightChanged += enabled =>
                {
                    Logger.Info($"Sidebar: Legion Power Light changed to {enabled}");
                    _legionMgr?.LegionPowerLight?.ForceSetValue(enabled);
                };

                _window.LegionTab.OnLightModeChanged += mode =>
                {
                    Logger.Info($"Sidebar: Legion Light Mode changed to {mode}");
                    _legionMgr?.LegionLightMode?.ForceSetValue(mode);
                };

                _window.LegionTab.OnLightSpeedChanged += value =>
                {
                    Logger.Info($"Sidebar: Legion Light Speed changed to {value}");
                    _legionMgr?.LegionLightSpeed?.ForceSetValue(value);
                };

                _window.LegionTab.OnLightBrightnessChanged += value =>
                {
                    Logger.Info($"Sidebar: Legion Light Brightness changed to {value}");
                    _legionMgr?.LegionLightBrightness?.ForceSetValue(value);
                };

                _window.LegionTab.OnVibrationLevelChanged += value =>
                {
                    Logger.Info($"Sidebar: Legion Vibration Level changed to {value}");
                    _legionMgr?.LegionVibration?.ForceSetValue(value);
                };

                _window.LegionTab.OnVibrationModeChanged += value =>
                {
                    Logger.Info($"Sidebar: Legion Vibration Mode changed to {value}");
                    _legionMgr?.LegionVibrationMode?.ForceSetValue(value);
                };

                // ══════════════════════════════════════════
                // Wire ProfilesTab events
                // ══════════════════════════════════════════
                _window.ProfilesTab.OnPerGameProfileChanged += enabled =>
                {
                    Logger.Info($"Sidebar: Per-game profile changed to {enabled}");
                    _profileMgr?.PerGameProfile?.ForceSetValue(enabled);
                };

                // Wire input handler → sidebar navigation (on dispatcher thread)
                _inputHandler.OnDPadUp = () => _dispatcher.BeginInvoke(new Action(() => _window.NavigateUp()));
                _inputHandler.OnDPadDown = () => _dispatcher.BeginInvoke(new Action(() => _window.NavigateDown()));
                _inputHandler.OnDPadLeft = () => _dispatcher.BeginInvoke(new Action(() => _window.NavigateLeft()));
                _inputHandler.OnDPadRight = () => _dispatcher.BeginInvoke(new Action(() => _window.NavigateRight()));
                _inputHandler.OnButtonA = () => _dispatcher.BeginInvoke(new Action(() => _window.Activate()));
                _inputHandler.OnButtonB = () => _dispatcher.BeginInvoke(new Action(() => _window.Dismiss()));

                // Wire LT/RT → tab navigation
                _inputHandler.OnLeftTrigger = () => _dispatcher.BeginInvoke(new Action(() => _window.TabLeft()));
                _inputHandler.OnRightTrigger = () => _dispatcher.BeginInvoke(new Action(() => _window.TabRight()));

                // Set up metrics timer
                _metricsTimer = new System.Timers.Timer(1000);
                _metricsTimer.Elapsed += (s, e) => UpdateMetricsAndBattery();
                _metricsTimer.AutoReset = true;

                _startupSignal.Set();
                System.Windows.Threading.Dispatcher.Run();
            }
            catch (Exception ex)
            {
                _startupException = ex;
                _startupSignal.Set();
            }
        }

        internal bool IsVisible
        {
            get
            {
                if (_window == null || _dispatcher == null) return false;
                return (bool)_dispatcher.Invoke(new Func<bool>(() => _window.IsVisible));
            }
        }

        internal void Toggle()
        {
            if (_window == null || _dispatcher == null) return;

            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window.IsVisible)
                {
                    _window.HideSidebar();
                    _inputHandler?.Stop();
                    _metricsTimer?.Stop();
                }
                else
                {
                    PopulateCurrentValues();
                    _window.ShowSidebar();
                    _inputHandler?.Start();
                    _metricsTimer?.Start();
                }
            }));
        }

        private void UpdateMetricsAndBattery()
        {
            if (_window == null || _dispatcher == null) return;

            try
            {
                float cpuUse = _perfMgr?.CPUUsage?.Value ?? 0;
                float cpuTemp = _perfMgr?.CPUTemperature?.Value ?? 0;
                float gpuUse = _perfMgr?.GPUUsage?.Value ?? 0;
                float gpuTemp = _perfMgr?.GPUTemperature?.Value ?? 0;
                float memUse = _perfMgr?.MemoryUsage?.Value ?? 0;
                float fps = _autoTDPMgr?.CurrentFPS?.Value ?? 0;

                float batteryPct = _perfMgr?.BatteryLevel?.Value ?? 0;
                float drainW = _perfMgr?.BatteryDischargeRate?.Value ?? 0;
                float chargeW = _perfMgr?.BatteryChargeRate?.Value ?? 0;
                float timeRemaining = _perfMgr?.BatteryTimeRemaining ?? -1;
                float timeToFull = _perfMgr?.BatteryTimeToFull ?? -1;
                bool isCharging = chargeW > 0;

                // Header battery text
                string headerText;
                if (isCharging)
                {
                    if (timeToFull > 0)
                    {
                        int h = (int)(timeToFull / 3600);
                        int m = (int)((timeToFull % 3600) / 60);
                        headerText = $"{(int)batteryPct}% ~{h}:{m:D2}";
                    }
                    else
                    {
                        headerText = $"{(int)batteryPct}% \u26A1";
                    }
                }
                else
                {
                    if (timeRemaining > 0)
                    {
                        int h = (int)(timeRemaining / 3600);
                        int m = (int)((timeRemaining % 3600) / 60);
                        headerText = $"{(int)batteryPct}% ~{h}:{m:D2}";
                    }
                    else
                    {
                        headerText = $"{(int)batteryPct}%";
                    }
                }

                System.Windows.Media.Color headerColor;
                if (batteryPct > 50) headerColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50");
                else if (batteryPct > 20) headerColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF9800");
                else headerColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F44336");

                _dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_window.IsVisible) return;

                    _window.QuickTab.UpdateMetrics(cpuUse, cpuTemp, gpuUse, gpuTemp, memUse, fps);
                    _window.QuickTab.UpdateBattery(batteryPct, drainW, chargeW,
                        timeRemaining > 0 ? timeRemaining : 0,
                        timeToFull > 0 ? timeToFull : 0,
                        isCharging);
                    _window.UpdateHeaderBattery(headerText, headerColor);
                }));
            }
            catch (Exception ex)
            {
                Logger.Debug($"Sidebar: Metrics update error: {ex.Message}");
            }
        }

        private void PopulateCurrentValues()
        {
            // ── Quick Tab ──
            // Volume
            int volume = _audioMgr?.GetVolume() ?? 50;
            _window.QuickTab.UpdateVolume(volume);

            // Brightness
            int brightness = BrightnessManager.GetBrightness();
            _window.QuickTab.UpdateBrightness(brightness);

            // Audio devices
            var devices = _audioMgr?.GetRenderDevices() ?? new List<AudioDevice>();
            var defaultDevice = _audioMgr?.GetDefaultDevice();
            int defaultIdx = 0;
            if (defaultDevice.HasValue)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    if (devices[i].Id == defaultDevice.Value.Id)
                    {
                        defaultIdx = i;
                        break;
                    }
                }
            }
            _window.QuickTab.UpdateAudioDevices(devices, defaultIdx);

            // Quick tab performance mode and TDP
            int perfMode = _legionMgr?.LegionPerformanceMode?.Value ?? 2;
            _window.QuickTab.UpdatePerformanceMode(perfMode);

            int tdp = _perfMgr?.TDP?.Value ?? 15;
            int tdpMin = 4;
            int tdpMax = 35;
            _window.QuickTab.UpdateTDP(tdp, tdpMin, tdpMax);

            bool ctrlEmu = _ctrlEmuMgr?.ControllerEmulationEnabled?.Value ?? false;
            _window.QuickTab.UpdateControllerEmulation(ctrlEmu);

            // Quick tab new tiles
            _window.QuickTab.UpdateCPUBoost(_powerMgr?.CPUBoost?.Value ?? false);
            _window.QuickTab.UpdateAutoTDP(_autoTDPMgr?.Enabled?.Value ?? false);
            _window.QuickTab.UpdatePerformanceOverlay(Program.onScreenDisplay?.Value ?? 0);
            _window.QuickTab.UpdateFanFullSpeed(_legionMgr?.LegionFanFullSpeed?.Value ?? false);

            // ── Performance Tab ──
            _window.PerformanceTab.UpdatePerformanceMode(perfMode);
            _window.PerformanceTab.UpdateTDP(tdp, tdpMin, tdpMax);
            _window.PerformanceTab.UpdateTDPBoost(_perfMgr?.TDPBoostEnabled?.Value ?? false);
            _window.PerformanceTab.UpdateAutoTDP(_autoTDPMgr?.Enabled?.Value ?? false);
            _window.PerformanceTab.UpdateAutoTDPTargetFPS(_autoTDPMgr?.TargetFPS?.Value ?? 60);
            _window.PerformanceTab.UpdateAutoTDPMinTDP(_autoTDPMgr?.MinTDP?.Value ?? 4);
            _window.PerformanceTab.UpdateAutoTDPMaxTDP(_autoTDPMgr?.MaxTDP?.Value ?? 35);
            _window.PerformanceTab.UpdateAutoTDPController(_autoTDPMgr?.ControllerType?.Value ?? 0);
            _window.PerformanceTab.UpdatePerformanceOverlay(Program.onScreenDisplay?.Value ?? 0);
            _window.PerformanceTab.UpdateCPUBoost(_powerMgr?.CPUBoost?.Value ?? false);
            _window.PerformanceTab.UpdateCPUEPP(_powerMgr?.CPUEPP?.Value ?? 50);
            _window.PerformanceTab.UpdateOSPowerMode(_powerMgr?.OSPowerMode?.Value ?? 1);
            _window.PerformanceTab.UpdateFPSLimit(_rtssMgr?.FPSLimit?.Value ?? 0);
            _window.PerformanceTab.UpdateControllerEmulation(ctrlEmu);

            // ── Display Tab ──
            var resolutions = _sysMgr?.Resolutions?.Value ?? new List<string>();
            string currentRes = _sysMgr?.Resolution?.Value ?? "";
            _window.DisplayTab.UpdateResolutions(resolutions, currentRes);

            var refreshRates = _sysMgr?.RefreshRates?.Value ?? new List<int>();
            int currentRR = _sysMgr?.RefreshRate?.Value ?? 60;
            _window.DisplayTab.UpdateRefreshRates(refreshRates, currentRR);

            bool hdrEnabled = _sysMgr?.HDREnabled?.Value ?? false;
            bool hdrSupported = _sysMgr?.HDRSupported?.Value ?? false;
            _window.DisplayTab.UpdateHDR(hdrEnabled, hdrSupported);

            int orientation = _sysMgr?.DisplayOrientation?.Value ?? 0;
            _window.DisplayTab.UpdateOrientation(orientation);

            // ── Legion Tab ──
            _window.LegionTab.UpdateFanFullSpeed(_legionMgr?.LegionFanFullSpeed?.Value ?? false);
            _window.LegionTab.UpdateChargeLimit(_legionMgr?.LegionChargeLimit?.Value ?? false);
            _window.LegionTab.UpdateTouchpad(_legionMgr?.LegionTouchpadEnabled?.Value ?? false);
            _window.LegionTab.UpdatePowerLight(_legionMgr?.LegionPowerLight?.Value ?? false);
            _window.LegionTab.UpdateLightMode(_legionMgr?.LegionLightMode?.Value ?? 0);
            _window.LegionTab.UpdateLightSpeed(_legionMgr?.LegionLightSpeed?.Value ?? 50);
            _window.LegionTab.UpdateLightBrightness(_legionMgr?.LegionLightBrightness?.Value ?? 0);
            _window.LegionTab.UpdateVibrationLevel(_legionMgr?.LegionVibration?.Value ?? 2);
            _window.LegionTab.UpdateVibrationMode(_legionMgr?.LegionVibrationMode?.Value ?? 1);

            // ── Profiles Tab ──
            string profileName = _profileMgr?.CurrentProfile?.GameId.Name ?? "Global";
            _window.UpdateProfile(profileName);
            _window.ProfilesTab.UpdateCurrentProfile(profileName);
            _window.ProfilesTab.UpdatePerGameProfile(_profileMgr?.PerGameProfile?.Value ?? false);

            // Detected game
            string gameName = _sysMgr?.RunningGame?.Value.GameId.Name;
            _window.ProfilesTab.UpdateDetectedGame(gameName);

            // Saved profiles
            _window.ProfilesTab.UpdateSavedProfiles(_profileMgr?.GameProfiles);

            // ── Initial metrics/battery ──
            UpdateMetricsAndBattery();
        }

        private void SubscribePropertyChanges()
        {
            // TDP
            if (_perfMgr?.TDP != null)
                _perfMgr.TDP.PropertyChanged += OnTDPChanged;
            if (_perfMgr?.TDPBoostEnabled != null)
                _perfMgr.TDPBoostEnabled.PropertyChanged += OnTDPBoostChanged;

            // AutoTDP
            if (_autoTDPMgr?.Enabled != null)
                _autoTDPMgr.Enabled.PropertyChanged += OnAutoTDPChanged;
            if (_autoTDPMgr?.TargetFPS != null)
                _autoTDPMgr.TargetFPS.PropertyChanged += OnAutoTDPTargetFPSChanged;
            if (_autoTDPMgr?.MinTDP != null)
                _autoTDPMgr.MinTDP.PropertyChanged += OnAutoTDPMinTDPChanged;
            if (_autoTDPMgr?.MaxTDP != null)
                _autoTDPMgr.MaxTDP.PropertyChanged += OnAutoTDPMaxTDPChanged;
            if (_autoTDPMgr?.ControllerType != null)
                _autoTDPMgr.ControllerType.PropertyChanged += OnAutoTDPControllerChanged;

            // Performance Overlay
            if (Program.onScreenDisplay != null)
                Program.onScreenDisplay.PropertyChanged += OnOverlayChanged;

            // CPU
            if (_powerMgr?.CPUBoost != null)
                _powerMgr.CPUBoost.PropertyChanged += OnCPUBoostChanged;
            if (_powerMgr?.CPUEPP != null)
                _powerMgr.CPUEPP.PropertyChanged += OnCPUEPPChanged;

            // Power
            if (_powerMgr?.OSPowerMode != null)
                _powerMgr.OSPowerMode.PropertyChanged += OnOSPowerModeChanged;
            if (_rtssMgr?.FPSLimit != null)
                _rtssMgr.FPSLimit.PropertyChanged += OnFPSLimitChanged;

            // Performance mode
            if (_legionMgr?.LegionPerformanceMode != null)
                _legionMgr.LegionPerformanceMode.PropertyChanged += OnPerfModeChanged;

            // Controller emulation
            if (_ctrlEmuMgr?.ControllerEmulationEnabled != null)
                _ctrlEmuMgr.ControllerEmulationEnabled.PropertyChanged += OnCtrlEmuChanged;

            // Display
            if (_sysMgr?.Resolution != null)
                _sysMgr.Resolution.PropertyChanged += OnResolutionChanged;
            if (_sysMgr?.RefreshRate != null)
                _sysMgr.RefreshRate.PropertyChanged += OnRefreshRateChanged;
            if (_sysMgr?.Resolutions != null)
                _sysMgr.Resolutions.PropertyChanged += OnResolutionsChanged;
            if (_sysMgr?.RefreshRates != null)
                _sysMgr.RefreshRates.PropertyChanged += OnRefreshRatesChanged;
            if (_sysMgr?.HDREnabled != null)
                _sysMgr.HDREnabled.PropertyChanged += OnHDRChanged;
            if (_sysMgr?.DisplayOrientation != null)
                _sysMgr.DisplayOrientation.PropertyChanged += OnDisplayOrientationChanged;

            // Profile
            if (_profileMgr?.CurrentProfile != null)
                _profileMgr.CurrentProfile.PropertyChanged += OnProfileChanged;
            if (_profileMgr?.PerGameProfile != null)
                _profileMgr.PerGameProfile.PropertyChanged += OnPerGameProfileChanged;

            // Running game
            if (_sysMgr?.RunningGame != null)
                _sysMgr.RunningGame.PropertyChanged += OnRunningGameChanged;

            // Legion properties
            if (_legionMgr?.LegionFanFullSpeed != null)
                _legionMgr.LegionFanFullSpeed.PropertyChanged += OnLegionFanFullSpeedChanged;
            if (_legionMgr?.LegionChargeLimit != null)
                _legionMgr.LegionChargeLimit.PropertyChanged += OnLegionChargeLimitChanged;
            if (_legionMgr?.LegionTouchpadEnabled != null)
                _legionMgr.LegionTouchpadEnabled.PropertyChanged += OnLegionTouchpadChanged;
            if (_legionMgr?.LegionPowerLight != null)
                _legionMgr.LegionPowerLight.PropertyChanged += OnLegionPowerLightChanged;
            if (_legionMgr?.LegionLightMode != null)
                _legionMgr.LegionLightMode.PropertyChanged += OnLegionLightModeChanged;
            if (_legionMgr?.LegionLightBrightness != null)
                _legionMgr.LegionLightBrightness.PropertyChanged += OnLegionLightBrightnessChanged;
            if (_legionMgr?.LegionLightSpeed != null)
                _legionMgr.LegionLightSpeed.PropertyChanged += OnLegionLightSpeedChanged;
            if (_legionMgr?.LegionVibration != null)
                _legionMgr.LegionVibration.PropertyChanged += OnLegionVibrationChanged;
            if (_legionMgr?.LegionVibrationMode != null)
                _legionMgr.LegionVibrationMode.PropertyChanged += OnLegionVibrationModeChanged;
        }

        #region Property Change Handlers

        private void DispatchIfVisible(Action action)
        {
            if (_window == null || _dispatcher == null) return;
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window.IsVisible)
                    action();
            }));
        }

        private void OnTDPChanged(object sender, PropertyChangedEventArgs e)
        {
            int value = _perfMgr?.TDP?.Value ?? 15;
            DispatchIfVisible(() =>
            {
                _window.PerformanceTab.UpdateTDP(value, (int)_window.PerformanceTab._tdpSlider.Minimum, (int)_window.PerformanceTab._tdpSlider.Maximum);
                _window.QuickTab.UpdateTDP(value, (int)_window.QuickTab._tdpSlider.Minimum, (int)_window.QuickTab._tdpSlider.Maximum);
            });
        }

        private void OnTDPBoostChanged(object sender, PropertyChangedEventArgs e)
        {
            bool enabled = _perfMgr?.TDPBoostEnabled?.Value ?? false;
            DispatchIfVisible(() => _window.PerformanceTab.UpdateTDPBoost(enabled));
        }

        private void OnAutoTDPChanged(object sender, PropertyChangedEventArgs e)
        {
            bool enabled = _autoTDPMgr?.Enabled?.Value ?? false;
            DispatchIfVisible(() =>
            {
                _window.PerformanceTab.UpdateAutoTDP(enabled);
                _window.QuickTab.UpdateAutoTDP(enabled);
            });
        }

        private void OnAutoTDPTargetFPSChanged(object sender, PropertyChangedEventArgs e)
        {
            int value = _autoTDPMgr?.TargetFPS?.Value ?? 60;
            DispatchIfVisible(() => _window.PerformanceTab.UpdateAutoTDPTargetFPS(value));
        }

        private void OnAutoTDPMinTDPChanged(object sender, PropertyChangedEventArgs e)
        {
            int value = _autoTDPMgr?.MinTDP?.Value ?? 4;
            DispatchIfVisible(() => _window.PerformanceTab.UpdateAutoTDPMinTDP(value));
        }

        private void OnAutoTDPMaxTDPChanged(object sender, PropertyChangedEventArgs e)
        {
            int value = _autoTDPMgr?.MaxTDP?.Value ?? 35;
            DispatchIfVisible(() => _window.PerformanceTab.UpdateAutoTDPMaxTDP(value));
        }

        private void OnAutoTDPControllerChanged(object sender, PropertyChangedEventArgs e)
        {
            int index = _autoTDPMgr?.ControllerType?.Value ?? 0;
            DispatchIfVisible(() => _window.PerformanceTab.UpdateAutoTDPController(index));
        }

        private void OnOverlayChanged(object sender, PropertyChangedEventArgs e)
        {
            int level = Program.onScreenDisplay?.Value ?? 0;
            DispatchIfVisible(() =>
            {
                _window.PerformanceTab.UpdatePerformanceOverlay(level);
                _window.QuickTab.UpdatePerformanceOverlay(level);
            });
        }

        private void OnCPUBoostChanged(object sender, PropertyChangedEventArgs e)
        {
            bool enabled = _powerMgr?.CPUBoost?.Value ?? false;
            DispatchIfVisible(() =>
            {
                _window.PerformanceTab.UpdateCPUBoost(enabled);
                _window.QuickTab.UpdateCPUBoost(enabled);
            });
        }

        private void OnCPUEPPChanged(object sender, PropertyChangedEventArgs e)
        {
            int value = _powerMgr?.CPUEPP?.Value ?? 50;
            DispatchIfVisible(() => _window.PerformanceTab.UpdateCPUEPP(value));
        }

        private void OnOSPowerModeChanged(object sender, PropertyChangedEventArgs e)
        {
            int index = _powerMgr?.OSPowerMode?.Value ?? 1;
            DispatchIfVisible(() => _window.PerformanceTab.UpdateOSPowerMode(index));
        }

        private void OnFPSLimitChanged(object sender, PropertyChangedEventArgs e)
        {
            int value = _rtssMgr?.FPSLimit?.Value ?? 0;
            DispatchIfVisible(() => _window.PerformanceTab.UpdateFPSLimit(value));
        }

        private void OnPerfModeChanged(object sender, PropertyChangedEventArgs e)
        {
            int mode = _legionMgr?.LegionPerformanceMode?.Value ?? 2;
            DispatchIfVisible(() =>
            {
                _window.PerformanceTab.UpdatePerformanceMode(mode);
                _window.QuickTab.UpdatePerformanceMode(mode);
            });
        }

        private void OnCtrlEmuChanged(object sender, PropertyChangedEventArgs e)
        {
            bool enabled = _ctrlEmuMgr?.ControllerEmulationEnabled?.Value ?? false;
            DispatchIfVisible(() =>
            {
                _window.PerformanceTab.UpdateControllerEmulation(enabled);
                _window.QuickTab.UpdateControllerEmulation(enabled);
            });
        }

        private void OnResolutionChanged(object sender, PropertyChangedEventArgs e)
        {
            string res = _sysMgr?.Resolution?.Value ?? "";
            DispatchIfVisible(() => _window.DisplayTab.UpdateResolution(res));
        }

        private void OnRefreshRateChanged(object sender, PropertyChangedEventArgs e)
        {
            int rate = _sysMgr?.RefreshRate?.Value ?? 60;
            DispatchIfVisible(() => _window.DisplayTab.UpdateRefreshRate(rate));
        }

        private void OnResolutionsChanged(object sender, PropertyChangedEventArgs e)
        {
            var resolutions = _sysMgr?.Resolutions?.Value ?? new List<string>();
            string current = _sysMgr?.Resolution?.Value ?? "";
            DispatchIfVisible(() => _window.DisplayTab.UpdateResolutions(resolutions, current));
        }

        private void OnRefreshRatesChanged(object sender, PropertyChangedEventArgs e)
        {
            var rates = _sysMgr?.RefreshRates?.Value ?? new List<int>();
            int current = _sysMgr?.RefreshRate?.Value ?? 60;
            DispatchIfVisible(() => _window.DisplayTab.UpdateRefreshRates(rates, current));
        }

        private void OnHDRChanged(object sender, PropertyChangedEventArgs e)
        {
            bool enabled = _sysMgr?.HDREnabled?.Value ?? false;
            bool supported = _sysMgr?.HDRSupported?.Value ?? false;
            DispatchIfVisible(() => _window.DisplayTab.UpdateHDR(enabled, supported));
        }

        private void OnDisplayOrientationChanged(object sender, PropertyChangedEventArgs e)
        {
            int orient = _sysMgr?.DisplayOrientation?.Value ?? 0;
            DispatchIfVisible(() => _window.DisplayTab.UpdateOrientation(orient));
        }

        private void OnProfileChanged(object sender, PropertyChangedEventArgs e)
        {
            string name = _profileMgr?.CurrentProfile?.GameId.Name ?? "Global";
            DispatchIfVisible(() =>
            {
                _window.UpdateProfile(name);
                _window.ProfilesTab.UpdateCurrentProfile(name);
                _window.ProfilesTab.UpdateSavedProfiles(_profileMgr?.GameProfiles);
            });
        }

        private void OnPerGameProfileChanged(object sender, PropertyChangedEventArgs e)
        {
            bool enabled = _profileMgr?.PerGameProfile?.Value ?? false;
            DispatchIfVisible(() => _window.ProfilesTab.UpdatePerGameProfile(enabled));
        }

        private void OnRunningGameChanged(object sender, PropertyChangedEventArgs e)
        {
            string gameName = _sysMgr?.RunningGame?.Value.GameId.Name;
            DispatchIfVisible(() => _window.ProfilesTab.UpdateDetectedGame(gameName));
        }

        // Legion property change handlers
        private void OnLegionFanFullSpeedChanged(object sender, PropertyChangedEventArgs e)
        {
            bool enabled = _legionMgr?.LegionFanFullSpeed?.Value ?? false;
            DispatchIfVisible(() =>
            {
                _window.LegionTab.UpdateFanFullSpeed(enabled);
                _window.QuickTab.UpdateFanFullSpeed(enabled);
            });
        }

        private void OnLegionChargeLimitChanged(object sender, PropertyChangedEventArgs e)
        {
            bool enabled = _legionMgr?.LegionChargeLimit?.Value ?? false;
            DispatchIfVisible(() => _window.LegionTab.UpdateChargeLimit(enabled));
        }

        private void OnLegionTouchpadChanged(object sender, PropertyChangedEventArgs e)
        {
            bool enabled = _legionMgr?.LegionTouchpadEnabled?.Value ?? false;
            DispatchIfVisible(() => _window.LegionTab.UpdateTouchpad(enabled));
        }

        private void OnLegionPowerLightChanged(object sender, PropertyChangedEventArgs e)
        {
            bool enabled = _legionMgr?.LegionPowerLight?.Value ?? false;
            DispatchIfVisible(() => _window.LegionTab.UpdatePowerLight(enabled));
        }

        private void OnLegionLightModeChanged(object sender, PropertyChangedEventArgs e)
        {
            int mode = _legionMgr?.LegionLightMode?.Value ?? 0;
            DispatchIfVisible(() => _window.LegionTab.UpdateLightMode(mode));
        }

        private void OnLegionLightBrightnessChanged(object sender, PropertyChangedEventArgs e)
        {
            int value = _legionMgr?.LegionLightBrightness?.Value ?? 0;
            DispatchIfVisible(() => _window.LegionTab.UpdateLightBrightness(value));
        }

        private void OnLegionLightSpeedChanged(object sender, PropertyChangedEventArgs e)
        {
            int value = _legionMgr?.LegionLightSpeed?.Value ?? 50;
            DispatchIfVisible(() => _window.LegionTab.UpdateLightSpeed(value));
        }

        private void OnLegionVibrationChanged(object sender, PropertyChangedEventArgs e)
        {
            int value = _legionMgr?.LegionVibration?.Value ?? 2;
            DispatchIfVisible(() => _window.LegionTab.UpdateVibrationLevel(value));
        }

        private void OnLegionVibrationModeChanged(object sender, PropertyChangedEventArgs e)
        {
            int value = _legionMgr?.LegionVibrationMode?.Value ?? 1;
            DispatchIfVisible(() => _window.LegionTab.UpdateVibrationMode(value));
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _metricsTimer?.Stop();
                _metricsTimer?.Dispose();
            }
            catch { }

            try
            {
                _inputHandler?.Dispose();
            }
            catch { }

            try
            {
                _audioMgr?.Dispose();
            }
            catch { }

            try
            {
                _dispatcher?.InvokeShutdown();
            }
            catch { }

            try
            {
                _wpfThread?.Join(2000);
            }
            catch { }

            _startupSignal.Dispose();
            Logger.Info("Sidebar: Disposed");
        }
    }
}
