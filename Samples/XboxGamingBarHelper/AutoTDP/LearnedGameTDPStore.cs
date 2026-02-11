using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XboxGamingBarHelper.AutoTDP
{
    /// <summary>
    /// Stores learned TDP values per game for quick startup optimization.
    /// When SARSA finds a stable TDP for a game, it's saved here so next time
    /// the game launches, we can start at the learned TDP instead of probing.
    /// </summary>
    internal class LearnedGameTDPStore
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly string storePath;
        private readonly string heatmapPath;
        private Dictionary<string, LearnedGameTDP> games = new Dictionary<string, LearnedGameTDP>();
        private bool isDirty = false;
        private Dictionary<string, Dictionary<int, int>> heatmaps = new Dictionary<string, Dictionary<int, int>>();
        private bool heatmapDirty = false;
        private DateTime lastSaveUtc = DateTime.MinValue;
        private static readonly TimeSpan MinSaveInterval = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Data structure for a single game's learned TDP.
        /// </summary>
        public class LearnedGameTDP
        {
            [JsonPropertyName("learnedTDP")]
            public int LearnedTDP { get; set; }

            [JsonPropertyName("targetFPS")]
            public int TargetFPS { get; set; }

            [JsonPropertyName("confidence")]
            public double Confidence { get; set; }

            [JsonPropertyName("stableCount")]
            public int StableCount { get; set; }

            [JsonPropertyName("lastUpdated")]
            public DateTime LastUpdated { get; set; }

            [JsonPropertyName("gameName")]
            public string GameName { get; set; }
        }

        public LearnedGameTDPStore(string localStatePath)
        {
            storePath = Path.Combine(localStatePath, "learned_game_tdp.json");
            heatmapPath = Path.Combine(localStatePath, "learned_game_tdp_heatmap.json");
            Load();
            LoadHeatmaps();
        }

        /// <summary>
        /// Gets a unique key for a game based on its executable path.
        /// </summary>
        private string GetGameKey(string gamePath)
        {
            if (string.IsNullOrEmpty(gamePath))
                return null;

            // Use the full path as key (normalized to lowercase for consistency)
            return gamePath.ToLowerInvariant();
        }

        /// <summary>
        /// Gets the learned TDP for a game, or 0 if not found.
        /// Only returns the learned TDP if confidence is high enough and target FPS matches.
        /// </summary>
        public int GetLearnedTDP(string gamePath, string gameName, int targetFPS)
        {
            string key = GetGameKey(gamePath);
            if (key == null)
                return 0;

            if (games.TryGetValue(key, out var data))
            {
                // Only use learned TDP if:
                // 1. Confidence is at least 0.5 (50%)
                // 2. Target FPS matches (different targets need different TDPs)
                // 3. Data is not too old (within 30 days)
                bool confidenceOK = data.Confidence >= 0.5;
                bool targetMatches = data.TargetFPS == targetFPS;
                bool notStale = (DateTime.Now - data.LastUpdated).TotalDays < 30;

                if (confidenceOK && targetMatches && notStale)
                {
                    Logger.Info($"LearnedTDP: Found learned TDP for '{gameName}': {data.LearnedTDP}W @ {data.TargetFPS}FPS (confidence={data.Confidence:P0})");
                    return data.LearnedTDP;
                }
                else
                {
                    Logger.Debug($"LearnedTDP: Found stale/mismatched data for '{gameName}': conf={data.Confidence:P0}, target={data.TargetFPS} (want {targetFPS}), age={(DateTime.Now - data.LastUpdated).TotalDays:F0}d");
                }
            }

            return 0;
        }

        /// <summary>
        /// Records a stable TDP observation for a game.
        /// Call this when SARSA has found a stable TDP (consecutive stays, low frametime variance).
        /// Multiple observations increase confidence.
        /// </summary>
        public void RecordStableTDP(string gamePath, string gameName, int stableTDP, int targetFPS)
        {
            string key = GetGameKey(gamePath);
            if (key == null)
                return;

            if (!games.TryGetValue(key, out var data))
            {
                // New game entry
                data = new LearnedGameTDP
                {
                    LearnedTDP = stableTDP,
                    TargetFPS = targetFPS,
                    Confidence = 0.2,  // Start with 20% confidence
                    StableCount = 1,
                    LastUpdated = DateTime.Now,
                    GameName = gameName
                };
                games[key] = data;
                isDirty = true;
                AddHeatmapSample(key, stableTDP);
                SaveIfNeeded();
                Logger.Info($"LearnedTDP: New game '{gameName}' - initial TDP={stableTDP}W @ {targetFPS}FPS (confidence=20%)");
            }
            else
            {
                // Update existing entry
                data.StableCount++;
                data.LastUpdated = DateTime.Now;
                data.GameName = gameName;  // Update name in case it changed

                // If target FPS changed, reset learning
                if (data.TargetFPS != targetFPS)
                {
                    Logger.Info($"LearnedTDP: Target FPS changed for '{gameName}' ({data.TargetFPS} -> {targetFPS}), resetting learning");
                    data.TargetFPS = targetFPS;
                    data.LearnedTDP = stableTDP;
                    data.Confidence = 0.2;
                    data.StableCount = 1;
                }
                else
                {
                    // Same target FPS - update learned TDP with weighted average
                    // More observations = higher weight for new data
                    double newWeight = Math.Min(0.3, 0.1 + data.StableCount * 0.02);
                    int oldTDP = data.LearnedTDP;
                    data.LearnedTDP = (int)Math.Round((1 - newWeight) * data.LearnedTDP + newWeight * stableTDP);

                    // Increase confidence (approaches 1.0 asymptotically)
                    double oldConfidence = data.Confidence;
                    data.Confidence = Math.Min(0.95, data.Confidence + (1 - data.Confidence) * 0.1);

                    if (data.LearnedTDP != oldTDP || data.Confidence - oldConfidence > 0.05)
                    {
                        Logger.Info($"LearnedTDP: Updated '{gameName}' - TDP={data.LearnedTDP}W (was {oldTDP}W), confidence={data.Confidence:P0}");
                    }
                }

                isDirty = true;
                AddHeatmapSample(key, stableTDP);
                SaveIfNeeded();
            }
        }

        /// <summary>
        /// Marks a learned TDP as unreliable (e.g., if it caused issues on startup).
        /// Reduces confidence so it's less likely to be used next time.
        /// </summary>
        public void MarkUnreliable(string gamePath)
        {
            string key = GetGameKey(gamePath);
            if (key == null)
                return;

            if (games.TryGetValue(key, out var data))
            {
                double oldConfidence = data.Confidence;
                data.Confidence *= 0.5;  // Cut confidence in half
                Logger.Info($"LearnedTDP: Marked unreliable for '{data.GameName}' - confidence {oldConfidence:P0} -> {data.Confidence:P0}");
                isDirty = true;
                SaveIfNeeded();
            }
        }

        /// <summary>
        /// Loads the learned TDP data from disk.
        /// </summary>
        public void Load()
        {
            try
            {
                if (File.Exists(storePath))
                {
                    string json = File.ReadAllText(storePath);
                    games = JsonSerializer.Deserialize<Dictionary<string, LearnedGameTDP>>(json)
                            ?? new Dictionary<string, LearnedGameTDP>();
                    Logger.Info($"LearnedTDP: Loaded {games.Count} game entries from {storePath}");
                }
                else
                {
                    games = new Dictionary<string, LearnedGameTDP>();
                    Logger.Info("LearnedTDP: No saved data found, starting fresh");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"LearnedTDP: Failed to load: {ex.Message}");
                games = new Dictionary<string, LearnedGameTDP>();
            }
            isDirty = false;
        }

        private void LoadHeatmaps()
        {
            try
            {
                if (File.Exists(heatmapPath))
                {
                    string json = File.ReadAllText(heatmapPath);
                    heatmaps = JsonSerializer.Deserialize<Dictionary<string, Dictionary<int, int>>>(json)
                               ?? new Dictionary<string, Dictionary<int, int>>();
                    Logger.Info($"LearnedTDP: Loaded heatmap data for {heatmaps.Count} game entries from {heatmapPath}");
                }
                else
                {
                    heatmaps = new Dictionary<string, Dictionary<int, int>>();
                    Logger.Info("LearnedTDP: No heatmap data found, starting fresh");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"LearnedTDP: Failed to load heatmap: {ex.Message}");
                heatmaps = new Dictionary<string, Dictionary<int, int>>();
            }
            heatmapDirty = false;
        }

        /// <summary>
        /// Saves the learned TDP data to disk.
        /// </summary>
        public void Save()
        {
            if (!isDirty)
                return;

            try
            {
                string json = JsonSerializer.Serialize(games, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(storePath, json);
                Logger.Info($"LearnedTDP: Saved {games.Count} game entries to {storePath}");
                isDirty = false;
                lastSaveUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Error($"LearnedTDP: Failed to save: {ex.Message}");
            }
        }

        private void SaveHeatmaps()
        {
            if (!heatmapDirty)
                return;

            try
            {
                string json = JsonSerializer.Serialize(heatmaps, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(heatmapPath, json);
                Logger.Info($"LearnedTDP: Saved heatmap data for {heatmaps.Count} game entries to {heatmapPath}");
                heatmapDirty = false;
                lastSaveUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Error($"LearnedTDP: Failed to save heatmap: {ex.Message}");
            }
        }

        private void SaveIfNeeded()
        {
            if (!isDirty)
                if (!heatmapDirty)
                    return;

            var now = DateTime.UtcNow;
            if (lastSaveUtc == DateTime.MinValue || (now - lastSaveUtc) >= MinSaveInterval)
            {
                Save();
                SaveHeatmaps();
            }
        }

        private void AddHeatmapSample(string gameKey, int tdp)
        {
            if (string.IsNullOrEmpty(gameKey))
                return;

            if (!heatmaps.TryGetValue(gameKey, out var map))
            {
                map = new Dictionary<int, int>();
                heatmaps[gameKey] = map;
            }

            map.TryGetValue(tdp, out var count);
            map[tdp] = count + 1;
            heatmapDirty = true;
        }

        public IReadOnlyDictionary<string, Dictionary<int, int>> GetHeatmaps()
        {
            return heatmaps;
        }

        /// <summary>
        /// Gets all learned games for display/debugging.
        /// </summary>
        public IReadOnlyDictionary<string, LearnedGameTDP> GetAllGames()
        {
            return games;
        }

        public string GetGameDataJson(string gamePath, string gameName, int targetFPS)
        {
            var payload = new LearnedGameDataPayload
            {
                GameName = gameName ?? "",
                GamePath = gamePath ?? "",
                TargetFPS = targetFPS,
                HasLearned = false,
                LearnedTDP = 0,
                Confidence = 0,
                StableCount = 0,
                LastUpdatedUtc = "",
                Heatmap = new Dictionary<int, int>()
            };

            string key = GetGameKey(gamePath);
            if (key != null)
            {
                if (games.TryGetValue(key, out var data))
                {
                    payload.HasLearned = true;
                    payload.LearnedTDP = data.LearnedTDP;
                    payload.Confidence = data.Confidence;
                    payload.StableCount = data.StableCount;
                    payload.LastUpdatedUtc = data.LastUpdated.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'");
                    if (string.IsNullOrEmpty(payload.GameName))
                    {
                        payload.GameName = data.GameName ?? "";
                    }
                }

                if (heatmaps.TryGetValue(key, out var map) && map != null)
                {
                    payload.Heatmap = new Dictionary<int, int>(map);
                }
            }

            return JsonSerializer.Serialize(payload);
        }

        /// <summary>
        /// Clears all learned TDP data.
        /// </summary>
        public void Clear()
        {
            games.Clear();
            isDirty = true;
            heatmaps.Clear();
            heatmapDirty = true;
            Save();
            SaveHeatmaps();
            Logger.Info("LearnedTDP: Cleared all learned data");
        }

        private class LearnedGameDataPayload
        {
            public string GameName { get; set; }
            public string GamePath { get; set; }
            public int TargetFPS { get; set; }
            public bool HasLearned { get; set; }
            public int LearnedTDP { get; set; }
            public double Confidence { get; set; }
            public int StableCount { get; set; }
            public string LastUpdatedUtc { get; set; }
            public Dictionary<int, int> Heatmap { get; set; }
        }
    }
}
