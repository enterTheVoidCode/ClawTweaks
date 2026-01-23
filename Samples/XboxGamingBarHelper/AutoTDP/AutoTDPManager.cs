using NLog;
using RTSSSharedMemoryNET;
using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.Systems;

namespace XboxGamingBarHelper.AutoTDP
{
    internal class AutoTDPManager : Manager
    {
        private readonly PerformanceManager performanceManager;
        private readonly SystemManager systemManager;

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

        // Update interval
        private const double MinUpdateIntervalMs = 1000; // Faster updates for quicker response

        // Smoothing - shorter history for faster response
        private double[] fpsHistory = new double[5];
        private int fpsHistoryIndex = 0;
        private int fpsHistoryCount = 0;

        // Trend detection
        private double[] fpsDeltas = new double[3];
        private int fpsDeltaIndex = 0;
        private int fpsDeltaCount = 0;
        private double lastSmoothedFPS = 0;

        // Hysteresis - reduced requirements for decreasing TDP
        private const int StableReadingsRequired = 2;
        private const double DeadZone = 2.0;  // Tighter dead zone
        private const double UpperDeadZone = 3.0;  // Reduced threshold before decreasing TDP

        // FPS cap detection - probe for power savings when at target
        private const int StableAtTargetRequired = 6;  // Faster probing (was 10)
        private int consecutiveAtTarget = 0;
        private int lastProbeTDP = 0;  // TDP we dropped to during probing

        // Sweet spot detection - track TDP history to find optimal value
        private const int TDPHistorySize = 20;  // Track last 20 TDP readings
        private int[] tdpHistory = new int[TDPHistorySize];
        private int tdpHistoryIndex = 0;
        private int tdpHistoryCount = 0;
        private int sweetSpotTDP = 0;  // Detected optimal TDP
        private int sweetSpotConfidence = 0;  // How confident we are (0-100)
        private const int SweetSpotThreshold = 60;  // Confidence needed to use sweet spot

        // OSD Status
        public string StatusText { get; private set; } = "";
        public string TrendText { get; private set; } = "";
        public int CurrentTDPValue { get; private set; } = 0;
        public int NewTDPValue { get; private set; } = 0;
        public bool IsProbing { get; private set; } = false;  // Are we currently probing lower TDP?
        public int SweetSpotTDP => sweetSpotTDP;
        public int SweetSpotConfidence => sweetSpotConfidence;

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

            // Initialize limits from properties
            minTDP = minTDPProperty.Value;
            maxTDP = maxTDPProperty.Value;

            Logger.Info("AutoTDPManager initialized with conservative tuning");
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
                return;
            }

            // Check if the game is in the foreground - pause AutoTDP if game is in background
            if (!runningGame.IsForeground)
            {
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

            // Now we're actively managing TDP - block widget changes
            performanceManager.IsAutoTDPActive = true;
            wasActivelyManaging = true;

            // Rate limit updates
            var now = DateTime.Now;
            if (lastUpdateTime != DateTime.MinValue)
            {
                var elapsed = (now - lastUpdateTime).TotalMilliseconds;
                if (elapsed < MinUpdateIntervalMs)
                {
                    return;
                }
            }
            lastUpdateTime = now;

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

            // Spike detection - detect sudden FPS jumps (menus opening, pause screens)
            // Only detect spikes if we have enough history and FPS suddenly jumps up
            double currentAverage = CalculateWeightedAverage();
            bool isSpike = false;

            if (fpsHistoryCount >= 4 && currentAverage > 0)
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

            // Run conservative controller
            // Use lastAppliedTDP as source of truth since SetTDP debounces and TDP.Value may be stale
            int currentTDP = lastAppliedTDP;
            int newTDP = CalculateConservativeTDP(smoothedFPS, predictedFPS, trend, currentTDP);

            // Update TDP values for OSD display
            CurrentTDPValue = currentTDP;
            NewTDPValue = newTDP;

            // Track TDP history for sweet spot detection
            UpdateTDPHistory(newTDP);
            AnalyzeSweetSpot();

            // Apply new TDP if different
            if (newTDP != currentTDP)
            {
                string action = newTDP > currentTDP ? "Increasing" : "Decreasing";
                StatusText = action;
                if (sweetSpotConfidence >= SweetSpotThreshold)
                {
                    StatusText += $" (sweet:{sweetSpotTDP}W)";
                }
                Logger.Info($"AutoTDP: FPS={measuredFPS} (smooth={smoothedFPS:F1}, pred={predictedFPS:F1}), Trend={trend:F2}, Target={targetFPS.Value}, TDP: {currentTDP}W -> {newTDP}W, SweetSpot={sweetSpotTDP}W@{sweetSpotConfidence}%");
                performanceManager.SetTDP(newTDP);
                // Note: Removed duplicate SetValue call that was causing double hardware apply
                // SetTDP already applies to hardware; the widget will sync via IPC
                lastAppliedTDP = newTDP;
                consecutiveStableReadings = 0;
            }
            else
            {
                double error = targetFPS.Value - smoothedFPS;
                if (Math.Abs(error) <= DeadZone)
                {
                    if (sweetSpotConfidence >= SweetSpotThreshold)
                    {
                        StatusText = $"Locked {sweetSpotTDP}W";
                    }
                    else
                    {
                        StatusText = "On target";
                    }
                    consecutiveStableReadings++;
                }
                else if (error > 0)
                {
                    StatusText = "Below target";
                }
                else
                {
                    StatusText = "Above target";
                    consecutiveStableReadings++;
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

        private int CalculateConservativeTDP(double smoothedFPS, double predictedFPS, double trend, int currentTDP)
        {
            int target = targetFPS.Value;
            double error = target - smoothedFPS;
            double predictedError = target - predictedFPS;

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
            double dt = MinUpdateIntervalMs / 1000.0;

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

        private void ResetState()
        {
            integral = 0;
            previousError = 0;
            fpsHistoryCount = 0;
            fpsHistoryIndex = 0;
            fpsDeltaCount = 0;
            fpsDeltaIndex = 0;
            lastSmoothedFPS = 0;
            consecutiveStableReadings = 0;
            consecutiveAtTarget = 0;
            IsProbing = false;
            lastProbeTDP = 0;
            lastUpdateTime = DateTime.MinValue;
            // Reset sweet spot tracking
            tdpHistoryCount = 0;
            tdpHistoryIndex = 0;
            sweetSpotTDP = 0;
            sweetSpotConfidence = 0;
            Logger.Debug("AutoTDP: State reset");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.Info("AutoTDPManager: Disposing");
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
}
