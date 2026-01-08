using System;
using System.ComponentModel;
using System.Threading.Tasks;
using NLog;
using Shared.Data;
using Shared.Utilities;
using Windows.ApplicationModel.AppService;
using Windows.System.Power;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Legion;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.Profile;
using XboxGamingBarHelper.RTSS;
using XboxGamingBarHelper.Systems;

namespace XboxGamingBarHelper.DefaultGameProfiles
{
    /// <summary>
    /// Manager for Default Game Profile feature.
    /// Handles profile lookup, property communication, and TDP/FPS application.
    /// </summary>
    internal class DefaultGameProfileManager : Manager
    {
        private readonly DefaultGameProfileService _service;
        private readonly PerformanceManager _performanceManager;
        private readonly RTSSManager _rtssManager;
        private readonly SystemManager _systemManager;
        private readonly ProfileManager _profileManager;
        private readonly LegionManager _legionManager;

        // Properties exposed to widget
        public DefaultGameProfileAvailableProperty ProfileAvailable { get; }
        public DefaultGameProfileDataProperty ProfileData { get; }
        public DefaultGameProfileEnabledProperty ProfileEnabled { get; }

        // Current state
        private DefaultGameProfile? _currentProfile;
        private string _currentGamePath;
        private bool _isApplied;

        // Saved state before applying default profile (for restoration)
        private int? _savedTdpMode;
        private int? _savedTdpValue;
        private int? _savedFpsLimit;

        /// <summary>
        /// Whether the default profile feature is available (has valid hardware detection).
        /// </summary>
        public bool IsFeatureAvailable => _service.HardwareVariant != LegionGoVariant.Unknown;

        public DefaultGameProfileManager(
            AppServiceConnection connection,
            PerformanceManager performanceManager,
            RTSSManager rtssManager,
            SystemManager systemManager,
            ProfileManager profileManager,
            LegionManager legionManager) : base(connection)
        {
            _performanceManager = performanceManager;
            _rtssManager = rtssManager;
            _systemManager = systemManager;
            _profileManager = profileManager;
            _legionManager = legionManager;

            // Initialize service (loads profiles from registry)
            _service = new DefaultGameProfileService();

            Logger.Info($"DefaultGameProfileManager: Service initialized with {_service.ProfileCount} profiles");
            Logger.Info($"DefaultGameProfileManager: Hardware variant = {_service.HardwareVariant}, key = {_service.PrimaryProfileKey ?? "none"}");

            // Create properties
            ProfileAvailable = new DefaultGameProfileAvailableProperty(this);
            ProfileData = new DefaultGameProfileDataProperty(this);
            ProfileEnabled = new DefaultGameProfileEnabledProperty(this);

            // Subscribe to running game changes
            if (_systemManager?.RunningGame != null)
            {
                _systemManager.RunningGame.PropertyChanged += OnRunningGameChanged;
            }

            // Subscribe to profile enabled changes from widget
            ProfileEnabled.PropertyChanged += OnProfileEnabledChanged;

            // Subscribe to power state changes
            PowerManager.BatteryStatusChanged += OnBatteryStatusChanged;
            PowerManager.PowerSupplyStatusChanged += OnPowerSupplyStatusChanged;
        }

        /// <summary>
        /// Called when the running game changes.
        /// </summary>
        private void OnRunningGameChanged(object sender, PropertyChangedEventArgs e)
        {
            var runningGame = _systemManager.RunningGame.Value;

            if (!runningGame.IsValid())
            {
                // No game running - clear state
                ClearCurrentProfile();
                return;
            }

            var gamePath = runningGame.GameId.Path;
            if (gamePath == _currentGamePath)
            {
                // Same game, no change needed
                return;
            }

            _currentGamePath = gamePath;

            // Get AUMID from TrackedGame for Xbox/MSIXVC game matching
            string aumId = null;
            if (_systemManager.TrackedGame != null && _systemManager.TrackedGame.IsValid())
            {
                aumId = _systemManager.TrackedGame.AumId;
            }

            Logger.Info($"DefaultGameProfileManager: Game changed to {runningGame.GameId.Name} ({gamePath}){(aumId != null ? $" [AUMID: {aumId}]" : "")}");

            // Look up profile for this game (by exe path or AUMID)
            if (_service.TryGetProfile(gamePath, out var profile, aumId))
            {
                _currentProfile = profile;
                Logger.Info($"DefaultGameProfileManager: Found profile for {profile.GameName}: {profile.TDP}W, {profile.FrameCap}fps");

                // Update properties
                ProfileAvailable.SetValue(true);
                ProfileData.SetValue(XmlHelper.ToXMLString(profile, true));

                // Determine if should auto-enable
                var isOnBattery = IsOnBattery();
                var userPref = GetUserPreference();
                var shouldEnable = _service.ShouldAutoEnable(userPref, isOnBattery);

                Logger.Info($"DefaultGameProfileManager: Auto-enable decision: isOnBattery={isOnBattery}, userPref={userPref}, shouldEnable={shouldEnable}");

                // Use ForceSetValue to ensure the value is always sent to widget, even if unchanged
                ProfileEnabled.ForceSetValue(shouldEnable);

                if (shouldEnable)
                {
                    ApplyProfile(profile);
                }
            }
            else
            {
                Logger.Debug($"DefaultGameProfileManager: No profile found for {runningGame.GameId.Name}");
                ClearCurrentProfile();
            }
        }

        /// <summary>
        /// Called when ProfileEnabled changes (user toggled in widget).
        /// </summary>
        private void OnProfileEnabledChanged(object sender, PropertyChangedEventArgs e)
        {
            var enabled = ProfileEnabled.Value;
            Logger.Info($"DefaultGameProfileManager: Profile enabled changed to {enabled}");

            if (!_currentProfile.HasValue)
            {
                return;
            }

            // Save user preference
            SaveUserPreference(enabled);

            if (enabled)
            {
                ApplyProfile(_currentProfile.Value);
            }
            else
            {
                UnapplyProfile();
            }
        }

        /// <summary>
        /// Called when battery status changes.
        /// </summary>
        private void OnBatteryStatusChanged(object sender, object e)
        {
            HandlePowerStateChange();
        }

        /// <summary>
        /// Called when power supply status changes.
        /// </summary>
        private void OnPowerSupplyStatusChanged(object sender, object e)
        {
            HandlePowerStateChange();
        }

        /// <summary>
        /// Handles power state changes for auto-enable logic.
        /// </summary>
        private void HandlePowerStateChange()
        {
            if (!_currentProfile.HasValue)
            {
                return;
            }

            var isOnBattery = IsOnBattery();
            var userPref = GetUserPreference();

            // Use saved preference if available, otherwise use default auto-enable logic
            var shouldEnable = _service.ShouldAutoEnable(userPref, isOnBattery);

            Logger.Info($"DefaultGameProfileManager: Power state changed, isOnBattery={isOnBattery}, userPref={userPref}, shouldEnable={shouldEnable}");

            // Use ForceSetValue to ensure widget is updated even if value hasn't changed
            ProfileEnabled.ForceSetValue(shouldEnable);

            if (shouldEnable && !_isApplied)
            {
                ApplyProfile(_currentProfile.Value);
            }
            else if (!shouldEnable && _isApplied)
            {
                // Skip TDP restoration - widget will handle profile loading for new power state
                UnapplyProfile(skipTdpRestore: true);
            }
        }

        /// <summary>
        /// Checks if device is currently on battery power.
        /// </summary>
        private bool IsOnBattery()
        {
            try
            {
                var batteryStatus = PowerManager.BatteryStatus;
                var powerSupply = PowerManager.PowerSupplyStatus;

                // On battery if discharging OR power supply not present
                return batteryStatus == BatteryStatus.Discharging ||
                       powerSupply == PowerSupplyStatus.NotPresent;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to check power status: {ex.Message}");
                return false; // Assume AC power if we can't detect
            }
        }

        /// <summary>
        /// Gets user's preference for current game and current power state.
        /// Returns preference for AC or DC based on current power state.
        /// </summary>
        private bool? GetUserPreference()
        {
            if (string.IsNullOrEmpty(_currentGamePath))
            {
                return null;
            }

            try
            {
                // Get the game profile from ProfileManager
                var gameProfile = _profileManager?.GetProfile(_currentGamePath);
                if (!gameProfile.HasValue || !gameProfile.Value.IsValid())
                {
                    Logger.Debug("No game profile found for DGP preference lookup");
                    return null;
                }

                var isOnBattery = IsOnBattery();
                var powerState = isOnBattery ? "DC" : "AC";
                var preference = isOnBattery ? gameProfile.Value.DgpEnabledOnDC : gameProfile.Value.DgpEnabledOnAC;

                if (preference.HasValue)
                {
                    Logger.Debug($"Found DGP preference for {powerState}: {preference.Value}");
                    return preference.Value;
                }

                Logger.Debug($"No DGP preference found for {powerState}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to get user preference: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Saves user's preference for current game and current power state.
        /// Stores in GameProfile's DgpEnabledOnAC or DgpEnabledOnDC field.
        /// </summary>
        private void SaveUserPreference(bool enabled)
        {
            if (string.IsNullOrEmpty(_currentGamePath))
            {
                return;
            }

            try
            {
                var isOnBattery = IsOnBattery();
                var powerState = isOnBattery ? "DC" : "AC";

                // Update the game profile's DGP preference
                _profileManager?.UpdateDgpPreference(_currentGamePath, isOnBattery, enabled);

                var gameName = _systemManager.RunningGame?.Value.GameId.Name ?? "Unknown";
                Logger.Info($"Saved DGP preference={enabled} for {gameName} on {powerState}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to save user preference: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the default profile's TDP and FPS settings.
        /// </summary>
        private async void ApplyProfile(DefaultGameProfile profile)
        {
            if (_isApplied)
            {
                Logger.Debug("Profile already applied, skipping");
                return;
            }

            // Set flag immediately to prevent race conditions with async operations
            _isApplied = true;

            Logger.Info($"Applying default profile for {profile.GameName}: TDP={profile.TDP}W, FPS={profile.FrameCap}");

            try
            {
                // Save current state before applying default profile
                SaveCurrentState();

                // On Legion devices, switch to Custom TDP mode (255) to allow direct TDP control
                if (_legionManager != null && _legionManager.LegionGoDetected.Value)
                {
                    Logger.Info("Switching Legion to Custom TDP mode for default profile");
                    _legionManager.LegionPerformanceMode.SetValue(255);

                    // Wait for mode change to propagate before setting TDP
                    // This ensures any mode-change handlers complete first
                    await Task.Delay(200);
                }

                // Apply TDP (set twice to ensure it takes effect after any mode-change handlers)
                if (profile.TDP > 0 && _performanceManager?.TDP != null)
                {
                    Logger.Info($"Setting TDP to {profile.TDP}W");
                    _performanceManager.TDP.SetValue(profile.TDP);

                    // Set again after a short delay to override any competing updates
                    await Task.Delay(100);
                    _performanceManager.TDP.SetValue(profile.TDP);
                    Logger.Info($"TDP confirmed at {profile.TDP}W");
                }

                // Apply FPS limit via RTSS
                if (profile.FrameCap.HasValue && profile.FrameCap.Value > 0)
                {
                    Logger.Info($"Setting FPS limit to {profile.FrameCap.Value}");
                    RTSSFPSLimiter.SetFPSLimit(profile.FrameCap.Value);
                }

                Logger.Info($"Default profile applied successfully for {profile.GameName}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to apply default profile: {ex.Message}");
                _isApplied = false; // Reset on failure so it can be retried
            }
        }

        /// <summary>
        /// Saves the current TDP mode, TDP value, and FPS limit before applying default profile.
        /// </summary>
        private void SaveCurrentState()
        {
            try
            {
                // Save TDP mode (Legion only)
                if (_legionManager != null && _legionManager.LegionGoDetected.Value)
                {
                    _savedTdpMode = _legionManager.LegionPerformanceMode.Value;
                    Logger.Info($"Saved TDP mode: {_savedTdpMode}");
                }

                // Save TDP value
                if (_performanceManager?.TDP != null)
                {
                    _savedTdpValue = _performanceManager.TDP.Value;
                    Logger.Info($"Saved TDP value: {_savedTdpValue}W");
                }

                // Save FPS limit
                _savedFpsLimit = _rtssManager?.FPSLimit?.Value ?? 0;
                Logger.Info($"Saved FPS limit: {_savedFpsLimit}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to save current state: {ex.Message}");
            }
        }

        /// <summary>
        /// Unapplies the default profile, restoring user's manual settings.
        /// </summary>
        /// <param name="skipTdpRestore">If true, skip TDP mode/value restoration (used when power state changed)</param>
        private void UnapplyProfile(bool skipTdpRestore = false)
        {
            if (!_isApplied)
            {
                Logger.Debug("Profile not applied, nothing to unapply");
                return;
            }

            Logger.Info($"Unapplying default profile, restoring saved settings (skipTdpRestore={skipTdpRestore})");

            try
            {
                // Skip TDP restoration when power state changed - widget will handle profile loading
                if (skipTdpRestore)
                {
                    Logger.Info("Skipping TDP restoration - power state changed, widget will handle profile loading");
                }
                else
                {
                    // Restore saved TDP mode first (Legion only)
                    if (_savedTdpMode.HasValue && _legionManager != null && _legionManager.LegionGoDetected.Value)
                    {
                        Logger.Info($"Restoring TDP mode: {_savedTdpMode.Value}");
                        _legionManager.LegionPerformanceMode.SetValue(_savedTdpMode.Value);
                    }

                    // Restore saved TDP value (only if we were in Custom mode, otherwise the mode handles TDP)
                    if (_savedTdpValue.HasValue && _performanceManager?.TDP != null)
                    {
                        // Only restore TDP if mode is Custom (255), otherwise the mode preset handles it
                        if (_savedTdpMode.HasValue && _savedTdpMode.Value == 255)
                        {
                            Logger.Info($"Restoring TDP value: {_savedTdpValue.Value}W");
                            _performanceManager.TDP.SetValue(_savedTdpValue.Value);
                        }
                        else
                        {
                            Logger.Info($"Skipping TDP value restore - mode {_savedTdpMode} will set its own TDP");
                        }
                    }
                }

                // Restore FPS limit
                if (_savedFpsLimit.HasValue)
                {
                    Logger.Info($"Restoring FPS limit: {_savedFpsLimit.Value}");
                    RTSSFPSLimiter.SetFPSLimit(_savedFpsLimit.Value);
                }
                else
                {
                    // Clear FPS limit if none was saved
                    RTSSFPSLimiter.SetFPSLimit(0);
                }

                _isApplied = false;

                // Clear saved state
                _savedTdpMode = null;
                _savedTdpValue = null;
                _savedFpsLimit = null;

                Logger.Info("Default profile unapplied successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to unapply default profile: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears current profile state (when no game or no profile found).
        /// </summary>
        private void ClearCurrentProfile()
        {
            if (_isApplied)
            {
                UnapplyProfile();
            }

            _currentProfile = null;
            _currentGamePath = null;
            _isApplied = false;

            ProfileAvailable.SetValue(false);
            ProfileData.SetValue("");
            ProfileEnabled.SetValue(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe from events
                if (_systemManager?.RunningGame != null)
                {
                    _systemManager.RunningGame.PropertyChanged -= OnRunningGameChanged;
                }
                ProfileEnabled.PropertyChanged -= OnProfileEnabledChanged;
                PowerManager.BatteryStatusChanged -= OnBatteryStatusChanged;
                PowerManager.PowerSupplyStatusChanged -= OnPowerSupplyStatusChanged;
            }

            base.Dispose(disposing);
        }
    }
}
