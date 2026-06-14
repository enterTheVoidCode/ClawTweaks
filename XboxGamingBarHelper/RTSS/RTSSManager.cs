using RTSSSharedMemoryNET;
using Shared.Enums;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using XboxGamingBarHelper.AutoTDP;
using XboxGamingBarHelper.Devices.Libraries.Legion;
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
        private const string OSDAppName = "GoTweaks OSD";

        // Set true while an in-app RTSS uninstall runs, so this manager's Update loop does not
        // relaunch RTSS — otherwise the NSIS uninstaller aborts because the app is still running.
        internal static volatile bool SuppressAutoStart = false;

        private OSD rtssOSD;
        private readonly OSDItem[] osdItems;
        private readonly OSDItemFan osdItemFan;
        private readonly OSDItemAutoTDP osdItemAutoTDP;
        private readonly OSDItemTDPLimits osdItemTDPLimits;
        private readonly OSDItemCPU osdItemCPU;
        private readonly OSDItemCPUCores osdItemCPUCores;
        private readonly OSDItemBlankLine osdItemBlank1, osdItemBlank2, osdItemBlank3, osdItemBlank4;
        private readonly OSDItemCPUWatts osdItemCPUWatts;
        private readonly OSDItemFPS osdItemFPS;
        private readonly OSDItemGPU osdItemGPU;
        private readonly OSDItemVRAM osdItemVRAM;
        private readonly OSDItemControllerBattery osdItemControllerBattery;

        // Active FPS cap info — set by Program.cs via SetFpsCapDisplay() whenever a limiter changes.
        private int _fpsCapDisplayValue = 0;
        private bool _fpsCapDisplayIsIntel = false;

        // Transient OSD notification (shown for a few seconds when an action tile fires)
        private string _pendingNotification = null;
        private DateTime _notificationExpiry = DateTime.MinValue;

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

        private DisplayOSDConfigProperty displayOSDConfig;
        public DisplayOSDConfigProperty DisplayOSDConfig
        {
            get { return displayOSDConfig; }
        }

        private RivatunerStatisticsServerState rtssState;

        // Frametime graph settings - uses <G=<FT>> tag processed by RTSSSharedMemoryNET
        // Width/height are hardcoded in ProcessGraphTags (-32 chars wide, -2 lines tall)
        private float currentMinFt = 0f;
        private float currentAvgFt = 0f;
        private float currentMaxFt = 0f;
        private float current1PercentLowFps = 0f;  // 1% low = framerate at the 99th-percentile (slowest 1%) frametime

        // Public frametime stats for stability detection (used by AutoTDPManager)
        public float FrametimeMin => currentMinFt;
        public float FrametimeAvg => currentAvgFt;
        public float FrametimeMax => currentMaxFt;
        public float FrametimeVariance => currentMaxFt - currentMinFt;  // Max-Min variance in ms

        // OSD configuration per level - stores which items are enabled
        // Level 1 (Basic):              Time, FPS, Battery                         — 3 cols, horizontal
        // Level 2 (Horizontal):         FPS, Battery, CPU, Time                    — 4 cols, horizontal (screenshot 2 style)
        // Level 3 (Horizontal Detailed):AppName, FPS, CPU+Clock, GPU+Clock, Battery, Memory, Time — 7 cols (IntelGameBar style)
        //                               CPU item renders watts+PL1/PL2 subtly inline
        // Level 4 (Full):               All options                                — 1 col, vertical
        private Dictionary<int, HashSet<string>> osdLevelConfig = new Dictionary<int, HashSet<string>>
        {
            { 1, new HashSet<string> { "Time", "FPS", "Battery" } },
            { 2, new HashSet<string> { "FPS", "Battery", "CPU", "Time" } },
            { 3, new HashSet<string> { "AppName", "FPS", "CPU", "CPUClock", "GPU", "GPUClock", "Battery", "Memory", "TDPLimits", "Time" } },
            { 4, new HashSet<string> { "AppName", "FPS", "FrametimeGraph", "Blank1", "GPU", "CPU", "CPUClock", "CPUCores", "Blank2", "Memory", "Blank3", "VRAM", "TDPLimits", "Battery", "GPUClock", "Blank4", "Time" } }
        };
        private Dictionary<int, string> osdCustomTags = new Dictionary<int, string>
        {
            { 1, "" },
            { 2, "" },
            { 3, "" },
            { 4, "" }
        };

        // Layout settings
        private int osdTextSize = 125;        // Percentage: 50=Small, 100=Medium, 125=Default, 150=Large, 200=X-Large
        private string osdTextColor = "FFFFFF";
        private string osdLabelColor = "DEFAULT";  // DEFAULT = use item-specific colors, or hex color code
        private string osdBackgroundColor = "80000000";
        private int osdOpacity = 100;         // Percentage: 10-100, darkens OSD colors for OLED protection

        // OSD position offset for OLED burn-in protection
        private int osdPositionOffsetX = 0;
        private int osdPositionOffsetY = 0;

        // OSD Position Shift (OLED burn-in protection)
        // Note: In RTSS, 1 vertical pixel = 2 horizontal pixels visually
        private Timer positionShiftTimer;
        private bool positionShiftEnabled = false;
        private const int MAX_OFFSET_X = 3;  // Horizontal pixels (±3)
        private const int MAX_OFFSET_Y = 2;  // Vertical pixels (±2) - appears similar to ±3 horizontal
        private readonly Random positionShiftRandom = new Random();

        // Frametime graph pinned mode - always on its own row at the bottom, left-aligned
        private bool frametimeGraphPinned = false;

        // Per-level columns
        // Level 1 (Basic):              3 cols
        // Level 2 (Horizontal):         4 cols — FPS | BAT | CPU | Time on one row
        // Level 3 (Horizontal Detailed):7 cols — AppName | FPS | CPU | GPU | BAT | MEM | Time
        //                               CPU item renders watts+PL1/PL2 as subtle inline hints
        // Level 4 (Full):               1 col  — vertical list
        private Dictionary<int, int> osdLevelColumns = new Dictionary<int, int>
        {
            { 1, 3 },  // Basic:              3 columns
            { 2, 4 },  // Horizontal:         4 columns
            { 3, 7 },  // Horizontal Detailed:7 columns (all on one row)
            { 4, 1 }   // Full:               1 column (vertical list)
        };

        // Per-level item order
        private Dictionary<int, List<string>> osdLevelOrder = new Dictionary<int, List<string>>
        {
            { 1, new List<string> { "AppName", "Time", "FPS", "Battery", "Memory", "VRAM", "CPU", "CPUClock", "CPUCores", "GPU", "GPUClock", "Fan", "TDPLimits", "FrametimeGraph" } },
            { 2, new List<string> { "FPS", "Battery", "CPU", "Time", "AppName", "Memory", "VRAM", "CPUClock", "CPUCores", "GPU", "GPUClock", "Fan", "TDPLimits", "FrametimeGraph" } },
            { 3, new List<string> { "AppName", "FPS", "CPU", "CPUClock", "CPUCores", "GPU", "GPUClock", "Battery", "Memory", "Time", "VRAM", "Fan", "TDPLimits", "FrametimeGraph" } },
            { 4, new List<string> { "AppName", "FPS", "FrametimeGraph", "Blank1", "GPU", "CPU", "CPUClock", "CPUCores", "Blank2", "TDPLimits", "Blank3", "Memory", "VRAM", "Battery", "GPUClock", "Blank4", "Time" } }
        };

        // Per-level, per-item label colors (e.g., osdItemLabelColors[1]["CPU"] = "FF0000")
        private Dictionary<int, Dictionary<string, string>> osdItemLabelColors = new Dictionary<int, Dictionary<string, string>>
        {
            { 1, new Dictionary<string, string>() },
            { 2, new Dictionary<string, string>() },
            { 3, new Dictionary<string, string>() },
            { 4, new Dictionary<string, string>() }
        };

        public RTSSManager(PerformanceManager performanceManager) : base()
        {
            rtssInstalled = new RTSSInstalledProperty(this);
            osdConfig = new OSDConfigProperty(this);
            fpsLimit = new FPSLimitProperty(this);

            RTSSFPSLimiter.Initialize();
            osdItemFan = new OSDItemFan();
            osdItemAutoTDP = new OSDItemAutoTDP();
            osdItemTDPLimits = new OSDItemTDPLimits();
            osdItemTDPLimits.SetPerformanceManager(performanceManager);
            osdItemCPU = new OSDItemCPU(performanceManager.CPUUsage, performanceManager.CPUClock, performanceManager.CPUWattage, performanceManager.CPUTemperature);
            osdItemCPU.SetPerformanceManager(performanceManager);
            osdItemCPUCores = new OSDItemCPUCores(performanceManager.PCoreClocks, performanceManager.ECoreClocks);
            osdItemBlank1 = new OSDItemBlankLine("Blank1");
            osdItemBlank2 = new OSDItemBlankLine("Blank2");
            osdItemBlank3 = new OSDItemBlankLine("Blank3");
            osdItemBlank4 = new OSDItemBlankLine("Blank4");
            osdItemCPUWatts = new OSDItemCPUWatts(performanceManager.CPUWattage);
            osdItemFPS = new OSDItemFPS();
            osdItemGPU = new OSDItemGPU(performanceManager.GPUUsage, performanceManager.GPUClock, performanceManager.GPUWattage, performanceManager.GPUTemperature);
            osdItemVRAM = new OSDItemVRAM(performanceManager.GPUMemoryUsed, performanceManager.GPUMemoryFree, performanceManager.GPUMemoryClock);
            osdItemControllerBattery = new OSDItemControllerBattery(null, null, null, null);
            osdItems = new OSDItem[]
            {
                new OSDItemTime(),
                new OSDItemAppName(),
                osdItemFPS,
                new OSDItemBattery(performanceManager.BatteryLevel, performanceManager.BatteryDischargeRate, performanceManager.BatteryChargeRate, performanceManager.BatteryRemainingTime, () => performanceManager.BatteryTimeToFull),
                osdItemControllerBattery,
                osdItemCPU,
                osdItemCPUCores,
                osdItemBlank1,
                osdItemBlank2,
                osdItemBlank3,
                osdItemBlank4,
                osdItemCPUWatts,
                osdItemGPU,
                osdItemVRAM,
                new OSDItemMemory(performanceManager.MemoryUsage, performanceManager.MemoryUsed, performanceManager.MemoryAvailable),
                osdItemFan,
                osdItemAutoTDP,
                osdItemTDPLimits,
            };

            rtssState = RivatunerStatisticsServerState.NotInstalled;
        }

        /// <summary>
        /// Called by Program.ProfileHandlers whenever a FPS limiter changes.
        /// fps=0 means no limiter active; isIntel=true for Intel platform limiter.
        /// The value is forwarded to OSDItemFPS on the next Update() tick.
        /// </summary>
        public void SetFpsCapDisplay(int fps, bool isIntel)
        {
            _fpsCapDisplayValue = fps;
            _fpsCapDisplayIsIntel = isIntel;
        }

        /// <summary>
        /// Re-pushes the RTSS framerate limit once RTSS has actually hooked a game.
        ///
        /// RTSS only enforces a cap on a process it has already hooked. At game start the limit
        /// is set before RTSS hooks the new render process (which can be many seconds late for
        /// launchers / anti-cheat / shader compile), so the cap never reaches the game — the user
        /// had to nudge the value to trigger a re-push. This polls the RTSS shared-memory app list
        /// and re-applies the limit as soon as a hooked app appears, so the cap takes effect on
        /// its own. Aborts if the limit value changes meanwhile (profile switch / user change).
        /// </summary>
        // Guards against stacking multiple re-apply poll loops when RunningGame fires
        // repeatedly (e.g. foreground changes for the same game).
        private int _fpsReapplyPolling = 0;

        public void ReapplyFpsLimitWhenHooked(int fpsLimit)
        {
            if (fpsLimit <= 0) return;

            // Only one poll loop at a time — if one is already running, let it finish.
            if (System.Threading.Interlocked.CompareExchange(ref _fpsReapplyPolling, 1, 0) != 0)
                return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    // Hard-bounded poll: ~30s max (15 × 2s). Stops immediately once the limit is
                    // applied to a hooked app, or if the value changes meanwhile. Never loops
                    // indefinitely.
                    for (int attempt = 0; attempt < 15; attempt++)
                    {
                        await System.Threading.Tasks.Task.Delay(2000);

                        // Value changed since scheduling (profile switch / user edit) → abandon.
                        if (FPSLimit.Value != fpsLimit) return;

                        try
                        {
                            var entries = OSD.GetAppEntries(AppFlags.MASK);
                            bool hooked = false;
                            if (entries != null)
                            {
                                foreach (var entry in entries)
                                {
                                    if (entry.ProcessId != 0) { hooked = true; break; }
                                }
                            }

                            if (hooked)
                            {
                                RTSSFPSLimiter.SetFPSLimit(fpsLimit);
                                Logger.Info($"RTSSManager: Re-applied FPS limit {fpsLimit} after RTSS hooked the game (attempt {attempt + 1})");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"ReapplyFpsLimitWhenHooked: {ex.Message}");
                        }
                    }
                    Logger.Debug("ReapplyFpsLimitWhenHooked: gave up after ~30s (no hooked app detected)");
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _fpsReapplyPolling, 0);
                }
            });
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
                    else if (key == "Font")
                    {
                        // value = "Face|Weight". Applied via the RTSS Global profile off-thread — it may
                        // rewrite the profile + restart RTSS, but only when the font actually changes.
                        string face = value;
                        int weight = RtssFontManager.DefaultWeight;
                        int bar = value.IndexOf('|');
                        if (bar >= 0)
                        {
                            face = value.Substring(0, bar);
                            int.TryParse(value.Substring(bar + 1), out weight);
                        }
                        string faceCapture = face;
                        int weightCapture = weight;
                        _ = System.Threading.Tasks.Task.Run(() => RtssFontManager.EnsureFont(faceCapture, weightCapture));
                        Logger.Debug($"OSD Font: {face} weight {weight}");
                    }
                    else if (key == "TextColor")
                    {
                        osdTextColor = value;
                        Logger.Debug($"OSD TextColor: {value}");
                    }
                    else if (key == "LabelColor")
                    {
                        osdLabelColor = value;
                        Logger.Debug($"OSD LabelColor: {value}");
                    }
                    else if (key == "BackgroundColor")
                    {
                        osdBackgroundColor = value;
                        Logger.Debug($"OSD BackgroundColor: {value}");
                    }
                    else if (key == "Opacity")
                    {
                        if (int.TryParse(value, out int opacity))
                        {
                            osdOpacity = Math.Max(10, Math.Min(100, opacity));
                            Logger.Debug($"OSD Opacity: {osdOpacity}");
                        }
                    }
                    else if (key == "FrametimeGraphPinned")
                    {
                        frametimeGraphPinned = value == "1" || value.ToLower() == "true";
                        Logger.Debug($"OSD FrametimeGraphPinned: {frametimeGraphPinned}");
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
                    else if (key.StartsWith("L") && key.EndsWith("_Order"))
                    {
                        // Order: L1_Order, L2_Order, L3_Order
                        var levelStr = key.Substring(1, key.Length - 7); // "L1_Order" -> "1"
                        if (int.TryParse(levelStr, out int level))
                        {
                            var orderList = value.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                            if (orderList.Count > 0)
                            {
                                osdLevelOrder[level] = orderList;
                                Logger.Debug($"OSD Level {level} order: {string.Join(", ", orderList)}");
                            }
                        }
                    }
                    else if (key.StartsWith("L") && key.Contains("_") && key.EndsWith("_Color"))
                    {
                        // Item label color: L1_CPU_Color, L2_FPS_Color, etc.
                        // Format: L{level}_{itemId}_Color
                        var underscoreIdx = key.IndexOf('_');
                        var lastUnderscoreIdx = key.LastIndexOf('_');
                        if (underscoreIdx > 1 && lastUnderscoreIdx > underscoreIdx)
                        {
                            var levelStr = key.Substring(1, underscoreIdx - 1);
                            var itemId = key.Substring(underscoreIdx + 1, lastUnderscoreIdx - underscoreIdx - 1);
                            if (int.TryParse(levelStr, out int level) && !string.IsNullOrEmpty(itemId))
                            {
                                if (!osdItemLabelColors.ContainsKey(level))
                                {
                                    osdItemLabelColors[level] = new Dictionary<string, string>();
                                }
                                osdItemLabelColors[level][itemId] = value;
                                Logger.Debug($"OSD Level {level} item '{itemId}' label color: {value}");
                            }
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
        /// Parses display/OSD config from the widget.
        /// Format: "PositionShift:1;PositionShiftInterval:5;AdaptiveBrightness:1"
        /// AdaptiveBrightness is handled by SystemManager via callback.
        /// </summary>
        public void ParseDisplayOSDConfig(string configString, Action<bool> setAdaptiveBrightness)
        {
            if (string.IsNullOrEmpty(configString))
                return;

            Logger.Info($"Parsing Display/OSD config: {configString}");

            try
            {
                var parts = configString.Split(';');
                foreach (var part in parts)
                {
                    var kv = part.Split(':');
                    if (kv.Length != 2) continue;

                    var key = kv[0];
                    var value = kv[1];

                    switch (key)
                    {
                        case "PositionShift":
                            bool newPosShiftEnabled = value == "1";
                            if (newPosShiftEnabled != positionShiftEnabled)
                            {
                                positionShiftEnabled = newPosShiftEnabled;
                                UpdatePositionShiftTimer();
                            }
                            break;
                        case "AdaptiveBrightness":
                            setAdaptiveBrightness?.Invoke(value == "1");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error parsing Display/OSD config: {ex.Message}");
            }
        }

        private void UpdatePositionShiftTimer()
        {
            positionShiftTimer?.Dispose();
            positionShiftTimer = null;

            if (positionShiftEnabled)
            {
                // Fixed 1-minute interval for OLED burn-in prevention
                const int intervalMs = 60 * 1000;
                positionShiftTimer = new Timer(PositionShiftTick, null, intervalMs, intervalMs);
                Logger.Info("OSD Position shift enabled, interval: 1 minute");
            }
            else
            {
                // Reset to origin
                osdPositionOffsetX = 0;
                osdPositionOffsetY = 0;
                Logger.Info("OSD Position shift disabled");
            }
        }

        private void PositionShiftTick(object state)
        {
            // Generate random offset within bounds (X and Y scaled for visual uniformity)
            osdPositionOffsetX = positionShiftRandom.Next(-MAX_OFFSET_X, MAX_OFFSET_X + 1);
            osdPositionOffsetY = positionShiftRandom.Next(-MAX_OFFSET_Y, MAX_OFFSET_Y + 1);
            Logger.Debug($"OSD Position shifted to ({osdPositionOffsetX}, {osdPositionOffsetY})");
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

        /// <summary>
        /// Sets the controller battery callbacks for the Controller Battery OSD item.
        /// Must be called after LegionManager is initialized.
        /// </summary>
        public void SetControllerBatteryCallbacks(Func<int> getLeftBattery, Func<int> getRightBattery,
            Func<bool> getLeftCharging, Func<bool> getRightCharging)
        {
            osdItemControllerBattery.SetCallbacks(getLeftBattery, getRightBattery, getLeftCharging, getRightCharging);
            Logger.Info("Controller battery callbacks set for RTSS OSD");
        }

        /// <summary>
        /// Initializes the DisplayOSDConfig property with the adaptive brightness callback.
        /// Must be called after SystemManager is initialized.
        /// </summary>
        public void InitializeDisplayOSDConfig(Action<bool> setAdaptiveBrightness)
        {
            displayOSDConfig = new DisplayOSDConfigProperty(this, setAdaptiveBrightness);
            Logger.Info("DisplayOSDConfig property initialized");
        }

        /// <summary>
        /// Resets the RTSS connection after hibernate/suspend resume.
        /// The OSD connection can become stale after hibernation, causing stale values.
        /// This forces the OSD to be recreated on the next Update() cycle.
        /// </summary>
        public void ResetRTSSConnection()
        {
            Logger.Info("ResetRTSSConnection: Resetting RTSS OSD connection after hibernate resume");

            // Dispose existing OSD connection
            if (rtssOSD != null)
            {
                try
                {
                    rtssOSD.Update(string.Empty); // Clear OSD content first
                    rtssOSD.Dispose();
                    Logger.Info("ResetRTSSConnection: Disposed stale RTSS OSD");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"ResetRTSSConnection: Error disposing RTSS OSD: {ex.Message}");
                }
                // If Dispose() threw (RTSS shared memory gone), the OSD finalizer (!OSD())
                // would re-open shared memory on the GC thread and throw a FATAL unhandled
                // exception that kills the helper. Suppress it.
                try { GC.SuppressFinalize(rtssOSD); } catch { }
                rtssOSD = null;
            }

            // Reset state so OSD will be recreated
            rtssState = RivatunerStatisticsServerState.NotRunning;

            Logger.Info("ResetRTSSConnection: RTSS connection reset complete, OSD will be recreated on next update");
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
                    // Prevent the throwing OSD finalizer from killing the helper (see ResetRTSSConnection).
                    try { GC.SuppressFinalize(rtssOSD); } catch { }
                    rtssOSD = null;
                }

                return;
            }

            if (!RTSSHelper.IsRunning())
            {
                if (SettingsManager.GetInstance().AutoStartRTSS && !SuppressAutoStart)
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
                            string rtssExe = RTSSHelper.ExecutablePath();
                            string rtssDir = RTSSHelper.InstalledLocation();
                            if (string.IsNullOrEmpty(rtssDir))
                                rtssDir = System.IO.Path.GetDirectoryName(rtssExe);
                            // Launch from the RTSS install folder (and via ShellExecute) so RTSS
                            // resolves its Skins\default.usf — otherwise it shows
                            // "Failed to load default.usf skin!" when started with the wrong CWD.
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = rtssExe,
                                WorkingDirectory = rtssDir,
                                UseShellExecute = true,
                            });
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
                // Guard: RTSSHelper.IsRunning() can return true before RTSS has
                // mapped its shared-memory segment (startup race), or the
                // segment can vanish if RTSS is killed mid-update. The OSD
                // ctor calls openSharedMemory() which throws
                // FileNotFoundException (HRESULT 0x80070002) in that window.
                // That exception previously bubbled all the way to Main and
                // killed the helper on launch when RTSS wasn't fully up —
                // bail out of this tick instead and retry next time.
                try
                {
                    rtssOSD = new OSD(OSDAppName);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"RTSS OSD unavailable (shared memory not ready): {ex.Message}");
                    rtssState = RivatunerStatisticsServerState.NotRunning;
                    return;
                }
            }

            // Forward active FPS cap info to the FPS OSD item (set externally by Program.ProfileHandlers)
            osdItemFPS.SetFpsCapDisplay(_fpsCapDisplayValue, _fpsCapDisplayIsIntel);

            // Update clock display settings based on config
            osdItemCPU.SetShowClock(IsItemEnabled("CPUClock"));
            osdItemGPU.SetShowClock(IsItemEnabled("GPUClock"));

            // Set text color and opacity on all items
            // Apply opacity for OLED protection
            var textColorWithOpacity = osdTextColor == "DYNAMIC" ? "DYNAMIC" : ApplyOpacityToColor(osdTextColor);
            foreach (var item in osdItems)
            {
                item.SetTextColor(textColorWithOpacity);
                item.SetOpacity(osdOpacity);
            }

            // Pre-build frametime graph string if enabled (so it can be placed in order)
            // Uses RTSS native <G=<FT>> tag - RTSS handles graph rendering internally
            string frametimeGraphString = null;
            if (IsItemEnabled("FrametimeGraph"))
            {
                try
                {
                    // Get frametime statistics from RTSS AppEntry
                    UpdateFrametimeStats();

                    // Build graph using <G=<FT>> tag - processed by RTSSSharedMemoryNET.ProcessGraphTags
                    // Width/height are hardcoded in ProcessGraphTags (-32 chars, -2 lines)
                    string graphColor = (osdTextColor == "DYNAMIC" || string.IsNullOrEmpty(osdTextColor)) ? "00FFFF" : osdTextColor;
                    graphColor = ApplyOpacityToColor(graphColor);

                    // Build stats label if we have valid data
                    string statsLabel = "";
                    if (currentMinFt > 0 && currentMaxFt > 0)
                    {
                        // Show the frametime stats as FPS metrics (not ms): min FPS is the slowest frame
                        // (= max frametime), max FPS the fastest (= min frametime). Base font size (no
                        // <S> shrink) so it stays readable and consistent with the rest of the overlay.
                        string labelColor = ApplyOpacityToColor("808080");
                        string minColor = ApplyOpacityToColor("FF6600");   // min fps = worst → orange
                        string avgColor = ApplyOpacityToColor("FFFF00");
                        string maxColor = ApplyOpacityToColor("00FF00");   // max fps = best → green
                        string lowColor = ApplyOpacityToColor("FF3030");
                        int minFps = currentMaxFt > 0.01f ? (int)System.Math.Round(1000f / currentMaxFt) : 0;
                        int avgFps = currentAvgFt > 0.01f ? (int)System.Math.Round(1000f / currentAvgFt) : 0;
                        int maxFps = currentMinFt > 0.01f ? (int)System.Math.Round(1000f / currentMinFt) : 0;
                        statsLabel = $"\n<C={labelColor}>min:<C={minColor}>{minFps} <C={labelColor}>avg:<C={avgColor}>{avgFps} <C={labelColor}>max:<C={maxColor}>{maxFps}";
                        if (current1PercentLowFps > 0)
                        {
                            statsLabel += $" <C={labelColor}>1% low:<C={lowColor}>{current1PercentLowFps:F0}";
                        }
                        statsLabel += $" <C={labelColor}>fps<C>";
                    }

                    // <G=<FT>> - RTSSSharedMemoryNET.ProcessGraphTags converts this to embedded graph object
                    frametimeGraphString = $"<C={graphColor}><G=<FT>><C>{statsLabel}";
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Failed to build frametime graph: {ex.Message}");
                }
            }

            // Build OSD header
            string osdString = BuildOSDHeader();

            // NOTE: Text-size scaling is applied GLOBALLY at the very end via ApplyOsdTextScale()
            // so the whole overlay scales proportionally (not just the leading FPS value).

            // Apply default text color (use white as base for dynamic mode)
            var baseTextColor = osdTextColor == "DYNAMIC" ? "FFFFFF" : osdTextColor;
            // Apply opacity for OLED protection
            baseTextColor = ApplyOpacityToColor(baseTextColor);
            osdString += $"<C={baseTextColor}>";

            // Collect all enabled items in custom order
            var enabledItems = new List<string>();

            // Get the order for current level (fall back to level 1 if not found)
            var order = osdLevelOrder.TryGetValue(onScreenDisplayLevel, out var levelOrder) ? levelOrder : osdLevelOrder[1];

            foreach (var itemId in order)
            {
                // Check if this item is enabled for the current level
                if (!IsItemEnabled(itemId))
                    continue;

                // Handle FrametimeGraph specially (it's not in osdItems array)
                if (itemId == "FrametimeGraph")
                {
                    // If pinned mode is enabled, skip here - we'll add it at the end
                    if (frametimeGraphPinned)
                        continue;

                    if (!string.IsNullOrEmpty(frametimeGraphString))
                    {
                        enabledItems.Add(frametimeGraphString);
                    }
                    continue;
                }

                // Find the OSD item by ID
                var item = osdItems.FirstOrDefault(i => i.Id == itemId);
                if (item == null)
                    continue;

                // Apply global label color if set (not DEFAULT)
                if (!string.IsNullOrEmpty(osdLabelColor) && osdLabelColor != "DEFAULT")
                {
                    item.SetLabelColor(osdLabelColor);
                }
                else
                {
                    item.SetLabelColor(null);  // Reset to item's default color
                }

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

            // (No closing tag here — global text-size scaling is applied centrally below.)

            // Add pinned frametime graph at the end on its own line
            if (frametimeGraphPinned && !string.IsNullOrEmpty(frametimeGraphString) && IsItemEnabled("FrametimeGraph"))
            {
                osdString += "\n" + frametimeGraphString;
            }

            // Append transient action notification if active
            if (_pendingNotification != null && DateTime.Now < _notificationExpiry)
            {
                var parts = _pendingNotification.Split('\n');
                if (parts.Length >= 2)
                {
                    // Line 1: tile name — bright white, slightly larger
                    osdString += $"\n<C=FFFFFF><S=110>{parts[0]}<S><C>";
                    // Line 2: action description — muted grey, slightly smaller
                    osdString += $"\n<C=AAAAAA><S=85>{parts[1]}<S><C>";
                }
                else
                {
                    osdString += $"\n<C=FFFFFF><S=110>{_pendingNotification}<S><C>";
                }
            }
            else if (_pendingNotification != null && DateTime.Now >= _notificationExpiry)
            {
                // Notification expired — clear it
                _pendingNotification = null;
            }

            // Scale the ENTIRE overlay by the configured Text Size (global, proportional).
            osdString = ApplyOsdTextScale(osdString);

            try
            {
                rtssOSD.Update(osdString);
            }
            catch (Exception ex)
            {
                // A throw here almost always means the RTSS shared-memory handle went stale:
                // Modern-Standby (S0) sleep/hibernate resume, or RTSS itself restarted. The
                // PowerModes.Resume hook that would call ResetRTSSConnection() does NOT fire on the
                // Claw's S0 standby, so the overlay would otherwise stay frozen forever. Drop +
                // recreate the OSD here (ResetRTSSConnection disposes, SuppressFinalize's the stale
                // instance so its finalizer can't throw on the GC thread, and nulls it) so the next
                // tick recreates a fresh OSD against the current shared memory. Self-heals regardless
                // of the unreliable resume event. Fires once per stale episode (next tick either
                // recreates cleanly or bails in the new-OSD guard), so this is not per-tick spam.
                Logger.Warn($"Error updating OSD ({ex.Message}) — resetting stale RTSS OSD so it recreates next tick");
                ResetRTSSConnection();
            }
        }

        /// <summary>
        /// Applies the user's "Text Size" (osdTextSize, percent) as a GLOBAL, proportional scale over
        /// the whole OSD string — not just the leading FPS value. RTSS &lt;S=N&gt; tags are absolute
        /// percentages and a bare &lt;S&gt; resets to 100% (RTSS default), which is why everything after
        /// the first reset (units, frametime stats, cap hint, notifications) used to ignore Text Size.
        ///
        /// Transform (only when size != 100%):
        ///   - every explicit &lt;S=N&gt; -> &lt;S=round(N * size/100)&gt;   (relative sizes scale)
        ///   - every bare    &lt;S&gt;    -> &lt;S=size&gt;                   (reset to the base, not 100%)
        ///   - prepend a leading &lt;S=size&gt; so untagged text scales too
        /// Graphs (&lt;G=...&gt;) are not text and keep their own (hardcoded) size.
        /// </summary>
        private string ApplyOsdTextScale(string osd)
        {
            if (string.IsNullOrEmpty(osd) || osdTextSize == 100)
                return osd;

            double m = osdTextSize / 100.0;

            // Scale explicit sizes first (the regex never matches a bare <S>).
            osd = System.Text.RegularExpressions.Regex.Replace(
                osd, @"<S=(\d+)>",
                mm =>
                {
                    int n = int.Parse(mm.Groups[1].Value);
                    int scaled = (int)System.Math.Round(n * m);
                    if (scaled < 1) scaled = 1;
                    return $"<S={scaled}>";
                });

            // Bare resets go back to the global base size, not 100%.
            osd = osd.Replace("<S>", $"<S={osdTextSize}>");

            // Untagged text (the main metric values) scales via a leading base size.
            return $"<S={osdTextSize}>" + osd;
        }

        /// <summary>
        /// Show a transient notification line in the RTSS OSD overlay.
        /// The text will be visible for <paramref name="durationMs"/> milliseconds,
        /// then automatically disappear on the next Update() call.
        /// Thread-safe: may be called from any thread.
        /// </summary>
        public void ShowNotification(string text, int durationMs = 3000)
        {
            if (string.IsNullOrEmpty(text)) return;
            _pendingNotification = text;
            _notificationExpiry = DateTime.Now.AddMilliseconds(durationMs);
            Logger.Info($"RTSSManager: notification set: '{text}' for {durationMs}ms");
        }

        /// <summary>
        /// Builds the OSD header string.
        /// Note: Position and background are controlled by RTSS application settings.
        /// RTSS background requires complex <B=x,y> bar drawing with \b backspace which
        /// doesn't work well with dynamic content.
        /// </summary>
        private string BuildOSDHeader()
        {
            // Apply position offset for OLED burn-in protection
            if (osdPositionOffsetX != 0 || osdPositionOffsetY != 0)
            {
                return $"<P={osdPositionOffsetX},{osdPositionOffsetY}>";
            }
            // Background not supported via simple tags - must be configured in RTSS app
            return "";
        }

        /// <summary>
        /// Sets the OSD position offset for OLED burn-in protection.
        /// Called by OLEDProtectionManager when the position shift timer fires.
        /// </summary>
        public void SetPositionOffset(int x, int y)
        {
            osdPositionOffsetX = x;
            osdPositionOffsetY = y;
            Logger.Debug($"OSD position offset set to ({x}, {y})");
        }

        /// <summary>
        /// Applies opacity to a hex color by reducing RGB values proportionally.
        /// Used for OLED protection to darken OSD colors.
        /// </summary>
        private string ApplyOpacityToColor(string hexColor)
        {
            if (osdOpacity >= 100 || string.IsNullOrEmpty(hexColor) || hexColor.Length < 6)
                return hexColor;

            try
            {
                float factor = osdOpacity / 100f;
                byte r = (byte)(Convert.ToByte(hexColor.Substring(0, 2), 16) * factor);
                byte g = (byte)(Convert.ToByte(hexColor.Substring(2, 2), 16) * factor);
                byte b = (byte)(Convert.ToByte(hexColor.Substring(4, 2), 16) * factor);
                return $"{r:X2}{g:X2}{b:X2}";
            }
            catch
            {
                return hexColor;
            }
        }

        /// <summary>
        /// Updates frametime statistics from RTSS AppEntry frametime buffer.
        /// Calculates min/avg/max from recent frames in the circular buffer.
        /// Values are in microseconds, converted to milliseconds for display.
        /// </summary>
        private void UpdateFrametimeStats()
        {
            try
            {
                var appEntries = OSD.GetAppEntries(AppFlags.MASK);
                if (appEntries == null || appEntries.Length == 0)
                {
                    currentMinFt = 0f;
                    currentAvgFt = 0f;
                    currentMaxFt = 0f;
                    return;
                }

                // Get the foreground window's process ID to prioritize that app's data
                int foregroundPid = User32.GetForegroundProcessId();
                AppEntry targetEntry = null;
                AppEntry fallbackEntry = null;

                // Find the matching app entry - prioritize foreground window
                foreach (var entry in appEntries)
                {
                    if (entry.StatFrameTimeBuf != null && entry.StatFrameTimeBuf.Length > 0)
                    {
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
                if (selectedEntry == null || selectedEntry.StatFrameTimeBuf == null)
                {
                    currentMinFt = 0f;
                    currentAvgFt = 0f;
                    currentMaxFt = 0f;
                    return;
                }

                // Calculate stats from recent frames in the circular buffer
                // Buffer is 1024 samples, we analyze the last 256 for recent stats
                const int sampleCount = 256;
                uint bufSize = (uint)selectedEntry.StatFrameTimeBuf.Length;
                uint bufPos = selectedEntry.StatFrameTimeBufPos;

                float minFt = float.MaxValue;
                float maxFt = 0f;
                float sumFt = 0f;
                int validSamples = 0;
                var ftSamples = new List<float>(sampleCount);

                for (int i = 0; i < sampleCount; i++)
                {
                    uint srcIndex = (bufPos - (uint)sampleCount + (uint)i + bufSize) % bufSize;
                    float frametimeMs = selectedEntry.StatFrameTimeBuf[srcIndex] / 1000.0f;

                    // Only count valid samples (non-zero, reasonable range)
                    if (frametimeMs > 0.1f && frametimeMs < 1000f)
                    {
                        validSamples++;
                        sumFt += frametimeMs;
                        if (frametimeMs < minFt) minFt = frametimeMs;
                        if (frametimeMs > maxFt) maxFt = frametimeMs;
                        ftSamples.Add(frametimeMs);
                    }
                }

                if (validSamples > 0)
                {
                    currentMinFt = minFt;
                    currentAvgFt = sumFt / validSamples;
                    currentMaxFt = maxFt;

                    // 1% low FPS = framerate at the 99th-percentile (slowest 1%) frametime — the same
                    // metric RTSS derives from this buffer. Needs enough samples to be meaningful.
                    if (ftSamples.Count >= 20)
                    {
                        ftSamples.Sort();
                        int idx = (int)System.Math.Ceiling(ftSamples.Count * 0.99f) - 1;
                        if (idx < 0) idx = 0;
                        if (idx >= ftSamples.Count) idx = ftSamples.Count - 1;
                        float p99Ft = ftSamples[idx];
                        current1PercentLowFps = p99Ft > 0.01f ? 1000f / p99Ft : 0f;
                    }
                    else
                    {
                        current1PercentLowFps = 0f;
                    }
                }
                else
                {
                    currentMinFt = 0f;
                    currentAvgFt = 0f;
                    currentMaxFt = 0f;
                    current1PercentLowFps = 0f;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error updating frametime stats: {ex.Message}");
                currentMinFt = 0f;
                currentAvgFt = 0f;
                currentMaxFt = 0f;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.Info("RTSSManager: Disposing resources");

                // Stop position shift timer
                if (positionShiftTimer != null)
                {
                    positionShiftTimer.Dispose();
                    positionShiftTimer = null;
                    Logger.Info("RTSSManager: Position shift timer disposed");
                }

                // Shutdown FPS limiter
                RTSSFPSLimiter.Shutdown();

                if (rtssOSD != null)
                {
                    try
                    {
                        rtssOSD.Update(string.Empty);
                        rtssOSD.Dispose();
                        Logger.Info("RTSSManager: RTSS OSD disposed");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"RTSSManager: Error disposing RTSS OSD: {ex.Message}");
                    }
                    // Prevent the throwing OSD finalizer from killing the helper (see ResetRTSSConnection).
                    try { GC.SuppressFinalize(rtssOSD); } catch { }
                    rtssOSD = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
