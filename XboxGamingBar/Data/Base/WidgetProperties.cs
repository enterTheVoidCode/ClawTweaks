using NLog;
using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;

namespace XboxGamingBar.Data
{
    internal class WidgetProperties : FunctionalProperties
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public WidgetProperties(params FunctionalProperty[] inProperties) : base(inProperties) { }

        /// <summary>
        /// Batch sync all properties in a single message for much faster performance.
        /// Falls back to individual sync if batch fails.
        /// </summary>
        public async Task Sync()
        {
            var syncTimer = Stopwatch.StartNew();

            // Try batch sync first
            if (await TryBatchSync())
            {
                // Batch sync bypasses individual Sync() calls which enable controls.
                // Call OnBatchSyncCompleted() on each property to enable controls.
                foreach (var property in properties.Values)
                {
                    await property.OnBatchSyncCompleted();
                }

                syncTimer.Stop();
                Logger.Info($"[TIMING] Batch sync {properties.Count} properties: {syncTimer.ElapsedMilliseconds}ms");
                return;
            }

            // Fallback to individual sync
            Logger.Warn("Batch sync failed, falling back to individual sync");
            int count = 0;
            foreach (var property in properties)
            {
                await property.Value.Sync();
                count++;
            }
            syncTimer.Stop();
            Logger.Info($"[TIMING] Individual sync {count} properties: {syncTimer.ElapsedMilliseconds}ms ({syncTimer.ElapsedMilliseconds / Math.Max(1, count)}ms avg)");
        }

        // Reserved for future startup-only skipping. Currently empty — lighting moved to
        // NeverSyncFromHelper because the "helper has current state after initial sync"
        // assumption is unreliable: if the first send races pipe connect, the helper's
        // default (#FFFFFF) leaks back into the widget on the next batch sync and then
        // into the profile on the next SaveControllerProfile, turning the lights white.
        private static readonly HashSet<Function> WidgetOwnedPropertiesInitial = new HashSet<Function>();

        // Properties that should NEVER be synced from helper - widget is always the source of truth.
        // The helper applies values to hardware but doesn't persist them across restarts,
        // so the widget (which loads from controller profiles) is authoritative.
        private static readonly HashSet<Function> NeverSyncFromHelper = new HashSet<Function>
        {
            // OSD level - loaded from LocalSettings (PerformanceOverlayLevel)
            Function.OSD,
            // Lighting - loaded from controller profiles; helper defaults to #FFFFFF
            Function.LegionLightMode,
            Function.LegionLightColor,
            Function.LegionLightBrightness,
            Function.LegionLightSpeed,
            Function.LegionPowerLight
        };

        // Set to true during initial startup to skip widget-owned property sync (widget loaded from profiles/settings)
        // After first sync, this is cleared so subsequent syncs include these from helper
        public bool SkipWidgetOwnedSyncOnce { get; set; } = true;

        /// <summary>
        /// Attempt to sync all properties in a single batch request.
        /// Retries if helper returns NotReady (managers still initializing).
        /// </summary>
        private async Task<bool> TryBatchSync()
        {
            const int maxNotReadyRetries = 30;  // 30 retries x 500ms = 15 seconds max wait
            const int notReadyRetryDelayMs = 500;

            for (int attempt = 0; attempt < maxNotReadyRetries; attempt++)
            {
                var result = await TryBatchSyncOnce(attempt > 0);
                if (result == BatchSyncResult.Success)
                {
                    return true;
                }
                else if (result == BatchSyncResult.NotReady)
                {
                    Logger.Info($"Batch sync: Helper not ready (attempt {attempt + 1}/{maxNotReadyRetries}), retrying in {notReadyRetryDelayMs}ms...");
                    await Task.Delay(notReadyRetryDelayMs);
                    continue;
                }
                else
                {
                    // Other failure - don't retry
                    return false;
                }
            }

            Logger.Warn($"Batch sync: Helper still not ready after {maxNotReadyRetries} attempts");
            return false;
        }

        private enum BatchSyncResult
        {
            Success,
            NotReady,
            Failed
        }

        /// <summary>
        /// Single attempt at batch sync.
        /// </summary>
        private async Task<BatchSyncResult> TryBatchSyncOnce(bool isRetry)
        {
            try
            {
                if (!App.IsConnected)
                {
                    Logger.Warn("Cannot batch sync - no connection");
                    return BatchSyncResult.Failed;
                }

                // Build list of function IDs to request as JSON array
                var jsonArray = new JsonArray();
                // Always skip widget-owned properties during initial startup, even on retry
                // The issue was that retries would include these properties, causing the helper's
                // default values (e.g., #FFFFFF for lighting) to overwrite profile values
                bool skipWidgetOwnedInitial = SkipWidgetOwnedSyncOnce;
                if (skipWidgetOwnedInitial)
                {
                    Logger.Info("Skipping widget-owned properties in batch sync (initial startup - widget loaded from settings/profiles)");
                }

                foreach (var prop in properties.Values)
                {
                    // Always skip properties that should never be synced from helper
                    if (NeverSyncFromHelper.Contains(prop.Function))
                    {
                        continue;
                    }
                    // Skip widget-owned properties on initial sync only (widget is source of truth from profiles)
                    // Subsequent syncs include them (helper may have applied game profiles)
                    if (skipWidgetOwnedInitial && WidgetOwnedPropertiesInitial.Contains(prop.Function))
                    {
                        continue;
                    }
                    jsonArray.Add(JsonValue.CreateNumberValue((int)prop.Function));
                }

                // Clear the skip flag after successful sync (not during NotReady retries)
                // Moved to after success check below

                // Create batch request
                var request = new ValueSet
                {
                    { "Command", (int)Command.BatchGet },
                    { "Functions", jsonArray.Stringify() }
                };

                var response = await App.SendMessageAsync(request);
                if (response == null)
                {
                    Logger.Warn("Batch sync got null response");
                    return BatchSyncResult.Failed;
                }

                // Check if helper returned NotReady (managers still initializing)
                if (response.TryGetValue("NotReady", out object notReadyObj) && notReadyObj is bool notReady && notReady)
                {
                    return BatchSyncResult.NotReady;
                }

                if (!response.TryGetValue("BatchData", out object batchDataObj) || !(batchDataObj is string batchDataJson))
                {
                    Logger.Warn("Batch sync response missing BatchData");
                    return BatchSyncResult.Failed;
                }

                // Clear the skip flag after first successful sync
                SkipWidgetOwnedSyncOnce = false;

                // Parse batch response - format: { "functionId": { "Content": value, "UpdatedTime": time }, ... }
                if (!JsonObject.TryParse(batchDataJson, out JsonObject batchData))
                {
                    Logger.Warn("Failed to parse batch data JSON");
                    return BatchSyncResult.Failed;
                }

                int updated = 0;
                foreach (var property in properties.Values)
                {
                    var funcId = ((int)property.Function).ToString();
                    if (batchData.ContainsKey(funcId))
                    {
                        try
                        {
                            var propData = batchData.GetNamedObject(funcId);
                            if (propData.ContainsKey("Content") && propData.ContainsKey("UpdatedTime"))
                            {
                                var updatedTime = (long)propData.GetNamedNumber("UpdatedTime");
                                object content = GetJsonValue(propData, "Content");
                                if (content != null)
                                {
                                    // Suppress remote sync to avoid echoing values back to helper
                                    // This prevents widget from overwriting helper-owned properties like RunningGame, DGP
                                    property.SuppressRemoteSync = true;
                                    try
                                    {
                                        if (property.SetValue(content, updatedTime))
                                        {
                                            updated++;
                                        }
                                    }
                                    finally
                                    {
                                        property.SuppressRemoteSync = false;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"Failed to parse property {property.Function}: {ex.Message}");
                        }
                    }
                }

                Logger.Info($"Batch sync updated {updated}/{properties.Count} properties");
                return BatchSyncResult.Success;
            }
            catch (Exception ex)
            {
                Logger.Error($"Batch sync failed: {ex.Message}");
                return BatchSyncResult.Failed;
            }
        }

        /// <summary>
        /// Get a value from a JsonObject, handling different value types.
        /// </summary>
        private object GetJsonValue(JsonObject obj, string key)
        {
            var value = obj.GetNamedValue(key);
            switch (value.ValueType)
            {
                case JsonValueType.String:
                    return value.GetString();
                case JsonValueType.Number:
                    var num = value.GetNumber();
                    // Return as int if it's a whole number
                    if (num == Math.Floor(num) && num >= int.MinValue && num <= int.MaxValue)
                        return (int)num;
                    return num;
                case JsonValueType.Boolean:
                    return value.GetBoolean();
                case JsonValueType.Null:
                    return null;
                case JsonValueType.Array:
                    // Convert JSON array to comma-separated string for GenericProperty.SetValue
                    // which expects "1920x1080,1280x720" not ["1920x1080","1280x720"]
                    var array = value.GetArray();
                    var items = new List<string>();
                    foreach (var item in array)
                    {
                        if (item.ValueType == JsonValueType.String)
                            items.Add(item.GetString());
                        else if (item.ValueType == JsonValueType.Number)
                            items.Add(((int)item.GetNumber()).ToString());
                        else
                            items.Add(item.Stringify());
                    }
                    return string.Join(",", items);
                case JsonValueType.Object:
                    return value.Stringify();
                default:
                    return null;
            }
        }

        public void Cleanup()
        {
            foreach (var property in properties)
            {
                if (property.Value is WidgetSliderProperty sliderProperty)
                {
                    sliderProperty.Cleanup();
                }
            }
        }

        public void StopPendingUpdates()
        {
            foreach (var property in properties)
            {
                if (property.Value is WidgetSliderProperty sliderProperty)
                {
                    sliderProperty.StopDebounceTimer();
                }
            }
        }
    }
}
