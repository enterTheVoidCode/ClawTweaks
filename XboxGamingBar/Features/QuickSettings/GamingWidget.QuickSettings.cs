using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // Tile brushes
        private SolidColorBrush tileOffBrush;
        private SolidColorBrush tileOnBrush;
        private SolidColorBrush tileActiveBrush;
        private SolidColorBrush tileTriggerBrush;
        private LinearGradientBrush tileDefaultProfileBrush;
        private bool quickSettingsInitialized = false;

        // Tile definitions with visibility tracking
        private class TileDefinition
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Glyph { get; set; }
            public bool IsVisible { get; set; } = true;
            public bool IsTrigger { get; set; } = false;  // True for tiles that trigger actions (keyboard, custom shortcuts)
            public bool IsAction { get; set; } = false;   // True for action tiles (Task Manager, Explorer, etc.) - shown at bottom
            public string CustomShortcut { get; set; }              // For custom shortcut tiles
            public TileActionType ActionType { get; set; } = TileActionType.None; // Predefined action
            public string ControllerHotkey { get; set; }            // Controller button combo mask (global, e.g. "48" = Start+Select)

            // Per-game hotkey overrides: key = app identity (e.g. "game.exe"), value = mask string.
            // Populated from storage key QS_{Id}_Hotkey_{appId}. Reserved for future per-game UI.
            public Dictionary<string, string> ControllerHotkeyPerGame { get; set; } = new Dictionary<string, string>();
            public int Order { get; set; } = 0;                     // Display order (lower = first)
            public Button TileButton { get; set; }
            public TextBlock StateText { get; set; }
            public CheckBox VisibilityCheckBox { get; set; }

            // For scrolling text animation (Profile tile)
            public Canvas StateTextCanvas { get; set; }
            public TranslateTransform StateTextTransform { get; set; }
            public Storyboard ScrollStoryboard { get; set; }

            // Split-tile: optional dropdown chevron button (null for normal tiles)
            public bool HasDropdown { get; set; } = false;
            public Button DropdownButton { get; set; }

            // Dual-button split tile (left = mode switch, right = value cycle)
            public bool HasDualButtons { get; set; } = false;
            public Button LeftButton { get; set; }
            public Button RightButton { get; set; }
        }

        // List of custom shortcut tiles
        private List<TileDefinition> qsCustomShortcuts = new List<TileDefinition>();

        private List<TileDefinition> qsTileDefinitions = new List<TileDefinition>();
        private Dictionary<string, TileDefinition> qsTileMap = new Dictionary<string, TileDefinition>();

        // Edit mode state for tile customization
        private bool qsEditMode = false;
        private TileDefinition qsSelectedTileForMove = null;

        // Column count setting (3 or 4 columns)
        private int qsColumnCount = 4;

        // Quick Metrics row state
        private bool quickMetricsEnabled = true;
        private bool isUpdatingMetricCheckboxes = false;
        private bool screenSaverEnabled = false;
        private bool sidebarMenuEnabled = false;
        private const string SidebarMenuEnabledKey = "SidebarMenuEnabled";
        private const string ScreenSaverEnabledKey = "QS_ScreenSaverEnabled";
        private const int ScreenSaverTimeoutSeconds = 60;
        private DispatcherTimer screenSaverCountdownTimer;
        private const string QuickMetricsEnabledKey = "QS_MetricsEnabled";
        private const string QuickMetricsSelectionKey = "QS_MetricsSelection";
        private const int MaxSelectedMetrics = 6;

        // Available metric types with their display properties
        private enum MetricType
        {
            BatteryDrain,
            BatteryLevel,
            CPUUsage,
            CPUTemp,
            CPUWattage,
            GPUUsage,
            GPUTemp,
            GPUWattage,
            MemoryUsage,
            TimeRemaining
        }

        // Metric display info
        private class MetricInfo
        {
            public string Id { get; set; }
            public string Label { get; set; }
            public string Glyph { get; set; }
            public string Unit { get; set; }
            public TextBlock ValueTextBlock { get; set; }
            public TextBlock LabelTextBlock { get; set; }
        }

        // Map of metric type to display info
        private readonly Dictionary<MetricType, MetricInfo> metricDefinitions = new Dictionary<MetricType, MetricInfo>
        {
            { MetricType.BatteryDrain, new MetricInfo { Id = "BatteryDrain", Label = "Battery", Glyph = "\uE83F", Unit = "W" } },
            { MetricType.BatteryLevel, new MetricInfo { Id = "BatteryLevel", Label = "Battery", Glyph = "\uE83F", Unit = "%" } },
            { MetricType.CPUUsage, new MetricInfo { Id = "CPUUsage", Label = "CPU Load", Glyph = "\uE950", Unit = "%" } },
            { MetricType.CPUTemp, new MetricInfo { Id = "CPUTemp", Label = "CPU Temp", Glyph = "\uE9CA", Unit = "°" } },
            { MetricType.CPUWattage, new MetricInfo { Id = "CPUWattage", Label = "CPU Power", Glyph = "\uE945", Unit = "W" } },
            { MetricType.GPUUsage, new MetricInfo { Id = "GPUUsage", Label = "GPU", Glyph = "\uE7F4", Unit = "%" } },
            { MetricType.GPUTemp, new MetricInfo { Id = "GPUTemp", Label = "GPU Temp", Glyph = "\uE9CA", Unit = "°" } },
            { MetricType.GPUWattage, new MetricInfo { Id = "GPUWattage", Label = "GPU", Glyph = "\uE945", Unit = "W" } },
            { MetricType.MemoryUsage, new MetricInfo { Id = "MemoryUsage", Label = "Memory", Glyph = "\uE964", Unit = "%" } },
            { MetricType.TimeRemaining, new MetricInfo { Id = "TimeRemaining", Label = "Time", Glyph = "\uE916", Unit = "" } }
        };

        // Currently selected metrics (in order of display)
        private List<MetricType> selectedMetrics = new List<MetricType>();

        // Current metrics data from helper
        private Dictionary<string, double> currentMetricsData = new Dictionary<string, double>();
        private bool currentMetricsIsCharging = false;

        // Timer for TDP reapply when switching to Custom mode
        private Windows.UI.Xaml.DispatcherTimer qsTdpReapplyTimer;

        // Add-tile type selector state: false = Keyboard (default), true = Action
        private bool _isTileTypeAction = false;

        /// <summary>
        /// Initialize Quick Settings resources and build tiles
        /// </summary>
        private void InitializeQuickSettings()
        {
            if (quickSettingsInitialized) return;

            try
            {
                // Clear any stale state from previous initialization attempts
                // This ensures fresh state when widget is reloaded
                qsTileDefinitions.Clear();
                qsTileMap.Clear();
                qsCustomShortcuts.Clear();
                qsEditMode = false;
                qsSelectedTileForMove = null;

                // Dark mode colors with sharp contrast for handheld devices
                // On state: use desaturated system accent color for subtle indication
                var accentDark3 = (Windows.UI.Color)Application.Current.Resources["SystemAccentColorDark3"];
                // Blend accent with dark gray to reduce saturation (40% accent, 60% dark base)
                var darkBase = Windows.UI.Color.FromArgb(255, 26, 28, 30); // Same as tile off
                var desaturatedAccent = Windows.UI.Color.FromArgb(
                    255,
                    (byte)((accentDark3.R * 0.4) + (darkBase.R * 0.6)),
                    (byte)((accentDark3.G * 0.4) + (darkBase.G * 0.6)),
                    (byte)((accentDark3.B * 0.4) + (darkBase.B * 0.6)));
                tileOnBrush = new SolidColorBrush(desaturatedAccent);

                // Other tile brushes - dark mode
                tileOffBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 28, 30));   // #1A1C1E
                tileActiveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 37, 48)); // #1A2530 - dark blue
                tileTriggerBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 32, 48)); // #252030 - dark purple

                // Default Game Profile gradient brush (matches Performance tab card)
                tileDefaultProfileBrush = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop { Color = Windows.UI.Color.FromArgb(0x40, 0xC0, 0x40, 0x40), Offset = 0.0 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(0x40, 0x40, 0x80, 0x50), Offset = 0.35 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(0x40, 0x40, 0x50, 0x80), Offset = 0.65 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(0x40, 0x80, 0x40, 0x60), Offset = 1.0 }
                    }
                };

                // Define all tiles
                DefineQuickSettingsTiles();

                // Load visibility settings from storage
                LoadQuickSettingsConfig();

                // Build tile UI
                RebuildQuickSettingsTiles();

                // Build sortable grid (for customize panel, initially hidden)
                BuildSortableGrid();

                // Populate the action ComboBox
                PopulateActionTypeComboBox();

                quickSettingsInitialized = true;
                Logger.Info("Quick Settings initialized with system accent color");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing Quick Settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Refresh Quick Settings tiles when Legion status changes
        /// </summary>
        private void RefreshQuickSettingsForLegion()
        {
            if (!quickSettingsInitialized) return;

            try
            {
                RebuildQuickSettingsTiles();
                BuildSortableGrid();
                UpdateQuickSettingsTileStates();
                Logger.Info("Quick Settings refreshed for Legion detection change");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error refreshing Quick Settings for Legion: {ex.Message}");
            }
        }

        /// <summary>
        /// Define all available Quick Settings tiles
        /// </summary>
        private void DefineQuickSettingsTiles()
        {
            qsTileDefinitions.Clear();
            qsTileMap.Clear();

            // MSI Claw mode tile pinned to position 0 (top-left) \u2014 shown only on MSI Claw (ShouldSkipTile hides it elsewhere).
            // Overlay follows at 1. All other tiles start at order=4.
            AddTileDefinition("MSIClawDesktopMode", "Mode",     "\uE7FC", order: 0, actionType: TileActionType.ToggleControllerMouseMode); // MSI Claw only \u2014 top-left
            // FPS Limit tile disabled \u2014 Performance tab is the single source of truth.
            // AddTileDefinition("FPSLimit",     "FPS Limit", "\uE916", order: 1);
            // AddTileDefinition("IntelFpsTier", "Intel FPS", "\uE916", order: 2, hasDropdown: true);
            // AddTileDefinition("FPSCombined", "FPS limit.", "\uE916", order: 1, hasDropdown: true);
            AddTileDefinition("Overlay",      "Overlay",   "\uE7B3", order: 1);

            int order = 4;

            // Row 1 - Performance Core (most used)
            // TDPMode tile removed \u2014 TDP only via slider in Performance tab
            // AddTileDefinition("AutoTDP", "AutoTDP", "\uE9F5", order: order++);  // removed: not applicable on Intel Claw
            // AddTileDefinition("PowerMode", "Power Mode", "\uE945", order: order++);  // removed: not used on MSI Claw

            // Row 2 - Performance Fine-tuning
            // AddTileDefinition("EPP", "EPP", "\uE83E", order: order++);  // removed: AMD EPP \u2014 Intel uses HWP/EPP differently, no direct slider
            // AddTileDefinition("RadeonChill", "Chill", "\uE9CA", order: order++);  // removed: AMD-only feature
            AddTileDefinition("Profile", "Profile", "\uE77B", order: order++);   // moved before CPU Boost
            AddTileDefinition("CPUBoost", "CPU Boost", "\uE7F4", order: order++);

            // Row 3 - Display
            AddTileDefinition("Resolution", "Resolution", "\uE7F8", order: order++);
            // AddTileDefinition("Rotation", "Rotation", "\uE7AD", order: order++);  // removed: not used on MSI Claw
            // AddTileDefinition("HDR", "HDR", "\uE706", order: order++);  // removed: MSI Claw has no HDR output
            AddTileDefinition("Fullscreen", "Fullscreen", "\uE740", order: order++);

            // Row 4 - Overlay Tools (hotkey tiles \u2014 close Game Bar then send key to game)
            // AddTileDefinition("AFMF", "AFMF", "\uE916", order: order++);   // removed: AMD-only feature
            // AddTileDefinition("AntiLag", "Anti-Lag", "\uE916", order: order++);  // removed: AMD-only feature
            AddTileDefinition("OptiScaler", "OptiScaler", "\uE736", order: order++, customShortcut: "Insert");  // Insert = OptiScaler overlay toggle; shortcut populated so helper fires directly when GameBar is closed
            AddTileDefinition("ReShade",    "ReShade",    "\uE8B8", order: order++, customShortcut: "Home");    // Home   = ReShade overlay toggle

            // Row 5 - Scaling/Quality
            AddTileDefinition("LosslessScaling", "Lossless", "\uE8A3", order: order++);  // E8A3 = zoom-in (magnifier+plus) \u2014 matches Lossl tab icon
            // Note: "Overlay" tile is defined above at order=1 (pinned to top-left)

            // Row 6 - Input & Interaction
            // AddTileDefinition("ScreenSaver", "Idle Screen Off", "\uE7E8", order: order++);  // removed: not needed on Claw
            AddTileDefinition("Keyboard", "Keyboard", "\uE765", isTrigger: true, order: order++);  // on-screen / touch keyboard toggle (TriggerOnScreenKeyboard)
            // AddTileDefinition("LegionTouchpad", "Touchpad", "\uE962", order: order++);  // removed: MSI Claw has no touchpad
            // AddTileDefinition("LegionRemapControls", "Remap", "\uE7FC", order: order++);  // removed: redundant \u2014 controller settings are in Per Game Contr. tab
            // AddTileDefinition("LegionDesktopControls", "Desktop", "\uE7F4", order: order++);  // removed: not used on MSI Claw
            // Quick toggle for the legacy controller emulation backend; state text shows
            // the active backend mode (Xbox / DS4 / Mouse) when on, "Off" otherwise.
            // Hidden on MSI Claw (replaced by MSIClawDesktopMode below).
            AddTileDefinition("ControllerEmulation", "Controller", "\uE7FC", order: order++);

            // MSI Claw-specific mode toggle: moved to top of list (order: 0, pinned above FPSLimit).
            // Hidden on non-MSI-Claw devices (ShouldSkipTile checks deviceDisplayName for "Claw").
            // AddTileDefinition("MSIClawDesktopMode", "Mode", "\uE7FC", order: order++);  // moved to order:0 at top

            // Row 7 - System/Device
            AddTileDefinition("MsiCenter", "MSI Center", "\uE7F4", order: order++);  // MSI Center M OEM software toggle
            // AddTileDefinition("LegionLightMode", "Light Mode", "\uE781", order: order++);  // removed: Legion-specific RGB light mode
            // AddTileDefinition("LegionPowerLight", "Power Light", "\uE7E8", order: order++);  // removed: not used
            // AddTileDefinition("LegionChargeLimit", "Charge Limit", "\uE83F", order: order++);  // removed: not used on MSI Claw
            // AddTileDefinition("LegionFanFullSpeed", "Fan Max", "\uE9CA", order: order++);  // removed: Legion-only feature
            // AddTileDefinition("Battery", "Battery", "\uE83F", order: order++);  // removed: battery display not needed

            // Load custom shortcut tiles from storage
            LoadCustomShortcutTiles();

            // Row 8 - Quick Actions (high order numbers to keep at bottom)
            int actionOrder = 1000;
            AddTileDefinition("ActionTaskManager", "Task Mgr", "\uE7EF", isAction: true, order: actionOrder++);
            AddTileDefinition("ActionExplorer", "Explorer", "\uEC50", isAction: true, order: actionOrder++);
            AddTileDefinition("ActionEndTask", "End Task", "\uE711", isAction: true, order: actionOrder++);
            // AddTileDefinition("ActionHibernate", "Hibernate", "\uE708", isAction: true, order: actionOrder++);  // removed: not needed on handheld
        }

        private void AddTileDefinition(string id, string name, string glyph, bool isTrigger = false, bool isAction = false, string customShortcut = null, int order = 0, bool hasDropdown = false, bool hasDualButtons = false, TileActionType actionType = TileActionType.None)
        {
            var def = new TileDefinition { Id = id, Name = name, Glyph = glyph, IsVisible = true, IsTrigger = isTrigger, IsAction = isAction, CustomShortcut = customShortcut, Order = order, HasDropdown = hasDropdown, HasDualButtons = hasDualButtons, ActionType = actionType };
            qsTileDefinitions.Add(def);
            qsTileMap[id] = def;
        }

        /// <summary>
        /// Load custom shortcut tiles from storage using QuickSettingsConfig
        /// </summary>
        private void LoadCustomShortcutTiles()
        {
            try
            {
                // Load from QuickSettingsConfig (the new unified storage)
                var config = QuickSettings.QuickSettingsConfig.Instance;
                var customTiles = config.Tiles.Where(t => t.Type == QuickSettings.TileType.CustomShortcut).ToList();

                // Calculate starting order (after built-in tiles)
                int startingOrder = qsTileDefinitions.Count > 0 ? qsTileDefinitions.Max(t => t.Order) + 1 : 100;

                int index = 0;
                foreach (var tile in customTiles)
                {
                    bool isKeyboard = !string.IsNullOrEmpty(tile.CustomShortcut);
                    bool isAction = tile.ActionType != TileActionType.None && tile.ActionType != TileActionType.KeyboardShortcut;

                    if (!isKeyboard && !isAction) continue; // skip empty/corrupt entries

                    string tileId = tile.Id;
                    string glyph = isAction
                        ? (string.IsNullOrEmpty(tile.Icon) ? TileActionHelper.GetGlyph(tile.ActionType) : tile.Icon)
                        : (tile.Icon ?? "\uE768");

                    var def = new TileDefinition
                    {
                        Id = tileId,
                        Name = tile.Name,
                        Glyph = glyph,
                        IsVisible = tile.IsVisible,
                        IsTrigger = true,
                        CustomShortcut = tile.CustomShortcut,
                        ActionType = tile.ActionType,
                        ControllerHotkey = tile.ControllerHotkey,
                        Order = startingOrder + index
                    };
                    qsTileDefinitions.Add(def);
                    qsTileMap[tileId] = def;
                    qsCustomShortcuts.Add(def);
                    index++;
                }
                Logger.Info($"Loaded {index} custom/action tiles from QuickSettingsConfig (using stable GUIDs)");

                // Migration: If old storage has shortcuts that aren't in the new system, migrate them
                MigrateOldCustomShortcuts();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading custom shortcut tiles: {ex.Message}");
            }
        }

        /// <summary>
        /// Migrate old custom shortcuts from the legacy storage format to QuickSettingsConfig
        /// </summary>
        private void MigrateOldCustomShortcuts()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("QS_CustomShortcuts", out object val) && val is string data && !string.IsNullOrEmpty(data))
                {
                    var config = QuickSettings.QuickSettingsConfig.Instance;
                    var existingShortcuts = config.Tiles
                        .Where(t => t.Type == QuickSettings.TileType.CustomShortcut)
                        .Select(t => t.CustomShortcut)
                        .ToHashSet();

                    var shortcuts = data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    int migratedCount = 0;

                    foreach (var shortcut in shortcuts)
                    {
                        var parts = shortcut.Split('|');
                        if (parts.Length == 2 && !existingShortcuts.Contains(parts[1]))
                        {
                            // Add to QuickSettingsConfig if not already present
                            config.AddCustomTile(parts[0], "\uE768", parts[1]);
                            migratedCount++;
                        }
                    }

                    if (migratedCount > 0)
                    {
                        Logger.Info($"Migrated {migratedCount} custom shortcuts from legacy storage");
                        // Clear old storage after migration
                        settings.Values.Remove("QS_CustomShortcuts");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error migrating old custom shortcuts: {ex.Message}");
            }
        }

        /// <summary>
        /// Save custom shortcut tiles to QuickSettingsConfig
        /// Note: This is now handled automatically by QuickSettingsConfig.AddCustomTile
        /// This method is kept for compatibility but delegates to QuickSettingsConfig
        /// </summary>
        private void SaveCustomShortcutTiles()
        {
            try
            {
                // QuickSettingsConfig.Save() is called automatically by AddCustomTile
                // This method now just triggers a save to ensure consistency
                QuickSettings.QuickSettingsConfig.Instance.Save();
                Logger.Info($"Custom shortcut tiles saved to QuickSettingsConfig");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving custom shortcut tiles: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a new custom shortcut tile using QuickSettingsConfig
        /// </summary>
        private void AddCustomShortcutTile(string name, string shortcut)
        {
            try
            {
                // Add to QuickSettingsConfig (saves automatically) - returns tile with GUID
                var config = QuickSettings.QuickSettingsConfig.Instance;
                var configTile = config.AddCustomTile(name, "\uE768", shortcut);

                // Calculate new order (place at end)
                int maxOrder = qsTileDefinitions.Count > 0 ? qsTileDefinitions.Max(t => t.Order) : 0;

                // Use the GUID from QuickSettingsConfig for stable tile identification
                string tileId = configTile.Id;
                var def = new TileDefinition
                {
                    Id = tileId,
                    Name = name,
                    Glyph = "\uE768",
                    IsVisible = true,
                    IsTrigger = true,
                    CustomShortcut = shortcut,
                    Order = maxOrder + 1
                };
                qsTileDefinitions.Add(def);
                qsTileMap[tileId] = def;
                qsCustomShortcuts.Add(def);

                RebuildQuickSettingsTiles();
                BuildSortableGrid();

                Logger.Info($"Added custom shortcut tile: {name} -> {shortcut} (id: {tileId})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error adding custom shortcut tile: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a new predefined-action tile
        /// </summary>
        private void AddActionTile(string name, TileActionType actionType)
        {
            try
            {
                var config = QuickSettings.QuickSettingsConfig.Instance;
                var configTile = config.AddActionTile(name, actionType);

                int maxOrder = qsTileDefinitions.Count > 0 ? qsTileDefinitions.Max(t => t.Order) : 0;
                var def = new TileDefinition
                {
                    Id = configTile.Id,
                    Name = name,
                    Glyph = TileActionHelper.GetGlyph(actionType),
                    IsVisible = true,
                    IsTrigger = true,
                    ActionType = actionType,
                    Order = maxOrder + 1
                };
                qsTileDefinitions.Add(def);
                qsTileMap[def.Id] = def;
                qsCustomShortcuts.Add(def);

                RebuildQuickSettingsTiles();
                BuildSortableGrid();
                Logger.Info($"Added action tile: {name} -> {actionType} (id: {def.Id})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error adding action tile: {ex.Message}");
            }
        }

        /// <summary>
        /// Load Quick Settings configuration from storage
        /// </summary>
        private void LoadQuickSettingsConfig()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load column count setting
                if (settings.Values.TryGetValue("QS_ColumnCount", out object colVal) && colVal is int colCount)
                {
                    qsColumnCount = Math.Max(3, Math.Min(5, colCount));  // Clamp to 3-5
                }

                // Load Quick Metrics toggle state
                if (settings.Values.TryGetValue(QuickMetricsEnabledKey, out object metricsVal) && metricsVal is bool metricsEnabled)
                {
                    quickMetricsEnabled = metricsEnabled;
                }

                // Load Screen Saver toggle state
                if (settings.Values.TryGetValue(ScreenSaverEnabledKey, out object ssVal) && ssVal is bool ssEnabled)
                {
                    screenSaverEnabled = ssEnabled;
                    if (screenSaverEnabled)
                    {
                        StartScreenSaverCountdown();
                    }
                }

                // Load Sidebar Menu toggle state
                if (settings.Values.TryGetValue(SidebarMenuEnabledKey, out object sbVal) && sbVal is bool sbEnabled)
                {
                    sidebarMenuEnabled = sbEnabled;
                    SidebarMenuToggle.IsOn = sidebarMenuEnabled;
                }

                // Load Quick Metrics selection
                selectedMetrics.Clear();
                if (settings.Values.TryGetValue(QuickMetricsSelectionKey, out object selectionVal) && selectionVal is string selectionStr)
                {
                    // Parse comma-separated metric IDs
                    foreach (var id in selectionStr.Split(','))
                    {
                        if (Enum.TryParse<MetricType>(id.Trim(), out var metricType))
                        {
                            selectedMetrics.Add(metricType);
                        }
                    }
                }
                else
                {
                    // Default selection (6): CPU Load, CPU Temp, CPU Power, GPU, Battery, Time — in display order
                    selectedMetrics.AddRange(new[]
                    {
                        MetricType.CPUUsage,
                        MetricType.CPUTemp,
                        MetricType.CPUWattage,
                        MetricType.GPUUsage,
                        MetricType.BatteryDrain,
                        MetricType.TimeRemaining
                    });
                }

                // Update Quick Metrics UI
                if (QuickMetricsToggle != null)
                    QuickMetricsToggle.IsOn = quickMetricsEnabled;
                if (QuickMetricsRow != null)
                    QuickMetricsRow.Visibility = quickMetricsEnabled ? Visibility.Visible : Visibility.Collapsed;
                if (MetricsSelectionPanel != null)
                    MetricsSelectionPanel.Visibility = quickMetricsEnabled ? Visibility.Visible : Visibility.Collapsed;

                // Update checkboxes and rebuild metrics grid
                UpdateMetricCheckboxes();
                RebuildMetricsGrid();

                // Apply default tile hotkeys before loading saved values so user overrides win
                ApplyTileHotkeyDefaults(settings);

                foreach (var tile in qsTileDefinitions)
                {
                    string visKey   = $"QS_{tile.Id}_Visible";
                    string orderKey = $"QS_{tile.Id}_Order";
                    string hotkeyKey = $"QS_{tile.Id}_Hotkey";

                    if (settings.Values.TryGetValue(visKey, out object val) && val is bool visible)
                        tile.IsVisible = visible;
                    if (settings.Values.TryGetValue(orderKey, out object orderVal) && orderVal is int order)
                        tile.Order = order;
                    // Restore controller hotkey for built-in tiles (custom tiles load theirs via QuickSettingsConfig).
                    // Distinguish by config membership, not IsTrigger: the built-in "Keyboard" tile is a trigger
                    // tile yet is NOT a QuickSettingsConfig entry, so it must persist via the QS_ key like other
                    // built-ins (mirrors BtnBindCommit's save path).
                    bool isConfigTile = QuickSettings.QuickSettingsConfig.Instance.GetTile(tile.Id) != null;
                    if (!isConfigTile && settings.Values.TryGetValue(hotkeyKey, out object hkVal) && hkVal is string hk && !string.IsNullOrEmpty(hk))
                        tile.ControllerHotkey = hk;

                    // Reserved: per-game hotkey overrides — load QS_{Id}_Hotkey_{appId} entries
                    // No UI yet; structure populated here for future use.
                    // (Implementation placeholder — iterate settings keys when per-game UI is built)
                }

                Logger.Info($"Quick Settings config loaded (columns: {qsColumnCount})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading Quick Settings config: {ex.Message}");
            }
        }

        /// <summary>
        /// Save Quick Settings configuration to storage
        /// </summary>
        private void SaveQuickSettingsConfig()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Save column count setting
                settings.Values["QS_ColumnCount"] = qsColumnCount;

                foreach (var tile in qsTileDefinitions)
                {
                    settings.Values[$"QS_{tile.Id}_Visible"] = tile.IsVisible;
                    settings.Values[$"QS_{tile.Id}_Order"]   = tile.Order;
                    // Persist controller hotkey for built-in tiles (custom tiles use QuickSettingsConfig).
                    // Config membership — not IsTrigger — is the correct discriminator so the built-in
                    // "Keyboard" trigger tile keeps its hotkey across restarts.
                    if (QuickSettings.QuickSettingsConfig.Instance.GetTile(tile.Id) == null)
                        settings.Values[$"QS_{tile.Id}_Hotkey"] = tile.ControllerHotkey ?? "";
                }

                Logger.Info($"Quick Settings config saved (columns: {qsColumnCount})");

                // Sync tile hotkeys (controller button combos) with helper
                _ = SendTileHotkeysToHelper();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving Quick Settings config: {ex.Message}");
            }
        }

        private void CalibrateGyroButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                if (!App.IsConnected) return;

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.ControllerEmulationCalibrateGyro },
                    { "Content", true }
                };
                App.PipeClient?.SendValueSet(request);
                Logger.Info("Sent gyro calibration request to helper");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending gyro calibration: {ex.Message}");
            }
        }

        /// <summary>
        /// Send Quick Metrics enabled state to helper
        /// </summary>
        private void SendQuickMetricsEnabledToHelper()
        {
            try
            {
                if (!App.IsConnected) return;

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.QuickMetricsEnabled },
                    { "Content", quickMetricsEnabled }
                };
                // Fire-and-forget: helper processes this but doesn't send a response,
                // so using SendRequestAsync would timeout after 10s for no reason
                App.PipeClient?.SendValueSet(request);
                Logger.Info($"Sent Quick Metrics enabled state to helper: {quickMetricsEnabled}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending Quick Metrics enabled state: {ex.Message}");
            }
        }

        private void SendScreenSaverEnabledToHelper()
        {
            try
            {
                if (!App.IsConnected) return;

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.ScreenSaverEnabled },
                    { "Content", screenSaverEnabled }
                };
                // Fire-and-forget: helper processes this but doesn't send a response,
                // so using SendRequestAsync would timeout after 10s for no reason
                App.PipeClient?.SendValueSet(request);
                Logger.Info($"Sent Screen Saver enabled state to helper: {screenSaverEnabled}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending Screen Saver enabled state: {ex.Message}");
            }
        }

        private void SidebarMenuToggle_Toggled(object sender, RoutedEventArgs e)
        {
            sidebarMenuEnabled = SidebarMenuToggle.IsOn;
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[SidebarMenuEnabledKey] = sidebarMenuEnabled;
            SendSidebarMenuEnabledToHelper();
            Logger.Info($"Sidebar Menu toggled: {sidebarMenuEnabled}");
        }

        private void SendSidebarMenuEnabledToHelper()
        {
            try
            {
                if (!App.IsConnected) return;

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.SidebarMenuEnabled },
                    { "Content", sidebarMenuEnabled }
                };
                App.PipeClient?.SendValueSet(request);
                Logger.Info($"Sent Sidebar Menu enabled state to helper: {sidebarMenuEnabled}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending Sidebar Menu enabled state: {ex.Message}");
            }
        }

        /// <summary>
        /// Populate the action type ComboBox with all predefined actions grouped by OS/App.
        /// Called once from InitializeQuickSettings.
        /// </summary>
        private void PopulateActionTypeComboBox()
        {
            try
            {
                if (CustomActionTypeComboBox == null) return;

                CustomActionTypeComboBox.Items.Clear();

                string lastGroup = null;
                foreach (var action in TileActionHelper.GetAllActions())
                {
                    string group = TileActionHelper.GetGroupName(action);
                    if (group != lastGroup)
                    {
                        // Add a disabled group-header item
                        string headerLabel = group == "OS" ? "— OS Aktionen —" : "— App Aktionen —";
                        var header = new ComboBoxItem
                        {
                            Content = headerLabel,
                            IsEnabled = false,
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 180, 180, 180))
                        };
                        CustomActionTypeComboBox.Items.Add(header);
                        lastGroup = group;
                    }

                    var item = new ComboBoxItem
                    {
                        Content = TileActionHelper.GetDisplayName(action),
                        Tag = action
                    };
                    CustomActionTypeComboBox.Items.Add(item);
                }

                Logger.Info("Action type ComboBox populated");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error populating action ComboBox: {ex.Message}");
            }
        }

        /// <summary>
        /// Send current controller hotkey config to helper via pipe so it can update
        /// its cached config for XInput-based button combo detection.
        /// </summary>
        private void SendControllerHotkeyConfigToHelper()
        {
            try
            {
                if (!App.IsConnected) return;

                var settings = ApplicationData.Current.LocalSettings;
                var hotkeyNames = new[] { "MenuA", "MenuB", "MenuX", "MenuY", "MenuDpadUp", "MenuDpadDown", "MenuDpadLeft", "MenuDpadRight" };

                // Build JSON config matching what ApplyControllerHotkeyConfig expects
                var jsonObj = new Windows.Data.Json.JsonObject();
                foreach (var name in hotkeyNames)
                {
                    int action = (int)(settings.Values[$"Hotkey_{name}_Action"] ?? 0);
                    string key = settings.Values[$"Hotkey_{name}_Key"] as string ?? "";
                    jsonObj[$"{name}_Action"] = Windows.Data.Json.JsonValue.CreateNumberValue(action);
                    jsonObj[$"{name}_Key"] = Windows.Data.Json.JsonValue.CreateStringValue(key);
                }

                string configJson = jsonObj.Stringify();

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.ControllerHotkeyConfig },
                    { "Content", configJson }
                };
                App.PipeClient?.SendValueSet(request);
                Logger.Info($"Sent controller hotkey config to helper");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending controller hotkey config: {ex.Message}");
            }
        }
    }
}
