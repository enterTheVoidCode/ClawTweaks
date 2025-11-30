using RTSSSharedMemoryNET;
using Shared.Enums;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.AutoTDP;
using XboxGamingBarHelper.Legion;
using XboxGamingBarHelper.OnScreenDisplay;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.RTSS.OSDItems;
using XboxGamingBarHelper.Settings;

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

        // OSD configuration per level - stores which items are enabled
        // Level 1 (Basic): Time, FPS, Battery - 3 columns
        // Level 2 (Detailed): Time, FPS, Battery, CPU, GPU, Fan - 1 column
        // Level 3 (Full): All options - 1 column
        private Dictionary<int, HashSet<string>> osdLevelConfig = new Dictionary<int, HashSet<string>>
        {
            { 1, new HashSet<string> { "Time", "FPS", "Battery" } },
            { 2, new HashSet<string> { "Time", "FPS", "Battery", "CPU", "GPU", "Fan" } },
            { 3, new HashSet<string> { "AppName", "Time", "FPS", "Battery", "Memory", "CPU", "CPUClock", "GPU", "GPUClock", "Fan", "AutoTDP" } }
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

            // Initialize FPS limiter
            RTSSFPSLimiter.Initialize();
            osdItemFan = new OSDItemFan();
            osdItemAutoTDP = new OSDItemAutoTDP();
            osdItemCPU = new OSDItemCPU(performanceManager.CPUUsage, performanceManager.CPUClock, performanceManager.CPUWattage, performanceManager.CPUTemperature);
            osdItemGPU = new OSDItemGPU(performanceManager.GPUUsage, performanceManager.GPUClock, performanceManager.GPUWattage, performanceManager.GPUTemperature);
            osdItems = new OSDItem[]
            {
                new OSDItemTime(),
                new OSDItemAppName(),
                new OSDItemFPS(),
                new OSDItemBattery(performanceManager.BatteryLevel, performanceManager.BatteryDischargeRate, performanceManager.BatteryChargeRate, performanceManager.BatteryRemainingTime),
                new OSDItemMemory(performanceManager.MemoryUsage, performanceManager.MemoryUsed),
                osdItemCPU,
                osdItemGPU,
                osdItemFan,
                osdItemAutoTDP,
            };

            rtssState = RivatunerStatisticsServerState.NotInstalled;
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
                    rtssOSD.Update(string.Empty);
                    rtssOSD.Dispose();
                    rtssOSD = null;
                }

                /*var rtssProcess = RTSSHelper.GetProcess();
                if (rtssProcess != null && SettingsManager.GetInstance().AutoStartRTSS)
                {
                    try
                    {
                        Logger.Info("Stopping Rivatuner Statistics Server..");
                        rtssProcess.Kill();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to stop Rivatuner Statistics Server.");
                    }
                }
                rtssState = RivatunerStatisticsServerState.NotRunning;*/

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

            rtssOSD.Update(osdString);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.Info("RTSSManager: Disposing resources");

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
