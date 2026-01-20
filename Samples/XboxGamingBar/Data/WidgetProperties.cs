using NLog;
using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.Data.Json;
using Windows.Foundation.Collections;

namespace XboxGamingBar.Data
{
    internal class WidgetProperties : FunctionalProperties
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public WidgetProperties(params FunctionalProperty[] inProperties) : base(inProperties) { }

        protected override Task<AppServiceResponseStatus> SendResponse(AppServiceRequest request, ValueSet response)
        {
            return request.SendResponseAsync(response).AsTask();
        }

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

        // Properties that should NOT be synced from helper during initial startup.
        // Widget loads these from saved profiles first, so helper's values would be stale/defaults.
        // After initial sync, these ARE synced (e.g., when reconnecting, helper has current state).
        private static readonly HashSet<Function> LightingProperties = new HashSet<Function>
        {
            Function.LegionLightMode,
            Function.LegionLightColor,
            Function.LegionLightBrightness,
            Function.LegionLightSpeed,
            Function.LegionPowerLight
        };

        // Set to true during initial startup to skip lighting sync (widget loaded from profiles)
        // After first sync, this is cleared so subsequent syncs include lighting from helper
        public bool SkipLightingSyncOnce { get; set; } = true;

        /// <summary>
        /// Attempt to sync all properties in a single batch request.
        /// </summary>
        private async Task<bool> TryBatchSync()
        {
            try
            {
                if (!App.IsConnected)
                {
                    Logger.Warn("Cannot batch sync - no connection");
                    return false;
                }

                // Build list of function IDs to request as JSON array
                var jsonArray = new JsonArray();
                bool skipLighting = SkipLightingSyncOnce;
                if (skipLighting)
                {
                    Logger.Info("Skipping lighting properties in batch sync (initial startup - widget loaded from profiles)");
                }

                foreach (var prop in properties.Values)
                {
                    // Skip lighting on initial sync only (widget is source of truth from profiles)
                    // Subsequent syncs include lighting (helper may have applied game profiles)
                    if (skipLighting && LightingProperties.Contains(prop.Function))
                    {
                        continue;
                    }
                    jsonArray.Add(JsonValue.CreateNumberValue((int)prop.Function));
                }

                // Clear the skip flag after this sync
                SkipLightingSyncOnce = false;

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
                    return false;
                }

                if (!response.TryGetValue("BatchData", out object batchDataObj) || !(batchDataObj is string batchDataJson))
                {
                    Logger.Warn("Batch sync response missing BatchData");
                    return false;
                }

                // Parse batch response - format: { "functionId": { "Content": value, "UpdatedTime": time }, ... }
                if (!JsonObject.TryParse(batchDataJson, out JsonObject batchData))
                {
                    Logger.Warn("Failed to parse batch data JSON");
                    return false;
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
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Batch sync failed: {ex.Message}");
                return false;
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
