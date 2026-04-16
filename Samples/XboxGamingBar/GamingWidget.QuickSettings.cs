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
            public string CustomShortcut { get; set; }    // For custom shortcut tiles
            public int Order { get; set; } = 0;           // Display order (lower = first)
            public Button TileButton { get; set; }
            public TextBlock StateText { get; set; }
            public CheckBox VisibilityCheckBox { get; set; }

            // For scrolling text animation (Profile tile)
            public Canvas StateTextCanvas { get; set; }
            public TranslateTransform StateTextTransform { get; set; }
            public Storyboard ScrollStoryboard { get; set; }
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
        private bool quickMetricsEnabled = false;
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
            { MetricType.CPUUsage, new MetricInfo { Id = "CPUUsage", Label = "CPU", Glyph = "\uE950", Unit = "%" } },
            { MetricType.CPUTemp, new MetricInfo { Id = "CPUTemp", Label = "CPU Temp", Glyph = "\uE9CA", Unit = "°" } },
            { MetricType.CPUWattage, new MetricInfo { Id = "CPUWattage", Label = "CPU", Glyph = "\uE945", Unit = "W" } },
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

            int order = 0;

            // Row 1 - Performance Core (most used)
            AddTileDefinition("TDPMode", "TDP Mode", "\uE945", order: order++);
            AddTileDefinition("AutoTDP", "AutoTDP", "\uE9F5", order: order++);
            AddTileDefinition("PowerMode", "Power Mode", "\uE945", order: order++);
            AddTileDefinition("CPUBoost", "CPU Boost", "\uE7F4", order: order++);

            // Row 2 - Performance Fine-tuning
            AddTileDefinition("EPP", "EPP", "\uE83E", order: order++);
            AddTileDefinition("FPSLimit", "FPS Limit", "\uE916", order: order++);
            AddTileDefinition("RadeonChill", "Chill", "\uE9CA", order: order++);
            AddTileDefinition("Profile", "Profile", "\uE77B", order: order++);

            // Row 3 - Display
            AddTileDefinition("Resolution", "Resolution", "\uE7F8", order: order++);
            AddTileDefinition("Rotation", "Rotation", "\uE7AD", order: order++);
            AddTileDefinition("HDR", "HDR", "\uE706", order: order++);
            AddTileDefinition("Fullscreen", "Fullscreen", "\uE740", order: order++);

            // Row 4 - AMD Graphics Features
            AddTileDefinition("RSR", "RSR", "\uE8B3", order: order++);
            AddTileDefinition("RIS", "RIS", "\uE8B3", order: order++);
            AddTileDefinition("AFMF", "AFMF", "\uE916", order: order++);
            AddTileDefinition("AntiLag", "Anti-Lag", "\uE916", order: order++);

            // Row 5 - Scaling/Quality
            AddTileDefinition("LosslessScaling", "Lossless", "\uE740", order: order++);
            AddTileDefinition("Overlay", "Overlay", "\uE7B3", order: order++);

            // Row 6 - Input & Interaction
            AddTileDefinition("ScreenSaver", "Idle Screen Off", "\uE7E8", order: order++);
            AddTileDefinition("Keyboard", "Keyboard", "\uE765", isTrigger: true, order: order++);
            AddTileDefinition("LegionTouchpad", "Touchpad", "\uE962", order: order++);
            AddTileDefinition("LegionRemapControls", "Remap", "\uE7FC", order: order++);
            AddTileDefinition("LegionDesktopControls", "Desktop", "\uE7F4", order: order++);

            // Row 7 - System/Device
            AddTileDefinition("LegionLightMode", "Light Mode", "\uE781", order: order++);
            AddTileDefinition("LegionPowerLight", "Power Light", "\uE7E8", order: order++);
            AddTileDefinition("LegionChargeLimit", "Charge Limit", "\uE83F", order: order++);
            AddTileDefinition("LegionFanFullSpeed", "Fan Max", "\uE9CA", order: order++);
            AddTileDefinition("Battery", "Battery", "\uE83F", order: order++);

            // Load custom shortcut tiles from storage
            LoadCustomShortcutTiles();

            // Row 8 - Quick Actions (high order numbers to keep at bottom)
            int actionOrder = 1000;
            AddTileDefinition("ActionTaskManager", "Task Mgr", "\uE7EF", isAction: true, order: actionOrder++);
            AddTileDefinition("ActionExplorer", "Explorer", "\uEC50", isAction: true, order: actionOrder++);
            AddTileDefinition("ActionEndTask", "End Task", "\uE711", isAction: true, order: actionOrder++);
            AddTileDefinition("ActionHibernate", "Hibernate", "\uE708", isAction: true, order: actionOrder++);
        }

        private void AddTileDefinition(string id, string name, string glyph, bool isTrigger = false, bool isAction = false, string customShortcut = null, int order = 0)
        {
            var def = new TileDefinition { Id = id, Name = name, Glyph = glyph, IsVisible = true, IsTrigger = isTrigger, IsAction = isAction, CustomShortcut = customShortcut, Order = order };
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
                    if (!string.IsNullOrEmpty(tile.CustomShortcut))
                    {
                        // Use the stable GUID from QuickSettingsConfig instead of index-based ID
                        // This prevents tile ID mismatch when widget is reloaded
                        string tileId = tile.Id;
                        var def = new TileDefinition
                        {
                            Id = tileId,
                            Name = tile.Name,
                            Glyph = tile.Icon ?? "\uE768",
                            IsVisible = tile.IsVisible,
                            IsTrigger = true,
                            CustomShortcut = tile.CustomShortcut,
                            Order = startingOrder + index  // Order will be overridden by LoadQuickSettingsConfig if saved
                        };
                        qsTileDefinitions.Add(def);
                        qsTileMap[tileId] = def;
                        qsCustomShortcuts.Add(def);
                        index++;
                    }
                }
                Logger.Info($"Loaded {index} custom shortcut tiles from QuickSettingsConfig (using stable GUIDs)");

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
                    // Default selection: Battery Drain, CPU Usage, CPU Temp, GPU Usage, Time Remaining
                    selectedMetrics.AddRange(new[] { MetricType.BatteryDrain, MetricType.CPUUsage, MetricType.CPUTemp, MetricType.GPUUsage, MetricType.TimeRemaining });
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

                foreach (var tile in qsTileDefinitions)
                {
                    string visKey = $"QS_{tile.Id}_Visible";
                    string orderKey = $"QS_{tile.Id}_Order";

                    if (settings.Values.TryGetValue(visKey, out object val) && val is bool visible)
                    {
                        tile.IsVisible = visible;
                    }
                    if (settings.Values.TryGetValue(orderKey, out object orderVal) && orderVal is int order)
                    {
                        tile.Order = order;
                    }
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
                    settings.Values[$"QS_{tile.Id}_Order"] = tile.Order;
                }

                Logger.Info($"Quick Settings config saved (columns: {qsColumnCount})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving Quick Settings config: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle Quick Metrics toggle change
        /// </summary>
        private async void QuickMetricsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            try
            {
                quickMetricsEnabled = QuickMetricsToggle.IsOn;

                // Save setting to local storage
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[QuickMetricsEnabledKey] = quickMetricsEnabled;

                // Update visibility of metrics row and selection panel
                if (QuickMetricsRow != null)
                    QuickMetricsRow.Visibility = quickMetricsEnabled ? Visibility.Visible : Visibility.Collapsed;
                if (MetricsSelectionPanel != null)
                    MetricsSelectionPanel.Visibility = quickMetricsEnabled ? Visibility.Visible : Visibility.Collapsed;

                // Rebuild the metrics grid if enabling
                if (quickMetricsEnabled)
                {
                    RebuildMetricsGrid();
                }

                // Notify helper to start/stop pushing metrics
                if (App.IsConnected)
                {
                    var request = new Windows.Foundation.Collections.ValueSet
                    {
                        { "Command", (int)Shared.Enums.Command.Set },
                        { "Function", (int)Shared.Enums.Function.QuickMetricsEnabled },
                        { "Content", quickMetricsEnabled }
                    };
                    await App.SendMessageAsync(request);
                    Logger.Info($"Quick Metrics toggle set to: {quickMetricsEnabled}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling Quick Metrics toggle: {ex.Message}");
            }
        }

        /// <summary>
        /// Update Quick Metrics display from helper push data
        /// </summary>
        private void UpdateQuickMetrics(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json)) return;

                // Parse all metrics from JSON
                var matches = System.Text.RegularExpressions.Regex.Matches(json,
                    @"""(\w+)""\s*:\s*(-?\d+\.?\d*|true|false)");

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var key = match.Groups[1].Value;
                    var value = match.Groups[2].Value;

                    if (key == "isCharging")
                    {
                        currentMetricsIsCharging = value == "true";
                    }
                    else if (double.TryParse(value, out double numValue))
                    {
                        currentMetricsData[key] = numValue;
                    }
                }

                // Update UI elements on dispatcher thread
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        UpdateMetricsDisplay();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error updating Quick Metrics UI: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error parsing Quick Metrics JSON: {ex.Message}");
            }
        }

        /// <summary>
        /// Update the metrics display based on current data and selected metrics
        /// </summary>
        private void UpdateMetricsDisplay()
        {
            foreach (var metricType in selectedMetrics)
            {
                if (!metricDefinitions.TryGetValue(metricType, out var info) || info.ValueTextBlock == null)
                    continue;

                string displayValue = "--";
                string label = info.Label;

                switch (metricType)
                {
                    case MetricType.BatteryDrain:
                        if (currentMetricsData.TryGetValue("batteryDrain", out var drain))
                        {
                            if (drain > 0)
                                displayValue = $"{drain:F1}W";
                            else if (drain < 0)
                                displayValue = $"+{-drain:F1}W";
                            else
                                displayValue = "--W";
                        }
                        break;

                    case MetricType.BatteryLevel:
                        if (currentMetricsData.TryGetValue("batteryLevel", out var level) && level >= 0)
                            displayValue = $"{level:F0}%";
                        break;

                    case MetricType.CPUUsage:
                        if (currentMetricsData.TryGetValue("cpuUsage", out var cpuUse) && cpuUse >= 0)
                            displayValue = $"{cpuUse:F0}%";
                        break;

                    case MetricType.CPUTemp:
                        if (currentMetricsData.TryGetValue("cpuTemp", out var cpuTemp) && cpuTemp > 0)
                            displayValue = $"{cpuTemp:F0}°";
                        break;

                    case MetricType.CPUWattage:
                        if (currentMetricsData.TryGetValue("cpuWattage", out var cpuWatt) && cpuWatt >= 0)
                            displayValue = $"{cpuWatt:F1}W";
                        break;

                    case MetricType.GPUUsage:
                        if (currentMetricsData.TryGetValue("gpuUsage", out var gpuUse) && gpuUse >= 0)
                            displayValue = $"{gpuUse:F0}%";
                        break;

                    case MetricType.GPUTemp:
                        if (currentMetricsData.TryGetValue("gpuTemp", out var gpuTemp) && gpuTemp > 0)
                            displayValue = $"{gpuTemp:F0}°";
                        break;

                    case MetricType.GPUWattage:
                        if (currentMetricsData.TryGetValue("gpuWattage", out var gpuWatt) && gpuWatt >= 0)
                            displayValue = $"{gpuWatt:F1}W";
                        break;

                    case MetricType.MemoryUsage:
                        if (currentMetricsData.TryGetValue("memoryUsage", out var memUse) && memUse >= 0)
                            displayValue = $"{memUse:F0}%";
                        break;

                    case MetricType.TimeRemaining:
                        currentMetricsData.TryGetValue("timeRemaining", out var timeRem);
                        currentMetricsData.TryGetValue("timeToFull", out var timeFull);
                        if (currentMetricsIsCharging && timeFull > 0)
                        {
                            var hours = (int)(timeFull / 3600);
                            var mins = (int)((timeFull % 3600) / 60);
                            displayValue = $"{hours}:{mins:D2}";
                            label = "To Full";
                        }
                        else if (!currentMetricsIsCharging && timeRem > 0)
                        {
                            var hours = (int)(timeRem / 3600);
                            var mins = (int)((timeRem % 3600) / 60);
                            displayValue = $"{hours}:{mins:D2}";
                            label = "Remaining";
                        }
                        else
                        {
                            displayValue = "--:--";
                            label = currentMetricsIsCharging ? "Charging" : "Time";
                        }
                        break;
                }

                info.ValueTextBlock.Text = displayValue;
                if (info.LabelTextBlock != null)
                    info.LabelTextBlock.Text = label;
            }
        }

        /// <summary>
        /// Update checkbox states based on selected metrics
        /// </summary>
        private void UpdateMetricCheckboxes()
        {
            // Guard to prevent MetricCheckBox_Changed from firing during programmatic updates.
            // Without this, setting IsChecked=true on checkboxes fires the handler, which sees
            // selectedMetrics.Count >= MaxSelectedMetrics and reverts the checkbox to false,
            // triggering Unchecked which removes the metric from the list.
            isUpdatingMetricCheckboxes = true;
            try
            {
                // Map checkboxes to metric types
                var checkboxMap = new Dictionary<CheckBox, MetricType>
                {
                    { MetricCheck_BatteryDrain, MetricType.BatteryDrain },
                    { MetricCheck_BatteryLevel, MetricType.BatteryLevel },
                    { MetricCheck_CPUUsage, MetricType.CPUUsage },
                    { MetricCheck_CPUTemp, MetricType.CPUTemp },
                    { MetricCheck_CPUWattage, MetricType.CPUWattage },
                    { MetricCheck_GPUUsage, MetricType.GPUUsage },
                    { MetricCheck_GPUTemp, MetricType.GPUTemp },
                    { MetricCheck_GPUWattage, MetricType.GPUWattage },
                    { MetricCheck_MemoryUsage, MetricType.MemoryUsage },
                    { MetricCheck_TimeRemaining, MetricType.TimeRemaining }
                };

                foreach (var kvp in checkboxMap)
                {
                    if (kvp.Key != null)
                        kvp.Key.IsChecked = selectedMetrics.Contains(kvp.Value);
                }

                UpdateMetricsSelectionCount();
            }
            finally
            {
                isUpdatingMetricCheckboxes = false;
            }
        }

        /// <summary>
        /// Update the metrics selection count display
        /// </summary>
        private void UpdateMetricsSelectionCount()
        {
            if (MetricsSelectionCount != null)
            {
                MetricsSelectionCount.Text = $"{selectedMetrics.Count}/{MaxSelectedMetrics} selected";
                MetricsSelectionCount.Foreground = new SolidColorBrush(
                    selectedMetrics.Count >= MaxSelectedMetrics
                        ? Windows.UI.Color.FromArgb(255, 255, 150, 100)  // Orange when at max
                        : Windows.UI.Color.FromArgb(255, 102, 102, 102)); // Gray otherwise
            }
        }

        /// <summary>
        /// Handle metric checkbox changes
        /// </summary>
        private void MetricCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (isUpdatingMetricCheckboxes) return;
            if (!(sender is CheckBox checkbox)) return;

            // Map checkbox to metric type
            var checkboxMap = new Dictionary<CheckBox, MetricType>
            {
                { MetricCheck_BatteryDrain, MetricType.BatteryDrain },
                { MetricCheck_BatteryLevel, MetricType.BatteryLevel },
                { MetricCheck_CPUUsage, MetricType.CPUUsage },
                { MetricCheck_CPUTemp, MetricType.CPUTemp },
                { MetricCheck_CPUWattage, MetricType.CPUWattage },
                { MetricCheck_GPUUsage, MetricType.GPUUsage },
                { MetricCheck_GPUTemp, MetricType.GPUTemp },
                { MetricCheck_GPUWattage, MetricType.GPUWattage },
                { MetricCheck_MemoryUsage, MetricType.MemoryUsage },
                { MetricCheck_TimeRemaining, MetricType.TimeRemaining }
            };

            if (!checkboxMap.TryGetValue(checkbox, out var metricType))
                return;

            bool isChecked = checkbox.IsChecked == true;

            if (isChecked)
            {
                // Trying to add - check if at max
                if (selectedMetrics.Count >= MaxSelectedMetrics)
                {
                    checkbox.IsChecked = false;
                    return;
                }
                if (!selectedMetrics.Contains(metricType))
                    selectedMetrics.Add(metricType);
            }
            else
            {
                selectedMetrics.Remove(metricType);
            }

            // Save selection
            SaveMetricsSelection();

            // Update count display
            UpdateMetricsSelectionCount();

            // Rebuild the metrics grid
            RebuildMetricsGrid();
        }

        /// <summary>
        /// Save metrics selection to local settings
        /// </summary>
        private void SaveMetricsSelection()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                var selectionStr = string.Join(",", selectedMetrics.Select(m => m.ToString()));
                settings.Values[QuickMetricsSelectionKey] = selectionStr;
                Logger.Info($"Saved metrics selection: {selectionStr}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving metrics selection: {ex.Message}");
            }
        }

        /// <summary>
        /// Rebuild the metrics grid based on selected metrics
        /// </summary>
        private void RebuildMetricsGrid()
        {
            if (QuickMetricsGrid == null) return;

            QuickMetricsGrid.Children.Clear();
            QuickMetricsGrid.ColumnDefinitions.Clear();

            if (selectedMetrics.Count == 0)
            {
                QuickMetricsRow.Visibility = Visibility.Collapsed;
                return;
            }

            // Create columns for each selected metric
            foreach (var _ in selectedMetrics)
            {
                QuickMetricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            // Create UI for each selected metric
            int colIndex = 0;
            foreach (var metricType in selectedMetrics)
            {
                if (!metricDefinitions.TryGetValue(metricType, out var info))
                    continue;

                // Create metric panel
                var panel = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Value row (icon + value)
                var valueRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var icon = new FontIcon
                {
                    Glyph = info.Glyph,
                    FontSize = 14,
                    Foreground = new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColorLight2"]),
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var valueText = new TextBlock
                {
                    Text = "--",
                    FontSize = 14,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    VerticalAlignment = VerticalAlignment.Center
                };
                info.ValueTextBlock = valueText;

                valueRow.Children.Add(icon);
                valueRow.Children.Add(valueText);

                // Label
                var labelText = new TextBlock
                {
                    Text = info.Label,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136)), // #888888
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                info.LabelTextBlock = labelText;

                panel.Children.Add(valueRow);
                panel.Children.Add(labelText);

                Grid.SetColumn(panel, colIndex);
                QuickMetricsGrid.Children.Add(panel);

                colIndex++;
            }

            // Show the row if we have metrics
            if (quickMetricsEnabled && selectedMetrics.Count > 0)
            {
                QuickMetricsRow.Visibility = Visibility.Visible;
            }

            // Also rebuild the reorder list
            RebuildMetricsReorderList();
        }

        /// <summary>
        /// Rebuild the metrics reorder list UI
        /// </summary>
        private void RebuildMetricsReorderList()
        {
            if (MetricsReorderList == null || MetricsReorderSection == null) return;

            MetricsReorderList.Children.Clear();

            // Hide reorder section if no metrics selected
            if (selectedMetrics.Count == 0)
            {
                MetricsReorderSection.Visibility = Visibility.Collapsed;
                return;
            }

            MetricsReorderSection.Visibility = Visibility.Visible;

            // Create a row for each selected metric
            for (int i = 0; i < selectedMetrics.Count; i++)
            {
                var metricType = selectedMetrics[i];
                if (!metricDefinitions.TryGetValue(metricType, out var info))
                    continue;

                var row = new Grid
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 4, 4),
                    Margin = new Thickness(0, 0, 0, 0)
                };

                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); // Index
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); // Up button
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); // Down button

                // Index number
                var indexText = new TextBlock
                {
                    Text = $"{i + 1}",
                    Foreground = new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColorLight2"]),
                    FontSize = 11,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(indexText, 0);
                row.Children.Add(indexText);

                // Metric name
                var nameText = new TextBlock
                {
                    Text = info.Label,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(nameText, 1);
                row.Children.Add(nameText);

                // Up button
                var upButton = new Button
                {
                    Content = new FontIcon { Glyph = "\uE70E", FontSize = 10 }, // ChevronUp
                    Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    Padding = new Thickness(4),
                    MinWidth = 24,
                    MinHeight = 24,
                    IsEnabled = i > 0,
                    Opacity = i > 0 ? 1.0 : 0.3,
                    Tag = metricType
                };
                upButton.Click += MetricMoveUp_Click;
                Grid.SetColumn(upButton, 2);
                row.Children.Add(upButton);

                // Down button
                var downButton = new Button
                {
                    Content = new FontIcon { Glyph = "\uE70D", FontSize = 10 }, // ChevronDown
                    Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    Padding = new Thickness(4),
                    MinWidth = 24,
                    MinHeight = 24,
                    IsEnabled = i < selectedMetrics.Count - 1,
                    Opacity = i < selectedMetrics.Count - 1 ? 1.0 : 0.3,
                    Tag = metricType
                };
                downButton.Click += MetricMoveDown_Click;
                Grid.SetColumn(downButton, 3);
                row.Children.Add(downButton);

                MetricsReorderList.Children.Add(row);
            }
        }

        /// <summary>
        /// Handle move up button click for metric reordering
        /// </summary>
        private void MetricMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is MetricType metricType))
                return;

            int index = selectedMetrics.IndexOf(metricType);
            if (index <= 0) return;

            // Swap with previous item
            selectedMetrics.RemoveAt(index);
            selectedMetrics.Insert(index - 1, metricType);

            // Save and rebuild
            SaveMetricsSelection();
            RebuildMetricsGrid();
        }

        /// <summary>
        /// Handle move down button click for metric reordering
        /// </summary>
        private void MetricMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is MetricType metricType))
                return;

            int index = selectedMetrics.IndexOf(metricType);
            if (index < 0 || index >= selectedMetrics.Count - 1) return;

            // Swap with next item
            selectedMetrics.RemoveAt(index);
            selectedMetrics.Insert(index + 1, metricType);

            // Save and rebuild
            SaveMetricsSelection();
            RebuildMetricsGrid();
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

        /// <summary>
        /// Build sortable grid for tile customization
        /// </summary>
        private void BuildSortableGrid()
        {
            if (TileSortableGrid == null) return;

            TileSortableGrid.Children.Clear();

            // Get all tiles sorted by order (including hidden ones)
            var allTiles = qsTileDefinitions
                .Where(t => !ShouldSkipTile(t))
                .OrderBy(t => t.Order)
                .ToList();

            // Build rows of tiles (3 or 4 columns based on setting)
            Grid currentRow = null;
            int colIndex = 0;

            for (int i = 0; i < allTiles.Count; i++)
            {
                if (colIndex == 0)
                {
                    currentRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    // Add column definitions dynamically based on qsColumnCount
                    for (int c = 0; c < qsColumnCount; c++)
                    {
                        if (c > 0) currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });  // Spacer
                        currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    }
                    TileSortableGrid.Children.Add(currentRow);
                }

                var tile = allTiles[i];
                var miniTile = CreateMiniTileForSort(tile, i);
                Grid.SetColumn(miniTile, colIndex * 2);
                currentRow.Children.Add(miniTile);

                colIndex++;
                if (colIndex >= qsColumnCount)
                {
                    colIndex = 0;
                }
            }
        }

        /// <summary>
        /// Create a mini tile button for the sortable grid
        /// </summary>
        private Button CreateMiniTileForSort(TileDefinition tile, int index)
        {
            bool isSelected = qsSelectedTileForMove?.Id == tile.Id;

            var button = new Button
            {
                Tag = tile.Id,
                MinHeight = 60,
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = isSelected
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 180))  // Highlight selected
                    : (tile.IsVisible
                        ? tileOffBrush
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(128, 26, 28, 30))),  // Dimmed if hidden
                BorderBrush = isSelected
                    ? new SolidColorBrush(Windows.UI.Colors.White)
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(80, 80, 85, 92)),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(8),
                UseSystemFocusVisuals = true,
                FocusVisualPrimaryBrush = new SolidColorBrush(Windows.UI.Colors.White),
                FocusVisualSecondaryBrush = new SolidColorBrush(Windows.UI.Colors.Transparent),
                TabIndex = index
            };

            var content = new Grid();

            // Icon and name stack
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new FontIcon
            {
                Glyph = tile.Glyph,
                FontSize = 18,
                Foreground = new SolidColorBrush(tile.IsVisible ? Windows.UI.Colors.White : Windows.UI.Colors.Gray)
            });
            stack.Children.Add(new TextBlock
            {
                Text = tile.Name,
                FontSize = 10,
                Foreground = new SolidColorBrush(tile.IsVisible ? Windows.UI.Colors.White : Windows.UI.Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            content.Children.Add(stack);

            // Eye icon (top-right) - shows visibility status
            var eyeIcon = new FontIcon
            {
                Glyph = tile.IsVisible ? "\uE7B3" : "\uED1A",  // Eye / Eye crossed
                FontSize = 12,
                Foreground = new SolidColorBrush(tile.IsVisible
                    ? Windows.UI.Color.FromArgb(255, 100, 200, 100)   // Green for visible
                    : Windows.UI.Color.FromArgb(255, 200, 100, 100)), // Red for hidden
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0)
            };
            content.Children.Add(eyeIcon);

            // Order number badge (bottom-left)
            var orderText = new TextBlock
            {
                Text = (index + 1).ToString(),
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 0, 0, 2)
            };
            content.Children.Add(orderText);

            // Custom shortcut indicator (bottom-right) - shows it can be deleted
            if (!string.IsNullOrEmpty(tile.CustomShortcut))
            {
                var customIcon = new FontIcon
                {
                    Glyph = "\uE932",  // Pin icon to indicate custom
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 200, 100)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 4, 2)
                };
                content.Children.Add(customIcon);
            }

            button.Content = content;
            button.Click += SortableTile_Click;

            return button;
        }

        /// <summary>
        /// Handle delete button click on sortable tile for custom shortcuts
        /// </summary>
        private void SortableTileDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button button) || !(button.Tag is string tileId))
                    return;

                if (!qsTileMap.TryGetValue(tileId, out var tile))
                    return;

                DeleteCustomShortcutTile(tile);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling sortable tile delete: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle tap on sortable tile - select, swap, or toggle visibility
        /// </summary>
        private void SortableTile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button button) || !(button.Tag is string tileId))
                    return;

                if (!qsTileMap.TryGetValue(tileId, out var clickedTile))
                    return;

                if (qsSelectedTileForMove == null)
                {
                    // First tap: select tile - just update visuals, don't rebuild
                    qsSelectedTileForMove = clickedTile;
                    UpdateSelectedTileIndicator(clickedTile);
                    UpdateSortableGridVisuals(tileId);
                }
                else if (qsSelectedTileForMove.Id == clickedTile.Id)
                {
                    // Tap same tile: toggle visibility - need rebuild for eye icon change
                    clickedTile.IsVisible = !clickedTile.IsVisible;
                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                    BuildSortableGridPreserveScroll(tileId);
                    Logger.Info($"Toggled visibility for {clickedTile.Name}: {clickedTile.IsVisible}");
                }
                else
                {
                    // Tap different tile: swap Order values - need rebuild for reorder
                    int tempOrder = qsSelectedTileForMove.Order;
                    qsSelectedTileForMove.Order = clickedTile.Order;
                    clickedTile.Order = tempOrder;

                    Logger.Info($"Swapped tile order: {qsSelectedTileForMove.Name} <-> {clickedTile.Name}");

                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                    BuildSortableGridPreserveScroll(tileId);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling sortable tile click: {ex.Message}");
            }
        }

        /// <summary>
        /// Build sortable grid while preserving scroll position and focus
        /// </summary>
        private void BuildSortableGridPreserveScroll(string focusTileId = null)
        {
            // Save scroll position
            double scrollOffset = 0;
            if (QuickSettingsScrollViewer != null)
            {
                scrollOffset = QuickSettingsScrollViewer.VerticalOffset;
            }

            BuildSortableGrid();

            // Restore scroll position and focus after layout update
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                if (QuickSettingsScrollViewer != null && scrollOffset > 0)
                {
                    QuickSettingsScrollViewer.ChangeView(null, scrollOffset, null, true);
                }

                // Restore focus to the specified tile
                if (!string.IsNullOrEmpty(focusTileId) && TileSortableGrid != null)
                {
                    foreach (var child in TileSortableGrid.Children)
                    {
                        if (child is Grid row)
                        {
                            foreach (var cell in row.Children)
                            {
                                if (cell is Button btn && btn.Tag is string id && id == focusTileId)
                                {
                                    btn.Focus(FocusState.Programmatic);
                                    return;
                                }
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Update visual state of sortable tiles without rebuilding (for selection changes)
        /// </summary>
        private void UpdateSortableGridVisuals(string focusTileId = null)
        {
            if (TileSortableGrid == null) return;

            foreach (var child in TileSortableGrid.Children)
            {
                if (child is Grid row)
                {
                    foreach (var cell in row.Children)
                    {
                        if (cell is Button btn && btn.Tag is string id && qsTileMap.TryGetValue(id, out var tile))
                        {
                            bool isSelected = qsSelectedTileForMove?.Id == id;

                            // Update button background and border
                            btn.Background = isSelected
                                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 180))
                                : (tile.IsVisible
                                    ? tileOffBrush
                                    : new SolidColorBrush(Windows.UI.Color.FromArgb(128, 26, 28, 30)));
                            btn.BorderBrush = isSelected
                                ? new SolidColorBrush(Windows.UI.Colors.White)
                                : new SolidColorBrush(Windows.UI.Color.FromArgb(80, 80, 85, 92));
                            btn.BorderThickness = new Thickness(isSelected ? 2 : 1);

                            // Focus the specified tile
                            if (!string.IsNullOrEmpty(focusTileId) && id == focusTileId)
                            {
                                btn.Focus(FocusState.Programmatic);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Update the selected tile indicator text
        /// </summary>
        private void UpdateSelectedTileIndicator(TileDefinition tile)
        {
            if (SelectedTileIndicator == null || SelectedTileText == null)
                return;

            if (tile == null)
            {
                SelectedTileIndicator.Visibility = Visibility.Collapsed;
                if (DeleteSelectedTileButton != null)
                    DeleteSelectedTileButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                SelectedTileIndicator.Visibility = Visibility.Visible;
                SelectedTileText.Text = $"Selected: {tile.Name}\nTap another tile to swap, or tap again to toggle visibility";

                // Show delete button for custom shortcuts (identified by having a CustomShortcut value)
                if (DeleteSelectedTileButton != null)
                {
                    DeleteSelectedTileButton.Visibility = !string.IsNullOrEmpty(tile.CustomShortcut)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Handle delete button click in the selected tile indicator
        /// </summary>
        private void DeleteSelectedTile_Click(object sender, RoutedEventArgs e)
        {
            if (qsSelectedTileForMove != null && !string.IsNullOrEmpty(qsSelectedTileForMove.CustomShortcut))
            {
                DeleteCustomShortcutTile(qsSelectedTileForMove);
            }
        }

        /// <summary>
        /// Delete a custom shortcut tile
        /// </summary>
        private void DeleteCustomShortcutTile(TileDefinition tile)
        {
            try
            {
                // Remove from QuickSettingsConfig persistent storage first
                // Need to find the matching config tile by custom shortcut path
                var config = QuickSettings.QuickSettingsConfig.Instance;
                var configTile = config.Tiles.FirstOrDefault(t =>
                    t.Type == QuickSettings.TileType.CustomShortcut &&
                    t.CustomShortcut == tile.CustomShortcut);
                if (configTile != null)
                {
                    config.RemoveTile(configTile.Id);
                }

                // Remove from local lists
                qsTileDefinitions.Remove(tile);
                qsTileMap.Remove(tile.Id);
                qsCustomShortcuts.Remove(tile);

                // Clear selection if we deleted the selected tile
                if (qsSelectedTileForMove?.Id == tile.Id)
                {
                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                }

                BuildSortableGridPreserveScroll();
                // Don't rebuild main tiles here - they'll update when panel closes

                Logger.Info($"Deleted custom shortcut tile: {tile.Name}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error deleting custom shortcut tile: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete a custom shortcut tile (button click handler - legacy)
        /// </summary>
        private void DeleteCustomShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is string tileId)
                {
                    var tile = qsTileDefinitions.FirstOrDefault(t => t.Id == tileId);
                    if (tile != null)
                    {
                        DeleteCustomShortcutTile(tile);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error deleting custom shortcut tile: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a tile should be skipped based on hardware detection
        /// </summary>
        private bool ShouldSkipTile(TileDefinition tile)
        {
            // Skip Legion tiles if not detected
            if ((tile.Id == "LegionTouchpad" || tile.Id == "LegionLightMode" ||
                 tile.Id == "LegionDesktopControls" || tile.Id == "LegionRemapControls" ||
                 tile.Id == "LegionChargeLimit" || tile.Id == "LegionPowerLight") &&
                (legionGoDetected?.Value != true))
            {
                return true;
            }

            // TDP Mode tile is now available for all devices (Legion uses hardware presets, generic uses TDP values)

            // Skip Lossless Scaling tile if not installed
            if (tile.Id == "LosslessScaling" && (losslessScalingInstalled?.Value != true))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Rebuild tile grid with only visible tiles, in 3-column layout
        /// </summary>
        private void RebuildQuickSettingsTiles()
        {
            if (QuickSettingsTilesContainer == null) return;

            QuickSettingsTilesContainer.Children.Clear();

            // Get tiles to display - in edit mode show all (including hidden), otherwise only visible
            var tilesToShow = qsTileDefinitions
                .Where(t => !ShouldSkipTile(t) && (qsEditMode || t.IsVisible))
                .OrderBy(t => t.Order)
                .ToList();

            // Build rows of tiles (3 or 4 columns based on setting)
            Grid currentRow = null;
            int colIndex = 0;

            for (int i = 0; i < tilesToShow.Count; i++)
            {
                if (colIndex == 0)
                {
                    currentRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    // Add column definitions dynamically based on qsColumnCount
                    for (int c = 0; c < qsColumnCount; c++)
                    {
                        if (c > 0) currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });  // Spacer
                        currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    }
                    QuickSettingsTilesContainer.Children.Add(currentRow);
                }

                var tile = tilesToShow[i];
                var tileButton = CreateTileButton(tile);
                Grid.SetColumn(tileButton, colIndex * 2);
                currentRow.Children.Add(tileButton);

                colIndex++;
                if (colIndex >= qsColumnCount)
                {
                    colIndex = 0;
                }
            }
        }

        /// <summary>
        /// Create a tile button for the given definition
        /// </summary>
        private Button CreateTileButton(TileDefinition tile)
        {
            // Action tiles get a distinct background color
            var bgBrush = tile.IsAction
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 32, 48))  // Dark purple for action tiles
                : tileOffBrush;

            var button = new Button
            {
                Tag = tile.Id,
                Style = Resources["QuickSettingsTileStyle"] as Style,
                Background = bgBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            content.Children.Add(new FontIcon
            {
                Glyph = tile.Glyph,
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            content.Children.Add(new TextBlock
            {
                Text = tile.Name,
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            });

            // Action tiles show "Action" instead of state
            var stateText = new TextBlock
            {
                Text = tile.IsAction ? "Action" : "Off",
                FontSize = 13,
                Foreground = tile.IsAction
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 150, 200))  // Light purple for action
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };

            // For Profile tile, wrap in Canvas for scrolling long names
            if (tile.Id == "Profile")
            {
                var transform = new TranslateTransform { X = 0 };
                stateText.RenderTransform = transform;
                stateText.Margin = new Thickness(0); // Remove margin, Canvas handles positioning

                var canvas = new Canvas
                {
                    Width = 90, // Tile width for text
                    Height = 18,
                    Margin = new Thickness(0, 2, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                canvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 90, 18) };
                canvas.Children.Add(stateText);

                content.Children.Add(canvas);

                tile.StateTextCanvas = canvas;
                tile.StateTextTransform = transform;
            }
            else
            {
                content.Children.Add(stateText);
            }

            button.Content = content;
            button.Click += QuickSettingsTile_Click;

            tile.TileButton = button;
            tile.StateText = stateText;

            return button;
        }

        /// <summary>
        /// Updates the scroll animation for the Profile tile's state text.
        /// If text is wider than the canvas, starts a scrolling animation.
        /// </summary>
        private void UpdateProfileTileScrollAnimation(TileDefinition profileTile)
        {
            if (profileTile?.StateText == null || profileTile.StateTextCanvas == null || profileTile.StateTextTransform == null)
                return;

            // Stop any existing animation
            if (profileTile.ScrollStoryboard != null)
            {
                profileTile.ScrollStoryboard.Stop();
                profileTile.ScrollStoryboard = null;
            }

            // Reset transform
            profileTile.StateTextTransform.X = 0;

            // Measure text width
            profileTile.StateText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = profileTile.StateText.DesiredSize.Width;
            double canvasWidth = profileTile.StateTextCanvas.Width;

            // If text fits, no animation needed
            if (textWidth <= canvasWidth)
            {
                // Center the text
                Canvas.SetLeft(profileTile.StateText, (canvasWidth - textWidth) / 2);
                return;
            }

            // Text is too wide - set up scrolling animation
            Canvas.SetLeft(profileTile.StateText, 0);

            // Calculate scroll distance and duration
            double scrollDistance = textWidth - canvasWidth + 10; // Extra padding
            double scrollSpeed = 30; // pixels per second
            double scrollDuration = scrollDistance / scrollSpeed;

            var storyboard = new Storyboard();
            var animation = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            // Pause at start
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = 0
            });
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5)),
                Value = 0
            });

            // Scroll left
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + scrollDuration)),
                Value = -scrollDistance
            });

            // Pause at end
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3 + scrollDuration)),
                Value = -scrollDistance
            });

            // Scroll back right
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3 + scrollDuration * 2)),
                Value = 0
            });

            // Pause before repeat
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.5 + scrollDuration * 2)),
                Value = 0
            });

            Storyboard.SetTarget(animation, profileTile.StateTextTransform);
            Storyboard.SetTargetProperty(animation, "X");
            storyboard.Children.Add(animation);

            profileTile.ScrollStoryboard = storyboard;
            storyboard.Begin();
        }

        /// <summary>
        /// Update all Quick Settings tile states based on current property values
        /// </summary>
        private void UpdateQuickSettingsTileStates()
        {
            if (!quickSettingsInitialized)
            {
                InitializeQuickSettings();
            }

            try
            {
                var accentForeground = new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColorLight2"]);
                var offForeground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));

                // TDP Mode tile - color-coded backgrounds based on preset or mode
                if (qsTileMap.TryGetValue("TDPMode", out var tdpTile) && tdpTile.TileButton != null)
                {
                    bool isLegion = legionGoDetected?.Value == true;
                    int selectedIndex = TDPModeComboBox?.SelectedIndex ?? 0;
                    string modeText;
                    SolidColorBrush tdpModeBrush;

                    // Use custom presets if enabled
                    if (useCustomTDPPresets && tdpPresets != null && tdpPresets.Count > 0)
                    {
                        if (selectedIndex < tdpPresets.Count)
                        {
                            var preset = tdpPresets[selectedIndex];
                            modeText = $"{preset.Name} ({preset.TdpWatts}W)";

                            // Color based on LegionModeValue or default to purple for custom
                            switch (preset.LegionModeValue)
                            {
                                case 1: // Quiet - Desaturated Blue
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 45, 60));
                                    break;
                                case 2: // Balanced - Grey
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 50, 55));
                                    break;
                                case 3: // Performance - Desaturated Red
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 40, 40));
                                    break;
                                default: // Custom preset (no LegionModeValue) - Purple
                                    tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 42, 58));
                                    break;
                            }
                        }
                        else
                        {
                            // Custom mode (last item, slider-controlled)
                            int currentTdp = (int)(TDPSlider?.Value ?? 15);
                            modeText = $"Custom ({currentTdp}W)";
                            tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 42, 58));
                        }
                    }
                    else
                    {
                        // Default hardcoded mode display
                        int mode;
                        if (isLegion && legionPerformanceMode != null)
                        {
                            mode = legionPerformanceMode.Value;
                        }
                        else
                        {
                            int[] modeValues = { 1, 2, 3, 255 };
                            mode = (selectedIndex >= 0 && selectedIndex < modeValues.Length) ? modeValues[selectedIndex] : 2;
                        }

                        int[] genericTDPValues = { 8, 15, 25 }; // Quiet, Balanced, Performance TDP values
                        switch (mode)
                        {
                            case 1: // Quiet - Desaturated Blue
                                modeText = isLegion ? "Quiet" : $"Quiet ({genericTDPValues[0]}W)";
                                tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 45, 60));
                                break;
                            case 2: // Balanced - Grey
                                modeText = isLegion ? "Balanced" : $"Balanced ({genericTDPValues[1]}W)";
                                tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 50, 55));
                                break;
                            case 3: // Performance - Desaturated Red
                                modeText = isLegion ? "Performance" : $"Perf ({genericTDPValues[2]}W)";
                                tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 40, 40));
                                break;
                            case 255: // Custom - Desaturated Purple
                                int currentTdp = (int)(TDPSlider?.Value ?? 15);
                                modeText = $"Custom ({currentTdp}W)";
                                tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 42, 58));
                                break;
                            default:
                                modeText = isLegion ? "Balanced" : $"Balanced ({genericTDPValues[1]}W)";
                                tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 50, 55));
                                break;
                        }
                    }

                    tdpTile.StateText.Text = modeText;
                    tdpTile.StateText.Foreground = accentForeground;
                    tdpTile.TileButton.Background = tdpModeBrush;
                }

                // AutoTDP tile
                if (qsTileMap.TryGetValue("AutoTDP", out var autoTdpTile) && autoTdpTile.TileButton != null)
                {
                    bool enabled = AutoTDPToggle?.IsOn ?? false;
                    int targetFps = (int)(AutoTDPTargetFPSSlider?.Value ?? 60);
                    string stateText = enabled ? $"{targetFps} FPS" : "Off";
                    autoTdpTile.StateText.Text = stateText;
                    autoTdpTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    autoTdpTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Profile tile
                if (qsTileMap.TryGetValue("Profile", out var profileTile) && profileTile.TileButton != null)
                {
                    bool perGame = perGameProfile?.Value ?? false;
                    bool defaultProfileActive = defaultGameProfileEnabled?.Value ?? false;
                    string gameName = (runningGame != null && runningGame.Value.IsValid()) ? runningGame.Value.GameId.Name : "Per-Game";

                    // Show game name with gradient when default game profile is active
                    string profileName;
                    if (defaultProfileActive)
                    {
                        // Use game name from current profile or running game
                        profileName = currentDefaultGameProfile?.GameName ?? gameName;
                        profileTile.StateText.Text = profileName;
                        profileTile.StateText.Foreground = accentForeground;
                        profileTile.TileButton.Background = tileDefaultProfileBrush;
                    }
                    else
                    {
                        profileName = perGame ? gameName : "Global";
                        profileTile.StateText.Text = profileName;
                        profileTile.StateText.Foreground = perGame ? accentForeground : offForeground;
                        profileTile.TileButton.Background = perGame ? tileOnBrush : tileOffBrush;
                    }

                    // Update scroll animation for long profile names
                    UpdateProfileTileScrollAnimation(profileTile);
                }

                // Performance Overlay tile
                if (qsTileMap.TryGetValue("Overlay", out var overlayTile) && overlayTile.TileButton != null)
                {
                    if (osdProvider == 1) // AMD
                    {
                        string amdLevelText = amdOverlayLevel > 0 ? $"AMD {amdOverlayLevel}" : "Off";
                        overlayTile.StateText.Text = amdLevelText;
                        overlayTile.StateText.Foreground = amdOverlayLevel > 0 ? accentForeground : offForeground;
                        overlayTile.TileButton.Background = amdOverlayLevel > 0 ? tileOnBrush : tileOffBrush;
                    }
                    else // RTSS
                    {
                        int level = (int)(osd?.Value ?? 0);
                        string levelText;
                        switch (level)
                        {
                            case 0: levelText = "Off"; break;
                            case 1: levelText = "Basic"; break;
                            case 2: levelText = "Detailed"; break;
                            case 3: levelText = "Full"; break;
                            default: levelText = "Off"; break;
                        }
                        overlayTile.StateText.Text = levelText;
                        overlayTile.StateText.Foreground = level > 0 ? accentForeground : offForeground;
                        overlayTile.TileButton.Background = level > 0 ? tileOnBrush : tileOffBrush;
                    }
                }

                // Power Mode tile
                if (qsTileMap.TryGetValue("PowerMode", out var powerModeTile) && powerModeTile.TileButton != null)
                {
                    int mode = osPowerMode?.Value ?? 1;
                    string modeText;
                    switch (mode)
                    {
                        case 0: modeText = "Efficiency"; break;
                        case 1: modeText = "Balanced"; break;
                        case 2: modeText = "Performance"; break;
                        default: modeText = "Balanced"; break;
                    }
                    powerModeTile.StateText.Text = modeText;
                    powerModeTile.StateText.Foreground = mode != 1 ? accentForeground : offForeground;
                    powerModeTile.TileButton.Background = mode == 2 ? tileOnBrush : (mode == 0 ? tileActiveBrush : tileOffBrush);
                }

                // FPS Limit tile
                if (qsTileMap.TryGetValue("FPSLimit", out var fpsLimitTile) && fpsLimitTile.TileButton != null)
                {
                    int limit = fpsLimit?.Value ?? 0;
                    string limitText = limit == 0 ? "Off" : $"{limit}";
                    fpsLimitTile.StateText.Text = limitText;
                    fpsLimitTile.StateText.Foreground = limit > 0 ? accentForeground : offForeground;
                    fpsLimitTile.TileButton.Background = limit > 0 ? tileOnBrush : tileOffBrush;
                }

                // Resolution tile
                if (qsTileMap.TryGetValue("Resolution", out var resTile) && resTile.TileButton != null)
                {
                    string currentRes = resolution?.Value ?? "1920x1080";
                    resTile.StateText.Text = currentRes;
                    resTile.StateText.Foreground = accentForeground;
                    resTile.TileButton.Background = tileOffBrush;
                }

                // Rotation tile
                if (qsTileMap.TryGetValue("Rotation", out var rotationTile) && rotationTile.TileButton != null)
                {
                    string orientationText = displayOrientation?.GetOrientationText() ?? "Landscape";
                    bool isPortrait = (displayOrientation?.Value ?? 0) == 1 || (displayOrientation?.Value ?? 0) == 3;
                    rotationTile.StateText.Text = orientationText;
                    rotationTile.StateText.Foreground = isPortrait ? accentForeground : offForeground;
                    rotationTile.TileButton.Background = isPortrait ? tileOnBrush : tileOffBrush;
                }

                // HDR tile
                if (qsTileMap.TryGetValue("HDR", out var hdrTile) && hdrTile.TileButton != null)
                {
                    bool supported = hdrSupported?.Value ?? false;
                    bool enabled = hdrEnabled?.Value ?? false;
                    hdrTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    hdrTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    hdrTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Lossless Scaling tile
                if (qsTileMap.TryGetValue("LosslessScaling", out var lsTile) && lsTile.TileButton != null)
                {
                    bool enabled = losslessScalingEnabled?.Value ?? false;
                    lsTile.StateText.Text = enabled ? "On" : "Off";
                    lsTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    lsTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // RIS (Radeon Image Sharpening) tile
                if (qsTileMap.TryGetValue("RIS", out var risTile) && risTile.TileButton != null)
                {
                    bool supported = amdImageSharpeningSupported?.Value ?? false;
                    bool enabled = amdImageSharpeningEnabled?.Value ?? false;
                    risTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    risTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    risTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // AFMF tile
                if (qsTileMap.TryGetValue("AFMF", out var afmfTile) && afmfTile.TileButton != null)
                {
                    bool supported = amdFluidMotionFrameSupported?.Value ?? false;
                    bool enabled = amdFluidMotionFrameEnabled?.Value ?? false;
                    afmfTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    afmfTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    afmfTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // RSR tile
                if (qsTileMap.TryGetValue("RSR", out var rsrTile) && rsrTile.TileButton != null)
                {
                    bool supported = amdRadeonSuperResolutionSupported?.Value ?? false;
                    bool enabled = amdRadeonSuperResolutionEnabled?.Value ?? false;
                    rsrTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    rsrTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    rsrTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Anti-Lag tile
                if (qsTileMap.TryGetValue("AntiLag", out var antiLagTile) && antiLagTile.TileButton != null)
                {
                    bool supported = amdRadeonAntiLagSupported?.Value ?? false;
                    bool enabled = amdRadeonAntiLagEnabled?.Value ?? false;
                    antiLagTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    antiLagTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    antiLagTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Radeon Chill tile
                if (qsTileMap.TryGetValue("RadeonChill", out var chillTile) && chillTile.TileButton != null)
                {
                    bool supported = amdRadeonChillSupported?.Value ?? false;
                    bool enabled = amdRadeonChillEnabled?.Value ?? false;
                    chillTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    chillTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    chillTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // CPU Boost tile
                if (qsTileMap.TryGetValue("CPUBoost", out var boostTile) && boostTile.TileButton != null)
                {
                    bool enabled = cpuBoost?.Value ?? false;
                    boostTile.StateText.Text = enabled ? "On" : "Off";
                    boostTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    boostTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // EPP tile
                if (qsTileMap.TryGetValue("EPP", out var eppTile) && eppTile.TileButton != null)
                {
                    int eppValue = (int)(cpuEPP?.Value ?? 0);
                    eppTile.StateText.Text = $"{eppValue}%";
                    eppTile.StateText.Foreground = accentForeground;
                    eppTile.TileButton.Background = eppValue > 50 ? tileActiveBrush : tileOffBrush;
                }

                // Keyboard trigger tile
                if (qsTileMap.TryGetValue("Keyboard", out var keyboardTile) && keyboardTile.TileButton != null)
                {
                    keyboardTile.StateText.Text = "Open";
                    keyboardTile.StateText.Foreground = accentForeground;
                    keyboardTile.TileButton.Background = tileTriggerBrush;
                }

                // Custom shortcut tiles
                foreach (var shortcutTile in qsCustomShortcuts)
                {
                    if (shortcutTile.TileButton != null && shortcutTile.StateText != null)
                    {
                        shortcutTile.StateText.Text = shortcutTile.CustomShortcut ?? "Run";
                        shortcutTile.StateText.Foreground = accentForeground;
                        shortcutTile.TileButton.Background = tileTriggerBrush;
                    }
                }

                // Legion Touchpad tile
                if (qsTileMap.TryGetValue("LegionTouchpad", out var touchpadTile) && touchpadTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = legionTouchpadEnabled?.Value ?? false;
                        touchpadTile.StateText.Text = enabled ? "On" : "Off";
                        touchpadTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        touchpadTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Light Mode tile
                if (qsTileMap.TryGetValue("LegionLightMode", out var lightTile) && lightTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        int mode = legionLightMode?.Value ?? 0;
                        string modeText;
                        switch (mode)
                        {
                            case 0: modeText = "Off"; break;
                            case 1: modeText = "Static"; break;
                            case 2: modeText = "Breathing"; break;
                            case 3: modeText = "Rainbow"; break;
                            case 4: modeText = "Spiral"; break;
                            default: modeText = "Off"; break;
                        }
                        lightTile.StateText.Text = modeText;
                        lightTile.StateText.Foreground = mode > 0 ? accentForeground : offForeground;
                        lightTile.TileButton.Background = mode > 0 ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Desktop Controls tile
                if (qsTileMap.TryGetValue("LegionDesktopControls", out var desktopTile) && desktopTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = LegionDesktopControlsToggle?.IsOn ?? false;
                        desktopTile.StateText.Text = enabled ? "On" : "Off";
                        desktopTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        desktopTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Remap Controls tile
                if (qsTileMap.TryGetValue("LegionRemapControls", out var remapTile) && remapTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool isGameProfile = LegionControllerProfileToggle?.IsOn == true && HasValidGame(currentGameName);
                        string profileName = isGameProfile ? currentGameName : "Global";
                        // Truncate long names
                        if (profileName.Length > 10)
                            profileName = profileName.Substring(0, 9) + "…";
                        remapTile.StateText.Text = profileName;
                        remapTile.StateText.Foreground = isGameProfile ? accentForeground : offForeground;
                        remapTile.TileButton.Background = isGameProfile ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Charge Limit tile (80% battery limit)
                if (qsTileMap.TryGetValue("LegionChargeLimit", out var chargeLimitTile) && chargeLimitTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = legionChargeLimit?.Value ?? false;
                        chargeLimitTile.StateText.Text = enabled ? "80%" : "Off";
                        chargeLimitTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        chargeLimitTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Power Light tile
                if (qsTileMap.TryGetValue("LegionPowerLight", out var powerLightTile) && powerLightTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = legionPowerLight?.Value ?? false;
                        powerLightTile.StateText.Text = enabled ? "On" : "Off";
                        powerLightTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        powerLightTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Fan Full Speed tile (Legion or GPD)
                if (qsTileMap.TryGetValue("LegionFanFullSpeed", out var fanFullSpeedTile) && fanFullSpeedTile.TileButton != null)
                {
                    bool enabled = false;
                    if (legionGoDetected?.Value == true)
                    {
                        enabled = legionFanFullSpeed?.Value ?? false;
                    }
                    else if (gpdDetected?.Value == true)
                    {
                        enabled = gpdFanMaxActive;
                    }
                    fanFullSpeedTile.StateText.Text = enabled ? "On" : "Off";
                    fanFullSpeedTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    fanFullSpeedTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Screen Saver tile
                if (qsTileMap.TryGetValue("ScreenSaver", out var screenSaverTile) && screenSaverTile.TileButton != null)
                {
                    bool enabled = screenSaverEnabled;
                    if (enabled)
                    {
                        // Don't overwrite countdown text — let the timer handle it
                        screenSaverTile.StateText.Foreground = accentForeground;
                    }
                    else
                    {
                        screenSaverTile.StateText.Text = "Off";
                        screenSaverTile.StateText.Foreground = offForeground;
                    }
                    screenSaverTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Battery tile - device battery in title, controllers in state text
                if (qsTileMap.TryGetValue("Battery", out var batteryTile) && batteryTile.TileButton != null)
                {
                    // Get device battery info (hide bolt at 100%)
                    int deviceBat = PowerManager.RemainingChargePercent;
                    bool deviceCharging = PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate;
                    string deviceIndicator = (deviceCharging && deviceBat < 100) ? "⚡" : "";

                    // Get the tile content elements
                    var content = batteryTile.TileButton.Content as StackPanel;
                    var iconElement = content?.Children.Count >= 1 ? content.Children[0] as FontIcon : null;
                    var labelText = content?.Children.Count >= 2 ? content.Children[1] as TextBlock : null;

                    // Update battery icon based on level and charging state
                    // Battery icons: \uE850-\uE859 (0-9), \uE83F (full)
                    // Charging icons: \uE85A-\uE863 (0-9), \uEBB5 (charging full)
                    if (iconElement != null)
                    {
                        string glyph;
                        if (deviceCharging)
                        {
                            // Charging icons
                            if (deviceBat >= 90) glyph = "\uEBB5";      // Full charging
                            else if (deviceBat >= 70) glyph = "\uE862"; // Charging 8
                            else if (deviceBat >= 50) glyph = "\uE85F"; // Charging 5
                            else if (deviceBat >= 30) glyph = "\uE85C"; // Charging 2
                            else glyph = "\uE85A";                       // Charging 0
                        }
                        else
                        {
                            // Normal battery icons
                            if (deviceBat >= 90) glyph = "\uE83F";      // Full
                            else if (deviceBat >= 70) glyph = "\uE858"; // Battery 8
                            else if (deviceBat >= 50) glyph = "\uE855"; // Battery 5
                            else if (deviceBat >= 30) glyph = "\uE852"; // Battery 2
                            else glyph = "\uE850";                       // Battery 0 (low)
                        }
                        iconElement.Glyph = glyph;
                    }

                    string stateText;
                    SolidColorBrush bgBrush;
                    int minBat = deviceBat; // Start with device battery

                    // Update title with device battery
                    if (labelText != null)
                    {
                        labelText.Text = $"{deviceBat}%{deviceIndicator}";
                    }

                    if (legionGoDetected?.Value == true)
                    {
                        int leftBat = controllerBatteryLeft?.Value ?? -1;
                        int rightBat = controllerBatteryRight?.Value ?? -1;
                        bool leftCharging = controllerChargingLeft?.Value ?? false;
                        bool rightCharging = controllerChargingRight?.Value ?? false;

                        if (leftBat > 0 && rightBat > 0)
                        {
                            // Controllers connected - show L/R with % (hide bolt at 100%)
                            string leftIndicator = (leftCharging && leftBat < 100) ? "⚡" : "";
                            string rightIndicator = (rightCharging && rightBat < 100) ? "⚡" : "";
                            stateText = $"L:{leftBat}%{leftIndicator} R:{rightBat}%{rightIndicator}";

                            // Color based on lowest of all batteries
                            minBat = Math.Min(deviceBat, Math.Min(leftBat, rightBat));
                        }
                        else
                        {
                            // Controllers not connected
                            stateText = "No Ctrl";
                        }
                    }
                    else
                    {
                        // Not Legion Go - just show "Device" in state
                        stateText = "Device";
                    }

                    // Color based on minimum battery level
                    if (minBat < 20)
                        bgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 35, 35)); // Red
                    else if (minBat < 50)
                        bgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 55, 35)); // Yellow
                    else
                        bgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 55, 40)); // Green

                    batteryTile.StateText.Text = stateText;
                    batteryTile.StateText.Foreground = accentForeground;
                    batteryTile.TileButton.Background = bgBrush;
                }

                Logger.Debug("Quick Settings tile states updated");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error updating Quick Settings tile states: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle Quick Settings tile clicks
        /// </summary>
        private void QuickSettingsTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tileTag)
            {
                try
                {
                    // First check if it's in our local qsTileMap (includes custom shortcuts with GUID IDs)
                    if (qsTileMap.TryGetValue(tileTag, out var mappedTile) && !string.IsNullOrEmpty(mappedTile.CustomShortcut))
                    {
                        _ = SendCustomShortcutAsync(mappedTile.CustomShortcut, mappedTile.Name);
                    }
                    // Fallback: Check QuickSettingsConfig by ID (tile IDs are now GUIDs)
                    else if (QuickSettings.QuickSettingsConfig.Instance.GetTile(tileTag) is QuickSettings.QuickSettingsTile configTile
                             && configTile.Type == QuickSettings.TileType.CustomShortcut
                             && !string.IsNullOrEmpty(configTile.CustomShortcut))
                    {
                        _ = SendCustomShortcutAsync(configTile.CustomShortcut, configTile.Name);
                    }
                    else
                    {
                        switch (tileTag)
                        {
                            case "TDPMode":
                                CycleTDPMode();
                                break;
                            case "AutoTDP":
                                ToggleAutoTDPTile();
                                break;
                            case "Profile":
                                TogglePerGameProfile();
                                break;
                            case "Overlay":
                                CyclePerformanceOverlay();
                                break;
                            case "PowerMode":
                                CyclePowerMode();
                                break;
                            case "FPSLimit":
                                CycleFPSLimit();
                                break;
                            case "Resolution":
                                CycleResolution();
                                break;
                            case "Rotation":
                                CycleRotation();
                                break;
                            case "HDR":
                                ToggleHDR();
                                break;
                            case "LosslessScaling":
                                ToggleLosslessScaling();
                                break;
                            case "RIS":
                                ToggleRIS();
                                break;
                            case "AFMF":
                                ToggleAFMF();
                                break;
                            case "RSR":
                                ToggleRSR();
                                break;
                            case "AntiLag":
                                ToggleAntiLag();
                                break;
                            case "RadeonChill":
                                ToggleRadeonChill();
                                break;
                            case "CPUBoost":
                                ToggleCPUBoost();
                                break;
                            case "EPP":
                                CycleEPP();
                                break;
                            case "ScreenSaver":
                                ToggleScreenSaver();
                                break;
                            case "Keyboard":
                                TriggerOnScreenKeyboard();
                                break;
                            case "LegionTouchpad":
                                ToggleLegionTouchpad();
                                break;
                            case "LegionLightMode":
                                CycleLegionLightMode();
                                break;
                            case "LegionDesktopControls":
                                ToggleLegionDesktopControls();
                                break;
                            case "LegionRemapControls":
                                ToggleRemapControlsProfile();
                                break;
                            case "LegionChargeLimit":
                                ToggleLegionChargeLimit();
                                break;
                            // Action tiles
                            case "ActionTaskManager":
                                LaunchTaskManager();
                                break;
                            case "ActionExplorer":
                                LaunchExplorer();
                                break;
                            case "ActionEndTask":
                                SendAltF4();
                                break;
                            case "Fullscreen":
                                ToggleFullscreen();
                                break;
                            case "ActionHibernate":
                                ExecuteHibernate();
                                break;
                            case "LegionPowerLight":
                                ToggleLegionPowerLight();
                                break;
                            case "LegionFanFullSpeed":
                                ToggleLegionFanFullSpeed();
                                break;
                        }
                    }

                    // Update tile states after action
                    UpdateQuickSettingsTileStates();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error handling Quick Settings tile click: {ex.Message}");
                }
            }
        }

        private void CycleTDPMode()
        {
            // If default game profile is active, turn it off when user manually changes TDP mode
            if (defaultGameProfileEnabled?.Value == true && DefaultProfileToggle != null)
            {
                Logger.Info("TDP Mode tile clicked - turning off Default Game Profile");
                DefaultProfileToggle.IsOn = false;
                // The toggle change will trigger OnDefaultProfileEnabledChanged which re-enables controls
            }

            bool isLegion = legionGoDetected?.Value == true;
            int currentIndex = TDPModeComboBox?.SelectedIndex ?? 0;

            // Use custom presets if enabled
            if (useCustomTDPPresets && tdpPresets != null && tdpPresets.Count > 0)
            {
                // Total items = presets + Custom mode
                int totalItems = tdpPresets.Count + 1;
                int nextIndex = (currentIndex + 1) % totalItems;

                // Update combobox
                if (TDPModeComboBox != null)
                {
                    isUserInitiatedTDPModeChange = true;
                    TDPModeComboBox.SelectedIndex = nextIndex;
                    isUserInitiatedTDPModeChange = false;
                }

                // Determine the Legion mode and TDP to apply
                int nextLegionMode;
                int? nextTdp = null;
                string presetName;

                if (nextIndex < tdpPresets.Count)
                {
                    var preset = tdpPresets[nextIndex];
                    nextLegionMode = preset.LegionModeValue ?? 255;
                    nextTdp = preset.TdpWatts;
                    presetName = preset.Name;
                }
                else
                {
                    // Custom mode (last item)
                    nextLegionMode = 255;
                    presetName = "Custom";
                }

                // For Legion devices, set the hardware mode
                if (isLegion && legionPerformanceMode != null)
                {
                    legionPerformanceMode.SetValue(nextLegionMode);
                }

                // Apply TDP for software-controlled presets (no LegionModeValue or Custom mode)
                if (nextLegionMode == 255 && nextTdp.HasValue)
                {
                    // Apply the preset's TDP via the TDP slider/property
                    if (TDPSlider != null)
                    {
                        TDPSlider.Value = nextTdp.Value;
                    }
                    ScheduleQsTdpReapply();
                }
                else if (nextLegionMode == 255)
                {
                    // Pure Custom mode - schedule reapply for current slider value
                    ScheduleQsTdpReapply();
                }

                Logger.Info($"TDP Mode cycled to preset '{presetName}' (index={nextIndex}, legionMode={nextLegionMode}, tdp={nextTdp})");
            }
            else
            {
                // Default hardcoded mode cycling: Quiet(1) -> Balanced(2) -> Performance(3) -> Custom(255)
                int[] modeValues = { 1, 2, 3, 255 };
                int currentMode;
                if (isLegion && legionPerformanceMode != null)
                {
                    currentMode = legionPerformanceMode.Value;
                }
                else
                {
                    currentMode = (currentIndex >= 0 && currentIndex < modeValues.Length) ? modeValues[currentIndex] : 2;
                }

                // Calculate next mode
                int nextMode;
                switch (currentMode)
                {
                    case 1: nextMode = 2; break;     // Quiet -> Balanced
                    case 2: nextMode = 3; break;     // Balanced -> Performance
                    case 3: nextMode = 255; break;   // Performance -> Custom
                    case 255: nextMode = 1; break;   // Custom -> Quiet
                    default: nextMode = 2; break;
                }

                // For Legion devices, update the Legion property
                if (isLegion && legionPerformanceMode != null)
                {
                    legionPerformanceMode.SetValue(nextMode);
                }

                // Update TDPModeComboBox
                int nextIndex = Array.IndexOf(modeValues, nextMode);
                if (nextIndex >= 0 && TDPModeComboBox != null)
                {
                    isUserInitiatedTDPModeChange = true;
                    TDPModeComboBox.SelectedIndex = nextIndex;
                    isUserInitiatedTDPModeChange = false;
                }

                // If switching to Custom mode on Legion, schedule TDP reapply
                if (isLegion && nextMode == 255)
                {
                    ScheduleQsTdpReapply();
                }

                Logger.Info($"TDP Mode cycled from {currentMode} to {nextMode} (isLegion={isLegion})");
            }
        }

        private void ToggleAutoTDPTile()
        {
            if (AutoTDPToggle != null)
            {
                AutoTDPToggle.IsOn = !AutoTDPToggle.IsOn;
                Logger.Info($"AutoTDP tile toggled to: {AutoTDPToggle.IsOn}");
            }
        }

        private void ScheduleQsTdpReapply()
        {
            try
            {
                // Cancel existing timer
                if (qsTdpReapplyTimer != null)
                {
                    qsTdpReapplyTimer.Stop();
                }

                // Create new timer
                qsTdpReapplyTimer = new Windows.UI.Xaml.DispatcherTimer();
                qsTdpReapplyTimer.Interval = TimeSpan.FromSeconds(5);
                qsTdpReapplyTimer.Tick += async (s, e) =>
                {
                    qsTdpReapplyTimer.Stop();
                    // Reapply TDP - still in Custom mode?
                    bool isCustomMode = TDPModeComboBox?.SelectedIndex == 3;
                    if (isCustomMode)
                    {
                        // Read TDP value NOW (at timer fire time), not when scheduled
                        // This ensures we use the current profile's TDP if profile switched
                        int currentTdpValue = (int)(TDPSlider?.Value ?? 15);

                        // Reapply using current Performance tab TDP value
                        if (tdp != null)
                        {
                            // Force reapply by sending different value to helper first, then the real value
                            // This ensures the helper doesn't skip due to "equals current value"
                            tdp.SetValue(currentTdpValue - 1);
                            await System.Threading.Tasks.Task.Delay(100);
                            tdp.SetValue(currentTdpValue);
                            Logger.Info($"Quick Settings: Reapplied TDP {currentTdpValue}W after Custom mode switch");
                        }
                    }
                };
                qsTdpReapplyTimer.Start();
                Logger.Info($"Quick Settings: Scheduled TDP reapply in 5 seconds");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error scheduling TDP reapply: {ex.Message}");
            }
        }

        private void TogglePerGameProfile()
        {
            // If Default Game Profile is active, toggle it off instead
            if (defaultGameProfileEnabled?.Value == true && DefaultProfileToggle != null)
            {
                Logger.Info("Profile tile clicked - turning off Default Game Profile");
                DefaultProfileToggle.IsOn = false;
                return;
            }

            // Only allow toggling when a game is detected
            if (perGameProfile != null && runningGame != null && runningGame.Value.IsValid())
            {
                bool newValue = !perGameProfile.Value;
                isUserInitiatedProfileToggle = true; // Flag this as user-initiated
                perGameProfile.SetValue(newValue);
                isUserInitiatedProfileToggle = false;
                Logger.Info($"Per-game profile toggled to {newValue}");
            }
            else
            {
                Logger.Info("Per-game profile toggle ignored - no game detected");
            }
        }

        private async void TriggerOnScreenKeyboard()
        {
            await ToggleTouchKeyboard();
        }

        /// <summary>
        /// Toggle the Windows touch keyboard using COM interop
        /// </summary>
        private async Task ToggleTouchKeyboard()
        {
            try
            {
                // Use helper to toggle touch keyboard via COM interop
                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet { { "ToggleTouchKeyboard", true } };
                    await App.SendMessageAsync(message);
                    Logger.Info("Touch keyboard toggle requested via helper");
                }
                else
                {
                    // Fallback to Win+Ctrl+O (accessibility keyboard shortcut)
                    QuickSettings.KeyboardShortcutHelper.SendShortcut("Win+Ctrl+O");
                    Logger.Info("On-screen keyboard triggered via Win+Ctrl+O (fallback)");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error toggling touch keyboard: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggle RTSS OSD between off and last used level
        /// </summary>
        private void ToggleRTSSOsd()
        {
            try
            {
                if (osd == null)
                {
                    Logger.Warn("ToggleRTSSOsd: osd property is null");
                    return;
                }

                int currentLevel = (int)osd.Value;

                if (currentLevel > 0)
                {
                    // Currently on - save level and turn off
                    lastNonZeroOsdLevel = currentLevel;
                    osd.SetValue(0);
                    Logger.Info($"RTSS OSD toggled OFF (was level {currentLevel})");
                }
                else
                {
                    // Currently off - restore to last level
                    osd.SetValue(lastNonZeroOsdLevel);
                    Logger.Info($"RTSS OSD toggled ON to level {lastNonZeroOsdLevel}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error toggling RTSS OSD: {ex.Message}");
            }
        }

        /// <summary>
        /// Launch Task Manager via helper
        /// </summary>
        private void LaunchTaskManager()
        {
            try
            {
                _ = SendKeyboardShortcutViaHelper("Ctrl+Shift+Escape");
                Logger.Info("Task Manager launched via Ctrl+Shift+Escape");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching Task Manager: {ex.Message}");
            }
        }

        /// <summary>
        /// Launch File Explorer via helper
        /// </summary>
        private void LaunchExplorer()
        {
            try
            {
                _ = SendKeyboardShortcutViaHelper("Win+E");
                Logger.Info("Explorer launched via Win+E");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching Explorer: {ex.Message}");
            }
        }

        /// <summary>
        /// Close the foreground game window
        /// Uses Alt+Tab to switch to game, then Alt+F4 to close it
        /// </summary>
        private async void SendAltF4()
        {
            try
            {
                // Alt+Tab to switch focus to the game (away from Game Bar)
                _ = SendKeyboardShortcutViaHelper("Alt+Tab");
                Logger.Info("Alt+Tab sent to focus game");

                // Wait for focus switch
                await Task.Delay(200);

                // Now send Alt+F4 to close the focused game
                _ = SendKeyboardShortcutViaHelper("Alt+F4");
                Logger.Info("Alt+F4 sent to close game");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error closing game: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggle fullscreen via F11
        /// Uses Alt+Tab first to focus the game
        /// </summary>
        private async void ToggleFullscreen()
        {
            try
            {
                // Alt+Tab to switch focus to the game (away from Game Bar)
                _ = SendKeyboardShortcutViaHelper("Alt+Tab");
                Logger.Info("Alt+Tab sent to focus game");

                // Wait for focus switch
                await Task.Delay(200);

                // F11 is the most universal fullscreen toggle
                _ = SendKeyboardShortcutViaHelper("F11");
                Logger.Info("Fullscreen toggled via F11");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error toggling fullscreen: {ex.Message}");
            }
        }

        // Resolutions to exclude from quick cycling (odd resolutions that don't scale well)
        private static readonly HashSet<string> excludedQuickResolutions = new HashSet<string>
        {
            "1680x1050"  // Odd 16:10 resolution that doesn't scale cleanly
        };

        private void CycleResolution()
        {
            if (resolution != null && resolutions?.Value != null && resolutions.Value.Count > 0)
            {
                // Filter out excluded resolutions for quick cycling
                var quickResolutions = resolutions.Value
                    .Where(r => !excludedQuickResolutions.Contains(r))
                    .ToList();

                if (quickResolutions.Count == 0)
                {
                    quickResolutions = resolutions.Value; // Fallback to all if filter removes everything
                }

                string currentRes = resolution.Value;
                int currentIndex = quickResolutions.IndexOf(currentRes);

                // If current resolution is not in quick list, start from first
                if (currentIndex < 0) currentIndex = -1;

                int nextIndex = (currentIndex + 1) % quickResolutions.Count;
                string nextRes = quickResolutions[nextIndex];
                resolution.SetValue(nextRes);
                Logger.Info($"Resolution cycled from {currentRes} to {nextRes}");
            }
        }

        /// <summary>
        /// Cycles display orientation between Landscape (0) and Portrait (1).
        /// </summary>
        private void CycleRotation()
        {
            if (displayOrientation != null)
            {
                int currentOrientation = displayOrientation.Value;
                // Cycle between Landscape (0) and Portrait (1)
                // Skip flipped modes (2, 3) for simple toggle behavior
                int nextOrientation = (currentOrientation == 0) ? 1 : 0;
                displayOrientation.SetValue(nextOrientation);
                Logger.Info($"Display orientation cycled from {currentOrientation} to {nextOrientation}");
            }
        }

        private void ToggleHDR()
        {
            if (hdrEnabled != null && (hdrSupported?.Value ?? false))
            {
                bool newValue = !hdrEnabled.Value;
                hdrEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (HDRToggle != null)
                    HDRToggle.IsOn = newValue;
                Logger.Info($"HDR toggled to {newValue}");
            }
        }

        private void ToggleLosslessScaling()
        {
            if (losslessScalingEnabled != null)
            {
                bool newValue = !losslessScalingEnabled.Value;
                losslessScalingEnabled.SetValue(newValue);
                Logger.Info($"Lossless Scaling toggled to {newValue}");
            }
        }

        private void ToggleAFMF()
        {
            if (amdFluidMotionFrameEnabled != null && (amdFluidMotionFrameSupported?.Value ?? false))
            {
                bool newValue = !amdFluidMotionFrameEnabled.Value;
                amdFluidMotionFrameEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDFluidMotionFrameToggle != null)
                    AMDFluidMotionFrameToggle.IsOn = newValue;
                Logger.Info($"AFMF toggled to {newValue}");
            }
        }

        private void ToggleRSR()
        {
            if (amdRadeonSuperResolutionEnabled != null && (amdRadeonSuperResolutionSupported?.Value ?? false))
            {
                bool newValue = !amdRadeonSuperResolutionEnabled.Value;
                amdRadeonSuperResolutionEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDRadeonSuperResolutionToggle != null)
                    AMDRadeonSuperResolutionToggle.IsOn = newValue;
                Logger.Info($"RSR toggled to {newValue}");
            }
        }

        private void ToggleRIS()
        {
            if (amdImageSharpeningEnabled != null && (amdImageSharpeningSupported?.Value ?? false))
            {
                bool newValue = !amdImageSharpeningEnabled.Value;
                amdImageSharpeningEnabled.SetValue(newValue);
                AMDImageSharpeningToggle.IsOn = newValue;
                Logger.Info($"RIS toggled to {newValue}");
            }
        }

        private void ToggleAntiLag()
        {
            if (amdRadeonAntiLagEnabled != null && (amdRadeonAntiLagSupported?.Value ?? false))
            {
                bool newValue = !amdRadeonAntiLagEnabled.Value;
                amdRadeonAntiLagEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDRadeonAntiLagToggle != null)
                    AMDRadeonAntiLagToggle.IsOn = newValue;
                Logger.Info($"Anti-Lag toggled to {newValue}");
            }
        }

        private void ToggleRadeonChill()
        {
            if (amdRadeonChillEnabled != null && (amdRadeonChillSupported?.Value ?? false))
            {
                bool newValue = !amdRadeonChillEnabled.Value;
                amdRadeonChillEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDRadeonChillToggle != null)
                    AMDRadeonChillToggle.IsOn = newValue;
                Logger.Info($"Radeon Chill toggled to {newValue}");
            }
        }

        private void ToggleCPUBoost()
        {
            if (cpuBoost != null)
            {
                bool newValue = !cpuBoost.Value;
                cpuBoost.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (CPUBoostToggle != null)
                    CPUBoostToggle.IsOn = newValue;
                Logger.Info($"CPU Boost toggled to {newValue}");
            }
        }

        private void CyclePowerMode()
        {
            if (osPowerMode != null)
            {
                // Cycle: Efficiency (0) -> Balanced (1) -> Performance (2) -> Efficiency (0)
                int currentMode = osPowerMode.Value;
                int nextMode = (currentMode + 1) % 3;
                osPowerMode.SetValue(nextMode);

                // Update the combobox and value text in Performance tab
                isLoadingOSPowerMode = true;
                try
                {
                    OSPowerModeComboBox.SelectedIndex = nextMode;
                    OSPowerModeValue.Text = OSPowerModeNames[nextMode];
                }
                finally
                {
                    isLoadingOSPowerMode = false;
                }

                Logger.Info($"Power Mode cycled to {OSPowerModeNames[nextMode]}");

                // Save the change to profile
                if (!isInitialSync && !isApplyingHelperUpdate && !isLoadingProfile && SaveOSPowerMode)
                {
                    Logger.Info($"Saving OS Power Mode change to profile: {currentProfileName}");
                    SaveCurrentSettingsToProfile(currentProfileName);
                }
            }
        }

        private void CycleEPP()
        {
            if (cpuEPP != null)
            {
                int currentValue = (int)cpuEPP.Value;
                int nextValue;
                switch (currentValue)
                {
                    case 0: nextValue = 30; break;
                    case 30: nextValue = 80; break;
                    case 80: nextValue = 100; break;
                    case 100: nextValue = 0; break;
                    default: nextValue = 0; break;
                }
                cpuEPP.SetValue(nextValue);

                // Update slider to match (SaveCurrentSettingsToProfile reads from it)
                if (CPUEPPSlider != null)
                {
                    CPUEPPSlider.Value = nextValue;
                }

                Logger.Info($"EPP cycled from {currentValue} to {nextValue}");

                // Save the change to profile
                // Use direct save to bypass isApplyingHelperUpdate check - this is a user-initiated action
                if (!isInitialSync && !isLoadingProfile && SaveCPUEPP && !string.IsNullOrEmpty(currentProfileName))
                {
                    try
                    {
                        var profile = GetProfile(currentProfileName);
                        profile.CPUEPP = nextValue;
                        SaveProfileToStorage(currentProfileName, profile);
                        Logger.Info($"Saved EPP {nextValue} to profile: {currentProfileName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to save EPP to profile: {ex.Message}");
                    }
                }
            }
        }

        private void CyclePerformanceOverlay()
        {
            if (osdProvider == 1) // AMD
            {
                // AMD has 4 overlay levels that cycle with Ctrl+Shift+X
                // Ctrl+Shift+O toggles the overlay on/off completely
                // Cycle: Off -> Level 1 -> Level 2 -> Level 3 -> Level 4 -> Off
                if (amdOverlayLevel == 0)
                {
                    // Currently off, turn on (starts at level 1)
                    SendAMDOverlayToggle();
                    amdOverlayLevel = 1;
                    SaveAMDOverlayLevel();
                    Logger.Info("AMD Overlay toggled ON (Level 1)");
                }
                else if (amdOverlayLevel < 4)
                {
                    // Cycle to next level
                    CycleAMDOverlayLevel();
                    amdOverlayLevel++;
                    SaveAMDOverlayLevel();
                    Logger.Info($"AMD Overlay cycled to Level {amdOverlayLevel}");
                }
                else
                {
                    // At level 4, turn off
                    SendAMDOverlayToggle();
                    amdOverlayLevel = 0;
                    SaveAMDOverlayLevel();
                    Logger.Info("AMD Overlay toggled OFF");
                }
                UpdateQuickSettingsTileStates();
            }
            else // RTSS
            {
                if (osd != null)
                {
                    int currentLevel = (int)osd.Value;
                    int nextLevel = (currentLevel + 1) % 4;
                    osd.SetValue(nextLevel);
                    Logger.Info($"RTSS Performance Overlay cycled from {currentLevel} to {nextLevel}");
                }
            }
        }

        /// <summary>
        /// Cycle FPS limit through: Off -> MaxRefresh -> MaxRefresh/2 -> MaxRefresh/3 -> Off
        /// </summary>
        private void CycleFPSLimit()
        {
            if (fpsLimit == null) return;

            // Get max refresh rate from current display
            int maxRefresh = 60; // Default
            if (refreshRates?.Value != null && refreshRates.Value.Count > 0)
            {
                maxRefresh = refreshRates.Value.Max();
            }

            // Calculate FPS limit values: Max, Max/2, Max/3
            int[] fpsValues = new int[]
            {
                0,                          // Off (unlimited)
                maxRefresh,                 // e.g., 144
                maxRefresh / 2,             // e.g., 72
                maxRefresh / 3              // e.g., 48
            };

            // Find current index and cycle to next
            int currentLimit = fpsLimit.Value;
            int currentIndex = 0;
            for (int i = 0; i < fpsValues.Length; i++)
            {
                if (fpsValues[i] == currentLimit)
                {
                    currentIndex = i;
                    break;
                }
            }

            int nextIndex = (currentIndex + 1) % fpsValues.Length;
            int nextLimit = fpsValues[nextIndex];

            fpsLimit.SetValue(nextLimit);
            Logger.Info($"FPS Limit cycled from {currentLimit} to {nextLimit} (max refresh: {maxRefresh})");

            // Sync the Performance tab FPS Limit controls
            isApplyingHelperUpdate = true;
            try
            {
                // Update slider maximum to current refresh rate
                FPSLimitSlider.Maximum = maxRefresh;

                if (nextLimit > 0)
                {
                    FPSLimitToggle.IsOn = true;
                    FPSLimitSlider.Value = nextLimit;
                }
                else
                {
                    FPSLimitToggle.IsOn = false;
                }
            }
            finally
            {
                isApplyingHelperUpdate = false;
            }

            // Save to profile if FPS Limit saving is enabled
            if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        /// <summary>
        /// FPS Limit toggle changed - set FPS limit to slider value or 0 (off)
        /// </summary>
        private void FPSLimitToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Update display text when toggle is enabled
            if (FPSLimitToggle.IsOn && FPSLimitValue != null)
            {
                FPSLimitValue.Text = $"{(int)FPSLimitSlider.Value} FPS";
            }

            if (fpsLimit == null || isApplyingHelperUpdate) return;

            if (FPSLimitToggle.IsOn)
            {
                // Get max refresh rate and update slider
                int maxRefresh = 60;
                if (refreshRates?.Value != null && refreshRates.Value.Count > 0)
                {
                    maxRefresh = refreshRates.Value.Max();
                }
                FPSLimitSlider.Maximum = maxRefresh;

                // If slider is at minimum (15) or below, set to max refresh as default
                int limit = (int)FPSLimitSlider.Value;
                if (limit <= 15)
                {
                    limit = maxRefresh;
                    FPSLimitSlider.Value = limit;
                }

                // Update display text with the final value
                if (FPSLimitValue != null)
                {
                    FPSLimitValue.Text = $"{limit} FPS";
                }

                fpsLimit.SetValue(limit);
                Logger.Info($"FPS Limit enabled: {limit}");
            }
            else
            {
                // Disable FPS limit (0 = unlimited)
                fpsLimit.SetValue(0);
                Logger.Info("FPS Limit disabled");
            }

            // Save to profile if FPS Limit saving is enabled
            // Don't save during DGP restoration - values being restored to original state
            if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile && !isRestoringFromDefaultProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        /// <summary>
        /// RSR toggle changed - disable RIS if RSR is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonSuperResolutionToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // RSR and RIS are mutually exclusive - enabling one disables the other
            if (AMDRadeonSuperResolutionToggle.IsOn && AMDImageSharpeningToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("RSR enabled - disabling RIS (mutually exclusive)");
                AMDImageSharpeningToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// RIS toggle changed - disable RSR if RIS is enabled (mutually exclusive)
        /// </summary>
        private void AMDImageSharpeningToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // RSR and RIS are mutually exclusive - enabling one disables the other
            if (AMDImageSharpeningToggle.IsOn && AMDRadeonSuperResolutionToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("RIS enabled - disabling RSR (mutually exclusive)");
                AMDRadeonSuperResolutionToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// Radeon Anti-Lag toggle changed - disable Chill if Anti-Lag is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonAntiLagToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Anti-Lag and Chill are mutually exclusive
            if (AMDRadeonAntiLagToggle.IsOn && AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("Anti-Lag enabled - disabling Chill (mutually exclusive)");
                AMDRadeonChillToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// Radeon Boost toggle changed - disable Chill if Boost is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonBoostToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Boost and Chill are mutually exclusive
            if (AMDRadeonBoostToggle.IsOn && AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("Boost enabled - disabling Chill (mutually exclusive)");
                AMDRadeonChillToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// Radeon Chill toggle changed - disable Anti-Lag and Boost if Chill is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonChillToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Chill is mutually exclusive with Anti-Lag and Boost
            if (AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                if (AMDRadeonAntiLagToggle.IsOn)
                {
                    Logger.Info("Chill enabled - disabling Anti-Lag (mutually exclusive)");
                    AMDRadeonAntiLagToggle.IsOn = false;
                }
                if (AMDRadeonBoostToggle.IsOn)
                {
                    Logger.Info("Chill enabled - disabling Boost (mutually exclusive)");
                    AMDRadeonBoostToggle.IsOn = false;
                }
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// FPS Limit slider changed - update FPS limit if toggle is on (with debouncing)
        /// </summary>
        private void FPSLimitSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // Always update the display text
            if (FPSLimitValue != null)
            {
                FPSLimitValue.Text = $"{(int)e.NewValue} FPS";
            }

            if (fpsLimit == null || isApplyingHelperUpdate) return;

            if (FPSLimitToggle.IsOn)
            {
                int limit = (int)e.NewValue;
                fpsLimitPendingValue = limit;

                // Initialize debounce timer if needed
                if (fpsLimitDebounceTimer == null)
                {
                    fpsLimitDebounceTimer = new DispatcherTimer();
                    fpsLimitDebounceTimer.Interval = TimeSpan.FromMilliseconds(FPS_LIMIT_DEBOUNCE_MS);
                    fpsLimitDebounceTimer.Tick += FPSLimitDebounceTimer_Tick;
                }

                // Restart the debounce timer
                fpsLimitDebounceTimer.Stop();
                fpsLimitDebounceTimer.Start();
            }
        }

        /// <summary>
        /// Debounce timer tick - apply the pending FPS limit value
        /// </summary>
        private void FPSLimitDebounceTimer_Tick(object sender, object e)
        {
            fpsLimitDebounceTimer?.Stop();

            if (fpsLimit != null && FPSLimitToggle.IsOn)
            {
                fpsLimit.SetValue(fpsLimitPendingValue);
                Logger.Info($"FPS Limit changed (debounced): {fpsLimitPendingValue}");

                // Save to profile if FPS Limit saving is enabled
                if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile)
                {
                    SaveCurrentSettingsToProfile(currentProfileName);
                }
            }
        }

        /// <summary>
        /// Update FPS Limit controls based on RTSS installed status and current fpsLimit value
        /// </summary>
        private void UpdateFPSLimitControls()
        {
            UpdateFPSLimitControls(rtssInstalled?.Value == true);
        }

        /// <summary>
        /// Update FPS Limit controls based on RTSS installed status
        /// </summary>
        private void UpdateFPSLimitControls(bool rtssAvailable)
        {
            // Dispatch to UI thread since this may be called from property callback on non-UI thread
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    if (isUnloading) return;

                    // Guard against null controls during initialization or shutdown
                    if (FPSLimitToggle == null || FPSLimitSlider == null) return;

                    FPSLimitToggle.IsEnabled = rtssAvailable;

                    // Update slider maximum to current refresh rate
                    int maxRefresh = 60; // Default
                    if (refreshRates?.Value != null && refreshRates.Value.Count > 0)
                    {
                        maxRefresh = refreshRates.Value.Max();
                    }
                    FPSLimitSlider.Maximum = maxRefresh;

                    // Set tick frequency based on max refresh rate (show ~5-8 ticks)
                    int tickFreq;
                    if (maxRefresh >= 144)
                        tickFreq = 24;
                    else if (maxRefresh >= 120)
                        tickFreq = 20;
                    else if (maxRefresh >= 90)
                        tickFreq = 15;
                    else
                        tickFreq = 10;
                    FPSLimitSlider.TickFrequency = tickFreq;

                    // Sync toggle/slider with fpsLimit value
                    if (fpsLimit != null)
                    {
                        isApplyingHelperUpdate = true;
                        try
                        {
                            int limit = fpsLimit.Value;
                            if (limit > 0)
                            {
                                FPSLimitToggle.IsOn = true;
                                // Clamp value to slider range
                                FPSLimitSlider.Value = Math.Min(limit, maxRefresh);
                            }
                            else
                            {
                                FPSLimitToggle.IsOn = false;
                            }
                        }
                        finally
                        {
                            isApplyingHelperUpdate = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in UpdateFPSLimitControls: {ex.Message}");
                }
            });
        }
        private void ToggleLegionTouchpad()
        {
            if (legionGoDetected?.Value == true && legionTouchpadEnabled != null)
            {
                bool newValue = !legionTouchpadEnabled.Value;
                legionTouchpadEnabled.SetValue(newValue);
                Logger.Info($"Legion Touchpad toggled to {newValue}");
            }
        }

        private void CycleLegionLightMode()
        {
            if (legionGoDetected?.Value == true && legionLightMode != null)
            {
                int currentMode = legionLightMode.Value;
                int nextMode = (currentMode + 1) % 5; // 0-4: Off, Static, Breathing, Rainbow, Spiral
                legionLightMode.SetValue(nextMode);
                Logger.Info($"Legion Light Mode cycled from {currentMode} to {nextMode}");
            }
        }

        private void ToggleLegionDesktopControls()
        {
            if (legionGoDetected?.Value == true && LegionDesktopControlsToggle != null)
            {
                bool newValue = !LegionDesktopControlsToggle.IsOn;
                LegionDesktopControlsToggle.IsOn = newValue;
                // The Toggled event handler will apply the mappings
                Logger.Info($"Legion Desktop Controls toggled to {newValue}");
            }
        }

        private void ToggleLegionChargeLimit()
        {
            if (legionGoDetected?.Value == true && legionChargeLimit != null)
            {
                bool newValue = !legionChargeLimit.Value;
                legionChargeLimit.SetValue(newValue);
                // Also update the toggle in Legion tab if it exists
                if (LegionChargeLimitToggle != null)
                {
                    LegionChargeLimitToggle.IsOn = newValue;
                }
                Logger.Info($"Legion Charge Limit toggled to {(newValue ? "80%" : "Off")}");
            }
        }

        private void ToggleRemapControlsProfile()
        {
            if (legionGoDetected?.Value != true)
                return;

            if (LegionControllerProfileToggle == null)
                return;

            // Toggle the per-game controller profile
            LegionControllerProfileToggle.IsOn = !LegionControllerProfileToggle.IsOn;
            Logger.Info($"Toggled per-game controller profile to: {LegionControllerProfileToggle.IsOn}");

            // Update Quick Settings tiles
            UpdateQuickSettingsTileStates();
        }

        /// <summary>
        /// Show/hide customization panel
        /// </summary>
        private void QuickSettingsCustomize_Click(object sender, RoutedEventArgs e)
        {
            if (QuickSettingsCustomizePanel != null)
            {
                // Enter edit mode
                qsEditMode = true;
                qsSelectedTileForMove = null;

                QuickSettingsCustomizePanel.Visibility = Visibility.Visible;
                QuickSettingsCustomizeButton.Visibility = Visibility.Collapsed;

                // Register keyboard handler for B/Escape to deselect
                QuickSettingsCustomizePanel.KeyDown -= QuickSettingsCustomizePanel_KeyDown;
                QuickSettingsCustomizePanel.KeyDown += QuickSettingsCustomizePanel_KeyDown;

                // Update column button visuals
                UpdateColumnButtonVisuals();

                // Rebuild UIs with edit mode enabled
                BuildSortableGrid();
                RebuildQuickSettingsTiles();  // Shows hidden tiles with overlay in edit mode
            }
        }

        /// <summary>
        /// Handle keyboard input in customize panel (B/Escape to deselect)
        /// </summary>
        private void QuickSettingsCustomizePanel_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape ||
                e.Key == Windows.System.VirtualKey.GamepadB)
            {
                if (qsSelectedTileForMove != null)
                {
                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                    BuildSortableGrid();
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Close customization panel
        /// </summary>
        private void QuickSettingsCustomizeDone_Click(object sender, RoutedEventArgs e)
        {
            if (QuickSettingsCustomizePanel != null)
            {
                // Exit edit mode
                qsEditMode = false;
                qsSelectedTileForMove = null;
                UpdateSelectedTileIndicator(null);

                QuickSettingsCustomizePanel.Visibility = Visibility.Collapsed;
                QuickSettingsCustomizeButton.Visibility = Visibility.Visible;

                // Save config and rebuild tiles without edit overlays
                SaveQuickSettingsConfig();
                RebuildQuickSettingsTiles();
                UpdateQuickSettingsTileStates();
            }
        }

        /// <summary>
        /// Set column count to 3
        /// </summary>
        private void ColumnCount3_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 3)
            {
                qsColumnCount = 3;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Set column count to 4
        /// </summary>
        private void ColumnCount4_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 4)
            {
                qsColumnCount = 4;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Set column count to 5
        /// </summary>
        private void ColumnCount5_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 5)
            {
                qsColumnCount = 5;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Update column button visuals to show current selection
        /// </summary>
        private void UpdateColumnButtonVisuals()
        {
            if (Column3Button == null || Column4Button == null || Column5Button == null) return;

            var selectedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 180));
            var normalBrush = tileOffBrush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 28, 30));

            Column3Button.Background = qsColumnCount == 3 ? selectedBrush : normalBrush;
            Column4Button.Background = qsColumnCount == 4 ? selectedBrush : normalBrush;
            Column5Button.Background = qsColumnCount == 5 ? selectedBrush : normalBrush;
        }

        /// <summary>
        /// Add a custom shortcut tile
        /// </summary>
        private void AddCustomShortcut_Click(object sender, RoutedEventArgs e)
        {
            string name = CustomShortcutNameBox?.Text?.Trim();
            string shortcut = GetCustomShortcutKeysString();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(shortcut))
            {
                Logger.Warn("Custom shortcut name or shortcut is empty");
                return;
            }

            AddCustomShortcutTile(name, shortcut);

            // Clear inputs
            if (CustomShortcutNameBox != null) CustomShortcutNameBox.Text = "";
            _customShortcutKeys.Clear();
            UpdateCustomShortcutKeyTags();

            UpdateQuickSettingsTileStates();
        }

        /// <summary>
        /// Handle tile visibility checkbox changes
        /// </summary>
        private void TileVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is string tileId)
            {
                bool isVisible = checkBox.IsChecked ?? true;

                if (qsTileMap.TryGetValue(tileId, out var tile))
                {
                    tile.IsVisible = isVisible;
                }
            }
        }

    }
}
