using NLog;
using RTSSSharedMemoryNET;
using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.RTSS;
using XboxGamingBarHelper.Systems;

namespace XboxGamingBarHelper.AutoTDP
{
    internal class AutoTDPManager : Manager
    {
        private readonly PerformanceManager performanceManager;
        private readonly SystemManager systemManager;
        private RTSSManager rtssManager;  // For frametime stability detection
        private LearnedGameTDPStore learnedTDPStore;  // Per-game learned TDP storage

        // Target FPS change tracking - reset state when target changes
        private int lastTargetFPS = 0;
        private DateTime lastTargetChangeTime = DateTime.MinValue;
        private const double TargetChangeSettleTimeMs = 2000;  // Ignore spike detection for 2 seconds after target change

        // Current game tracking for per-game TDP learning
        private string currentGamePath = null;
        private string currentGameName = null;
        private bool hasAppliedLearnedTDP = false;  // True if we've applied learned TDP for current game
        private int learnedTDPApplied = 0;  // The learned TDP we applied (for reliability tracking)
        private DateTime gameStartTime = DateTime.MinValue;  // When current game was detected
        private int stableObservations = 0;  // Count of stable observations for learning
        private const int MinStableObservationsToLearn = 10;  // Need this many stable readings to learn
        private const float MaxFrametimeVarianceForLearning = 2.0f;  // Must have low variance to learn

        // Properties
        private readonly AutoTDPEnabledProperty enabled;
        public AutoTDPEnabledProperty Enabled => enabled;

        private readonly AutoTDPTargetFPSProperty targetFPS;
        public AutoTDPTargetFPSProperty TargetFPS => targetFPS;

        private readonly AutoTDPCurrentFPSProperty currentFPS;
        public AutoTDPCurrentFPSProperty CurrentFPS => currentFPS;

        private readonly AutoTDPMinTDPProperty minTDPProperty;
        public AutoTDPMinTDPProperty MinTDP => minTDPProperty;

        private readonly AutoTDPMaxTDPProperty maxTDPProperty;
        public AutoTDPMaxTDPProperty MaxTDP => maxTDPProperty;

        private readonly TDPLimitsProperty tdpLimits;
        public TDPLimitsProperty TDPLimits => tdpLimits;

        // ML Mode properties (AutoTDPUseMLMode is deprecated, use AutoTDPControllerType)
        private readonly AutoTDPUseMLModeProperty useMLMode;
        public AutoTDPUseMLModeProperty UseMLMode => useMLMode;

        private readonly AutoTDPMLStatusProperty mlStatus;
        public AutoTDPMLStatusProperty MLStatus => mlStatus;

        private readonly AutoTDPResetMLProperty resetML;
        public AutoTDPResetMLProperty ResetML => resetML;

        private readonly AutoTDPLearnedGameDataProperty learnedGameData;
        public AutoTDPLearnedGameDataProperty LearnedGameData => learnedGameData;

        // Controller type (0=PID, 1=Q-Learning, 2=SARSA)
        private readonly AutoTDPControllerTypeProperty controllerType;
        public AutoTDPControllerTypeProperty ControllerType => controllerType;

        private readonly AutoTDPPauseWhenUnfocusedProperty pauseWhenUnfocused;
        public AutoTDPPauseWhenUnfocusedProperty PauseWhenUnfocused => pauseWhenUnfocused;

        // ML controllers for ML mode
        private QLearningController qLearningController;
        private SARSAController sarsaController;
        private bool isMLFallbackActive = false;  // True when ML has fallen back to PID

        // Controller state
        private double integral = 0;
        private double previousError = 0;
        private DateTime lastUpdateTime = DateTime.MinValue;
        private int lastAppliedTDP = 15;
        private int consecutiveStableReadings = 0;
        private bool wasActivelyManaging = false; // Track if we were actively managing TDP

        // PID Tuning - conservative increase, aggressive decrease
        private const double Kp = 0.15;   // Lower proportional gain for gentler response
        private const double Ki = 0.02;   // Lower integral to avoid overshoot
        private const double Kd = 0.08;   // Good derivative for stability

        // Asymmetric response - slow to increase TDP, quick to decrease
        private const double IncreaseMultiplier = 0.5;  // Very conservative increase
        private const double DecreaseMultiplier = 1.0;  // Aggressive decrease to find minimum TDP

        // TDP limits (configurable from widget)
        private int minTDP = 4;
        private int maxTDP = 35;

        // Update intervals - sample FPS frequently but make TDP decisions less often
        private const double SampleIntervalMs = 250;   // 250ms FPS sampling for smoother data
        private const double DecisionIntervalMs = 500; // 500ms TDP decisions to respect hardware response time
        private const int SamplesPerDecision = 2;      // DecisionIntervalMs / SampleIntervalMs
        private int samplesSinceLastDecision = 0;      // Counter for samples since last TDP decision

        // Smoothing - 10 samples at 250ms = 2.5 seconds of history
        private double[] fpsHistory = new double[10];
        private int fpsHistoryIndex = 0;
        private int fpsHistoryCount = 0;

        // Trend detection - 5 deltas for better trend accuracy
        private double[] fpsDeltas = new double[5];
        private int fpsDeltaIndex = 0;
        private int fpsDeltaCount = 0;
        private double lastSmoothedFPS = 0;
        private double lastInstantFPS = 0;  // Track instantaneous FPS for spike detection

        // Hysteresis - reduced requirements for decreasing TDP
        private const int StableReadingsRequired = 2;
        private const double DeadZone = 2.0;  // Tighter dead zone
        private const double UpperDeadZone = 3.0;  // Reduced threshold before decreasing TDP

        // FPS cap detection - probe for power savings when at target
        private const int StableAtTargetRequired = 6;  // Faster probing (was 10)
        private int consecutiveAtTarget = 0;
        private int lastProbeTDP = 0;  // TDP we dropped to during probing

        // Emergency spike recovery - track safe TDP for quick recovery on large FPS drops
        private int safeTDP = 0;                    // Last known stable TDP before probing/reduction started
        private int totalProbeReduction = 0;        // Total watts reduced from safeTDP
        private const double SpikeThreshold = 12.0; // FPS drop threshold for emergency recovery (12+ FPS)
        private const double SevereSpike = 15.0;    // Severe spike threshold (15+ FPS) - jump to safeTDP + margin
        private bool spikeRecoveryActive = false;   // True when recovering from a spike

        // Sweet spot detection - track TDP history to find optimal value
        private const int TDPHistorySize = 20;  // Track last 20 TDP readings
        private int[] tdpHistory = new int[TDPHistorySize];
        private int tdpHistoryIndex = 0;
        private int tdpHistoryCount = 0;
        private int sweetSpotTDP = 0;  // Detected optimal TDP
        private int sweetSpotConfidence = 0;  // How confident we are (0-100)
        private const int SweetSpotThreshold = 60;  // Confidence needed to use sweet spot

        // Ceiling detection - handles unreachable FPS targets
        private double achievableFPS = 0;          // EWMA estimate of max achievable FPS
        private double lockedAchievableFPS = 0;    // Locked ceiling FPS (doesn't drift down)
        private int effectiveTarget = 60;          // Adjusted target (min of user target and achievable)
        private bool isCeilingDetected = false;    // True when target is unreachable
        private int consecutiveCeilingFrames = 0;  // Counter for steady-state below target
        private int consecutiveAboveCeilingFrames = 0;  // Counter for hysteresis when exiting ceiling mode
        private const int CeilingDetectionThreshold = 12;  // Frames at ceiling before detection (stricter: 12 frames at max TDP)
        private const int CeilingExitHysteresis = 3;      // Frames above target before exiting ceiling mode (faster exit)
        private const double CeilingGpuUtilThreshold = 95.0;  // GPU util below this at max TDP = CPU bound
        private const double AchievableFPSAlpha = 0.15;  // EWMA smoothing factor for achievable FPS
        private const int CeilingMargin = 3;  // FPS margin below achievable for effective target

        // Diminishing returns tracking
        private double[] tdpIncreaseFPSGains = new double[4];  // Track FPS gain from last 4 TDP increases
        private int tdpGainIndex = 0;
        private int tdpGainCount = 0;
        private double lastFPSBeforeTDPIncrease = 0;  // FPS before most recent TDP increase
        private int lastTDPBeforeIncrease = 0;  // TDP before most recent increase
        private bool pendingTDPGainMeasurement = false;  // True when waiting to measure FPS gain

        // Power optimization mode - minimize TDP while maintaining FPS when ceiling hit
        private bool isPowerOptimizationMode = false;
        private int optimalCeilingTDP = 0;  // TDP that achieves max FPS with minimal power

        // OSD Status
        public string StatusText { get; private set; } = "";
        public string TrendText { get; private set; } = "";
        public int CurrentTDPValue { get; private set; } = 0;
        public int NewTDPValue { get; private set; } = 0;
        public bool IsProbing { get; private set; } = false;  // Are we currently probing lower TDP?
        public int SweetSpotTDP => sweetSpotTDP;
        public int SweetSpotConfidence => sweetSpotConfidence;
        public bool IsCeilingDetected => isCeilingDetected;
        public int EffectiveTarget => effectiveTarget;
        public double AchievableFPS => achievableFPS;
        public bool IsPowerOptimizationMode => isPowerOptimizationMode;

        // ML Mode OSD Status
        public bool IsMLModeActive => controllerType.Value > 0 && GetActiveMLController() != null && !isMLFallbackActive;
        public double LastMLReward { get; private set; } = 0;
        public long MLUpdateCount => controllerType.Value == 2 ? (sarsaController?.TotalUpdates ?? 0) : (qLearningController?.TotalUpdates ?? 0);
        public int MLExplorationPercent => controllerType.Value == 2
            ? (sarsaController != null ? (int)(sarsaController.ExplorationRate * 100) : 0)
            : (qLearningController != null ? (int)(qLearningController.ExplorationRate * 100) : 0);
        public double MLCumulativeReward => controllerType.Value == 2 ? (sarsaController?.CumulativeReward ?? 0) : (qLearningController?.CumulativeReward ?? 0);
        public double MLAverageReward => controllerType.Value == 2 ? (sarsaController?.AverageRecentReward ?? 0) : (qLearningController?.AverageRecentReward ?? 0);

        /// <summary>
        /// Gets the currently active ML controller based on controller type.
        /// Returns null for PID mode (type 0).
        /// </summary>
        private object GetActiveMLController()
        {
            return controllerType.Value switch
            {
                1 => qLearningController,
                2 => sarsaController,
                _ => null
            };
        }

        /// <summary>
        /// Gets the average FPS gained per watt from recent TDP increases.
        /// Returns -1 if no data available yet.
        /// </summary>
        public double GetAverageFpsPerWatt()
        {
            if (tdpGainCount == 0) return -1;
            double sum = 0;
            for (int i = 0; i < tdpGainCount; i++)
                sum += tdpIncreaseFPSGains[i];
            return sum / tdpGainCount;
        }

        public AutoTDPManager(PerformanceManager performanceManager, SystemManager systemManager) : base()
        {
            this.performanceManager = performanceManager;
            this.systemManager = systemManager;

            enabled = new AutoTDPEnabledProperty(false, this);
            targetFPS = new AutoTDPTargetFPSProperty(60, this);
            currentFPS = new AutoTDPCurrentFPSProperty(0, this);
            minTDPProperty = new AutoTDPMinTDPProperty(8, this);  // Default 8W
            maxTDPProperty = new AutoTDPMaxTDPProperty(30, this); // Default 30W
            tdpLimits = new TDPLimitsProperty(this);

            // ML Mode properties
            useMLMode = new AutoTDPUseMLModeProperty(false, this);
            mlStatus = new AutoTDPMLStatusProperty("", this);
            resetML = new AutoTDPResetMLProperty(false, this);
            learnedGameData = new AutoTDPLearnedGameDataProperty("", this);

            // Controller type (0=PID, 1=Q-Learning, 2=SARSA)
            controllerType = new AutoTDPControllerTypeProperty(0, this);

            // Pause when unfocused (default: true)
            pauseWhenUnfocused = new AutoTDPPauseWhenUnfocusedProperty(true, this);

            // Initialize ML controllers
            InitializeMLControllers();

            // Initialize per-game learned TDP store
            InitializeLearnedTDPStore();

            // Initialize limits from properties
            minTDP = minTDPProperty.Value;
            maxTDP = maxTDPProperty.Value;

            // Initialize target FPS tracking
            lastTargetFPS = targetFPS.Value;

            Logger.Info("AutoTDPManager initialized with conservative tuning and ML support (Q-Learning + SARSA)");
        }

        /// <summary>
        /// Sets the RTSSManager reference for frametime stability detection.
        /// </summary>
        public void SetRTSSManager(RTSSManager manager)
        {
            rtssManager = manager;
            Logger.Info("AutoTDPManager: RTSSManager reference set for frametime stability detection");
        }

        /// <summary>
        /// Gets the current frametime variance (max - min) in milliseconds.
        /// Returns 0 if RTSSManager is not available.
        /// </summary>
        private float GetFrametimeVariance()
        {
            return rtssManager?.FrametimeVariance ?? 0f;
        }

        /// <summary>
        /// Gets the current average frametime in milliseconds.
        /// Returns 0 if RTSSManager is not available.
        /// </summary>
        private float GetFrametimeAvg()
        {
            return rtssManager?.FrametimeAvg ?? 0f;
        }

        private void InitializeMLControllers()
        {
            try
            {
                // Get the LocalState path for storing Q-tables
                string localStatePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages",
                    "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg",
                    "LocalState"
                );

                qLearningController = new QLearningController(localStatePath);
                Logger.Info("Q-Learning controller initialized");

                sarsaController = new SARSAController(localStatePath);
                Logger.Info("SARSA controller initialized");

                UpdateMLStatusProperty();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize ML controllers: {ex.Message}");
                qLearningController = null;
                sarsaController = null;
            }
        }

        private void InitializeLearnedTDPStore()
        {
            try
            {
                string localStatePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages",
                    "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg",
                    "LocalState"
                );
                learnedTDPStore = new LearnedGameTDPStore(localStatePath);
                Logger.Info("LearnedGameTDPStore initialized");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize LearnedGameTDPStore: {ex.Message}");
                learnedTDPStore = null;
            }
        }

        private void UpdateMLStatusProperty()
        {
            if (mlStatus == null) return;

            string statusString;
            switch (controllerType.Value)
            {
                case 1:
                    statusString = qLearningController != null
                        ? $"Q-Learning: {qLearningController.GetStatusString()}"
                        : "Q-Learning: Not initialized";
                    break;
                case 2:
                    statusString = sarsaController != null
                        ? $"SARSA: {sarsaController.GetStatusString()}"
                        : "SARSA: Not initialized";
                    break;
                default:
                    statusString = "PID Controller";
                    break;
            }
            mlStatus.SetValue(statusString);
        }

        internal void UpdateLearnedGameDataProperty()
        {
            if (learnedGameData == null)
                return;

            if (learnedTDPStore == null || string.IsNullOrEmpty(currentGamePath))
            {
                learnedGameData.SetValue("");
                return;
            }

            var json = learnedTDPStore.GetGameDataJson(currentGamePath, currentGameName, targetFPS?.Value ?? 0);
            learnedGameData.SetValue(json);
        }

        internal void FlushLearnedTDPStore()
        {
            if (learnedTDPStore != null)
            {
                Logger.Info("AutoTDPManager: Flushing learned TDP data");
                learnedTDPStore.Save();
            }
        }

        /// <summary>
        /// Resets the ML learning data for the currently active controller (called when user clicks Reset button).
        /// </summary>
        public void ResetMLLearning()
        {
            switch (controllerType.Value)
            {
                case 1:
                    if (qLearningController != null)
                    {
                        Logger.Info("Resetting Q-Learning data");
                        qLearningController.Reset();
                    }
                    break;
                case 2:
                    if (sarsaController != null)
                    {
                        Logger.Info("Resetting SARSA data");
                        sarsaController.Reset();
                    }
                    break;
                default:
                    Logger.Info("Reset requested but PID mode is active, nothing to reset");
                    break;
            }
            isMLFallbackActive = false;
            UpdateMLStatusProperty();
        }

        /// <summary>
        /// Reloads all ML models from disk (called after importing backup).
        /// </summary>
        public void ReloadMLModels()
        {
            try
            {
                Logger.Info("Reloading ML models from disk");
                if (qLearningController != null)
                {
                    qLearningController.Load();
                    Logger.Info("Q-learning model reloaded successfully");
                }
                if (sarsaController != null)
                {
                    sarsaController.Load();
                    Logger.Info("SARSA model reloaded successfully");
                }
                isMLFallbackActive = false;
                UpdateMLStatusProperty();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to reload ML models: {ex.Message}");
            }
        }

        /// <summary>
        /// Reloads the Q-learning model from disk (called after importing backup).
        /// DEPRECATED: Use ReloadMLModels() instead.
        /// </summary>
        public void ReloadQLearningModel()
        {
            ReloadMLModels();
        }

        public void UpdateTDPLimits(int min, int max)
        {
            if (min < 4) min = 4;
            if (max > 85) max = 85;
            if (min > max) min = max;

            minTDP = min;
            maxTDP = max;

            // Clamp lastAppliedTDP to new limits
            if (lastAppliedTDP < minTDP) lastAppliedTDP = minTDP;
            if (lastAppliedTDP > maxTDP) lastAppliedTDP = maxTDP;

            Logger.Info($"AutoTDP limits updated: Min={minTDP}W, Max={maxTDP}W");
        }

        public override void Update()
        {
            base.Update();

            if (!enabled.Value)
            {
                // Restore profile TDP when transitioning from active to disabled
                if (wasActivelyManaging && performanceManager.TDP != null)
                {
                    int profileTDP = performanceManager.TDP.Value;
                    Logger.Info($"AutoTDP disabled - restoring profile TDP: {profileTDP}W");
                    performanceManager.SetTDP(profileTDP);
                }
                wasActivelyManaging = false;

                // Reset state when disabled
                if (integral != 0 || previousError != 0)
                {
                    ResetState();
                }
                // Sync lastAppliedTDP with current TDP so we start from correct value when re-enabled
                if (performanceManager.TDP != null)
                {
                    lastAppliedTDP = performanceManager.TDP.Value;
                }
                // Clear the AutoTDP active flag so widget TDP changes are applied
                performanceManager.IsAutoTDPActive = false;
                StatusText = "";
                TrendText = "";
                return;
            }

            // Check if a game is running
            var runningGame = systemManager.RunningGame.Value;
            if (!runningGame.IsValid())
            {
                // Restore profile TDP when game exits
                if (wasActivelyManaging && performanceManager.TDP != null)
                {
                    int profileTDP = performanceManager.TDP.Value;
                    Logger.Info($"AutoTDP: Game exited - restoring profile TDP: {profileTDP}W");
                    performanceManager.SetTDP(profileTDP);
                }
                wasActivelyManaging = false;

                // Reset per-game learning state
                if (currentGamePath != null)
                {
                    Logger.Info($"AutoTDP: Game '{currentGameName}' exited, resetting per-game learning state");
                    currentGamePath = null;
                    currentGameName = null;
                    hasAppliedLearnedTDP = false;
                    learnedTDPApplied = 0;
                    stableObservations = 0;
                }

                // No game running - reset state and clear FPS
                if (currentFPS.Value != 0)
                {
                    currentFPS.SetValue(0);
                }
                ResetState();
                // Clear flag so widget TDP changes are applied when no game is running
                performanceManager.IsAutoTDPActive = false;
                StatusText = "Waiting for game";
                TrendText = "";
                UpdateLearnedGameDataProperty();
                return;
            }

            // Detect game change and apply learned TDP
            string newGamePath = runningGame.GameId.Path;
            string newGameName = runningGame.GameId.Name;
            if (newGamePath != currentGamePath)
            {
                // Game changed - reset learning state and try to apply learned TDP
                currentGamePath = newGamePath;
                currentGameName = newGameName;
                hasAppliedLearnedTDP = false;
                learnedTDPApplied = 0;
                stableObservations = 0;
                gameStartTime = DateTime.Now;

                Logger.Info($"AutoTDP: New game detected - '{currentGameName}' at {currentGamePath}");
                UpdateLearnedGameDataProperty();

                // Try to get learned TDP for this game
                if (learnedTDPStore != null && controllerType.Value == 2)  // Only use learned TDP in SARSA mode
                {
                    int learnedTDP = learnedTDPStore.GetLearnedTDP(currentGamePath, currentGameName, targetFPS.Value);
                    if (learnedTDP > 0)
                    {
                        // Clamp to current TDP limits
                        learnedTDP = Math.Max(minTDP, Math.Min(maxTDP, learnedTDP));

                        Logger.Info($"AutoTDP: Applying learned TDP for '{currentGameName}': {learnedTDP}W");
                        performanceManager.SetTDP(learnedTDP);
                        lastAppliedTDP = learnedTDP;
                        hasAppliedLearnedTDP = true;
                        learnedTDPApplied = learnedTDP;
                        StatusText = $"[Learned] {learnedTDP}W";
                        UpdateLearnedGameDataProperty();
                    }
                }
            }

            // Check if the game is in the foreground - pause AutoTDP if game is in background (if enabled)
            // Note: pauseWhenUnfocused.Value defaults to true, user can disable via checkbox
            if (!runningGame.IsForeground && pauseWhenUnfocused.Value)
            {
                Logger.Debug($"AutoTDP: Game not focused, pauseWhenUnfocused={pauseWhenUnfocused.Value}, pausing");
                // Restore profile TDP when game goes to background
                if (wasActivelyManaging && performanceManager.TDP != null)
                {
                    int profileTDP = performanceManager.TDP.Value;
                    Logger.Info($"AutoTDP: Game in background - restoring profile TDP: {profileTDP}W");
                    performanceManager.SetTDP(profileTDP);
                }
                wasActivelyManaging = false;

                // Allow widget TDP changes when game is in background
                performanceManager.IsAutoTDPActive = false;
                StatusText = "Game not focused";
                TrendText = "";
                return;
            }

            // If game is not focused but pauseWhenUnfocused is disabled, continue managing
            if (!runningGame.IsForeground && !pauseWhenUnfocused.Value)
            {
                Logger.Debug($"AutoTDP: Game not focused but pauseWhenUnfocused=false, continuing to manage TDP");
            }

            // Check if we're in Custom TDP mode - AutoTDP can only manage TDP in Custom mode
            if (!performanceManager.IsInCustomMode)
            {
                // Restore profile TDP when leaving Custom mode
                if (wasActivelyManaging && performanceManager.TDP != null)
                {
                    int profileTDP = performanceManager.TDP.Value;
                    Logger.Info($"AutoTDP: Not in Custom mode - restoring profile TDP: {profileTDP}W");
                    performanceManager.SetTDP(profileTDP);
                }
                wasActivelyManaging = false;

                // Allow widget TDP changes when not in Custom mode
                performanceManager.IsAutoTDPActive = false;
                ResetState();
                StatusText = "Not in Custom mode";
                TrendText = "";
                return;
            }

            // Now we're actively managing TDP - block widget changes
            performanceManager.IsAutoTDPActive = true;
            wasActivelyManaging = true;

            // Rate limit FPS sampling (250ms intervals)
            var now = DateTime.Now;
            if (lastUpdateTime != DateTime.MinValue)
            {
                var elapsed = (now - lastUpdateTime).TotalMilliseconds;
                if (elapsed < SampleIntervalMs)
                {
                    return;
                }
            }
            lastUpdateTime = now;
            samplesSinceLastDecision++;

            // Get current FPS from RTSS
            int measuredFPS = GetCurrentFPS(runningGame.ProcessId);
            if (measuredFPS <= 0)
            {
                StatusText = "No FPS data";
                return;
            }

            // Update the current FPS property for UI
            if (currentFPS.Value != measuredFPS)
            {
                currentFPS.SetValue(measuredFPS);
            }

            // Detect target FPS changes and reset history to avoid stale data affecting decisions
            if (targetFPS.Value != lastTargetFPS)
            {
                Logger.Info($"AutoTDP: Target FPS changed from {lastTargetFPS} to {targetFPS.Value}, resetting FPS history");
                lastTargetFPS = targetFPS.Value;
                lastTargetChangeTime = DateTime.Now;
                // Reset FPS history so we don't use stale data
                fpsHistoryCount = 0;
                fpsHistoryIndex = 0;
                fpsDeltaCount = 0;
                fpsDeltaIndex = 0;
                lastSmoothedFPS = 0;
                consecutiveStableReadings = 0;
                consecutiveAtTarget = 0;
                StatusText = "Target changed";
                return;  // Skip this cycle to let things settle
            }

            // Spike detection - detect sudden FPS jumps (menus opening, pause screens)
            // Only detect spikes if we have enough history and FPS suddenly jumps up
            // Skip spike detection briefly after target FPS changes to avoid false positives
            double currentAverage = CalculateWeightedAverage();
            bool isSpike = false;
            bool recentTargetChange = (DateTime.Now - lastTargetChangeTime).TotalMilliseconds < TargetChangeSettleTimeMs;

            if (fpsHistoryCount >= 6 && currentAverage > 0 && !recentTargetChange)
            {
                // Spike = FPS suddenly jumps more than 50% above recent average
                // AND the jump is significant (at least 50 FPS increase)
                double spikeThreshold = currentAverage * 1.5;
                if (measuredFPS > spikeThreshold && measuredFPS > currentAverage + 50)
                {
                    isSpike = true;
                    Logger.Debug($"AutoTDP: Spike detected - FPS={measuredFPS}, avg={currentAverage:F1} (likely menu, skipping)");
                    StatusText = "Spike detected";
                    return;
                }
            }

            // Add to history for smoothing
            fpsHistory[fpsHistoryIndex] = measuredFPS;
            fpsHistoryIndex = (fpsHistoryIndex + 1) % fpsHistory.Length;
            if (fpsHistoryCount < fpsHistory.Length)
            {
                fpsHistoryCount++;
            }

            // Calculate smoothed FPS (weighted average - recent values weighted more)
            double smoothedFPS = CalculateWeightedAverage();

            // Track FPS trend (delta between smoothed readings)
            if (lastSmoothedFPS > 0)
            {
                double delta = smoothedFPS - lastSmoothedFPS;
                fpsDeltas[fpsDeltaIndex] = delta;
                fpsDeltaIndex = (fpsDeltaIndex + 1) % fpsDeltas.Length;
                if (fpsDeltaCount < fpsDeltas.Length)
                {
                    fpsDeltaCount++;
                }
            }
            lastSmoothedFPS = smoothedFPS;

            // Calculate trend (average of recent deltas)
            double trend = CalculateTrend();

            // Predict future FPS based on trend
            double predictedFPS = smoothedFPS + (trend * 2); // Predict 2 intervals ahead

            // Update trend text for OSD
            if (Math.Abs(trend) < 0.5)
                TrendText = "Stable";
            else if (trend > 2)
                TrendText = "Rising++";
            else if (trend > 0)
                TrendText = "Rising";
            else if (trend < -2)
                TrendText = "Falling--";
            else
                TrendText = "Falling";

            // Use lastAppliedTDP as source of truth since SetTDP debounces and TDP.Value may be stale
            int currentTDP = lastAppliedTDP;
            int newTDP;

            // Get GPU utilization for ceiling detection
            double gpuUtil = performanceManager.GPUUsage?.Value ?? 50.0;

            // Update ceiling detection (handles unreachable FPS targets)
            UpdateCeilingDetection(smoothedFPS, currentTDP, gpuUtil);

            // Check for ML fallback recovery (must be before routing decision)
            // Recovery happens in PID mode when FPS stabilizes
            double fpsError = targetFPS.Value - smoothedFPS;
            if (isMLFallbackActive && controllerType.Value > 0)
            {
                // Allow recovery if FPS is reasonably close to target
                if (Math.Abs(fpsError) <= 10.0)
                {
                    string controllerName = controllerType.Value == 2 ? "SARSA" : "Q-Learning";
                    Logger.Info($"FPS stabilized ({smoothedFPS:F1}/{targetFPS.Value}) - resuming {controllerName} mode");
                    isMLFallbackActive = false;
                    if (controllerType.Value == 2)
                        sarsaController?.ResetFallbackCounter();
                    else
                        qLearningController?.ResetFallbackCounter();
                }
            }

            // Check if it's time to make a TDP decision (every 500ms / 2 samples)
            // We sample FPS every 250ms for smoother data, but only adjust TDP every 500ms
            // to give the hardware time to respond to previous changes
            bool shouldMakeDecision = samplesSinceLastDecision >= SamplesPerDecision;

            if (!shouldMakeDecision)
            {
                // Just collecting FPS samples, no TDP decision this cycle
                // Don't change status text - keep showing last decision status
                return;
            }

            // Reset sample counter for next decision window
            samplesSinceLastDecision = 0;

            // Route to ML or PID controller based on mode
            // 0 = PID, 1 = Q-Learning, 2 = SARSA
            bool useML = controllerType.Value > 0 && GetActiveMLController() != null && !isMLFallbackActive;

            // Cliff detection: If actual FPS drops significantly below smoothed FPS,
            // immediately increase TDP to recover (don't wait for smoothed FPS to catch up)
            bool cliffDetected = false;
            string mlControllerName = controllerType.Value == 2 ? "SARSA" : "Q-Learning";
            if (useML && measuredFPS < smoothedFPS - 15 && measuredFPS < targetFPS.Value - 10)
            {
                // Actual FPS dropped more than 15 below smoothed and is below target
                // This indicates we hit a cliff - TDP is too low
                cliffDetected = true;
                newTDP = Math.Min(maxTDP, currentTDP + 2);  // Emergency +2W
                Logger.Info($"AutoTDP [{mlControllerName}]: Cliff detected! Actual={measuredFPS}, Smooth={smoothedFPS:F1}, Target={targetFPS.Value}. Emergency TDP: {currentTDP}W -> {newTDP}W");
            }
            else if (useML)
            {
                // ML Mode: Use Q-Learning or SARSA controller based on type
                if (controllerType.Value == 2)
                {
                    // SARSA mode
                    newTDP = CalculateSARSATDP(smoothedFPS, trend, currentTDP, gpuUtil, measuredFPS);
                }
                else
                {
                    // Q-Learning mode (default ML)
                    newTDP = CalculateMLTDP(smoothedFPS, trend, currentTDP, gpuUtil);
                }
            }
            else
            {
                // PID Mode: Use conservative PID controller
                newTDP = CalculateConservativeTDP(smoothedFPS, predictedFPS, trend, currentTDP, gpuUtil);
            }

            // Update TDP values for OSD display
            CurrentTDPValue = currentTDP;
            NewTDPValue = newTDP;

            // Track TDP history for sweet spot detection (PID mode only)
            if (!useML)
            {
                UpdateTDPHistory(newTDP);
                AnalyzeSweetSpot();
            }

            // Apply new TDP if different
            if (newTDP != currentTDP)
            {
                // Track TDP increases for diminishing returns detection
                if (newTDP > currentTDP)
                {
                    RecordTDPIncrease(smoothedFPS, currentTDP);
                }

                string action = newTDP > currentTDP ? "Increasing" : "Decreasing";
                string modeStr = controllerType.Value == 2 ? "SARSA" : (controllerType.Value == 1 ? "Q-Learn" : "PID");
                string modePrefix = useML ? $"[{modeStr}] " : "";
                string ceilingPrefix = isCeilingDetected ? "[Ceiling] " : "";
                string powerOptPrefix = isPowerOptimizationMode ? "[PwrOpt] " : "";
                StatusText = ceilingPrefix + powerOptPrefix + modePrefix + action;
                if (!useML && sweetSpotConfidence >= SweetSpotThreshold)
                {
                    StatusText += $" (sweet:{sweetSpotTDP}W)";
                }
                string ceilingInfo = isCeilingDetected ? $", Ceiling(eff={effectiveTarget}, achv≈{achievableFPS:F0})" : "";
                string logModeStr = controllerType.Value == 2 ? " [SARSA]" : (controllerType.Value == 1 ? " [Q-Learning]" : "");
                Logger.Info($"AutoTDP{logModeStr}: FPS={measuredFPS} (smooth={smoothedFPS:F1}, pred={predictedFPS:F1}), Trend={trend:F2}, Target={targetFPS.Value}, TDP: {currentTDP}W -> {newTDP}W{ceilingInfo}{(useML ? "" : $", SweetSpot={sweetSpotTDP}W@{sweetSpotConfidence}%")}");
                performanceManager.SetTDP(newTDP);
                // Note: Removed duplicate SetValue call that was causing double hardware apply
                // SetTDP already applies to hardware; the widget will sync via IPC
                lastAppliedTDP = newTDP;
                consecutiveStableReadings = 0;
            }
            else
            {
                // Use effective target (ceiling-adjusted) for status
                double error = effectiveTarget - smoothedFPS;
                double userError = targetFPS.Value - smoothedFPS;
                string modePrefix = useML ? "[ML] " : "";
                string ceilingPrefix = isCeilingDetected ? "[Ceiling] " : "";
                string powerOptPrefix = isPowerOptimizationMode ? "[PwrOpt] " : "";

                if (Math.Abs(error) <= DeadZone)
                {
                    if (isPowerOptimizationMode)
                    {
                        StatusText = $"{ceilingPrefix}Optimized {currentTDP}W";
                    }
                    else if (isCeilingDetected)
                    {
                        StatusText = $"{ceilingPrefix}At limit ({achievableFPS:F0} FPS)";
                    }
                    else if (!useML && sweetSpotConfidence >= SweetSpotThreshold)
                    {
                        StatusText = $"Locked {sweetSpotTDP}W";
                    }
                    else
                    {
                        StatusText = modePrefix + "On target";
                    }
                    consecutiveStableReadings++;
                }
                else if (error > 0)
                {
                    if (isCeilingDetected && userError > DeadZone)
                    {
                        StatusText = $"{ceilingPrefix}Target unreachable";
                    }
                    else
                    {
                        StatusText = ceilingPrefix + powerOptPrefix + modePrefix + "Below target";
                    }
                }
                else
                {
                    StatusText = ceilingPrefix + powerOptPrefix + modePrefix + "Above target";
                    consecutiveStableReadings++;
                }
            }

            // Update ML status periodically
            if (controllerType.Value > 0)
            {
                UpdateMLStatusProperty();
            }

            // Per-game TDP learning: detect stability and save learned TDP
            // Only learn in SARSA mode with valid game and store
            if (controllerType.Value == 2 && sarsaController != null && learnedTDPStore != null &&
                !string.IsNullOrEmpty(currentGamePath))
            {
                // Check stability conditions:
                // 1. SARSA has 5+ consecutive stays (at target, choosing to stay)
                // 2. Frametime variance is low (smooth gameplay)
                // 3. FPS is within 2 of target
                // 4. At least 30 seconds since game started (let things settle)
                float ftVariance = GetFrametimeVariance();
                double learningFpsError = targetFPS.Value - smoothedFPS;
                bool sarsaStable = sarsaController.ConsecutiveStays >= 5;
                bool frametimeStable = ftVariance > 0 && ftVariance < MaxFrametimeVarianceForLearning;
                bool fpsOnTarget = Math.Abs(learningFpsError) <= 2.0;
                bool settledIn = (DateTime.Now - gameStartTime).TotalSeconds >= 30;

                if (sarsaStable && fpsOnTarget && settledIn)
                {
                    stableObservations++;

                    // If frametime is also stable, this is a high-quality observation
                    if (frametimeStable && stableObservations >= MinStableObservationsToLearn)
                    {
                        // Record this as a stable TDP for this game
                        learnedTDPStore.RecordStableTDP(currentGamePath, currentGameName, lastAppliedTDP, targetFPS.Value);
                        stableObservations = 0;  // Reset counter after recording
                        UpdateLearnedGameDataProperty();

                        // If we applied a learned TDP and it's working, increase confidence
                        if (hasAppliedLearnedTDP && Math.Abs(lastAppliedTDP - learnedTDPApplied) <= 2)
                        {
                            Logger.Debug($"AutoTDP: Learned TDP confirmed working for '{currentGameName}'");
                        }
                    }
                }
                else
                {
                    // Reset if conditions not met
                    if (stableObservations > 0 && !sarsaStable)
                    {
                        stableObservations = 0;
                    }

                    // If we applied a learned TDP and SARSA is struggling (FPS way off), mark it unreliable
                    if (hasAppliedLearnedTDP && settledIn && Math.Abs(learningFpsError) > 10)
                    {
                        Logger.Warn($"AutoTDP: Learned TDP ({learnedTDPApplied}W) not working for '{currentGameName}', marking unreliable");
                        learnedTDPStore.MarkUnreliable(currentGamePath);
                        UpdateLearnedGameDataProperty();
                        hasAppliedLearnedTDP = false;  // Don't mark again
                    }
                }
            }
        }

        private void UpdateTDPHistory(int tdp)
        {
            tdpHistory[tdpHistoryIndex] = tdp;
            tdpHistoryIndex = (tdpHistoryIndex + 1) % TDPHistorySize;
            if (tdpHistoryCount < TDPHistorySize)
            {
                tdpHistoryCount++;
            }
        }

        private void AnalyzeSweetSpot()
        {
            if (tdpHistoryCount < 10) return;  // Need enough history

            // Count occurrences of each TDP value
            var tdpCounts = new Dictionary<int, int>();
            int minTDP = int.MaxValue;
            int maxTDP = int.MinValue;

            for (int i = 0; i < tdpHistoryCount; i++)
            {
                int tdp = tdpHistory[i];
                if (!tdpCounts.ContainsKey(tdp))
                    tdpCounts[tdp] = 0;
                tdpCounts[tdp]++;
                minTDP = Math.Min(minTDP, tdp);
                maxTDP = Math.Max(maxTDP, tdp);
            }

            // Find the most common TDP value
            int mostCommonTDP = 0;
            int mostCommonCount = 0;
            foreach (var kvp in tdpCounts)
            {
                if (kvp.Value > mostCommonCount)
                {
                    mostCommonCount = kvp.Value;
                    mostCommonTDP = kvp.Key;
                }
            }

            // Calculate confidence based on:
            // 1. How often the most common TDP appears
            // 2. How tight the range is (small oscillation = high confidence)
            int range = maxTDP - minTDP;
            double frequencyScore = (double)mostCommonCount / tdpHistoryCount * 100;
            double stabilityScore = range <= 2 ? 100 : range <= 4 ? 75 : range <= 6 ? 50 : 25;

            // Also count adjacent values (sweet spot ± 1W)
            int adjacentCount = mostCommonCount;
            if (tdpCounts.ContainsKey(mostCommonTDP - 1))
                adjacentCount += tdpCounts[mostCommonTDP - 1];
            if (tdpCounts.ContainsKey(mostCommonTDP + 1))
                adjacentCount += tdpCounts[mostCommonTDP + 1];
            double adjacentScore = (double)adjacentCount / tdpHistoryCount * 100;

            // Combined confidence
            sweetSpotConfidence = (int)((frequencyScore * 0.3 + stabilityScore * 0.3 + adjacentScore * 0.4));
            sweetSpotTDP = mostCommonTDP;

            if (sweetSpotConfidence >= SweetSpotThreshold)
            {
                Logger.Debug($"AutoTDP: Sweet spot detected at {sweetSpotTDP}W with {sweetSpotConfidence}% confidence (range={range}, freq={frequencyScore:F0}%, adjacent={adjacentScore:F0}%)");
            }
        }

        private double CalculateWeightedAverage()
        {
            if (fpsHistoryCount == 0) return 0;

            double weightedSum = 0;
            double totalWeight = 0;

            for (int i = 0; i < fpsHistoryCount; i++)
            {
                // More recent values get higher weight
                int age = (fpsHistoryIndex - 1 - i + fpsHistory.Length) % fpsHistory.Length;
                if (age >= fpsHistoryCount) continue;

                double weight = fpsHistoryCount - age; // Higher weight for recent
                weightedSum += fpsHistory[(fpsHistoryIndex - 1 - i + fpsHistory.Length) % fpsHistory.Length] * weight;
                totalWeight += weight;
            }

            return totalWeight > 0 ? weightedSum / totalWeight : 0;
        }

        private double CalculateTrend()
        {
            if (fpsDeltaCount == 0) return 0;

            double sum = 0;
            for (int i = 0; i < fpsDeltaCount; i++)
            {
                sum += fpsDeltas[i];
            }
            return sum / fpsDeltaCount;
        }

        private int GetCurrentFPS(int processId)
        {
            try
            {
                var appEntries = OSD.GetAppEntries(AppFlags.MASK);
                var entry = appEntries?.FirstOrDefault(e => e.ProcessId == processId);
                if (entry != null)
                {
                    return (int)entry.InstantaneousFrames;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"AutoTDP: Error getting FPS from RTSS: {ex.Message}");
            }
            return 0;
        }

        private int CalculateConservativeTDP(double smoothedFPS, double predictedFPS, double trend, int currentTDP, double gpuUtil)
        {
            // Use effective target when ceiling is detected, otherwise use user target
            int target = isCeilingDetected ? effectiveTarget : targetFPS.Value;
            double error = target - smoothedFPS;
            double predictedError = target - predictedFPS;

            // Power optimization mode: minimize TDP while maintaining achievable FPS
            if (isPowerOptimizationMode && isCeilingDetected)
            {
                double achError = achievableFPS - smoothedFPS;

                // If FPS is stable at achievable level, try reducing TDP
                if (Math.Abs(achError) <= 2.0 && currentTDP > minTDP)
                {
                    // Check if GPU is underutilized (could save power)
                    if (gpuUtil < 90)
                    {
                        // Try reducing TDP by 1W
                        Logger.Debug($"AutoTDP [PwrOpt]: GPU={gpuUtil:F0}%, trying to reduce TDP from {currentTDP}W");
                        return currentTDP - 1;
                    }
                }
                // If FPS dropped significantly, we went too low - restore
                else if (achError > 3.0 && currentTDP < optimalCeilingTDP)
                {
                    Logger.Debug($"AutoTDP [PwrOpt]: FPS dropped ({smoothedFPS:F1} < {achievableFPS:F1}), restoring TDP");
                    return Math.Min(maxTDP, currentTDP + 1);
                }

                // Update optimal ceiling TDP if current is better
                if (Math.Abs(achError) <= 1.5 && currentTDP < optimalCeilingTDP)
                {
                    optimalCeilingTDP = currentTDP;
                    Logger.Debug($"AutoTDP [PwrOpt]: New optimal ceiling TDP: {optimalCeilingTDP}W");
                }

                // Maintain current TDP in power optimization mode unless FPS is dropping
                if (Math.Abs(achError) <= 3.0)
                {
                    return currentTDP;
                }
            }

            // Detect if we're at or above FPS target (potential FPS cap scenario)
            // FPS at or above target with stable trend means we might be able to reduce TDP
            bool atOrAboveTarget = error <= 1.0 && Math.Abs(trend) < 1.0;  // error <= 1 means FPS >= target-1

            // Use the worse of current or predicted error for decisions
            double effectiveError = error;
            if (predictedError > error)
            {
                // Predicted FPS drop - act proactively
                effectiveError = (error + predictedError) / 2;
            }

            // Dead zone logic with asymmetric thresholds
            bool inDeadZone = Math.Abs(error) <= DeadZone;
            bool inUpperDeadZone = error < -UpperDeadZone; // FPS significantly above target

            // If FPS is dropping (negative trend) and we're near target, be proactive
            if (trend < -1.0 && error > -DeadZone)
            {
                // FPS is dropping - increase TDP proactively
                effectiveError = Math.Max(effectiveError, DeadZone + 1);
                // Cancel any probing - we need more power
                if (IsProbing)
                {
                    Logger.Info($"AutoTDP: FPS dropping during probe, canceling probe");
                    IsProbing = false;
                    consecutiveAtTarget = 0;
                }
            }

            // Handle FPS cap probing - try to find minimum TDP needed
            // This must be checked BEFORE the dead zone early return
            if (atOrAboveTarget && !IsProbing)
            {
                consecutiveAtTarget++;
                Logger.Debug($"AutoTDP: At/above target ({smoothedFPS:F1}/{target}), consecutiveAtTarget={consecutiveAtTarget}/{StableAtTargetRequired}");
                if (consecutiveAtTarget >= StableAtTargetRequired && currentTDP > minTDP)
                {
                    // We've been at target for a while, try reducing TDP
                    IsProbing = true;
                    lastProbeTDP = currentTDP;
                    int probeTDP = currentTDP - 1;
                    Logger.Info($"AutoTDP: Stable at/above target, probing lower TDP: {currentTDP}W -> {probeTDP}W");
                    consecutiveAtTarget = 0;
                    return probeTDP;  // Return probe TDP immediately
                }
            }
            else if (IsProbing)
            {
                // We're probing - check if FPS dropped below target
                if (error > 2.0)
                {
                    // FPS dropped below target - probe failed, restore TDP
                    Logger.Info($"AutoTDP: Probe failed (FPS dropped to {smoothedFPS:F1}), restoring TDP: {currentTDP}W -> {lastProbeTDP}W");
                    IsProbing = false;
                    consecutiveAtTarget = 0;
                    return lastProbeTDP;
                }
                else if (error <= 1.0)
                {
                    // Probe succeeded - FPS still at or above target with lower TDP
                    consecutiveAtTarget++;
                    Logger.Debug($"AutoTDP: Probing, FPS at {smoothedFPS:F1}/{target}, consecutiveAtTarget={consecutiveAtTarget}/{StableAtTargetRequired / 2}");
                    if (consecutiveAtTarget >= StableAtTargetRequired / 2)
                    {
                        // Probe was successful, this is our new baseline
                        Logger.Info($"AutoTDP: Probe succeeded at {currentTDP}W, will probe again");
                        lastProbeTDP = currentTDP;
                        IsProbing = false;
                        consecutiveAtTarget = StableAtTargetRequired - 2; // Almost ready for next probe
                    }
                    // Stay at current (probed) TDP while testing
                    return currentTDP;
                }
            }
            else if (!atOrAboveTarget)
            {
                // Not at/above target - reset counter
                if (consecutiveAtTarget > 0)
                {
                    Logger.Debug($"AutoTDP: Below target (FPS={smoothedFPS:F1}, target={target}, trend={trend:F2}), resetting counter");
                }
                consecutiveAtTarget = 0;
            }

            // If in dead zone and stable, don't change
            if (inDeadZone && consecutiveStableReadings >= StableReadingsRequired)
            {
                integral *= 0.95; // Slowly decay integral
                return currentTDP;
            }

            // Calculate time delta
            double dt = SampleIntervalMs / 1000.0;

            // Proportional term
            double P = Kp * effectiveError;

            // Integral term with anti-windup
            integral += effectiveError * dt;
            integral = Math.Max(-30, Math.Min(30, integral)); // Tighter clamp
            double I = Ki * integral;

            // Derivative term (use trend instead of raw derivative for smoothness)
            double D = -Kd * trend; // Negative because positive trend means FPS rising

            // Calculate base adjustment
            double adjustment = P + I + D;

            // Apply asymmetric multiplier
            if (adjustment > 0)
            {
                // Need to increase TDP (FPS below target)
                adjustment *= IncreaseMultiplier;
                // Cancel probing if we need to increase
                if (IsProbing)
                {
                    IsProbing = false;
                    consecutiveAtTarget = 0;
                }
            }
            else
            {
                // Want to decrease TDP (FPS above target)
                // Decrease if FPS is above target and we have at least 1 stable reading
                if (error >= 0 || consecutiveStableReadings < 1)
                {
                    // FPS at or below target, or no stable readings yet - don't decrease
                    adjustment = 0;
                }
                else
                {
                    // Apply decrease multiplier
                    adjustment *= DecreaseMultiplier;
                    // Limit decrease based on how far above target we are
                    double maxDecrease = inUpperDeadZone ? -2.0 : -1.0;
                    adjustment = Math.Max(adjustment, maxDecrease);
                }
            }

            // Calculate new TDP
            int newTDP = (int)Math.Round(currentTDP + adjustment);

            // Limit maximum change per cycle - conservative increase, aggressive decrease
            int maxIncrease = 1;  // Only increase by 1W at a time to avoid overshoot
            int maxDecreaseCycle = inUpperDeadZone ? 2 : 1;  // Decrease faster when well above target
            newTDP = Math.Max(currentTDP - maxDecreaseCycle, Math.Min(currentTDP + maxIncrease, newTDP));

            // Clamp to valid range
            newTDP = Math.Max(minTDP, Math.Min(maxTDP, newTDP));

            Logger.Debug($"AutoTDP: Err={error:F1}, PredErr={predictedError:F1}, Trend={trend:F2}, P={P:F2}, I={I:F2}, D={D:F2}, Adj={adjustment:F2}, Stable={consecutiveStableReadings}, AtTarget={consecutiveAtTarget}");

            return newTDP;
        }

        /// <summary>
        /// Calculates TDP using Q-Learning ML controller.
        /// </summary>
        private int CalculateMLTDP(double smoothedFPS, double trend, int currentTDP, double gpuUtil)
        {
            if (qLearningController == null)
            {
                // Fallback to PID if Q-learning is unavailable
                return currentTDP;
            }

            // Use effective target when ceiling is detected, otherwise use user target
            int target = isCeilingDetected ? effectiveTarget : targetFPS.Value;
            double fpsError = target - smoothedFPS;

            // Also track error from user's actual target for ceiling-aware decisions
            double userTargetError = targetFPS.Value - smoothedFPS;
            bool atMaxTDP = currentTDP >= maxTDP - 1;
            bool headroomExhausted = isCeilingDetected || (atMaxTDP && gpuUtil < CeilingGpuUtilThreshold && userTargetError > DeadZone);

            // Encode current state (with headroom exhausted info embedded via GPU util)
            int state = qLearningController.EncodeState(fpsError, trend, currentTDP, minTDP, maxTDP, gpuUtil);

            // Check if we should fall back to PID due to poor ML performance
            // Don't trigger fallback if ceiling is detected (FPS deviation is expected)
            if (!isCeilingDetected && qLearningController.ShouldFallbackToPID(fpsError))
            {
                Logger.Warn("ML performance poor - temporarily falling back to PID controller");
                isMLFallbackActive = true;
                StatusText = "[ML->PID] Fallback";
                // Will use PID on next update
                return currentTDP;
            }

            // Calculate TDP percentage for action selection override logic
            int tdpRange = maxTDP - minTDP;
            double currentTdpPercent = tdpRange > 0 ? (double)(currentTDP - minTDP) / tdpRange : 0.5;

            // Select action using epsilon-greedy policy with safety overrides
            // Pass FPS error and TDP percentage so the model can make smart overrides
            int tdpDelta = qLearningController.SelectAction(state, fpsError, currentTdpPercent);

            // Safety: Limit aggressive decreases at low TDP to prevent oscillation
            // At low TDP (below 30% of range), only allow -1W decrease max
            if (tdpDelta <= -2 && currentTdpPercent < 0.30)
            {
                tdpDelta = -1;  // Limit to -1W at low TDP
                Logger.Debug($"AutoTDP [ML]: Limited -2W to -1W at low TDP ({currentTdpPercent:P0})");
            }

            // Ceiling mode safeguards
            if (headroomExhausted && optimalCeilingTDP > 0)
            {
                // Don't let TDP drop more than 3W below where ceiling was detected
                // This prevents the death spiral where TDP reduction causes FPS to drop,
                // which updates achievable FPS downward, which confirms the "ceiling"
                int minCeilingTDP = Math.Max(minTDP, optimalCeilingTDP - 3);
                int proposedTDP = currentTDP + tdpDelta;

                if (proposedTDP < minCeilingTDP)
                {
                    int originalDelta = tdpDelta;
                    tdpDelta = minCeilingTDP - currentTDP;
                    if (tdpDelta != originalDelta)
                    {
                        Logger.Debug($"AutoTDP [ML]: Limiting TDP decrease at ceiling (optimalCeilingTDP={optimalCeilingTDP}W, min={minCeilingTDP}W)");
                    }
                }

                // Also, if FPS dropped significantly below locked achievable, increase TDP to recover
                // Use lockedAchievableFPS to prevent the death spiral
                double effectiveAchievable = lockedAchievableFPS > 0 ? lockedAchievableFPS : achievableFPS;
                double fpsFromCeiling = smoothedFPS - effectiveAchievable;
                if (fpsFromCeiling < -5 && tdpDelta <= 0)
                {
                    // FPS dropped too much - override and increase TDP to recover
                    tdpDelta = Math.Max(1, tdpDelta + 2);
                    Logger.Debug($"AutoTDP [ML]: FPS {fpsFromCeiling:F1} below locked ceiling ({effectiveAchievable:F0}), forcing TDP increase to recover");
                }
            }

            // Calculate new TDP
            int newTDP = currentTDP + tdpDelta;

            // Clamp to valid range
            newTDP = Math.Max(minTDP, Math.Min(maxTDP, newTDP));

            // Calculate reward with ceiling awareness, efficiency tracking, and oscillation tracking
            int prevTdpChange = qLearningController.PreviousTdpChange;
            double avgFpsPerWatt = GetAverageFpsPerWatt();  // Get actual FPS/W efficiency from recent data
            double reward = qLearningController.CalculateReward(fpsError, currentTDP, newTDP, headroomExhausted, gpuUtil, achievableFPS, smoothedFPS, minTDP, maxTDP, prevTdpChange, avgFpsPerWatt);
            LastMLReward = reward;  // Store for OSD display

            // Get next state for Q-learning update
            int nextState = qLearningController.EncodeState(fpsError, trend, newTDP, minTDP, maxTDP, gpuUtil);

            // Update Q-values
            qLearningController.Update(nextState, reward);

            string ceilingStr = headroomExhausted ? ", CEILING" : "";
            Logger.Debug($"AutoTDP [ML]: state={state}, action={tdpDelta:+#;-#;0}W, reward={reward:F2}, " +
                        $"FPS={smoothedFPS:F1}, GPU={gpuUtil:F0}%, TDP: {currentTDP}W -> {newTDP}W{ceilingStr}");

            return newTDP;
        }

        /// <summary>
        /// Calculates TDP using SARSA ML controller.
        /// SARSA is on-policy, using the actual next action for updates (more conservative than Q-learning).
        /// </summary>
        private int CalculateSARSATDP(double smoothedFPS, double trend, int currentTDP, double gpuUtil, double instantFPS = 0)
        {
            if (sarsaController == null)
            {
                // Fallback to PID if SARSA is unavailable
                return currentTDP;
            }

            // Use effective target when ceiling is detected, otherwise use user target
            int target = isCeilingDetected ? effectiveTarget : targetFPS.Value;
            double fpsError = target - smoothedFPS;

            // Emergency spike detection - use INSTANTANEOUS FPS for quick detection of large drops
            // Smoothed FPS changes too slowly to catch sudden spikes
            if (lastInstantFPS > 0 && instantFPS > 0)
            {
                double fpsDrop = lastInstantFPS - instantFPS;

                // Update safeTDP when stable at target (not in spike recovery)
                bool stableAtTarget = Math.Abs(fpsError) <= 2.0 && Math.Abs(trend) < 1.0;
                if (stableAtTarget && !spikeRecoveryActive)
                {
                    if (safeTDP == 0 || currentTDP > safeTDP)
                    {
                        // First stable reading or higher TDP is now stable - update safeTDP
                        safeTDP = currentTDP;
                        totalProbeReduction = 0;
                        Logger.Debug($"AutoTDP [SARSA]: Updated safeTDP to {safeTDP}W (stable at target)");
                    }
                }

                // Detect spike and trigger emergency recovery
                if (fpsDrop >= SpikeThreshold && safeTDP > 0 && currentTDP < safeTDP)
                {
                    spikeRecoveryActive = true;
                    int recoveryTDP;

                    if (fpsDrop >= SevereSpike)
                    {
                        // Severe spike (15+ FPS) - jump to safeTDP + 1W margin
                        recoveryTDP = Math.Min(maxTDP, safeTDP + 1);
                        Logger.Warn($"AutoTDP [SARSA]: SEVERE SPIKE detected ({fpsDrop:F1} FPS drop)! Emergency recovery: {currentTDP}W -> {recoveryTDP}W (safeTDP={safeTDP}W)");
                    }
                    else
                    {
                        // Moderate spike (12-15 FPS) - jump back to safeTDP
                        recoveryTDP = safeTDP;
                        Logger.Warn($"AutoTDP [SARSA]: SPIKE detected ({fpsDrop:F1} FPS drop)! Recovery: {currentTDP}W -> {recoveryTDP}W");
                    }

                    StatusText = "[SARSA] Spike recovery";
                    // Reset probe reduction since we're recovering
                    totalProbeReduction = 0;
                    return recoveryTDP;
                }
                else if (spikeRecoveryActive && stableAtTarget)
                {
                    // Recovered from spike, back to normal operation
                    spikeRecoveryActive = false;
                    Logger.Info($"AutoTDP [SARSA]: Spike recovery complete, stable at {smoothedFPS:F1} FPS / {currentTDP}W");
                }

                // Track probe reduction when decreasing TDP below safeTDP
                if (safeTDP > 0 && currentTDP < safeTDP)
                {
                    totalProbeReduction = safeTDP - currentTDP;
                }

                // Update last instant FPS for next spike detection
                lastInstantFPS = instantFPS;
            }

            // Also track error from user's actual target for ceiling-aware decisions
            double userTargetError = targetFPS.Value - smoothedFPS;
            bool atMaxTDP = currentTDP >= maxTDP - 1;
            bool headroomExhausted = isCeilingDetected || (atMaxTDP && gpuUtil < CeilingGpuUtilThreshold && userTargetError > DeadZone);

            // Encode current state
            int state = sarsaController.EncodeState(fpsError, trend, currentTDP, minTDP, maxTDP, gpuUtil);

            // Check if we should fall back to PID due to poor ML performance
            if (!isCeilingDetected && sarsaController.ShouldFallbackToPID(fpsError))
            {
                Logger.Warn("SARSA performance poor - temporarily falling back to PID controller");
                isMLFallbackActive = true;
                StatusText = "[SARSA->PID] Fallback";
                return currentTDP;
            }

            // Calculate TDP percentage for action selection override logic
            int tdpRange = maxTDP - minTDP;
            double currentTdpPercent = tdpRange > 0 ? (double)(currentTDP - minTDP) / tdpRange : 0.5;

            // Select action using epsilon-greedy policy with safety overrides
            // Pass currentTDP and minTDP for power probing when at FPS target
            int tdpDelta = sarsaController.SelectAction(state, fpsError, currentTdpPercent, currentTDP, minTDP);

            // Safety: Limit aggressive decreases at low TDP to prevent oscillation
            if (tdpDelta <= -2 && currentTdpPercent < 0.30)
            {
                tdpDelta = -1;
                Logger.Debug($"AutoTDP [SARSA]: Limited -2W to -1W at low TDP ({currentTdpPercent:P0})");
            }

            // Frametime stability gate (Option 3): Block TDP reduction when frametime is unstable
            // If frametime variance > 3ms, force "stay" to avoid making stuttering worse
            float frametimeVariance = GetFrametimeVariance();
            if (tdpDelta < 0 && frametimeVariance > 3.0f)
            {
                Logger.Info($"AutoTDP [SARSA]: Frametime unstable ({frametimeVariance:F1}ms variance), blocking TDP decrease");
                tdpDelta = 0;  // Force stay
                StatusText = "[SARSA] Stabilizing";
            }

            // Ceiling mode safeguards
            if (headroomExhausted && optimalCeilingTDP > 0)
            {
                int minCeilingTDP = Math.Max(minTDP, optimalCeilingTDP - 3);
                int proposedTDP = currentTDP + tdpDelta;

                if (proposedTDP < minCeilingTDP)
                {
                    int originalDelta = tdpDelta;
                    tdpDelta = minCeilingTDP - currentTDP;
                    if (tdpDelta != originalDelta)
                    {
                        Logger.Debug($"AutoTDP [SARSA]: Limiting TDP decrease at ceiling (optimalCeilingTDP={optimalCeilingTDP}W, min={minCeilingTDP}W)");
                    }
                }

                double effectiveAchievable = lockedAchievableFPS > 0 ? lockedAchievableFPS : achievableFPS;
                double fpsFromCeiling = smoothedFPS - effectiveAchievable;
                if (fpsFromCeiling < -5 && tdpDelta <= 0)
                {
                    tdpDelta = Math.Max(1, tdpDelta + 2);
                    Logger.Debug($"AutoTDP [SARSA]: FPS {fpsFromCeiling:F1} below locked ceiling ({effectiveAchievable:F0}), forcing TDP increase to recover");
                }
            }

            // Calculate new TDP
            int newTDP = currentTDP + tdpDelta;
            newTDP = Math.Max(minTDP, Math.Min(maxTDP, newTDP));

            // Calculate reward
            int prevTdpChange = sarsaController.PreviousTdpChange;
            double avgFpsPerWatt = GetAverageFpsPerWatt();
            float ftVariance = GetFrametimeVariance();
            double reward = sarsaController.CalculateReward(fpsError, currentTDP, newTDP, headroomExhausted, gpuUtil, achievableFPS, smoothedFPS, minTDP, maxTDP, prevTdpChange, avgFpsPerWatt, lastSmoothedFPS, ftVariance);
            LastMLReward = reward;

            // Get next state for SARSA update
            int nextState = sarsaController.EncodeState(fpsError, trend, newTDP, minTDP, maxTDP, gpuUtil);

            // Update Q-values using SARSA (on-policy)
            sarsaController.Update(nextState, reward);

            string ceilingStr = headroomExhausted ? ", CEILING" : "";
            Logger.Debug($"AutoTDP [SARSA]: state={state}, action={tdpDelta:+#;-#;0}W, reward={reward:F2}, " +
                        $"FPS={smoothedFPS:F1}, GPU={gpuUtil:F0}%, TDP: {currentTDP}W -> {newTDP}W{ceilingStr}");

            return newTDP;
        }

        private void ResetState()
        {
            integral = 0;
            previousError = 0;
            fpsHistoryCount = 0;
            fpsHistoryIndex = 0;
            fpsDeltaCount = 0;
            fpsDeltaIndex = 0;
            lastSmoothedFPS = 0;
            lastInstantFPS = 0;
            consecutiveStableReadings = 0;
            consecutiveAtTarget = 0;
            IsProbing = false;
            lastProbeTDP = 0;
            lastUpdateTime = DateTime.MinValue;
            samplesSinceLastDecision = 0;
            // Reset sweet spot tracking
            tdpHistoryCount = 0;
            tdpHistoryIndex = 0;
            sweetSpotTDP = 0;
            sweetSpotConfidence = 0;
            // Reset ML fallback state
            isMLFallbackActive = false;
            qLearningController?.ResetFallbackCounter();
            sarsaController?.ResetFallbackCounter();
            // Reset ceiling detection state
            achievableFPS = 0;
            lockedAchievableFPS = 0;
            effectiveTarget = targetFPS.Value;
            isCeilingDetected = false;
            consecutiveCeilingFrames = 0;
            consecutiveAboveCeilingFrames = 0;
            isPowerOptimizationMode = false;
            optimalCeilingTDP = 0;
            tdpGainCount = 0;
            tdpGainIndex = 0;
            pendingTDPGainMeasurement = false;
            // Reset spike recovery state
            safeTDP = 0;
            totalProbeReduction = 0;
            spikeRecoveryActive = false;
            // Reset per-game learning observations
            stableObservations = 0;
            Logger.Debug("AutoTDP: State reset");
        }

        /// <summary>
        /// Detects and handles unreachable FPS targets (ceiling detection).
        /// Returns true if we're in a ceiling state and should use power optimization.
        /// </summary>
        private void UpdateCeilingDetection(double smoothedFPS, int currentTDP, double gpuUtil)
        {
            int userTarget = targetFPS.Value;
            double fpsError = userTarget - smoothedFPS;
            bool atMaxTDP = currentTDP >= maxTDP - 1;
            bool gpuNotSaturated = gpuUtil < CeilingGpuUtilThreshold;

            // Measure FPS gain from previous TDP increase
            if (pendingTDPGainMeasurement && fpsHistoryCount >= 3)
            {
                double fpsGain = smoothedFPS - lastFPSBeforeTDPIncrease;
                int tdpIncrease = currentTDP - lastTDPBeforeIncrease;

                if (tdpIncrease > 0)
                {
                    double gainPerWatt = fpsGain / tdpIncrease;
                    tdpIncreaseFPSGains[tdpGainIndex] = gainPerWatt;
                    tdpGainIndex = (tdpGainIndex + 1) % tdpIncreaseFPSGains.Length;
                    if (tdpGainCount < tdpIncreaseFPSGains.Length)
                        tdpGainCount++;

                    Logger.Debug($"AutoTDP Ceiling: TDP +{tdpIncrease}W yielded {fpsGain:F1} FPS ({gainPerWatt:F2} FPS/W)");
                }
                pendingTDPGainMeasurement = false;
            }

            // Check for ceiling conditions
            bool ceilingConditionMet = false;

            // Minimum TDP threshold for ceiling detection: must be at 75% of TDP range or higher
            // This prevents false ceiling detection at low TDP values
            int tdpRange = maxTDP - minTDP;
            int minCeilingDetectionTDP = minTDP + (int)(tdpRange * 0.75);
            bool atHighTDP = currentTDP >= minCeilingDetectionTDP;

            // IMPORTANT: If FPS is at or above user target, we should NOT detect/maintain ceiling
            // This prevents false ceiling detection when FPS just dips temporarily
            if (fpsError <= 0)
            {
                // FPS >= target - definitely not at a ceiling
                consecutiveCeilingFrames = 0;
                // Don't set ceilingConditionMet
            }
            else
            {
                // FPS is below target - check ceiling conditions

                // Condition 1: At max TDP but GPU not saturated (CPU-bound or other bottleneck)
                // STRICTER: Must be at actual max TDP, not just "high"
                if (atMaxTDP && gpuNotSaturated && fpsError > DeadZone)
                {
                    ceilingConditionMet = true;
                    Logger.Debug($"AutoTDP Ceiling: CPU-bound detected (GPU={gpuUtil:F0}% at max TDP={maxTDP}W, FPS error={fpsError:F1})");
                }

                // Condition 2: Diminishing returns - last TDP increases yielded minimal FPS gains
                // STRICTER: Only check if at MAX TDP (not just high TDP) to avoid false positives
                if (atMaxTDP && fpsError > DeadZone && tdpGainCount >= 3)
                {
                    double avgGainPerWatt = 0;
                    for (int i = 0; i < tdpGainCount; i++)
                        avgGainPerWatt += tdpIncreaseFPSGains[i];
                    avgGainPerWatt /= tdpGainCount;

                    if (avgGainPerWatt < 0.3)  // Stricter: Less than 0.3 FPS per watt = diminishing returns
                    {
                        ceilingConditionMet = true;
                        Logger.Debug($"AutoTDP Ceiling: Diminishing returns at {currentTDP}W (avg {avgGainPerWatt:F2} FPS/W)");
                    }
                }

                // Condition 3: Steady state below target at MAX TDP (not just high TDP)
                // STRICTER: Must be at actual max TDP
                if (fpsError > DeadZone && atMaxTDP)
                {
                    consecutiveCeilingFrames++;
                }
                else
                {
                    // Not at max TDP or within dead zone - reset counter
                    consecutiveCeilingFrames = 0;
                }
            }

            if (consecutiveCeilingFrames >= CeilingDetectionThreshold)
            {
                ceilingConditionMet = true;
                Logger.Debug($"AutoTDP Ceiling: Steady-state below target for {consecutiveCeilingFrames} frames");
            }

            // If ceiling detected, update achievable FPS and effective target
            if (ceilingConditionMet && !isCeilingDetected)
            {
                // First time detecting ceiling
                isCeilingDetected = true;
                achievableFPS = smoothedFPS;
                lockedAchievableFPS = smoothedFPS;  // Lock the initial ceiling value
                effectiveTarget = Math.Max(30, (int)(smoothedFPS - CeilingMargin));
                optimalCeilingTDP = currentTDP;
                consecutiveAboveCeilingFrames = 0;
                Logger.Info($"AutoTDP: Ceiling detected! User target={userTarget}, achievable FPS≈{achievableFPS:F0}, effective target={effectiveTarget}");
            }
            else if (isCeilingDetected)
            {
                // Update achievable FPS estimate using EWMA, but ONLY allow it to increase
                // This prevents the death spiral where TDP reduction causes FPS to drop,
                // which updates achievable FPS downward, which "confirms" the lower ceiling
                double newAchievable = (AchievableFPSAlpha * smoothedFPS) + ((1 - AchievableFPSAlpha) * achievableFPS);
                if (newAchievable > achievableFPS)
                {
                    achievableFPS = newAchievable;
                    // Also update locked value if we found a higher ceiling
                    if (achievableFPS > lockedAchievableFPS)
                    {
                        lockedAchievableFPS = achievableFPS;
                        Logger.Debug($"AutoTDP Ceiling: Updated locked achievable FPS to {lockedAchievableFPS:F0}");
                    }
                }
                // Use locked value for decisions to prevent downward drift
                double effectiveAchievable = lockedAchievableFPS;

                // Update effective target if achievable FPS has improved
                int newEffectiveTarget = Math.Max(30, (int)(effectiveAchievable - CeilingMargin));
                if (newEffectiveTarget > effectiveTarget)
                {
                    effectiveTarget = newEffectiveTarget;
                    Logger.Debug($"AutoTDP Ceiling: Updated effective target to {effectiveTarget} (achievable≈{effectiveAchievable:F0})");
                }

                // Check if we should enter power optimization mode
                // (at ceiling, FPS stable - now minimize TDP while maintaining FPS)
                double errorFromAchievable = effectiveAchievable - smoothedFPS;
                if (Math.Abs(errorFromAchievable) <= 2.0 && !isPowerOptimizationMode)
                {
                    isPowerOptimizationMode = true;
                    optimalCeilingTDP = currentTDP;
                    Logger.Info($"AutoTDP: Entering power optimization mode at {currentTDP}W (FPS≈{smoothedFPS:F0})");
                }

                // Hysteresis for exiting ceiling mode: require sustained above-ceiling performance
                if (smoothedFPS >= userTarget - DeadZone)
                {
                    consecutiveAboveCeilingFrames++;
                    if (consecutiveAboveCeilingFrames >= CeilingExitHysteresis)
                    {
                        Logger.Info($"AutoTDP: Target now reachable (sustained {consecutiveAboveCeilingFrames} frames), exiting ceiling mode");
                        isCeilingDetected = false;
                        isPowerOptimizationMode = false;
                        effectiveTarget = userTarget;
                        lockedAchievableFPS = 0;
                        consecutiveAboveCeilingFrames = 0;
                    }
                }
                else
                {
                    consecutiveAboveCeilingFrames = 0;
                }
            }
        }

        /// <summary>
        /// Records a TDP increase for diminishing returns tracking.
        /// </summary>
        private void RecordTDPIncrease(double currentFPS, int currentTDP)
        {
            lastFPSBeforeTDPIncrease = currentFPS;
            lastTDPBeforeIncrease = currentTDP;
            pendingTDPGainMeasurement = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.Info("AutoTDPManager: Disposing");
                // Save ML models on shutdown
                if (qLearningController != null)
                {
                    Logger.Info("AutoTDPManager: Saving Q-Learning table on shutdown");
                    qLearningController.Save();
                }
                if (sarsaController != null)
                {
                    Logger.Info("AutoTDPManager: Saving SARSA table on shutdown");
                    sarsaController.Save();
                }
                // Save per-game learned TDP data
                if (learnedTDPStore != null)
                {
                    Logger.Info("AutoTDPManager: Saving learned TDP data on shutdown");
                    learnedTDPStore.Save();
                }
            }
            base.Dispose(disposing);
        }
    }

    // Property classes
    internal class AutoTDPEnabledProperty : HelperProperty<bool, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPEnabledProperty(bool inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP enabled: {Value}");

            if (!Value)
            {
                Manager?.FlushLearnedTDPStore();
            }
        }
    }

    internal class AutoTDPTargetFPSProperty : HelperProperty<int, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPTargetFPSProperty(int inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPTargetFPS, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP target FPS: {Value}");
            Manager?.UpdateLearnedGameDataProperty();
        }
    }

    internal class AutoTDPCurrentFPSProperty : HelperProperty<int, AutoTDPManager>
    {
        public AutoTDPCurrentFPSProperty(int inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPCurrentFPS, inManager)
        {
        }
    }

    internal class AutoTDPMinTDPProperty : HelperProperty<int, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPMinTDPProperty(int inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPMinTDP, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP min TDP: {Value}W");
            Manager.UpdateTDPLimits(Value, Manager.MaxTDP.Value);
        }
    }

    internal class AutoTDPMaxTDPProperty : HelperProperty<int, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPMaxTDPProperty(int inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPMaxTDP, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP max TDP: {Value}W");
            Manager.UpdateTDPLimits(Manager.MinTDP.Value, Value);
        }
    }

    internal class AutoTDPUseMLModeProperty : HelperProperty<bool, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPUseMLModeProperty(bool inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPUseMLMode, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP ML Mode: {(Value ? "Enabled" : "Disabled")}");
        }
    }

    internal class AutoTDPMLStatusProperty : HelperProperty<string, AutoTDPManager>
    {
        public AutoTDPMLStatusProperty(string inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPMLStatus, inManager)
        {
        }
    }

    internal class AutoTDPLearnedGameDataProperty : HelperProperty<string, AutoTDPManager>
    {
        public AutoTDPLearnedGameDataProperty(string inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPLearnedGameData, inManager)
        {
        }
    }

    internal class AutoTDPResetMLProperty : HelperProperty<bool, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPResetMLProperty(bool inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPResetML, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            // When set to true, trigger reset
            if (Value)
            {
                Logger.Info("AutoTDP ML Reset triggered");
                Manager.ResetMLLearning();
                // Reset the property back to false
                SetValue(false);
            }
        }
    }

    internal class AutoTDPPauseWhenUnfocusedProperty : HelperProperty<bool, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPPauseWhenUnfocusedProperty(bool inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPPauseWhenUnfocused, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP pause when unfocused: {Value}");
        }
    }

    internal class AutoTDPControllerTypeProperty : HelperProperty<int, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string[] ControllerNames = { "PID", "Q-Learning", "SARSA" };

        public AutoTDPControllerTypeProperty(int inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPControllerType, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            string name = Value >= 0 && Value < ControllerNames.Length ? ControllerNames[Value] : "Unknown";
            Logger.Info($"AutoTDP Controller Type: {name} ({Value})");
        }
    }
}
