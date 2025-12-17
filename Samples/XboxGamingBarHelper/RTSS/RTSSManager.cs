using RTSSSharedMemoryNET;
using Shared.Enums;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.AutoTDP;
using XboxGamingBarHelper.Legion;
using XboxGamingBarHelper.OnScreenDisplay;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.RTSS.OSDItems;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Windows;

namespace XboxGamingBarHelper.RTSS
{
    internal class RTSSManager : OnScreenDisplayManager
    {
        private const string OSDSeparator = " <C=6E006A>|<C> ";
        private const string OSDBackground = "<P=0,0><L0><C=80000000><B=0,0>\b<C>";
        private const string OSDAppName = "Xbox Gaming Bar OSD";

        private OSD rtssOSD;
        private readonly OSDItem[] osdItems;
        private readonly OSDItemFan osdItemFan;
        private readonly OSDItemAutoTDP osdItemAutoTDP;
        private readonly OSDItemCPU osdItemCPU;
        private readonly OSDItemGPU osdItemGPU;
        private readonly OSDItemVRAM osdItemVRAM;

        private readonly RTSSInstalledProperty rtssInstalled;
        public RTSSInstalledProperty RTSSInstalled
        {
            get { return rtssInstalled; }
        }

        private readonly OSDConfigProperty osdConfig;
        public OSDConfigProperty OSDConfig
        {
            get { return osdConfig; }
        }

        private readonly FPSLimitProperty fpsLimit;
        public FPSLimitProperty FPSLimit
        {
            get { return fpsLimit; }
        }

        private RivatunerStatisticsServerState rtssState;

        // Frametime graph settings - now using native RTSS buffer (1024 per-frame samples)
        private const int FrametimeHistorySize = 256;  // Show last 256 frames (~4 seconds at 60fps)
        private const int GraphWidth = 256;    // pixels (1 pixel per frame)
        private const int GraphHeight = 32;    // pixels
        private const int GraphMargin = 1;
        private const float GraphMinMs = 0f;
        private const float GraphMaxMs = 50f;  // 50ms = 20 FPS floor
        private readonly float[] frametimeHistory = new float[FrametimeHistorySize];
        private uint frametimeHistoryPos = 0;
        private uint lastEmbeddedGraphSize = 0;
        private float currentMinFt = 0f;
        private float currentAvgFt = 0f;
        private float currentMaxFt = 0f;

        // Fast OSD update timer (for smooth graph updates)
        private Timer osdUpdateTimer;
        private const int DefaultOSDUpdateIntervalMs = 50; // 20Hz default
        private const int MinOSDUpdateIntervalMs = 8;      // Max 120Hz (cap for CPU load)
        private const int MaxOSDUpdateIntervalMs = 100;    // Min 10Hz
        private int currentUpdateIntervalMs = DefaultOSDUpdateIntervalMs;
        private readonly object osdUpdateLock = new object();
        private string cachedOsdString = "";
        private bool osdUpdatePending = false;
        private int cachedForegroundPid = 0;  // Cached foreground process ID for direct API

        // OSD configuration per level - stores which items are enabled
        // Level 1 (Basic): Time, FPS, Battery - 3 columns
        // Level 2 (Detailed): Time, FPS, Battery, CPU, GPU, Fan, FrametimeGraph - 1 column
        // Level 3 (Full): All options - 1 column
        private Dictionary<int, HashSet<string>> osdLevelConfig = new Dictionary<int, HashSet<string>>
        {
            { 1, new HashSet<string> { "Time", "FPS", "Battery" } },
            { 2, new HashSet<string> { "Time", "FPS", "Battery", "CPU", "GPU", "Fan", "FrametimeGraph" } },
            { 3, new HashSet<string> { "AppName", "Time", "FPS", "Battery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "Fan", "AutoTDP", "FrametimeGraph" } }
        };
        private Dictionary<int, string> osdCustomTags = new Dictionary<int, string>
        {
            { 1, "" },
            { 2, "" },
            { 3, "" }
        };

        // Layout settings
        private int osdTextSize = 100;        // Percentage: 50=Small, 100=Medium, 150=Large, 200=X-Large
        private string osdTextColor = "FFFFFF";
        private string osdBackgroundColor = "80000000";

        // Per-level columns (Basic=3, Detailed=1, Full=1)
        private Dictionary<int, int> osdLevelColumns = new Dictionary<int, int>
        {
            { 1, 3 },  // Basic: 3 columns
            { 2, 1 },  // Detailed: 1 column (vertical list)
            { 3, 1 }   // Full: 1 column (vertical list)
        };

        public RTSSManager(PerformanceManager performanceManager, AppServiceConnection connection) : base(connection)
        {
            rtssInstalled = new RTSSInstalledProperty(this);
            osdConfig = new OSDConfigProperty(this);
            fpsLimit = new FPSLimitProperty(this);

            RTSSFPSLimiter.Initialize();
            osdItemFan = new OSDItemFan();
            osdItemAutoTDP = new OSDItemAutoTDP();
            osdItemCPU = new OSDItemCPU(performanceManager.CPUUsage, performanceManager.CPUClock, performanceManager.CPUWattage, performanceManager.CPUTemperature);
            osdItemGPU = new OSDItemGPU(performanceManager.GPUUsage, performanceManager.GPUClock, performanceManager.GPUWattage, performanceManager.GPUTemperature);
            osdItemVRAM = new OSDItemVRAM(performanceManager.GPUMemoryUsed, performanceManager.GPUMemoryFree, performanceManager.GPUMemoryClock);
            osdItems = new OSDItem[]
            {
                new OSDItemTime(),
                new OSDItemAppName(),
                new OSDItemFPS(),
                new OSDItemBattery(performanceManager.BatteryLevel, performanceManager.BatteryDischargeRate, performanceManager.BatteryChargeRate, performanceManager.BatteryRemainingTime),
                osdItemCPU,
                osdItemGPU,
                osdItemVRAM,
                new OSDItemMemory(performanceManager.MemoryUsage, performanceManager.MemoryUsed, performanceManager.MemoryAvailable),
                osdItemFan,
                osdItemAutoTDP,
            };

            rtssState = RivatunerStatisticsServerState.NotInstalled;

            // Start fast OSD update timer for smooth graph updates (adaptive timing)
            osdUpdateTimer = new Timer(FastOSDUpdate, null, DefaultOSDUpdateIntervalMs, DefaultOSDUpdateIntervalMs);
        }

        /// <summary>
        /// Fast timer callback for updating the OSD graph at high frequency.
        /// Uses EmbedGraphDirect for optimal performance - reads directly from RTSS shared memory.
        /// Adapts update rate to match game FPS.
        /// </summary>
        private void FastOSDUpdate(object state)
        {
            // Early exit checks - avoid any work if not properly initialized
            if (rtssOSD == null || onScreenDisplayLevel == 0)
                return;

            if (!IsItemEnabled("FrametimeGraph"))
                return;

            // Verify RTSS is still running before attempting any operations
            if (!RTSSHelper.IsRunning())
                return;

            // Verify process is still valid before using cached PID
            int pid = cachedForegroundPid;
            if (pid <= 0)
                return;

            try
            {
                // Update the graph in RTSS using direct API
                lock (osdUpdateLock)
                {
                    // Re-check rtssOSD inside lock as it could be disposed
                    if (string.IsNullOrEmpty(cachedOsdString) || rtssOSD == null)
                        return;

                    // Use EmbedGraphDirect - reads directly from RTSS shared memory
                    // Eliminates intermediate copy operations
                    float minFt, avgFt, maxFt;
                    uint graphOffset = 0;
                    var flags = (EMBEDDED_OBJECT_GRAPH)0;

                    uint graphSize = rtssOSD.EmbedGraphDirect(
                        graphOffset,
                        (uint)pid,
                        (uint)FrametimeHistorySize,
                        GraphWidth,
                        GraphHeight,
                        GraphMargin,
                        GraphMinMs,
                        GraphMaxMs,
                        flags,
                        out minFt,
                        out avgFt,
                        out maxFt);

                    if (graphSize > 0)
                    {
                        // Update cached statistics from direct API
                        currentMinFt = minFt;
                        currentAvgFt = avgFt;
                        currentMaxFt = maxFt;
                        lastEmbeddedGraphSize = graphSize;

                        // Update the OSD with cached string (graph data is in buffer)
                        rtssOSD.Update(cachedOsdString);

                        // Adapt update rate to match game FPS
                        AdjustUpdateInterval();
                    }
                    else
                    {
                        // Graph embedding failed - process may have exited, clear cached PID
                        cachedForegroundPid = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"FastOSDUpdate error: {ex.Message}");
                // Clear cached PID on error to force re-detection
                cachedForegroundPid = 0;
            }
        }

        /// <summary>
        /// Gets the game process ID from RTSS app entries.
        /// Prioritizes the foreground window if it's in RTSS, otherwise falls back to first valid app.
        /// </summary>
        private int GetRTSSGameProcessId()
        {
            try
            {
                var appEntries = OSD.GetAppEntries(AppFlags.MASK);
                if (appEntries == null || appEntries.Length == 0)
                    return 0;

                int foregroundPid = User32.GetForegroundProcessId();
                int fallbackPid = 0;

                foreach (var entry in appEntries)
                {
                    if (entry.StatFrameTimeBuf != null && entry.StatFrameTimeBuf.Length > 0)
                    {
                        // Check if this is the foreground app
                        if (entry.ProcessId == foregroundPid)
                        {
                            return entry.ProcessId; // Found foreground app in RTSS
                        }
                        // Keep track of first valid entry as fallback
                        if (fallbackPid == 0)
                        {
                            fallbackPid = entry.ProcessId;
                        }
                    }
                }

                return fallbackPid; // Return fallback if foreground not in RTSS
            }
            catch (Exception ex)
            {
                Logger.Debug($"GetRTSSGameProcessId error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Adjusts the OSD update timer interval to match the game's framerate.
        /// This ensures the graph updates at a rate appropriate for the game's FPS.
        /// </summary>
        private void AdjustUpdateInterval()
        {
            try
            {
                if (cachedForegroundPid == 0)
                    return;

                // Get current game framerate from RTSS
                uint gameFramerate = OSD.GetAppFramerate((uint)cachedForegroundPid);

                if (gameFramerate > 0)
                {
                    // Calculate target interval to match game FPS (capped)
                    int targetInterval = Math.Max(MinOSDUpdateIntervalMs,
                        Math.Min(MaxOSDUpdateIntervalMs, (int)(1000.0f / gameFramerate)));

                    // Only adjust if significantly different (avoid constant changes)
                    if (Math.Abs(targetInterval - currentUpdateIntervalMs) > 5)
                    {
                        currentUpdateIntervalMs = targetInterval;
                        osdUpdateTimer.Change(targetInterval, targetInterval);
                        Logger.Debug($"Graph update rate adjusted to {1000 / targetInterval}Hz (game: {gameFramerate}fps)");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"AdjustUpdateInterval error: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses the OSD configuration string from the widget.
        /// Format: "Position:0;Columns:3;TextSize:100;TextColor:FFFFFF;BackgroundColor:80000000;L1:FPS,Battery;L2:...;L1_Custom:tags"
        /// </summary>
        public void ParseOSDConfig(string configString)
        {
            if (string.IsNullOrEmpty(configString))
            {
                Logger.Warn("Empty OSD config string received");
                return;
            }

            Logger.Info($"Parsing OSD config: {configString}");

            try
            {
                var parts = configString.Split(';');
                foreach (var part in parts)
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;

                    var colonIndex = part.IndexOf(':');
                    if (colonIndex <= 0) continue;

                    var key = part.Substring(0, colonIndex);
                    var value = colonIndex < part.Length - 1 ? part.Substring(colonIndex + 1) : "";

                    // Layout settings
                    if (key == "TextSize")
                    {
                        if (int.TryParse(value, out int size))
                        {
                            osdTextSize = size;
                            Logger.Debug($"OSD TextSize: {size}");
                        }
                    }
                    else if (key == "TextColor")
                    {
                        osdTextColor = value;
                        Logger.Debug($"OSD TextColor: {value}");
                    }
                    else if (key == "BackgroundColor")
                    {
                        osdBackgroundColor = value;
                        Logger.Debug($"OSD BackgroundColor: {value}");
                    }
                    else if (key.StartsWith("L") && key.EndsWith("_Columns"))
                    {
                        // Per-level columns: L1_Columns, L2_Columns, L3_Columns
                        var levelStr = key.Substring(1, key.Length - 9); // "L1_Columns" -> "1"
                        if (int.TryParse(levelStr, out int level) && int.TryParse(value, out int cols))
                        {
                            osdLevelColumns[level] = cols;
                            Logger.Debug($"OSD Level {level} columns: {cols}");
                        }
                    }
                    else if (key.StartsWith("L") && key.EndsWith("_Custom"))
                    {
                        // Custom tags: L1_Custom, L2_Custom, L3_Custom
                        var levelStr = key.Substring(1, key.Length - 8);
                        if (int.TryParse(levelStr, out int level))
                        {
                            osdCustomTags[level] = value;
                            Logger.Debug($"OSD Level {level} custom tags: {value}");
                        }
                    }
                    else if (key.StartsWith("L"))
                    {
                        // Level config: L1, L2, L3
                        var levelStr = key.Substring(1);
                        if (int.TryParse(levelStr, out int level))
                        {
                            var items = new HashSet<string>();
                            if (!string.IsNullOrEmpty(value))
                            {
                                foreach (var item in value.Split(','))
                                {
                                    if (!string.IsNullOrWhiteSpace(item))
                                    {
                                        items.Add(item.Trim());
                                    }
                                }
                            }
                            osdLevelConfig[level] = items;
                            Logger.Debug($"OSD Level {level} items: {string.Join(", ", items)}");
                        }
                    }
                }

                Logger.Info("OSD configuration updated successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error parsing OSD config: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the given OSD item should be shown for the current level.
        /// </summary>
        private bool IsItemEnabled(string itemId)
        {
            if (osdLevelConfig.TryGetValue(onScreenDisplayLevel, out var enabledItems))
            {
                return enabledItems.Contains(itemId);
            }
            return false;
        }

        /// <summary>
        /// Sets the Legion Manager reference for fan speed OSD support.
        /// Must be called after LegionManager is initialized.
        /// </summary>
        public void SetLegionManager(LegionManager legionManager)
        {
            osdItemFan.SetLegionManager(legionManager);
            Logger.Info("LegionManager reference set for RTSS OSD fan speed");
        }

        /// <summary>
        /// Sets the AutoTDP Manager reference for AutoTDP OSD support.
        /// Must be called after AutoTDPManager is initialized.
        /// </summary>
        public void SetAutoTDPManager(AutoTDPManager autoTDPManager)
        {
            osdItemAutoTDP.SetAutoTDPManager(autoTDPManager);
            Logger.Info("AutoTDPManager reference set for RTSS OSD AutoTDP status");
        }

        public override void Update()
        {
            base.Update();

            var isRTSSInstalled = RTSSHelper.IsInstalled();
            if (rtssInstalled.Value != isRTSSInstalled)
                rtssInstalled.SetValue(isRTSSInstalled);

            if (!isRTSSInstalled)
            {
                Logger.Debug("Rivatuner Statistics Server is not installed.");
                rtssState = RivatunerStatisticsServerState.NotInstalled;
                return;
            }

            if (onScreenDisplayLevel == 0)
            {
                if (rtssOSD != null)
                {
                    try
                    {
                        rtssOSD.Update(string.Empty);
                        rtssOSD.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Error clearing OSD: {ex.Message}");
                    }
                    rtssOSD = null;
                }

                return;
            }

            if (!RTSSHelper.IsRunning())
            {
                if (SettingsManager.GetInstance().AutoStartRTSS)
                {
                    if (rtssState == RivatunerStatisticsServerState.Starting)
                    {
                        Logger.Info("Starting Rivatuner Statistics Server..");
                    }
                    else
                    {
                        rtssState = RivatunerStatisticsServerState.Starting;
                        try
                        {
                            Logger.Info("Start Rivatuner Statistics Server.");
                            Process.Start(RTSSHelper.ExecutablePath());
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Failed to start Rivatuner Statistics Server.");
                            rtssState = RivatunerStatisticsServerState.NotRunning;
                        }
                    }
                }
                return;
            }

            rtssState = RivatunerStatisticsServerState.Running;

            if (rtssOSD == null)
            {
                rtssOSD = new OSD(OSDAppName);
            }

            // Update cached game PID for direct graph API
            // Gets the RTSS app entry that matches the foreground window, or falls back to first valid app
            if (IsItemEnabled("FrametimeGraph"))
            {
                cachedForegroundPid = GetRTSSGameProcessId();
            }

            // Update clock display settings based on config
            osdItemCPU.SetShowClock(IsItemEnabled("CPUClock"));
            osdItemGPU.SetShowClock(IsItemEnabled("GPUClock"));

            // Set text color on all items so they can reset to it after colored names
            foreach (var item in osdItems)
            {
                item.SetTextColor(osdTextColor);
            }

            // Build OSD header
            string osdString = BuildOSDHeader();

            // Apply text size if not default
            if (osdTextSize != 100)
            {
                osdString += $"<S={osdTextSize}>";
            }

            // Apply default text color (use white as base for dynamic mode)
            var baseTextColor = osdTextColor == "DYNAMIC" ? "FFFFFF" : osdTextColor;
            osdString += $"<C={baseTextColor}>";

            // Collect all enabled items
            var enabledItems = new List<string>();
            for (int i = 0; i < osdItems.Length; i++)
            {
                var item = osdItems[i];

                // Check if this item is enabled for the current level
                if (!IsItemEnabled(item.Id))
                    continue;

                var osdItemString = item.GetOSDString(onScreenDisplayLevel);
                if (string.IsNullOrEmpty(osdItemString))
                    continue;

                enabledItems.Add(osdItemString);
            }

            // Add custom tags if configured for this level
            if (osdCustomTags.TryGetValue(onScreenDisplayLevel, out var customTags) && !string.IsNullOrWhiteSpace(customTags))
            {
                enabledItems.Add(customTags);
            }

            // Build output with columns - use per-level setting
            int itemsPerRow = 3; // Fallback default
            if (osdLevelColumns.TryGetValue(onScreenDisplayLevel, out int levelColumns) && levelColumns > 0)
            {
                itemsPerRow = levelColumns;
            }
            for (int i = 0; i < enabledItems.Count; i++)
            {
                if (i > 0)
                {
                    // Check if we need a newline (new row)
                    if (i % itemsPerRow == 0)
                    {
                        osdString += "\n";
                    }
                    else
                    {
                        osdString += OSDSeparator;
                    }
                }
                osdString += enabledItems[i];
            }

            // Close text size tag if used
            if (osdTextSize != 100)
            {
                osdString += "<S>";
            }

            // Embed frametime graph if enabled and add reference tag
            if (IsItemEnabled("FrametimeGraph") && cachedForegroundPid != 0)
            {
                try
                {
                    uint graphOffset = 0;
                    // No flags = transparent line graph (no fill, no background)
                    var flags = (EMBEDDED_OBJECT_GRAPH)0;

                    // Use EmbedGraphDirect for optimal performance - reads directly from RTSS shared memory
                    float minFt, avgFt, maxFt;
                    lastEmbeddedGraphSize = rtssOSD.EmbedGraphDirect(
                        graphOffset,
                        (uint)cachedForegroundPid,
                        (uint)FrametimeHistorySize,
                        GraphWidth,
                        GraphHeight,
                        GraphMargin,
                        GraphMinMs,
                        GraphMaxMs,
                        flags,
                        out minFt,
                        out avgFt,
                        out maxFt);

                    if (lastEmbeddedGraphSize > 0)
                    {
                        // Update cached statistics from direct API
                        currentMinFt = minFt;
                        currentAvgFt = avgFt;
                        currentMaxFt = maxFt;

                        // Add min/avg/max labels in smaller text above graph
                        string statsLabel = "";
                        if (currentMinFt > 0 && currentMaxFt > 0)
                        {
                            // Smaller text (50%) for stats: "min: X.Xms  avg: X.Xms  max: X.Xms"
                            statsLabel = $"<S=50><C=808080>min:<C=00FF00>{currentMinFt:F1}ms <C=808080>avg:<C=FFFF00>{currentAvgFt:F1}ms <C=808080>max:<C=FF6600>{currentMaxFt:F1}ms<C><S>\n";
                        }
                        // Add Y-axis scale legend vertically: max above graph, min below graph
                        // Layout:
                        //   stats line
                        //   50ms  <- max at top
                        //   [graph]
                        //   0ms   <- min at bottom
                        string yAxisMax = $"<S=50><C=808080>{GraphMaxMs:F0}ms<C><S>";
                        string yAxisMin = $"<S=50><C=808080>{GraphMinMs:F0}ms<C><S>";
                        osdString += $"\n{statsLabel}{yAxisMax}\n<OBJ={graphOffset:X8}>\n{yAxisMin}";
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Failed to embed frametime graph: {ex.Message}");
                }
            }

            // Cache the OSD string for fast timer updates
            lock (osdUpdateLock)
            {
                cachedOsdString = osdString;
            }

            try
            {
                rtssOSD.Update(osdString);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error updating OSD: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the OSD header string.
        /// Note: Position and background are controlled by RTSS application settings.
        /// RTSS background requires complex <B=x,y> bar drawing with \b backspace which
        /// doesn't work well with dynamic content.
        /// </summary>
        private string BuildOSDHeader()
        {
            // Background not supported via simple tags - must be configured in RTSS app
            return "";
        }

        /// <summary>
        /// Collects frametime data from RTSS for the frametime graph.
        /// Uses the native RTSS frametime buffer (StatFrameTimeBuf) which contains
        /// per-frame data (up to 1024 samples at actual framerate).
        /// Frametime values are in microseconds, converted to milliseconds for display.
        /// </summary>
        private void CollectFrametimeData()
        {
            try
            {
                var appEntries = OSD.GetAppEntries(AppFlags.MASK);
                if (appEntries == null || appEntries.Length == 0)
                    return;

                // Get the foreground window's process ID to prioritize that app's data
                int foregroundPid = User32.GetForegroundProcessId();
                AppEntry targetEntry = null;
                AppEntry fallbackEntry = null;

                // Find the matching app entry - prioritize foreground window
                foreach (var entry in appEntries)
                {
                    if (entry.StatFrameTimeBuf != null && entry.StatFrameTimeBuf.Length > 0)
                    {
                        // Check if this is the foreground app
                        if (entry.ProcessId == foregroundPid)
                        {
                            targetEntry = entry;
                            break; // Found foreground app, use it
                        }
                        // Keep track of first valid entry as fallback
                        if (fallbackEntry == null)
                        {
                            fallbackEntry = entry;
                        }
                    }
                }

                // Use foreground app if found, otherwise fallback to first valid app
                var selectedEntry = targetEntry ?? fallbackEntry;
                if (selectedEntry == null)
                    return;

                // Process the selected app's frametime data
                uint nativeBufSize = (uint)selectedEntry.StatFrameTimeBuf.Length; // 1024
                uint nativePos = selectedEntry.StatFrameTimeBufPos;

                // Copy the most recent FrametimeHistorySize samples from RTSS's circular buffer
                // RTSS buffer is 1024 entries, we take the last 256
                float minFt = float.MaxValue;
                float maxFt = 0f;
                float sumFt = 0f;
                int validSamples = 0;

                for (int i = 0; i < FrametimeHistorySize; i++)
                {
                    // Calculate index in RTSS circular buffer
                    // Start from (nativePos - FrametimeHistorySize) and go forward
                    uint srcIndex = (nativePos - (uint)FrametimeHistorySize + (uint)i + nativeBufSize) % nativeBufSize;

                    // Convert from microseconds to milliseconds
                    float frametimeMs = selectedEntry.StatFrameTimeBuf[srcIndex] / 1000.0f;

                    // Store in our buffer (linear, starting from position 0)
                    frametimeHistory[i] = frametimeMs;

                    // Track min/avg/max (only non-zero values)
                    if (frametimeMs > 0 && frametimeMs < 1000)
                    {
                        validSamples++;
                        sumFt += frametimeMs;
                        if (frametimeMs < minFt) minFt = frametimeMs;
                        if (frametimeMs > maxFt) maxFt = frametimeMs;
                    }
                }

                // Set buffer position to end (since we copied linearly)
                frametimeHistoryPos = 0;

                if (validSamples > 0)
                {
                    currentMinFt = minFt;
                    currentAvgFt = sumFt / validSamples;
                    currentMaxFt = maxFt;
                }
                else
                {
                    currentMinFt = 0f;
                    currentAvgFt = 0f;
                    currentMaxFt = 0f;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error collecting frametime data: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.Info("RTSSManager: Disposing resources");

                // Stop the fast OSD update timer
                if (osdUpdateTimer != null)
                {
                    osdUpdateTimer.Dispose();
                    osdUpdateTimer = null;
                    Logger.Info("RTSSManager: OSD update timer disposed");
                }

                // Shutdown FPS limiter
                RTSSFPSLimiter.Shutdown();

                if (rtssOSD != null)
                {
                    try
                    {
                        rtssOSD.Update(string.Empty);
                        rtssOSD.Dispose();
                        rtssOSD = null;
                        Logger.Info("RTSSManager: RTSS OSD disposed");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"RTSSManager: Error disposing RTSS OSD: {ex.Message}");
                    }
                }
            }
            base.Dispose(disposing);
        }
    }
}
