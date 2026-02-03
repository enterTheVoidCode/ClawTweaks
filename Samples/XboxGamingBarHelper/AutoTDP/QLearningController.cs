using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace XboxGamingBarHelper.AutoTDP
{
    /// <summary>
    /// Q-Learning controller for AutoTDP ML mode.
    /// Learns optimal TDP values based on FPS error, trend, current TDP level, and GPU utilization.
    /// </summary>
    internal class QLearningController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // State space dimensions
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

        // Hyperparameters
        private double alpha = 0.2;      // Learning rate (initial)
        private const double AlphaMin = 0.05;
        private const double AlphaDecay = 0.999;  // Decay per update

        private const double Gamma = 0.9;  // Discount factor (fixed)

        private double epsilon = 0.3;    // Exploration rate (initial)
        private const double EpsilonMin = 0.05;
        private const double EpsilonDecay = 0.995;  // Decay per update

        // Statistics
        private long totalUpdates = 0;
        private int lastState = -1;
        private int lastAction = -1;
        private double cumulativeReward = 0;      // Total reward accumulated
        private double recentRewardSum = 0;       // Sum of last N rewards for moving average
        private Queue<double> recentRewards = new Queue<double>();
        private const int RecentWindowSize = 50;  // Window for moving average

        // Persistence
        private const int SaveInterval = 100;  // Save every N updates
        private string savePath;
        private bool isDirty = false;

        // Fallback tracking - increased thresholds to reduce false triggers during scene changes
        private int consecutivePoorPerformance = 0;
        private const int FallbackThreshold = 7;  // Seconds of poor performance before fallback (was 5)
        private const double PoorPerformanceFpsError = 20.0;  // FPS deviation threshold (was 15)

        // Random for exploration
        private readonly Random random = new Random();

        // Oscillation tracking - penalize rapid direction reversals
        private int previousTdpChange = 0;  // Track PREVIOUS iteration's TDP delta
        private int currentTdpChange = 0;   // Track current iteration's TDP delta

        public QLearningController(string dataPath)
        {
            savePath = Path.Combine(dataPath, "autotdp_qtable.json");
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
        /// Selects an action using epsilon-greedy policy with state-dependent exploration boost.
        /// Returns the TDP delta (-2, -1, 0, +1, or +2).
        /// </summary>
        /// <param name="state">Encoded state</param>
        /// <param name="fpsError">Raw FPS error (target - current). Negative = above target.</param>
        /// <param name="currentTdpPercent">Current TDP as percentage of range (0-1)</param>
        public int SelectAction(int state, double fpsError = 0, double currentTdpPercent = 0.5)
        {
            int actionIndex;

            // CRITICAL SAFETY OVERRIDE: When situation is unambiguous, override Q-values
            // This prevents the model from making clearly wrong decisions due to bad learned values
            bool overrideApplied = false;

            // If FPS is significantly ABOVE target (error < -8) and TDP is not at minimum, FORCE decrease
            if (fpsError < -8 && currentTdpPercent > 0.15)
            {
                // FPS is 8+ above target - we're wasting power, definitely decrease TDP
                actionIndex = fpsError < -15 ? 0 : 1;  // -2W if way above, -1W otherwise
                overrideApplied = true;
                Logger.Info($"Q-Learning: OVERRIDE - FPS {-fpsError:F0} above target, forcing TDP decrease (action={ActionDeltas[actionIndex]})");
            }
            // If FPS is significantly BELOW target (error > 10) and TDP is not at maximum, FORCE increase
            else if (fpsError > 10 && currentTdpPercent < 0.95)
            {
                // FPS is 10+ below target - we need more power, definitely increase TDP
                actionIndex = fpsError > 20 ? 4 : 3;  // +2W if way below, +1W otherwise
                overrideApplied = true;
                Logger.Info($"Q-Learning: OVERRIDE - FPS {fpsError:F0} below target, forcing TDP increase (action={ActionDeltas[actionIndex]})");
            }
            else
            {
                // Normal Q-learning action selection

                // State-dependent exploration boost: increase exploration for rarely-visited states
                // This helps the model learn better in unfamiliar situations
                int visits = stateVisits.TryGetValue(state, out int v) ? v : 0;
                double effectiveEpsilon = visits < 10 ? Math.Max(epsilon, 0.15) : epsilon;

                // Epsilon-greedy exploration
                if (random.NextDouble() < effectiveEpsilon)
                {
                    // Explore: random action, but bias based on FPS error direction
                    double r = random.NextDouble();
                    if (fpsError < -3)
                    {
                        // Above target: bias toward decreasing TDP
                        if (r < 0.35) actionIndex = 1;       // -1W (most likely)
                        else if (r < 0.55) actionIndex = 0;  // -2W
                        else if (r < 0.80) actionIndex = 2;  // 0W
                        else if (r < 0.92) actionIndex = 3;  // +1W (unlikely)
                        else actionIndex = 4;                // +2W (very unlikely)
                    }
                    else if (fpsError > 3)
                    {
                        // Below target: bias toward increasing TDP
                        if (r < 0.35) actionIndex = 3;       // +1W (most likely)
                        else if (r < 0.55) actionIndex = 4;  // +2W
                        else if (r < 0.80) actionIndex = 2;  // 0W
                        else if (r < 0.92) actionIndex = 1;  // -1W (unlikely)
                        else actionIndex = 0;                // -2W (very unlikely)
                    }
                    else
                    {
                        // Near target: bias toward stability
                        if (r < 0.50) actionIndex = 2;       // 0W (most likely)
                        else if (r < 0.70) actionIndex = 1;  // -1W (try to save power)
                        else if (r < 0.85) actionIndex = 3;  // +1W
                        else if (r < 0.93) actionIndex = 0;  // -2W
                        else actionIndex = 4;                // +2W
                    }
                }
                else
                {
                    // Exploit: best action based on Q-values
                    actionIndex = GetBestAction(state);
                }
            }

            // Store for learning update
            lastState = state;
            lastAction = actionIndex;

            // Track TDP change for oscillation detection - shift current to previous before updating
            previousTdpChange = currentTdpChange;
            currentTdpChange = ActionDeltas[actionIndex];

            Logger.Debug($"Q-Learning SelectAction: state={state}, action={ActionDeltas[actionIndex]}, fpsErr={fpsError:F1}, tdp%={currentTdpPercent:P0}, override={overrideApplied}");

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
                // Initialize with small values that favor maintaining TDP (action 2 = 0W change)
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
        /// Updates Q-value based on observed reward and next state.
        /// Uses standard Q-learning update: Q(s,a) = Q(s,a) + α * (r + γ * max(Q(s')) - Q(s,a))
        /// </summary>
        public void Update(int nextState, double reward)
        {
            if (lastState < 0 || lastAction < 0)
            {
                Logger.Warn($"Q-Learning Update skipped: lastState={lastState}, lastAction={lastAction}");
                return;
            }

            // Get current Q-value
            double[] qValues = GetQValues(lastState);
            double currentQ = qValues[lastAction];

            // Get max Q-value for next state
            double[] nextQValues = GetQValues(nextState);
            double maxNextQ = nextQValues[0];
            for (int i = 1; i < NumActions; i++)
            {
                if (nextQValues[i] > maxNextQ)
                    maxNextQ = nextQValues[i];
            }

            // Q-learning update
            double newQ = currentQ + alpha * (reward + Gamma * maxNextQ - currentQ);
            qValues[lastAction] = newQ;

            // Update state visit count
            if (!stateVisits.ContainsKey(lastState))
                stateVisits[lastState] = 0;
            stateVisits[lastState]++;

            // Decay hyperparameters
            totalUpdates++;
            alpha = Math.Max(AlphaMin, alpha * AlphaDecay);
            epsilon = Math.Max(EpsilonMin, epsilon * EpsilonDecay);

            // Track cumulative and recent rewards
            cumulativeReward += reward;
            recentRewards.Enqueue(reward);
            recentRewardSum += reward;
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

            Logger.Info($"Q-Learning Update #{totalUpdates}: state={lastState}, action={ActionDeltas[lastAction]}, reward={reward:F2}, " +
                        $"Q: {currentQ:F3} -> {newQ:F3}, α={alpha:F3}, ε={epsilon:F3}");
        }

        /// <summary>
        /// Calculates reward based on FPS error, power efficiency, and ceiling detection.
        /// When headroom is exhausted (ceiling detected), the reward function changes to
        /// optimize for stability and power efficiency rather than hitting target FPS.
        /// </summary>
        /// <param name="fpsError">Current FPS minus target FPS (negative = below target)</param>
        /// <param name="prevTdp">Previous TDP value</param>
        /// <param name="newTdp">New TDP value after action</param>
        /// <param name="headroomExhausted">True if ceiling detected (at max TDP but still below target)</param>
        /// <param name="gpuUtil">Current GPU utilization percentage (0-100)</param>
        /// <param name="achievableFPS">The FPS ceiling estimate (0 if not in ceiling mode)</param>
        /// <param name="currentFPS">The current smoothed FPS value</param>
        /// <param name="minTdp">Minimum TDP limit for calculating overhead penalty</param>
        /// <param name="maxTdp">Maximum TDP limit for calculating overhead penalty</param>
        /// <param name="prevTdpChange">Previous TDP change delta for oscillation detection</param>
        /// <param name="avgFpsPerWatt">Average FPS gained per watt from recent TDP increases (-1 if no data)</param>
        public double CalculateReward(double fpsError, int prevTdp, int newTdp, bool headroomExhausted, double gpuUtil, double achievableFPS = 0, double currentFPS = 0, int minTdp = 8, int maxTdp = 30, int prevTdpChange = 0, double avgFpsPerWatt = -1)
        {
            double totalReward;
            int currentTdpChange = newTdp - prevTdp;

            // Oscillation penalty: penalize rapid direction reversals
            // If we increased TDP last time and are decreasing now (or vice versa), apply penalty
            double oscillationPenalty = 0;
            if (prevTdpChange != 0 && currentTdpChange != 0 && (prevTdpChange * currentTdpChange < 0))
            {
                // Direction reversal detected - this causes instability
                oscillationPenalty = -3.0;
                Logger.Debug($"Oscillation penalty applied: prev={prevTdpChange:+#;-#;0}, curr={currentTdpChange:+#;-#;0}");
            }

            if (headroomExhausted && achievableFPS > 0 && currentFPS > 0)
            {
                // Ceiling detected: Target is unreachable
                // Goal: Maintain FPS near achievable ceiling while being power efficient
                // REBALANCED: Positive rewards for maintaining ceiling

                double fpsFromCeiling = currentFPS - achievableFPS;  // Positive = above ceiling, negative = below
                int tdpChange = newTdp - prevTdp;

                // Primary: Reward maintaining achievable FPS
                double fpsReward;
                if (fpsFromCeiling >= -2 && fpsFromCeiling <= 5)
                {
                    // At or near ceiling - good positive reward
                    fpsReward = 5.0;
                }
                else if (fpsFromCeiling > 5)
                {
                    // Above ceiling (somehow) - still good
                    fpsReward = 4.0;
                }
                else if (fpsFromCeiling >= -5)
                {
                    // Slightly below ceiling - smaller positive
                    fpsReward = 3.0 + fpsFromCeiling * 0.4;  // 3.0 at -0, 1.0 at -5
                }
                else
                {
                    // Significantly below ceiling - capped negative
                    fpsReward = Math.Max(-3.0, fpsFromCeiling * 0.3);
                }

                // Direction bonus for ceiling mode
                double directionBonus = 0;
                if (fpsFromCeiling < -3 && tdpChange > 0)
                {
                    // Below ceiling and increasing TDP to recover - good
                    directionBonus = 2.0;
                }
                else if (fpsFromCeiling >= -2 && tdpChange <= 0)
                {
                    // At ceiling and holding or reducing TDP - good (power saving)
                    directionBonus = 2.0;
                }
                else if (fpsFromCeiling < -3 && tdpChange < 0)
                {
                    // Below ceiling and still decreasing TDP - bad!
                    directionBonus = -4.0;
                }

                // Stability bonus - prefer small or no TDP changes when at ceiling
                double stabilityBonus = 0;
                if (fpsFromCeiling >= -2)
                {
                    if (tdpChange == 0)
                        stabilityBonus = 2.0;
                    else if (Math.Abs(tdpChange) == 1)
                        stabilityBonus = 1.0;
                }

                // Power efficiency bonus when at ceiling - encourage finding minimum TDP
                double powerBonus = 0;
                if (fpsFromCeiling >= -2)
                {
                    // Base bonus for lower TDP while maintaining ceiling
                    powerBonus = Math.Max(0.2, 2.0 - newTdp * 0.04);

                    // Extra bonus for trying to reduce TDP while at ceiling
                    // BUT scale down when already at low TDP to prevent oscillation
                    if (tdpChange < 0)
                    {
                        int tdpRange = maxTdp - minTdp;
                        double tdpPercent = tdpRange > 0 ? (double)(newTdp - minTdp) / tdpRange : 0.5;
                        double decreaseBonus = 2.5 * Math.Min(1.0, tdpPercent * 2.0);
                        decreaseBonus = Math.Max(0.5, decreaseBonus);
                        powerBonus += decreaseBonus;
                    }
                }

                // Efficiency-based penalty at ceiling - use actual FPS/W instead of arbitrary threshold
                // If we have efficiency data and it shows diminishing returns, penalize high TDP
                double efficiencyPenalty = 0;
                if (fpsFromCeiling >= -2 && avgFpsPerWatt >= 0)
                {
                    // We have FPS/W efficiency data
                    if (avgFpsPerWatt < 0.5)
                    {
                        // Diminishing returns detected - TDP increases aren't worth it
                        if (tdpChange > 0)
                        {
                            // Penalize increasing TDP when efficiency is low
                            efficiencyPenalty = -3.0 * tdpChange;
                            Logger.Debug($"Efficiency penalty (ceiling): FPS/W={avgFpsPerWatt:F2} < 0.5, TDP+{tdpChange} penalized");
                        }
                        else if (tdpChange < 0)
                        {
                            // Reward decreasing TDP when we're in diminishing returns
                            efficiencyPenalty = 2.0 * Math.Abs(tdpChange);
                            Logger.Debug($"Efficiency bonus (ceiling): FPS/W={avgFpsPerWatt:F2} < 0.5, TDP{tdpChange} rewarded");
                        }
                        else
                        {
                            // Holding steady at high TDP with low efficiency - slight penalty
                            int tdpRange = maxTdp - minTdp;
                            double tdpPercent = tdpRange > 0 ? (double)(newTdp - minTdp) / tdpRange : 0.5;
                            if (tdpPercent > 0.5)
                            {
                                efficiencyPenalty = -(tdpPercent - 0.5) * 2.0;
                            }
                        }
                    }
                    else
                    {
                        // Good efficiency - TDP increases are still beneficial
                        // Small bonus for finding good efficiency at current TDP
                        if (tdpChange == 0)
                        {
                            efficiencyPenalty = 1.0;  // Reward stable efficient operation
                        }
                    }
                }

                totalReward = fpsReward + directionBonus + stabilityBonus + powerBonus + efficiencyPenalty + oscillationPenalty;

                Logger.Debug($"Reward (ceiling): fpsFromCeil={fpsFromCeiling:F1}, achv={achievableFPS:F0}, FPS/W={avgFpsPerWatt:F2}, " +
                            $"fps={fpsReward:F2}, dir={directionBonus:F2}, stab={stabilityBonus:F2}, " +
                            $"power={powerBonus:F2}, eff={efficiencyPenalty:F2}, osc={oscillationPenalty:F2}, total={totalReward:F2}");
            }
            else
            {
                // Normal operation: Target should be reachable
                // fpsError = targetFPS - currentFPS
                // Positive fpsError = below target (need more TDP)
                // Negative fpsError = above target (can reduce TDP)

                // REBALANCED REWARD FUNCTION: Designed to give positive rewards when doing well
                // Goal: On-target behavior should accumulate positive score over time

                double absError = Math.Abs(fpsError);
                int tdpChange = newTdp - prevTdp;

                // Primary: FPS accuracy reward (centered around positive for good performance)
                double fpsReward;
                if (absError <= 2.0)
                {
                    // Perfect! In dead zone - good positive reward
                    fpsReward = 5.0;
                }
                else if (absError <= 5.0)
                {
                    // Close to target - still positive but smaller
                    fpsReward = 3.0 - (absError - 2.0) * 0.6;  // 3.0 at error=2, 1.2 at error=5
                }
                else if (absError <= 15.0)
                {
                    // Moderately off - neutral to slight negative
                    fpsReward = 1.0 - (absError - 5.0) * 0.3;  // 1.0 at error=5, -2.0 at error=15
                }
                else
                {
                    // Far from target - capped negative
                    fpsReward = -2.0 - Math.Min(absError - 15.0, 30.0) * 0.1;  // Max -5.0
                }

                // Direction bonus: Reward correct actions, penalize wrong ones
                double directionBonus = 0;
                if (absError > 5.0)
                {
                    // We need to act - reward correct direction
                    bool correctDirection = (fpsError > 0 && tdpChange > 0) || (fpsError < 0 && tdpChange < 0);
                    bool wrongDirection = (fpsError > 0 && tdpChange < 0) || (fpsError < 0 && tdpChange > 0);

                    if (correctDirection)
                    {
                        directionBonus = 2.0;
                    }
                    else if (wrongDirection)
                    {
                        directionBonus = -4.0;  // Strong penalty for wrong direction
                    }
                }
                else if (tdpChange == 0)
                {
                    // Near target and holding steady - small bonus
                    directionBonus = 1.0;
                }

                // Stability bonus: Reward small or no changes when on target
                double stabilityBonus = 0;
                if (absError <= 3.0)
                {
                    if (tdpChange == 0)
                        stabilityBonus = 2.0;  // Perfect stability
                    else if (Math.Abs(tdpChange) == 1)
                        stabilityBonus = 1.0;  // Minor adjustment OK
                }

                // Power efficiency bonus - encourages finding minimum TDP
                // When at target (FPS limiter scenario), actively reward TDP reduction attempts
                double powerBonus = 0;
                if (absError <= 2.0)
                {
                    // Base bonus for being efficient (lower TDP = more bonus)
                    // At 8W: +1.6 bonus, at 30W: +0.6 bonus, at 50W: +0.2 bonus
                    powerBonus = Math.Max(0.2, 2.0 - newTdp * 0.04);

                    // Extra bonus for trying to reduce TDP while at target
                    // BUT scale down the bonus when already at low TDP to prevent oscillation
                    if (tdpChange < 0)
                    {
                        int tdpRange = maxTdp - minTdp;
                        double tdpPercent = tdpRange > 0 ? (double)(newTdp - minTdp) / tdpRange : 0.5;

                        // Full bonus (2.5) at high TDP, reduced bonus at low TDP
                        // At 50%+ of range: full 2.5 bonus
                        // At 25% of range: 1.25 bonus
                        // At 10% of range: 0.5 bonus (much more cautious)
                        double decreaseBonus = 2.5 * Math.Min(1.0, tdpPercent * 2.0);
                        decreaseBonus = Math.Max(0.5, decreaseBonus);  // Minimum 0.5 bonus
                        powerBonus += decreaseBonus;
                    }
                }

                // Efficiency-based penalty - use actual FPS/W instead of arbitrary 30% threshold
                // This encourages the model to find the efficiency knee dynamically
                double efficiencyPenalty = 0;
                if (avgFpsPerWatt >= 0)
                {
                    // We have FPS/W efficiency data from recent TDP increases
                    if (avgFpsPerWatt < 0.5)
                    {
                        // Diminishing returns detected - we're past the efficiency knee
                        if (tdpChange > 0)
                        {
                            // Strong penalty for increasing TDP when efficiency is low
                            efficiencyPenalty = -4.0 * tdpChange;
                            Logger.Debug($"Efficiency penalty: FPS/W={avgFpsPerWatt:F2} < 0.5, TDP+{tdpChange} penalized");
                        }
                        else if (tdpChange < 0)
                        {
                            // Reward decreasing TDP when in diminishing returns zone
                            efficiencyPenalty = 3.0 * Math.Abs(tdpChange);
                            Logger.Debug($"Efficiency bonus: FPS/W={avgFpsPerWatt:F2} < 0.5, TDP{tdpChange} rewarded");
                        }
                        else if (absError <= 2.0)
                        {
                            // At target with low efficiency - encourage TDP reduction
                            int tdpRange = maxTdp - minTdp;
                            double tdpPercent = tdpRange > 0 ? (double)(newTdp - minTdp) / tdpRange : 0.5;
                            if (tdpPercent > 0.3)
                            {
                                // Above 30% with low efficiency - should decrease
                                efficiencyPenalty = -(tdpPercent - 0.3) * 3.0;
                            }
                        }
                    }
                    else if (avgFpsPerWatt >= 0.5 && avgFpsPerWatt < 1.5)
                    {
                        // Moderate efficiency - we're near the knee, be careful
                        if (absError <= 2.0 && tdpChange == 0)
                        {
                            // At target with decent efficiency - small reward for stability
                            efficiencyPenalty = 1.0;
                        }
                    }
                    else
                    {
                        // High efficiency (>1.5 FPS/W) - TDP increases are very beneficial
                        // This typically means we're at low TDP where gains are significant
                        if (absError <= 2.0 && tdpChange == 0)
                        {
                            // Settled at efficient TDP - good!
                            efficiencyPenalty = 2.0;
                        }
                    }
                }
                else
                {
                    // No efficiency data yet - use conservative TDP-based heuristic
                    if (absError <= 2.0 && tdpChange == 0)
                    {
                        int tdpRange = maxTdp - minTdp;
                        double tdpPercent = tdpRange > 0 ? (double)(newTdp - minTdp) / tdpRange : 0.5;
                        // Slight preference for lower TDP until we have efficiency data
                        efficiencyPenalty = (0.5 - tdpPercent) * 2.0;
                    }
                }

                totalReward = fpsReward + directionBonus + stabilityBonus + powerBonus + efficiencyPenalty + oscillationPenalty;

                Logger.Debug($"Reward (normal): fpsErr={fpsError:F1}, FPS/W={avgFpsPerWatt:F2}, fps={fpsReward:F2}, dir={directionBonus:F2}, " +
                            $"stab={stabilityBonus:F2}, power={powerBonus:F2}, eff={efficiencyPenalty:F2}, osc={oscillationPenalty:F2}, total={totalReward:F2}");
            }

            return totalReward;
        }

        /// <summary>
        /// Checks if ML is performing poorly and should fall back to PID.
        /// </summary>
        public bool ShouldFallbackToPID(double fpsError)
        {
            if (Math.Abs(fpsError) > PoorPerformanceFpsError)
            {
                consecutivePoorPerformance++;
                if (consecutivePoorPerformance >= FallbackThreshold)
                {
                    Logger.Warn($"ML fallback triggered: {consecutivePoorPerformance} consecutive poor readings (error={fpsError:F1})");
                    return true;
                }
            }
            else
            {
                // Reset counter on good performance
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
        /// Called on cold start to provide reasonable initial behavior.
        /// </summary>
        public void InitializeFromPID()
        {
            Logger.Info("Initializing Q-table with PID-like values (cold start)");
            qTable.Clear();
            stateVisits.Clear();

            // Pre-populate common states with PID-like behavior
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

                            // PID-like initialization based on state
                            // FPS error encoding: fpsError = target - smoothedFPS
                            //   Negative fpsError = FPS ABOVE target (need less TDP)
                            //   Positive fpsError = FPS BELOW target (need more TDP)
                            // FPS error bins: 0-2 = ABOVE target (reduce TDP), 3 = on target, 4-6 = BELOW target (increase TDP)
                            if (fpsErr <= 2)
                            {
                                // FPS is ABOVE target: favor DECREASING TDP for efficiency
                                qValues[0] = 1.0;   // -2W: good for big overshoot
                                qValues[1] = 1.5;   // -1W: best - gradual decrease
                                qValues[2] = 0.0;   // 0W: okay
                                qValues[3] = -1.5;  // +1W: wasteful
                                qValues[4] = -2.0;  // +2W: very wasteful
                            }
                            else if (fpsErr == 3)
                            {
                                // On target: favor DECREASING TDP to find minimum power needed
                                // This is key for power efficiency - we want to probe lower, not hold steady
                                qValues[0] = 0.5;   // -2W: good - probe aggressively
                                qValues[1] = 1.0;   // -1W: best - gradual probe
                                qValues[2] = 0.5;   // 0W: okay - stability has some value
                                qValues[3] = -0.5;  // +1W: unnecessary
                                qValues[4] = -1.0;  // +2W: wasteful
                            }
                            else
                            {
                                // FPS is BELOW target: favor INCREASING TDP
                                qValues[0] = -2.0;  // -2W: very bad - makes FPS worse
                                qValues[1] = -1.5;  // -1W: bad
                                qValues[2] = 0.0;   // 0W: neutral
                                qValues[3] = 1.5;   // +1W: good
                                qValues[4] = 1.0;   // +2W: okay but may overshoot
                            }

                            // Adjust for trend
                            if (trend == 0) // Falling FPS
                            {
                                // Boost increase actions
                                qValues[3] += 0.5;
                                qValues[4] += 0.3;
                            }
                            else if (trend == 2) // Rising FPS
                            {
                                // Boost decrease actions
                                qValues[0] += 0.3;
                                qValues[1] += 0.5;
                            }

                            // Adjust for GPU utilization
                            if (gpuUtil == 3) // 95-100% - GPU bound, TDP increase won't help much
                            {
                                qValues[3] -= 0.3;
                                qValues[4] -= 0.5;
                            }
                            else if (gpuUtil <= 1) // Low GPU usage - power savings possible
                            {
                                qValues[0] += 0.2;
                                qValues[1] += 0.3;
                            }

                            qTable[state] = qValues;
                        }
                    }
                }
            }

            // Reset learning state
            totalUpdates = 0;
            alpha = 0.2;
            epsilon = 0.3;
            isDirty = true;

            Save();
            Logger.Info($"Q-table initialized with {qTable.Count} states");
        }

        /// <summary>
        /// Resets all learning data.
        /// </summary>
        public void Reset()
        {
            Logger.Info("Resetting Q-Learning data");
            qTable.Clear();
            stateVisits.Clear();
            totalUpdates = 0;
            alpha = 0.2;
            epsilon = 0.3;
            lastState = -1;
            lastAction = -1;
            previousTdpChange = 0;
            currentTdpChange = 0;
            consecutivePoorPerformance = 0;
            cumulativeReward = 0;
            recentRewardSum = 0;
            recentRewards.Clear();
            isDirty = true;

            // Initialize with PID-like values for cold start
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
                var saveData = new QLearningData
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
                Logger.Info($"Q-table saved: {qTable.Count} states, {totalUpdates} updates, ε={epsilon:F3}, α={alpha:F3}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save Q-table: {ex.Message}");
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
                    Logger.Info("No existing Q-table found, initializing from PID");
                    InitializeFromPID();
                    return;
                }

                string json = File.ReadAllText(savePath);
                var saveData = JsonSerializer.Deserialize<QLearningData>(json);

                if (saveData == null || saveData.Version != 1)
                {
                    Logger.Warn("Invalid Q-table version, reinitializing");
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

                Logger.Info($"Q-table loaded: {qTable.Count} states, {totalUpdates} updates, ε={epsilon:F3}, α={alpha:F3}, cumulative reward={cumulativeReward:F1}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load Q-table: {ex.Message}, reinitializing");
                InitializeFromPID();
            }
        }

        /// <summary>
        /// Gets the current ML status string for display.
        /// </summary>
        public string GetStatusString()
        {
            int explorationPercent = (int)(epsilon * 100);
            double avgReward = recentRewards.Count > 0 ? recentRewardSum / recentRewards.Count : 0;
            // Show average recent reward (more meaningful than cumulative)
            return $"Updates: {totalUpdates} | Avg: {avgReward:F1} | Exploration: {explorationPercent}%";
        }

        /// <summary>
        /// Gets total number of Q-learning updates performed.
        /// </summary>
        public long TotalUpdates => totalUpdates;

        /// <summary>
        /// Gets current exploration rate (0-1).
        /// </summary>
        public double ExplorationRate => epsilon;

        /// <summary>
        /// Gets current learning rate (0-1).
        /// </summary>
        public double LearningRate => alpha;

        /// <summary>
        /// Gets number of unique states visited.
        /// </summary>
        public int UniqueStatesVisited => stateVisits.Count;

        /// <summary>
        /// Gets cumulative reward accumulated across all updates.
        /// </summary>
        public double CumulativeReward => cumulativeReward;

        /// <summary>
        /// Gets average reward over recent updates (moving average).
        /// </summary>
        public double AverageRecentReward => recentRewards.Count > 0 ? recentRewardSum / recentRewards.Count : 0;

        /// <summary>
        /// Gets the previous TDP change delta for oscillation detection.
        /// </summary>
        public int PreviousTdpChange => previousTdpChange;

        /// <summary>
        /// Data class for JSON serialization.
        /// </summary>
        private class QLearningData
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
