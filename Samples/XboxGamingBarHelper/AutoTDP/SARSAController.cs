using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace XboxGamingBarHelper.AutoTDP
{
    /// <summary>
    /// SARSA controller for AutoTDP ML mode.
    /// On-policy learning: uses the actual next action's Q-value for updates.
    /// More conservative than Q-learning, learns the policy it's actually following.
    /// </summary>
    internal class SARSAController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // State space dimensions (same as Q-Learning)
        private const int FpsErrorBins = 7;    // [-∞,-10], [-10,-5], [-5,-2], [-2,+2], [+2,+5], [+5,+10], [+10,+∞]
        private const int TrendBins = 3;       // Falling, Stable, Rising
        private const int TdpLevelBins = 10;   // 0-10%, 10-20%, ..., 90-100% of TDP range
        private const int GpuUtilBins = 4;     // [0-50%], [50-80%], [80-95%], [95-100%]

        // Total states = 7 * 3 * 10 * 4 = 840
        private const int TotalStates = FpsErrorBins * TrendBins * TdpLevelBins * GpuUtilBins;

        // Action space: 5 actions (-2W, -1W, 0W, +1W, +2W)
        private const int NumActions = 5;
        private static readonly int[] ActionDeltas = { -2, -1, 0, 1, 2 };

        // Q-table: state -> action values
        private Dictionary<int, double[]> qTable = new Dictionary<int, double[]>();

        // State visit counts for tracking learning progress
        private Dictionary<int, int> stateVisits = new Dictionary<int, int>();

        // Hyperparameters (same as Q-Learning for fair comparison)
        private double alpha = 0.2;      // Learning rate (initial)
        private const double AlphaMin = 0.05;
        private const double AlphaDecay = 0.999;  // Decay per update

        private const double Gamma = 0.9;  // Discount factor (fixed)

        private double epsilon = 0.3;    // Exploration rate (initial)
        private const double EpsilonMin = 0.05;
        private const double EpsilonDecay = 0.995;  // Decay per update

        // Statistics
        private long totalUpdates = 0;
        private double cumulativeReward = 0;      // Total reward accumulated
        private double recentRewardSum = 0;       // Sum of last N rewards for moving average
        private Queue<double> recentRewards = new Queue<double>();
        private const int RecentWindowSize = 50;  // Window for moving average

        // SARSA-specific: track state-action pair for deferred update
        private int lastState = -1;
        private int lastAction = -1;
        private int pendingState = -1;   // State waiting for next action to complete update
        private int pendingAction = -1;  // Action waiting for next action to complete update
        private double pendingReward = 0; // Reward waiting for next action

        // Persistence
        private const int SaveInterval = 100;  // Save every N updates
        private string savePath;
        private bool isDirty = false;

        // Fallback tracking
        private int consecutivePoorPerformance = 0;
        private const int FallbackThreshold = 7;
        private const double PoorPerformanceFpsError = 20.0;

        // Random for exploration
        private readonly Random random = new Random();

        // Oscillation tracking
        private int previousTdpChange = 0;
        private int currentTdpChange = 0;

        public SARSAController(string dataPath)
        {
            savePath = Path.Combine(dataPath, "autotdp_sarsa_table.json");
            Load();
        }

        /// <summary>
        /// Encodes the current state into a single integer index.
        /// </summary>
        public int EncodeState(double fpsError, double trend, int currentTdp, int minTdp, int maxTdp, double gpuUtil)
        {
            // FPS Error bins: [-∞,-10], [-10,-5], [-5,-2], [-2,+2], [+2,+5], [+5,+10], [+10,+∞]
            int fpsErrorBin;
            if (fpsError < -10) fpsErrorBin = 0;
            else if (fpsError < -5) fpsErrorBin = 1;
            else if (fpsError < -2) fpsErrorBin = 2;
            else if (fpsError <= 2) fpsErrorBin = 3;  // Dead zone
            else if (fpsError <= 5) fpsErrorBin = 4;
            else if (fpsError <= 10) fpsErrorBin = 5;
            else fpsErrorBin = 6;

            // Trend bins: Falling (-1), Stable (0), Rising (+1)
            int trendBin;
            if (trend < -1.0) trendBin = 0;      // Falling
            else if (trend > 1.0) trendBin = 2;  // Rising
            else trendBin = 1;                   // Stable

            // TDP level bin (0-9 based on percentage of TDP range)
            int tdpRange = maxTdp - minTdp;
            int tdpLevelBin;
            if (tdpRange <= 0)
            {
                tdpLevelBin = 5;  // Middle bin if no range
            }
            else
            {
                double tdpPercent = (double)(currentTdp - minTdp) / tdpRange;
                tdpLevelBin = Math.Min(9, Math.Max(0, (int)(tdpPercent * 10)));
            }

            // GPU utilization bins: [0-50%], [50-80%], [80-95%], [95-100%]
            int gpuUtilBin;
            if (gpuUtil < 50) gpuUtilBin = 0;
            else if (gpuUtil < 80) gpuUtilBin = 1;
            else if (gpuUtil < 95) gpuUtilBin = 2;
            else gpuUtilBin = 3;

            // Combine into single state index
            int state = fpsErrorBin;
            state = state * TrendBins + trendBin;
            state = state * TdpLevelBins + tdpLevelBin;
            state = state * GpuUtilBins + gpuUtilBin;

            return state;
        }

        /// <summary>
        /// Selects an action using epsilon-greedy policy.
        /// Returns the TDP delta (-2, -1, 0, +1, or +2).
        /// </summary>
        public int SelectAction(int state, double fpsError = 0, double currentTdpPercent = 0.5)
        {
            int actionIndex;
            bool overrideApplied = false;

            // CRITICAL SAFETY OVERRIDE: When situation is unambiguous, override Q-values
            if (fpsError < -8 && currentTdpPercent > 0.15)
            {
                actionIndex = fpsError < -15 ? 0 : 1;
                overrideApplied = true;
                Logger.Info($"SARSA: OVERRIDE - FPS {-fpsError:F0} above target, forcing TDP decrease (action={ActionDeltas[actionIndex]})");
            }
            else if (fpsError > 5 && currentTdpPercent < 0.95)
            {
                // FPS is 5+ below target - we need more power, definitely increase TDP
                actionIndex = fpsError > 15 ? 4 : 3;  // +2W if way below, +1W otherwise
                overrideApplied = true;
                Logger.Info($"SARSA: OVERRIDE - FPS {fpsError:F0} below target, forcing TDP increase (action={ActionDeltas[actionIndex]})");
            }
            else
            {
                // Normal SARSA action selection
                int visits = stateVisits.TryGetValue(state, out int v) ? v : 0;
                double effectiveEpsilon = visits < 10 ? Math.Max(epsilon, 0.15) : epsilon;

                if (random.NextDouble() < effectiveEpsilon)
                {
                    // Explore: random action with directional bias
                    double r = random.NextDouble();
                    if (fpsError < -3)
                    {
                        if (r < 0.35) actionIndex = 1;
                        else if (r < 0.55) actionIndex = 0;
                        else if (r < 0.80) actionIndex = 2;
                        else if (r < 0.92) actionIndex = 3;
                        else actionIndex = 4;
                    }
                    else if (fpsError > 3)
                    {
                        if (r < 0.35) actionIndex = 3;
                        else if (r < 0.55) actionIndex = 4;
                        else if (r < 0.80) actionIndex = 2;
                        else if (r < 0.92) actionIndex = 1;
                        else actionIndex = 0;
                    }
                    else
                    {
                        if (r < 0.50) actionIndex = 2;
                        else if (r < 0.70) actionIndex = 1;
                        else if (r < 0.85) actionIndex = 3;
                        else if (r < 0.93) actionIndex = 0;
                        else actionIndex = 4;
                    }
                }
                else
                {
                    // Exploit: best action based on Q-values
                    actionIndex = GetBestAction(state);
                }
            }

            // Store for SARSA update (we need the actual action taken)
            lastState = state;
            lastAction = actionIndex;

            // Track TDP change for oscillation detection
            previousTdpChange = currentTdpChange;
            currentTdpChange = ActionDeltas[actionIndex];

            Logger.Debug($"SARSA SelectAction: state={state}, action={ActionDeltas[actionIndex]}, fpsErr={fpsError:F1}, tdp%={currentTdpPercent:P0}, override={overrideApplied}");

            return ActionDeltas[actionIndex];
        }

        /// <summary>
        /// Gets the best action for a state (highest Q-value).
        /// </summary>
        private int GetBestAction(int state)
        {
            double[] qValues = GetQValues(state);
            int bestAction = 0;
            double bestValue = qValues[0];

            for (int i = 1; i < NumActions; i++)
            {
                if (qValues[i] > bestValue)
                {
                    bestValue = qValues[i];
                    bestAction = i;
                }
            }

            return bestAction;
        }

        /// <summary>
        /// Gets or initializes Q-values for a state.
        /// </summary>
        private double[] GetQValues(int state)
        {
            if (!qTable.TryGetValue(state, out double[] values))
            {
                values = new double[NumActions];
                values[0] = -0.5;  // -2W: slight penalty
                values[1] = -0.2;  // -1W: small penalty
                values[2] = 0.5;   // 0W: prefer stability
                values[3] = -0.2;  // +1W: small penalty
                values[4] = -0.5;  // +2W: slight penalty
                qTable[state] = values;
            }
            return values;
        }

        /// <summary>
        /// SARSA update: Q(s,a) = Q(s,a) + α * (r + γ * Q(s',a') - Q(s,a))
        /// Unlike Q-learning, SARSA uses the actual next action (a') taken, not the max.
        /// This makes SARSA more conservative during learning.
        /// </summary>
        public void Update(int nextState, double reward)
        {
            // SARSA requires knowing the next action to complete the update
            // On first call, we don't have a previous (s,a) to update, just store for next time
            if (pendingState < 0 || pendingAction < 0)
            {
                // First iteration: store current (s,a,r) as pending
                pendingState = lastState;
                pendingAction = lastAction;
                pendingReward = reward;
                Logger.Debug($"SARSA: First update, storing pending (s={pendingState}, a={pendingAction}, r={pendingReward:F2})");
                return;
            }

            // Now we have:
            // - pendingState, pendingAction, pendingReward: the (s,a,r) from previous step
            // - lastState, lastAction: the (s',a') from current step
            // We can now complete the SARSA update for the pending state-action

            // Get Q(s,a) - the pending state-action pair
            double[] qValues = GetQValues(pendingState);
            double currentQ = qValues[pendingAction];

            // Get Q(s',a') - using the ACTUAL action taken (SARSA is on-policy)
            double[] nextQValues = GetQValues(lastState);
            double nextQ = nextQValues[lastAction];

            // SARSA update: Q(s,a) = Q(s,a) + α * (r + γ * Q(s',a') - Q(s,a))
            double newQ = currentQ + alpha * (pendingReward + Gamma * nextQ - currentQ);
            qValues[pendingAction] = newQ;

            // Update state visit count
            if (!stateVisits.ContainsKey(pendingState))
                stateVisits[pendingState] = 0;
            stateVisits[pendingState]++;

            // Decay hyperparameters
            totalUpdates++;
            alpha = Math.Max(AlphaMin, alpha * AlphaDecay);
            epsilon = Math.Max(EpsilonMin, epsilon * EpsilonDecay);

            // Track rewards
            cumulativeReward += pendingReward;
            recentRewards.Enqueue(pendingReward);
            recentRewardSum += pendingReward;
            if (recentRewards.Count > RecentWindowSize)
            {
                recentRewardSum -= recentRewards.Dequeue();
            }

            isDirty = true;

            // Periodic save
            if (totalUpdates % SaveInterval == 0)
            {
                Save();
            }

            Logger.Info($"SARSA Update #{totalUpdates}: state={pendingState}, action={ActionDeltas[pendingAction]}, reward={pendingReward:F2}, " +
                        $"nextAction={ActionDeltas[lastAction]}, Q: {currentQ:F3} -> {newQ:F3}, α={alpha:F3}, ε={epsilon:F3}");

            // Move current to pending for next iteration
            pendingState = lastState;
            pendingAction = lastAction;
            pendingReward = reward;
        }

        /// <summary>
        /// Calculates reward (same as Q-Learning for fair comparison).
        /// </summary>
        public double CalculateReward(double fpsError, int prevTdp, int newTdp, bool headroomExhausted, double gpuUtil, double achievableFPS = 0, double currentFPS = 0, int minTdp = 8, int maxTdp = 30, int prevTdpChange = 0, double avgFpsPerWatt = -1)
        {
            double totalReward;
            int currentTdpChange = newTdp - prevTdp;

            // Oscillation penalty
            double oscillationPenalty = 0;
            if (prevTdpChange != 0 && currentTdpChange != 0 && (prevTdpChange * currentTdpChange < 0))
            {
                oscillationPenalty = -3.0;
                Logger.Debug($"SARSA Oscillation penalty: prev={prevTdpChange:+#;-#;0}, curr={currentTdpChange:+#;-#;0}");
            }

            if (headroomExhausted && achievableFPS > 0 && currentFPS > 0)
            {
                // Ceiling detected: same reward structure as Q-Learning
                double fpsFromCeiling = currentFPS - achievableFPS;
                int tdpChange = newTdp - prevTdp;

                double fpsReward;
                if (fpsFromCeiling >= -2 && fpsFromCeiling <= 5)
                    fpsReward = 5.0;
                else if (fpsFromCeiling > 5)
                    fpsReward = 4.0;
                else if (fpsFromCeiling >= -5)
                    fpsReward = 3.0 + fpsFromCeiling * 0.4;
                else
                    fpsReward = Math.Max(-3.0, fpsFromCeiling * 0.3);

                double directionBonus = 0;
                if (fpsFromCeiling < -3 && tdpChange > 0)
                    directionBonus = 2.0;
                else if (fpsFromCeiling >= -2 && tdpChange <= 0)
                    directionBonus = 2.0;
                else if (fpsFromCeiling < -3 && tdpChange < 0)
                    directionBonus = -4.0;

                double stabilityBonus = 0;
                if (fpsFromCeiling >= -2)
                {
                    if (tdpChange == 0)
                        stabilityBonus = 2.0;
                    else if (Math.Abs(tdpChange) == 1)
                        stabilityBonus = 1.0;
                }

                double powerBonus = 0;
                if (fpsFromCeiling >= -2)
                {
                    powerBonus = Math.Max(0.2, 2.0 - newTdp * 0.04);
                    if (tdpChange < 0)
                    {
                        int tdpRange = maxTdp - minTdp;
                        double tdpPercent = tdpRange > 0 ? (double)(newTdp - minTdp) / tdpRange : 0.5;
                        double decreaseBonus = 2.5 * Math.Min(1.0, tdpPercent * 2.0);
                        powerBonus += Math.Max(0.5, decreaseBonus);
                    }
                }

                double efficiencyPenalty = 0;
                if (fpsFromCeiling >= -2 && avgFpsPerWatt >= 0)
                {
                    if (avgFpsPerWatt < 0.5)
                    {
                        if (tdpChange > 0)
                            efficiencyPenalty = -3.0 * tdpChange;
                        else if (tdpChange < 0)
                            efficiencyPenalty = 2.0 * Math.Abs(tdpChange);
                        else
                        {
                            int tdpRange = maxTdp - minTdp;
                            double tdpPercent = tdpRange > 0 ? (double)(newTdp - minTdp) / tdpRange : 0.5;
                            if (tdpPercent > 0.5)
                                efficiencyPenalty = -(tdpPercent - 0.5) * 2.0;
                        }
                    }
                    else if (tdpChange == 0)
                        efficiencyPenalty = 1.0;
                }

                totalReward = fpsReward + directionBonus + stabilityBonus + powerBonus + efficiencyPenalty + oscillationPenalty;
            }
            else
            {
                // Normal operation
                double absError = Math.Abs(fpsError);
                int tdpChange = newTdp - prevTdp;

                double fpsReward;
                if (absError <= 2.0)
                    fpsReward = 5.0;
                else if (absError <= 5.0)
                    fpsReward = 3.0 - (absError - 2.0) * 0.6;
                else if (absError <= 15.0)
                    fpsReward = 1.0 - (absError - 5.0) * 0.3;
                else
                    fpsReward = -2.0 - Math.Min(absError - 15.0, 30.0) * 0.1;

                double directionBonus = 0;
                if (absError > 5.0)
                {
                    bool correctDirection = (fpsError > 0 && tdpChange > 0) || (fpsError < 0 && tdpChange < 0);
                    bool wrongDirection = (fpsError > 0 && tdpChange < 0) || (fpsError < 0 && tdpChange > 0);

                    if (correctDirection)
                        directionBonus = 2.0;
                    else if (wrongDirection)
                        directionBonus = -4.0;
                }
                else if (tdpChange == 0)
                    directionBonus = 1.0;

                double stabilityBonus = 0;
                if (absError <= 3.0)
                {
                    if (tdpChange == 0)
                        stabilityBonus = 2.0;
                    else if (Math.Abs(tdpChange) == 1)
                        stabilityBonus = 1.0;
                }

                double powerBonus = 0;
                if (absError <= 2.0)
                {
                    powerBonus = Math.Max(0.2, 2.0 - newTdp * 0.04);
                    if (tdpChange < 0)
                    {
                        int tdpRange = maxTdp - minTdp;
                        double tdpPercent = tdpRange > 0 ? (double)(newTdp - minTdp) / tdpRange : 0.5;
                        double decreaseBonus = 2.5 * Math.Min(1.0, tdpPercent * 2.0);
                        powerBonus += Math.Max(0.5, decreaseBonus);
                    }
                }

                double efficiencyPenalty = 0;
                if (avgFpsPerWatt >= 0)
                {
                    if (avgFpsPerWatt < 0.5)
                    {
                        if (tdpChange > 0)
                            efficiencyPenalty = -4.0 * tdpChange;
                        else if (tdpChange < 0)
                            efficiencyPenalty = 3.0 * Math.Abs(tdpChange);
                        else if (absError <= 2.0)
                        {
                            int tdpRange = maxTdp - minTdp;
                            double tdpPercent = tdpRange > 0 ? (double)(newTdp - minTdp) / tdpRange : 0.5;
                            if (tdpPercent > 0.3)
                                efficiencyPenalty = -(tdpPercent - 0.3) * 3.0;
                        }
                    }
                    else if (avgFpsPerWatt >= 0.5 && avgFpsPerWatt < 1.5)
                    {
                        if (absError <= 2.0 && tdpChange == 0)
                            efficiencyPenalty = 1.0;
                    }
                    else
                    {
                        if (absError <= 2.0 && tdpChange == 0)
                            efficiencyPenalty = 2.0;
                    }
                }
                else
                {
                    if (absError <= 2.0 && tdpChange == 0)
                    {
                        int tdpRange = maxTdp - minTdp;
                        double tdpPercent = tdpRange > 0 ? (double)(newTdp - minTdp) / tdpRange : 0.5;
                        efficiencyPenalty = (0.5 - tdpPercent) * 2.0;
                    }
                }

                totalReward = fpsReward + directionBonus + stabilityBonus + powerBonus + efficiencyPenalty + oscillationPenalty;
            }

            return totalReward;
        }

        /// <summary>
        /// Checks if SARSA is performing poorly and should fall back to PID.
        /// </summary>
        public bool ShouldFallbackToPID(double fpsError)
        {
            if (Math.Abs(fpsError) > PoorPerformanceFpsError)
            {
                consecutivePoorPerformance++;
                if (consecutivePoorPerformance >= FallbackThreshold)
                {
                    Logger.Warn($"SARSA fallback triggered: {consecutivePoorPerformance} consecutive poor readings (error={fpsError:F1})");
                    return true;
                }
            }
            else
            {
                consecutivePoorPerformance = 0;
            }
            return false;
        }

        /// <summary>
        /// Resets the fallback counter after recovering.
        /// </summary>
        public void ResetFallbackCounter()
        {
            consecutivePoorPerformance = 0;
        }

        /// <summary>
        /// Initializes Q-table with PID-like conservative values.
        /// </summary>
        public void InitializeFromPID()
        {
            Logger.Info("SARSA: Initializing Q-table with PID-like values (cold start)");
            qTable.Clear();
            stateVisits.Clear();

            for (int fpsErr = 0; fpsErr < FpsErrorBins; fpsErr++)
            {
                for (int trend = 0; trend < TrendBins; trend++)
                {
                    for (int tdpLevel = 0; tdpLevel < TdpLevelBins; tdpLevel++)
                    {
                        for (int gpuUtil = 0; gpuUtil < GpuUtilBins; gpuUtil++)
                        {
                            int state = fpsErr * TrendBins * TdpLevelBins * GpuUtilBins +
                                       trend * TdpLevelBins * GpuUtilBins +
                                       tdpLevel * GpuUtilBins +
                                       gpuUtil;

                            double[] qValues = new double[NumActions];

                            if (fpsErr <= 2)
                            {
                                qValues[0] = 1.0;
                                qValues[1] = 1.5;
                                qValues[2] = 0.0;
                                qValues[3] = -1.5;
                                qValues[4] = -2.0;
                            }
                            else if (fpsErr == 3)
                            {
                                qValues[0] = 0.5;
                                qValues[1] = 1.0;
                                qValues[2] = 0.5;
                                qValues[3] = -0.5;
                                qValues[4] = -1.0;
                            }
                            else
                            {
                                qValues[0] = -2.0;
                                qValues[1] = -1.5;
                                qValues[2] = 0.0;
                                qValues[3] = 1.5;
                                qValues[4] = 1.0;
                            }

                            if (trend == 0)
                            {
                                qValues[3] += 0.5;
                                qValues[4] += 0.3;
                            }
                            else if (trend == 2)
                            {
                                qValues[0] += 0.3;
                                qValues[1] += 0.5;
                            }

                            if (gpuUtil == 3)
                            {
                                qValues[3] -= 0.3;
                                qValues[4] -= 0.5;
                            }
                            else if (gpuUtil <= 1)
                            {
                                qValues[0] += 0.2;
                                qValues[1] += 0.3;
                            }

                            qTable[state] = qValues;
                        }
                    }
                }
            }

            totalUpdates = 0;
            alpha = 0.2;
            epsilon = 0.3;
            isDirty = true;

            Save();
            Logger.Info($"SARSA: Q-table initialized with {qTable.Count} states");
        }

        /// <summary>
        /// Resets all learning data.
        /// </summary>
        public void Reset()
        {
            Logger.Info("SARSA: Resetting learning data");
            qTable.Clear();
            stateVisits.Clear();
            totalUpdates = 0;
            alpha = 0.2;
            epsilon = 0.3;
            lastState = -1;
            lastAction = -1;
            pendingState = -1;
            pendingAction = -1;
            pendingReward = 0;
            previousTdpChange = 0;
            currentTdpChange = 0;
            consecutivePoorPerformance = 0;
            cumulativeReward = 0;
            recentRewardSum = 0;
            recentRewards.Clear();
            isDirty = true;

            InitializeFromPID();
        }

        /// <summary>
        /// Saves Q-table to JSON file.
        /// </summary>
        public void Save()
        {
            if (!isDirty)
                return;

            try
            {
                var saveData = new SARSAData
                {
                    Version = 1,
                    TotalUpdates = totalUpdates,
                    Epsilon = epsilon,
                    Alpha = alpha,
                    CumulativeReward = cumulativeReward,
                    QTable = new Dictionary<string, double[]>(),
                    StateVisits = new Dictionary<string, int>()
                };

                foreach (var kvp in qTable)
                {
                    saveData.QTable[kvp.Key.ToString()] = kvp.Value;
                }

                foreach (var kvp in stateVisits)
                {
                    saveData.StateVisits[kvp.Key.ToString()] = kvp.Value;
                }

                string directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(saveData, options);
                File.WriteAllText(savePath, json);

                isDirty = false;
                Logger.Info($"SARSA: Q-table saved: {qTable.Count} states, {totalUpdates} updates, ε={epsilon:F3}, α={alpha:F3}");
            }
            catch (Exception ex)
            {
                Logger.Error($"SARSA: Failed to save Q-table: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads Q-table from JSON file.
        /// </summary>
        public void Load()
        {
            try
            {
                if (!File.Exists(savePath))
                {
                    Logger.Info("SARSA: No existing Q-table found, initializing from PID");
                    InitializeFromPID();
                    return;
                }

                string json = File.ReadAllText(savePath);
                var saveData = JsonSerializer.Deserialize<SARSAData>(json);

                if (saveData == null || saveData.Version != 1)
                {
                    Logger.Warn("SARSA: Invalid Q-table version, reinitializing");
                    InitializeFromPID();
                    return;
                }

                qTable.Clear();
                stateVisits.Clear();

                foreach (var kvp in saveData.QTable)
                {
                    if (int.TryParse(kvp.Key, out int state))
                    {
                        qTable[state] = kvp.Value;
                    }
                }

                foreach (var kvp in saveData.StateVisits)
                {
                    if (int.TryParse(kvp.Key, out int state))
                    {
                        stateVisits[state] = kvp.Value;
                    }
                }

                totalUpdates = saveData.TotalUpdates;
                epsilon = saveData.Epsilon;
                alpha = saveData.Alpha;
                cumulativeReward = saveData.CumulativeReward;

                Logger.Info($"SARSA: Q-table loaded: {qTable.Count} states, {totalUpdates} updates, ε={epsilon:F3}, α={alpha:F3}");
            }
            catch (Exception ex)
            {
                Logger.Error($"SARSA: Failed to load Q-table: {ex.Message}, reinitializing");
                InitializeFromPID();
            }
        }

        /// <summary>
        /// Gets the current status string for display.
        /// </summary>
        public string GetStatusString()
        {
            int explorationPercent = (int)(epsilon * 100);
            double avgReward = recentRewards.Count > 0 ? recentRewardSum / recentRewards.Count : 0;
            return $"Updates: {totalUpdates} | Avg: {avgReward:F1} | Exploration: {explorationPercent}%";
        }

        public long TotalUpdates => totalUpdates;
        public double ExplorationRate => epsilon;
        public double LearningRate => alpha;
        public int UniqueStatesVisited => stateVisits.Count;
        public double CumulativeReward => cumulativeReward;
        public double AverageRecentReward => recentRewards.Count > 0 ? recentRewardSum / recentRewards.Count : 0;
        public int PreviousTdpChange => previousTdpChange;

        private class SARSAData
        {
            public int Version { get; set; }
            public long TotalUpdates { get; set; }
            public double Epsilon { get; set; }
            public double Alpha { get; set; }
            public double CumulativeReward { get; set; }
            public Dictionary<string, double[]> QTable { get; set; }
            public Dictionary<string, int> StateVisits { get; set; }
        }
    }
}
