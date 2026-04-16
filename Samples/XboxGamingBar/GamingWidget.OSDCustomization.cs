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
        // OSD configuration per level - stores which items are enabled
        // Level 1 (Basic): FPS, Battery, Time - 3 columns
        // Level 2 (Detailed): Time, FPS, Battery, CPU, GPU, Fan - 1 column
        // Level 3 (Full): All options - 1 column
        private Dictionary<int, Dictionary<string, bool>> osdLevelConfig = new Dictionary<int, Dictionary<string, bool>>
        {
            { 1, new Dictionary<string, bool> { { "AppName", false }, { "Time", true }, { "FPS", true }, { "Battery", true }, { "ControllerBattery", false }, { "Memory", false }, { "VRAM", false }, { "CPU", false }, { "CPUClock", false }, { "GPU", false }, { "GPUClock", false }, { "Fan", false }, { "AutoTDP", false }, { "FrametimeGraph", false } } },
            { 2, new Dictionary<string, bool> { { "AppName", false }, { "Time", true }, { "FPS", true }, { "Battery", true }, { "ControllerBattery", false }, { "Memory", false }, { "VRAM", false }, { "CPU", true }, { "CPUClock", false }, { "GPU", true }, { "GPUClock", false }, { "Fan", true }, { "AutoTDP", false }, { "FrametimeGraph", true } } },
            { 3, new Dictionary<string, bool> { { "AppName", true }, { "Time", true }, { "FPS", true }, { "Battery", true }, { "ControllerBattery", true }, { "Memory", true }, { "VRAM", true }, { "CPU", true }, { "CPUClock", true }, { "GPU", true }, { "GPUClock", true }, { "Fan", true }, { "AutoTDP", true }, { "FrametimeGraph", true } } }
        };

        private Dictionary<int, string> osdCustomTags = new Dictionary<int, string>
        {
            { 1, "" },
            { 2, "" },
            { 3, "" }
        };

        // Per-level column settings (Basic=3, Detailed=1, Full=1)
        private Dictionary<int, int> osdLevelColumns = new Dictionary<int, int>
        {
            { 1, 3 },  // Basic: 3 columns
            { 2, 1 },  // Detailed: 1 column
            { 3, 1 }   // Full: 1 column
        };

        // Current OSD customization level (1=Basic, 2=Detailed, 3=Full)
        private int osdCustomizeLevel = 1;

        // Per-level item order (list of item IDs in display order)
        private Dictionary<int, List<string>> osdLevelOrder = new Dictionary<int, List<string>>
        {
            { 1, new List<string> { "AppName", "Time", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "Fan", "AutoTDP", "TDPLimits", "FrametimeGraph" } },
            { 2, new List<string> { "AppName", "Time", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "Fan", "AutoTDP", "TDPLimits", "FrametimeGraph" } },
            { 3, new List<string> { "AppName", "Time", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "Fan", "AutoTDP", "TDPLimits", "FrametimeGraph" } }
        };

        // Per-level item label colors (DEFAULT = use global text color)
        private Dictionary<int, Dictionary<string, string>> osdItemLabelColors = new Dictionary<int, Dictionary<string, string>>
        {
            { 1, new Dictionary<string, string>() },
            { 2, new Dictionary<string, string>() },
            { 3, new Dictionary<string, string>() }
        };

        // Item display names for UI
        private static readonly Dictionary<string, string> osdItemDisplayNames = new Dictionary<string, string>
        {
            { "AppName", "App Name (D3D11, Vulkan, etc.)" },
            { "Time", "Time (12-hour)" },
            { "FPS", "FPS & Frametime" },
            { "Battery", "Battery" },
            { "ControllerBattery", "Controller Battery (L/R)" },
            { "Memory", "Memory (RAM)" },
            { "VRAM", "VRAM (GPU Memory)" },
            { "CPU", "CPU (Usage, Wattage, Temp)" },
            { "CPUClock", "CPU Clock Speed" },
            { "GPU", "GPU (Usage, Wattage, Temp)" },
            { "GPUClock", "GPU Clock Speed" },
            { "Fan", "Fan Speed" },
            { "AutoTDP", "AutoTDP Status" },
            { "TDPLimits", "TDP Limits (SPL/SPPT/FPPT)" },
            { "FrametimeGraph", "Frametime Graph" }
        };

        // Observable collection for OSD items UI
        private ObservableCollection<OSDItemViewModel> osdItemViewModels = new ObservableCollection<OSDItemViewModel>();

        // Global OSD layout settings
        private int osdTextSize = 100;    // Percentage: 50=Small, 100=Medium, 150=Large, 200=X-Large, 250=XX-Large, 300=XXX-Large
        private string osdTextColor = "DYNAMIC";  // DYNAMIC = value-based colors, or hex color code
        private string osdLabelColor = "DEFAULT";  // DEFAULT = use item-specific colors, or hex color code
        private int osdProvider = 0;  // 0=RTSS, 1=AMD
        private int amdOverlayLevel = 0;  // Track AMD overlay level: 0=Off, 1-4=Level 1-4 (can't query from AMD)
        private bool isOSDCustomizeExpanded = false;
        private bool isProfileDetectionExpanded = false;
        private bool isProfileSettingsExpanded = false;
        private bool isTDPSettingsExpanded = false;
        private bool isColorSettingsExpanded = false;
        private bool isButtonRemappingExpanded = false;
        private bool isGyroSettingsExpanded = false;
        private bool isSavedProfilesExpanded = false;
        private bool isSpecialRemappingExpanded = false;
        private bool isStickDeadzonesExpanded = false;
        private bool isTouchpadVibrationExpanded = false;
        private bool isLightingExpanded = false;
        private bool isFanCurveExpanded = false;
        private bool isControllerEmulationExpanded = false;
        private bool isControllerEmulationInputNotesExpanded = false;
        private bool fanCurveGraphInitialized = false;

        // Display and OSD settings
        private bool adaptiveBrightnessEnabled = false;
        private bool osdPositionShiftEnabled = false;
        private bool frametimeGraphPinned = false;
        private int osdOpacity = 100; // percentage 10-100
        private bool isLoadingOLEDSettings = false;
        private bool isLoadingPerformanceOverlaySetting = false;
        private readonly Windows.UI.Xaml.Shapes.Ellipse[] fanCurvePoints = new Windows.UI.Xaml.Shapes.Ellipse[10];
        private int[] currentFanCurveValues = new int[10];
        private int draggedPointIndex = -1;
        private bool isDraggingPoint = false;

        // Legion Go fan curve temperature thresholds (°C) - FIXED by EC at 10°C increments
        private static readonly int[] FanCurveTemperatures = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        // Minimum fan speeds - set to 0 since EC enforces its own thermal protection floor
        // EC override floor: 0-44°C=0%, 45°C=27%, 50°C=40%, 55°C=55%, 60°C=65%, 70°C=85%, 80+°C=100%
        private static readonly int[] FanCurveMinSpeeds = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        // EC Override Floor points: (temperature, minimum fan %) - what the EC enforces regardless of user curve
        private static readonly (int temp, int floor)[] ECFloorPoints = new[]
        {
            (10, 0), (20, 0), (30, 0), (44, 0),
            (45, 27), (50, 40), (55, 55), (60, 65), (70, 85),
            (80, 100), (90, 100), (100, 100)
        };
        // Fan curve preset definitions (values are fan % for temps 10,20,30,40,50,60,70,80,90,100°C)
        private static readonly Dictionary<string, int[]> FanCurvePresets = new Dictionary<string, int[]>
        {
            { "Silent", new int[] { 0, 0, 0, 27, 30, 40, 55, 65, 80, 100 } },       // Silent (Safe)
            { "Balanced", new int[] { 0, 0, 25, 30, 35, 45, 55, 70, 85, 100 } },    // Balanced
            { "Performance", new int[] { 30, 35, 40, 45, 50, 60, 70, 80, 90, 100 } }, // Performance
            { "MaxCooling", new int[] { 40, 45, 50, 55, 60, 70, 80, 90, 100, 100 } }  // Max Cooling
        };
        private string currentFanCurvePreset = "Custom";
        private bool isFanCurvePresetLoading = false;
        private bool isTDPExtrasExpanded = false;
        private bool isCPUExtrasExpanded = false;
        private bool isDebugExpanded = false;
        private bool isLoadingTDPLimits = false;
        private bool isLoadingPowerPlans = false;
        private List<PowerPlanItem> availablePowerPlans = new List<PowerPlanItem>();
        private Guid acPowerPlanGuid = Guid.Empty;
        private Guid dcPowerPlanGuid = Guid.Empty;
        private bool powerPlanAutoSwitch = false; // Default to OFF - will be loaded from settings
        private int deviceTDPMin = 4;
        private int deviceTDPMax = 35;
        private DispatcherTimer tdpLimitsDebounceTimer;
        private const int TDP_LIMITS_DEBOUNCE_MS = 300;

        // TDP Custom Presets
        private bool useCustomTDPPresets = false;
        private bool useLenovoModes = true; // Default to using Lenovo hardware modes for built-in presets
        private List<Shared.Data.TdpPreset> tdpPresets = new List<Shared.Data.TdpPreset>();
        private Shared.Data.TdpPreset editingPreset = null;
        private int editingPresetIndex = -1;

        private bool isLoadingOSDConfig = false;

        private void OSDCustomizeLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Don't process during initialization - LoadOSDConfigFromStorage will handle it
            if (isLoadingOSDConfig) return;

            if (OSDCustomizeLevelComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int level))
                {
                    LoadOSDOptionsForLevel(level);
                    // Note: This is only for RTSS customization - AMD overlay doesn't have configurable levels
                }
            }
        }

        private void OSDProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            if (OSDProviderComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int provider))
                {
                    int previousProvider = osdProvider;
                    osdProvider = provider;

                    // Save to storage
                    try
                    {
                        ApplicationData.Current.LocalSettings.Values["OSD_Provider"] = osdProvider;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error saving OSD provider: {ex.Message}");
                    }

                    // Update UI visibility
                    UpdateOSDProviderUI();

                    // When switching providers, disable the other one
                    if (provider == 0) // RTSS
                    {
                        // Disable AMD overlay if it was enabled (send Ctrl+Shift+O to toggle off)
                        if (previousProvider == 1 && amdOverlayLevel > 0)
                        {
                            SendAMDOverlayToggle();
                            amdOverlayLevel = 0;
                            SaveAMDOverlayLevel();
                        }
                        // Enable RTSS OSD by sending config
                        SendOSDConfigToHelper();
                    }
                    else if (provider == 1) // AMD
                    {
                        // Disable RTSS OSD by setting level to 0
                        if (osd != null)
                        {
                            osd.SetValue(0);
                        }
                        // Don't auto-toggle AMD overlay - we can't know its actual state
                        // User should manually enable via Quick Settings tile if needed
                    }

                    // Update Quick Settings tiles
                    UpdateQuickSettingsTileStates();

                    Logger.Info($"OSD Provider changed to: {(provider == 0 ? "RTSS" : "AMD")}");
                }
            }
        }

        private void UpdateOSDProviderUI()
        {
            if (RTSSOptionsPanel != null)
            {
                RTSSOptionsPanel.Visibility = osdProvider == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (AMDOptionsPanel != null)
            {
                AMDOptionsPanel.Visibility = osdProvider == 1 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void SendAMDOverlayToggle()
        {
            // Send Ctrl+Shift+O to toggle AMD Adrenaline's metrics overlay on/off
            // Use helper's InputInjector since UWP widget can't use SendInput directly
            try
            {
                if (App.IsConnected)
                {
                    var request = new Windows.Foundation.Collections.ValueSet();
                    request.Add("SendKeyboardShortcut", "Ctrl+Shift+O");
                    await App.SendMessageAsync(request);
                    Logger.Info("Sent AMD overlay toggle hotkey (Ctrl+Shift+O) via helper");
                }
                else
                {
                    Logger.Warn("Cannot send AMD overlay toggle - not connected to helper");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending AMD overlay toggle: {ex.Message}");
            }
        }

        private async void CycleAMDOverlayLevel()
        {
            // Send Ctrl+Shift+X to cycle AMD Adrenaline's metrics overlay levels
            // Use helper's InputInjector since UWP widget can't use SendInput directly
            try
            {
                if (App.IsConnected)
                {
                    var request = new Windows.Foundation.Collections.ValueSet();
                    request.Add("SendKeyboardShortcut", "Ctrl+Shift+X");
                    await App.SendMessageAsync(request);
                    Logger.Info("Sent AMD overlay cycle hotkey (Ctrl+Shift+X) via helper");
                }
                else
                {
                    Logger.Warn("Cannot cycle AMD overlay level - not connected to helper");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error cycling AMD overlay level: {ex.Message}");
            }
        }

        private void LoadOSDOptionsForLevel(int level)
        {
            if (!osdLevelConfig.ContainsKey(level)) return;

            isLoadingOSDConfig = true;
            try
            {
                // Update the current level
                osdCustomizeLevel = level;

                // Refresh the OSD items control with current level's order and states
                RefreshOSDItemsControl();

                if (OSDCustomTagsTextBox != null) OSDCustomTagsTextBox.Text = osdCustomTags.GetValueOrDefault(level, "");

                // Load columns for this level
                int columns = osdLevelColumns.GetValueOrDefault(level, 3);
                if (OSDColumnsComboBox != null)
                {
                    foreach (ComboBoxItem item in OSDColumnsComboBox.Items)
                    {
                        if (item.Tag is string tag && int.TryParse(tag, out int val) && val == columns)
                        {
                            OSDColumnsComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            finally
            {
                isLoadingOSDConfig = false;
            }
        }

        private void OSDOption_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            SaveCurrentOSDConfig();
        }

        /// <summary>
        /// Refreshes the OSD items control with the current level's order and enabled states
        /// </summary>
        private void RefreshOSDItemsControl()
        {
            if (OSDItemsControl == null) return;

            int currentLevel = osdCustomizeLevel;
            if (!osdLevelOrder.ContainsKey(currentLevel)) return;

            var order = osdLevelOrder[currentLevel];
            if (!osdLevelConfig.ContainsKey(currentLevel))
            {
                osdLevelConfig[currentLevel] = new Dictionary<string, bool>();
            }
            var config = osdLevelConfig[currentLevel];

            osdItemViewModels.Clear();
            var labelColors = osdItemLabelColors.ContainsKey(currentLevel) ? osdItemLabelColors[currentLevel] : new Dictionary<string, string>();
            for (int i = 0; i < order.Count; i++)
            {
                var id = order[i];
                osdItemViewModels.Add(new OSDItemViewModel
                {
                    Id = id,
                    DisplayName = osdItemDisplayNames.ContainsKey(id) ? osdItemDisplayNames[id] : id,
                    IsEnabled = config.ContainsKey(id) && config[id],
                    CanMoveUp = i > 0,
                    CanMoveDown = i < order.Count - 1,
                    LabelColor = labelColors.ContainsKey(id) ? labelColors[id] : "DEFAULT"
                });
            }

            OSDItemsControl.ItemsSource = osdItemViewModels;
        }

        private void OSDItemCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            if (sender is CheckBox cb && cb.Tag is string itemId)
            {
                int currentLevel = osdCustomizeLevel;
                if (!osdLevelConfig.ContainsKey(currentLevel))
                {
                    osdLevelConfig[currentLevel] = new Dictionary<string, bool>();
                }
                osdLevelConfig[currentLevel][itemId] = cb.IsChecked == true;

                SaveOSDConfigToStorage();
                SendOSDConfigToHelper();
            }
        }

        private void OSDItemMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string itemId)
            {
                int currentLevel = osdCustomizeLevel;
                var order = osdLevelOrder[currentLevel];
                int index = order.IndexOf(itemId);
                if (index > 0)
                {
                    order.RemoveAt(index);
                    order.Insert(index - 1, itemId);
                    RefreshOSDItemsControl();
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
        }

        private void OSDItemMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string itemId)
            {
                int currentLevel = osdCustomizeLevel;
                var order = osdLevelOrder[currentLevel];
                int index = order.IndexOf(itemId);
                if (index >= 0 && index < order.Count - 1)
                {
                    order.RemoveAt(index);
                    order.Insert(index + 1, itemId);
                    RefreshOSDItemsControl();
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
        }

        private void OSDItemLabelColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            if (sender is ComboBox cb && cb.Tag is string itemId && cb.SelectedItem is ComboBoxItem selected && selected.Tag is string colorTag)
            {
                int currentLevel = osdCustomizeLevel;
                if (!osdItemLabelColors.ContainsKey(currentLevel))
                {
                    osdItemLabelColors[currentLevel] = new Dictionary<string, string>();
                }
                osdItemLabelColors[currentLevel][itemId] = colorTag;

                // Update the view model to refresh the preview
                var vm = osdItemViewModels.FirstOrDefault(v => v.Id == itemId);
                if (vm != null) vm.LabelColor = colorTag;

                SaveOSDConfigToStorage();
                SendOSDConfigToHelper();
            }
        }

        private void OSDCustomTagsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            SaveCurrentOSDConfig();
        }

        private void SaveCurrentOSDConfig()
        {
            int level = osdCustomizeLevel;

            // Item enabled states are already in osdLevelConfig (updated by OSDItemCheckBox_Changed)
            // Just save custom tags and columns here

            osdCustomTags[level] = OSDCustomTagsTextBox?.Text ?? "";

            // Save columns for this level
            if (OSDColumnsComboBox?.SelectedItem is ComboBoxItem colItem && colItem.Tag is string colTag)
            {
                if (int.TryParse(colTag, out int cols))
                {
                    osdLevelColumns[level] = cols;
                }
            }

            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        private void SaveOSDConfigToStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                foreach (var level in osdLevelConfig.Keys)
                {
                    var config = osdLevelConfig[level];
                    foreach (var item in config)
                    {
                        settings.Values[$"OSD_L{level}_{item.Key}"] = item.Value;
                    }
                    settings.Values[$"OSD_L{level}_CustomTags"] = osdCustomTags.GetValueOrDefault(level, "");
                    settings.Values[$"OSD_L{level}_Columns"] = osdLevelColumns.GetValueOrDefault(level, 3);

                    // Save item order
                    if (osdLevelOrder.ContainsKey(level))
                    {
                        settings.Values[$"OSD_L{level}_Order"] = string.Join(",", osdLevelOrder[level]);
                    }

                    // Save item label colors
                    if (osdItemLabelColors.ContainsKey(level))
                    {
                        foreach (var colorItem in osdItemLabelColors[level])
                        {
                            settings.Values[$"OSD_L{level}_{colorItem.Key}_Color"] = colorItem.Value;
                        }
                    }
                }

                // Save global layout settings (text size is per-resolution)
                string currentRes = resolution?.Value ?? "default";
                settings.Values[$"OSD_TextSize_{currentRes}"] = osdTextSize;
                settings.Values["OSD_TextColor"] = osdTextColor;
                settings.Values["OSD_LabelColor"] = osdLabelColor;
                settings.Values["OSD_Opacity"] = osdOpacity;
                settings.Values["OSD_FrametimeGraphPinned"] = frametimeGraphPinned;

                Logger.Info($"OSD configuration saved to storage (resolution: {currentRes}, text size: {osdTextSize}, opacity: {osdOpacity})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving OSD config: {ex.Message}");
            }
        }

        private void LoadOSDConfigFromStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                var itemKeys = new[] { "AppName", "Time", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "Fan", "AutoTDP", "TDPLimits", "FrametimeGraph" };

                foreach (var level in new[] { 1, 2, 3 })
                {
                    if (!osdLevelConfig.ContainsKey(level))
                    {
                        osdLevelConfig[level] = new Dictionary<string, bool>();
                    }

                    foreach (var key in itemKeys)
                    {
                        string settingKey = $"OSD_L{level}_{key}";
                        if (settings.Values.TryGetValue(settingKey, out object val) && val is bool enabled)
                        {
                            osdLevelConfig[level][key] = enabled;
                        }
                    }

                    string customTagsKey = $"OSD_L{level}_CustomTags";
                    if (settings.Values.TryGetValue(customTagsKey, out object tagsVal) && tagsVal is string tags)
                    {
                        osdCustomTags[level] = tags;
                    }

                    // Load per-level columns
                    string columnsKey = $"OSD_L{level}_Columns";
                    if (settings.Values.TryGetValue(columnsKey, out object colsVal) && colsVal is int levelCols)
                    {
                        osdLevelColumns[level] = levelCols;
                    }

                    // Load per-level order
                    string orderKey = $"OSD_L{level}_Order";
                    if (settings.Values.TryGetValue(orderKey, out object orderVal) && orderVal is string orderStr)
                    {
                        var orderList = orderStr.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                        if (orderList.Count == itemKeys.Length)
                        {
                            osdLevelOrder[level] = orderList;
                        }
                    }

                    // Load per-level item label colors
                    if (!osdItemLabelColors.ContainsKey(level))
                    {
                        osdItemLabelColors[level] = new Dictionary<string, string>();
                    }
                    foreach (var key in itemKeys)
                    {
                        string colorKey = $"OSD_L{level}_{key}_Color";
                        if (settings.Values.TryGetValue(colorKey, out object colorVal) && colorVal is string color)
                        {
                            osdItemLabelColors[level][key] = color;
                        }
                    }
                }

                // Load global layout settings (text size is per-resolution)
                string currentRes = resolution?.Value ?? "default";
                string textSizeKey = $"OSD_TextSize_{currentRes}";
                if (settings.Values.TryGetValue(textSizeKey, out object sizeVal) && sizeVal is int size)
                {
                    osdTextSize = size;
                    Logger.Info($"Loaded OSD text size {osdTextSize} for resolution {currentRes}");
                }
                else
                {
                    // Default to 100 if no per-resolution setting exists
                    osdTextSize = 100;
                    Logger.Info($"No OSD text size saved for resolution {currentRes}, using default 100");
                }
                if (settings.Values.TryGetValue("OSD_TextColor", out object textColorVal) && textColorVal is string textColor)
                {
                    osdTextColor = textColor;
                }
                if (settings.Values.TryGetValue("OSD_LabelColor", out object labelColorVal) && labelColorVal is string labelColor)
                {
                    osdLabelColor = labelColor;
                }
                if (settings.Values.TryGetValue("OSD_Opacity", out object opacityVal) && opacityVal is int opacity)
                {
                    osdOpacity = opacity;
                }
                if (settings.Values.TryGetValue("OSD_FrametimeGraphPinned", out object pinnedVal) && pinnedVal is bool pinned)
                {
                    frametimeGraphPinned = pinned;
                    if (FrametimeGraphPinnedToggle != null)
                        FrametimeGraphPinnedToggle.IsOn = frametimeGraphPinned;
                }
                if (settings.Values.TryGetValue("OSD_Provider", out object providerVal) && providerVal is int provider)
                {
                    osdProvider = provider;
                }
                if (settings.Values.TryGetValue("AMD_OverlayLevel", out object amdLevelVal) && amdLevelVal is int amdLevel)
                {
                    amdOverlayLevel = amdLevel;
                }

                // Update layout UI
                UpdateOSDLayoutUI();

                Logger.Info("OSD configuration loaded from storage");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading OSD config: {ex.Message}");
            }
        }

        private async void SendOSDConfigToHelper()
        {
            try
            {
                if (!App.IsConnected) return;

                // Build config string to send to helper
                var configParts = new List<string>();

                // Add global layout settings
                configParts.Add($"TextSize:{osdTextSize}");
                configParts.Add($"TextColor:{osdTextColor}");
                configParts.Add($"LabelColor:{osdLabelColor}");
                configParts.Add($"Opacity:{osdOpacity}");
                configParts.Add($"FrametimeGraphPinned:{(frametimeGraphPinned ? "1" : "0")}");

                // Add per-level item configuration
                foreach (var level in osdLevelConfig.Keys)
                {
                    var config = osdLevelConfig[level];
                    var enabledItems = new List<string>();
                    foreach (var item in config)
                    {
                        if (item.Value)
                        {
                            enabledItems.Add(item.Key);
                        }
                    }
                    configParts.Add($"L{level}:{string.Join(",", enabledItems)}");

                    if (!string.IsNullOrWhiteSpace(osdCustomTags.GetValueOrDefault(level, "")))
                    {
                        configParts.Add($"L{level}_Custom:{osdCustomTags[level]}");
                    }

                    // Add per-level columns
                    configParts.Add($"L{level}_Columns:{osdLevelColumns.GetValueOrDefault(level, 3)}");

                    // Add per-level order
                    if (osdLevelOrder.ContainsKey(level))
                    {
                        configParts.Add($"L{level}_Order:{string.Join(",", osdLevelOrder[level])}");
                    }

                    // Add per-level item label colors
                    if (osdItemLabelColors.ContainsKey(level))
                    {
                        var colors = osdItemLabelColors[level];
                        foreach (var colorItem in colors)
                        {
                            if (!string.IsNullOrEmpty(colorItem.Value) && colorItem.Value != "DEFAULT")
                            {
                                configParts.Add($"L{level}_{colorItem.Key}_Color:{colorItem.Value}");
                            }
                        }
                    }
                }

                var configString = string.Join(";", configParts);
                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.OSDConfig },
                    { "Content", configString },
                    { "UpdatedTime", DateTimeOffset.Now.Ticks }
                };
                await App.SendMessageAsync(request);

                Logger.Info($"OSD config sent to helper: {configString}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending OSD config to helper: {ex.Message}");
            }
        }

        private void OSDCustomizeExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isOSDCustomizeExpanded = !isOSDCustomizeExpanded;

            if (OSDCustomizeContent != null)
            {
                OSDCustomizeContent.Visibility = isOSDCustomizeExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (OSDCustomizeExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                OSDCustomizeExpandIcon.Glyph = isOSDCustomizeExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void OSDOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;
            osdOpacity = (int)Math.Round(e.NewValue);
            if (OSDOpacityValue != null)
                OSDOpacityValue.Text = $"{osdOpacity}%";
            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }
        private void ProfileSettingsExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isProfileSettingsExpanded = !isProfileSettingsExpanded;

            if (ProfileSettingsContent != null)
            {
                ProfileSettingsContent.Visibility = isProfileSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ProfileSettingsExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ProfileSettingsExpandIcon.Glyph = isProfileSettingsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void ProfileDetectionExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isProfileDetectionExpanded = !isProfileDetectionExpanded;

            if (ProfileDetectionContent != null)
            {
                ProfileDetectionContent.Visibility = isProfileDetectionExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ProfileDetectionExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ProfileDetectionExpandIcon.Glyph = isProfileDetectionExpanded ? "\uE70E" : "\uE70D";
            }
        }

        /* DISABLED: Custom games, blacklist, and current apps features - caused user confusion
        private async void CustomGameAddButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
                picker.FileTypeFilter.Add(".exe");
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    profileCustomGamePath?.AddPath(file.Path);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding custom game: {ex.Message}");
            }
        }

        private void CustomGameRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string path)
            {
                profileCustomGamePath?.RemovePath(path);
            }
        }

        private void BlacklistRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string path)
            {
                profileBlacklistPaths?.RemovePath(path);
            }
        }

        private async void ForegroundAppAddCustom_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var path = button?.Tag as string;
            if (!string.IsNullOrEmpty(path))
            {
                profileBlacklistPaths?.RemovePath(path);
                profileCustomGamePath?.AddPath(path);
                await System.Threading.Tasks.Task.Delay(200);
                await foregroundApp?.Sync();
            }
        }

        private async void ForegroundAppAddBlacklist_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var path = button?.Tag as string;
            if (!string.IsNullOrEmpty(path))
            {
                profileCustomGamePath?.RemovePath(path);
                profileBlacklistPaths?.AddPath(path);
                await System.Threading.Tasks.Task.Delay(200);
                await foregroundApp?.Sync();
            }
        }

        private void UpdateForegroundAppsList(List<string> paths)
        {
            // ... method body removed for brevity ...
        }

        private Border CreateForegroundAppRow(string path)
        {
            // ... method body removed for brevity ...
            return null;
        }
        END DISABLED */

        private void ButtonRemappingExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isButtonRemappingExpanded = !isButtonRemappingExpanded;

            if (ButtonRemappingContent != null)
            {
                ButtonRemappingContent.Visibility = isButtonRemappingExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ButtonRemappingExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ButtonRemappingExpandIcon.Glyph = isButtonRemappingExpanded ? "\uE70E" : "\uE70D";
            }

            if (isButtonRemappingExpanded)
            {
                RefreshLegionEnhancedRemapUi();
            }
        }

        private void GyroSettingsExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isGyroSettingsExpanded = !isGyroSettingsExpanded;

            if (GyroSettingsContent != null)
            {
                GyroSettingsContent.Visibility = isGyroSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (GyroSettingsExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                GyroSettingsExpandIcon.Glyph = isGyroSettingsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void SavedProfilesExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isSavedProfilesExpanded = !isSavedProfilesExpanded;

            if (SavedProfilesContent != null)
            {
                SavedProfilesContent.Visibility = isSavedProfilesExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (SavedProfilesExpandIcon != null)
            {
                SavedProfilesExpandIcon.Glyph = isSavedProfilesExpanded ? "\uE70E" : "\uE70D";
            }

            // Refresh the list when expanding
            if (isSavedProfilesExpanded)
            {
                RefreshSavedProfilesList();
            }
        }

        // Gamepad action names for profile summary display
        private static readonly string[] GamepadActionShortNames = new[]
        {
            "-", "LSC", "LSU", "LSD", "LSL", "LSR", "RSC", "RSU", "RSD", "RSL", "RSR",
            "DU", "DD", "DL", "DR", "A", "B", "X", "Y", "LB", "LT", "RB", "RT", "View", "Menu"
        };

        private void RefreshSavedProfilesList()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var savedProfiles = new List<SavedProfileInfo>();

                // Look for all controller profile containers
                foreach (var containerName in settings.Containers.Keys)
                {
                    if (!containerName.StartsWith("ControllerProfile_"))
                        continue;

                    var container = settings.Containers[containerName];
                    string displayName;
                    bool isGlobal = false;

                    if (containerName == "ControllerProfile_Global")
                    {
                        displayName = "Global (Default)";
                        isGlobal = true;
                    }
                    else if (containerName.StartsWith("ControllerProfile_Game_"))
                    {
                        // Extract game name: "ControllerProfile_Game_{gameName}"
                        displayName = containerName.Substring("ControllerProfile_Game_".Length).Replace("_", " ");
                    }
                    else
                    {
                        continue; // Unknown format
                    }

                    // Build settings summary
                    var summaryParts = new List<string>();

                    // Check for custom button mappings and show which buttons are remapped
                    var remapParts = new List<string>();
                    foreach (var btnName in new[] { "Y1", "Y2", "Y3", "M1", "M2", "M3", "Desktop", "Page" })
                    {
                        if (container.Values.TryGetValue($"Button{btnName}", out var mappingVal) && mappingVal is string mappingJson)
                        {
                            var mapping = ButtonMapping.FromJson(mappingJson);
                            if (mapping != null)
                            {
                                if (mapping.Type == 0)
                                {
                                    if (mapping.GamepadMode == 1 && mapping.GamepadActions != null && mapping.GamepadActions.Count > 0)
                                    {
                                        var comboNames = mapping.GamepadActions
                                            .Where(action => action > 0 && action < GamepadActionShortNames.Length)
                                            .Select(action => GamepadActionShortNames[action])
                                            .ToList();
                                        if (comboNames.Count > 0)
                                        {
                                            string comboText = string.Join("+", comboNames);
                                            remapParts.Add(mapping.Turbo ? $"{btnName}:{comboText}(T)" : $"{btnName}:{comboText}");
                                        }
                                    }
                                    else if (mapping.GamepadAction > 0 && mapping.GamepadAction < GamepadActionShortNames.Length)
                                    {
                                        // Gamepad remap
                                        string single = GamepadActionShortNames[mapping.GamepadAction];
                                        remapParts.Add(mapping.Turbo ? $"{btnName}:{single}(T)" : $"{btnName}:{single}");
                                    }
                                }
                                else if (mapping.Type == 1 && mapping.KeyboardKeys != null && mapping.KeyboardKeys.Count > 0)
                                {
                                    // Keyboard remap - show actual keys
                                    var keyNames = mapping.KeyboardKeys.Select(k => GetKeyDisplayName(k));
                                    remapParts.Add($"{btnName}:{string.Join("+", keyNames)}");
                                }
                                else if (mapping.Type == 2 && mapping.MouseButton > 0)
                                {
                                    // Mouse remap - show which button
                                    var mouseButtons = new[] { "", "Left", "Right", "Middle", "Back", "Forward" };
                                    var mouseName = mapping.MouseButton < mouseButtons.Length ? mouseButtons[mapping.MouseButton] : "Mouse";
                                    remapParts.Add($"{btnName}:{mouseName}Click");
                                }
                            }
                        }
                    }
                    if (remapParts.Count > 0)
                    {
                        summaryParts.Add(string.Join(" ", remapParts));
                    }

                    // Check gyro settings
                    if (container.Values.TryGetValue("GyroTarget", out var gyroTarget) && (int)gyroTarget > 0)
                    {
                        var gyroTargets = new[] { "", "LStick", "RStick", "Mouse" };
                        var targetIdx = (int)gyroTarget;
                        if (targetIdx > 0 && targetIdx < gyroTargets.Length)
                            summaryParts.Add($"Gyro:{gyroTargets[targetIdx]}");
                    }

                    // Check deadzones
                    if (container.Values.TryGetValue("LeftStickDeadzone", out var lsDz) && (int)lsDz != 4)
                    {
                        summaryParts.Add($"LDZ:{lsDz}%");
                    }
                    if (container.Values.TryGetValue("RightStickDeadzone", out var rsDz) && (int)rsDz != 4)
                    {
                        summaryParts.Add($"RDZ:{rsDz}%");
                    }

                    // Check joystick as mouse
                    if (container.Values.TryGetValue("JoystickAsMouseMode", out var jamMode) && (int)jamMode > 0)
                    {
                        summaryParts.Add("JoyMouse");
                    }

                    // Check RGB lighting settings
                    if (container.Values.TryGetValue("LightMode", out var lightModeVal))
                    {
                        int lightMode = (int)lightModeVal;
                        if (lightMode > 0) // 0 = Off
                        {
                            var lightModes = new[] { "Off", "Solid", "Breathe", "Rainbow", "Spiral" };
                            string modeName = lightMode < lightModes.Length ? lightModes[lightMode] : $"Mode{lightMode}";

                            // Get color if solid or breathe mode
                            if (lightMode == 1 || lightMode == 2) // Solid or Breathe
                            {
                                if (container.Values.TryGetValue("LightColorR", out var r) &&
                                    container.Values.TryGetValue("LightColorG", out var g) &&
                                    container.Values.TryGetValue("LightColorB", out var b))
                                {
                                    summaryParts.Add($"RGB:{modeName}({r},{g},{b})");
                                }
                                else
                                {
                                    summaryParts.Add($"RGB:{modeName}");
                                }
                            }
                            else
                            {
                                summaryParts.Add($"RGB:{modeName}");
                            }
                        }
                    }

                    // Check brightness
                    if (container.Values.TryGetValue("LightBrightness", out var brightnessVal) && (int)brightnessVal != 50)
                    {
                        summaryParts.Add($"Bright:{brightnessVal}%");
                    }

                    // Check power light
                    if (container.Values.TryGetValue("PowerLight", out var powerLightVal) && !(bool)powerLightVal)
                    {
                        summaryParts.Add("PwrLight:Off");
                    }

                    var summary = summaryParts.Count > 0 ? string.Join(" | ", summaryParts) : "Default settings";

                    // Get stored game exe path for icon loading
                    string gameExePath = null;
                    if (!isGlobal && container.Values.TryGetValue("GameExePath", out var exePathObj) && exePathObj is string exePath)
                    {
                        gameExePath = exePath;
                    }

                    savedProfiles.Add(new SavedProfileInfo
                    {
                        ProfileKey = containerName,
                        GameName = displayName,
                        SettingsSummary = summary,
                        IsGlobal = isGlobal,
                        GameExePath = gameExePath
                    });
                }

                // Sort: Global first, then alphabetically by game name
                savedProfiles.Sort((a, b) =>
                {
                    if (a.IsGlobal && !b.IsGlobal) return -1;
                    if (!a.IsGlobal && b.IsGlobal) return 1;
                    return string.Compare(a.GameName, b.GameName, StringComparison.OrdinalIgnoreCase);
                });

                // Update UI
                SavedProfilesList.ItemsSource = savedProfiles;
                NoSavedProfilesText.Visibility = savedProfiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // Load icons asynchronously for saved profiles
                _ = LoadSavedProfileIconsAsync(savedProfiles);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to refresh saved profiles list: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads icons for saved profiles asynchronously.
        /// </summary>
        private async Task LoadSavedProfileIconsAsync(List<SavedProfileInfo> profiles)
        {
            Logger.Info($"LoadSavedProfileIconsAsync: Loading icons for {profiles.Count} profiles");

            foreach (var profile in profiles)
            {
                if (profile.IsGlobal)
                {
                    Logger.Debug($"LoadSavedProfileIconsAsync: Skipping global profile");
                    continue;
                }

                if (string.IsNullOrEmpty(profile.GameExePath))
                {
                    Logger.Info($"LoadSavedProfileIconsAsync: No exe path for {profile.GameName}");
                    continue;
                }

                try
                {
                    Logger.Info($"LoadSavedProfileIconsAsync: Loading icon for {profile.GameName} from {profile.GameExePath}");
                    var icon = await LoadSavedProfileIconAsync(profile.GameExePath);
                    if (icon != null)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            profile.IconSource = icon;
                        });
                        Logger.Info($"LoadSavedProfileIconsAsync: Icon loaded for {profile.GameName}");
                    }
                    else
                    {
                        Logger.Info($"LoadSavedProfileIconsAsync: No icon found for {profile.GameName}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info($"LoadSavedProfileIconsAsync: Error loading icon for {profile.GameName}: {ex.Message}");
                }
            }
        }

        private void DeleteSavedProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string profileKey)
            {
                try
                {
                    // Don't allow deleting Global profile
                    if (profileKey == "ControllerProfile_Global")
                    {
                        Logger.Warn("Cannot delete Global controller profile");
                        return;
                    }

                    var settings = Windows.Storage.ApplicationData.Current.LocalSettings;

                    // Delete the controller profile container
                    if (settings.Containers.ContainsKey(profileKey))
                    {
                        settings.DeleteContainer(profileKey);
                        Logger.Info($"Deleted controller profile: {profileKey}");
                    }

                    // Refresh the list
                    RefreshSavedProfilesList();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to delete profile {profileKey}: {ex.Message}");
                }
            }
        }

        private void StickDeadzonesExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isStickDeadzonesExpanded = !isStickDeadzonesExpanded;

            if (StickDeadzonesContent != null)
            {
                StickDeadzonesContent.Visibility = isStickDeadzonesExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (StickDeadzonesExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                StickDeadzonesExpandIcon.Glyph = isStickDeadzonesExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void TouchpadVibrationExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isTouchpadVibrationExpanded = !isTouchpadVibrationExpanded;

            if (TouchpadVibrationContent != null)
            {
                TouchpadVibrationContent.Visibility = isTouchpadVibrationExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TouchpadVibrationExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                TouchpadVibrationExpandIcon.Glyph = isTouchpadVibrationExpanded ? "\uE70E" : "\uE70D";
            }
        }
        private void LightingExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isLightingExpanded = !isLightingExpanded;

            if (LightingContent != null)
            {
                LightingContent.Visibility = isLightingExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (LightingExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                LightingExpandIcon.Glyph = isLightingExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void FanCurveExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isFanCurveExpanded = !isFanCurveExpanded;

            if (FanCurveContent != null)
            {
                FanCurveContent.Visibility = isFanCurveExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (FanCurveExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                FanCurveExpandIcon.Glyph = isFanCurveExpanded ? "\uE70E" : "\uE70D";
            }

            // Initialize graph on first expand
            if (isFanCurveExpanded && !fanCurveGraphInitialized)
            {
                InitializeFanCurveGraph();
            }

            // Tell helper whether to push CPU temp/RPM updates
            legionFanCurveVisible?.SetVisible(isFanCurveExpanded);
        }
        private void TDPExtrasExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isTDPExtrasExpanded = !isTDPExtrasExpanded;

            if (TDPExtrasContent != null)
            {
                TDPExtrasContent.Visibility = isTDPExtrasExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TDPExtrasExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                TDPExtrasExpandIcon.Glyph = isTDPExtrasExpanded ? "\uE70E" : "\uE70D";
            }

            // Update XY focus chain based on expanded state
            if (TDPExtrasExpandToggle != null && OSPowerModeComboBox != null)
            {
                TDPExtrasExpandToggle.XYFocusDown = isTDPExtrasExpanded ? (DependencyObject)TDPBoostToggle : OSPowerModeComboBox;
                OSPowerModeComboBox.XYFocusUp = isTDPExtrasExpanded ? (DependencyObject)StickyTDPToggle : TDPExtrasExpandToggle;
            }
        }
        private void CPUExtrasExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isCPUExtrasExpanded = !isCPUExtrasExpanded;

            if (CPUExtrasContent != null)
            {
                CPUExtrasContent.Visibility = isCPUExtrasExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (CPUExtrasExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                CPUExtrasExpandIcon.Glyph = isCPUExtrasExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void ControllerEmulationExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isControllerEmulationExpanded = !isControllerEmulationExpanded;

            if (ControllerEmulationContent != null)
            {
                ControllerEmulationContent.Visibility = isControllerEmulationExpanded
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (ControllerEmulationExpandIcon != null)
            {
                ControllerEmulationExpandIcon.Glyph = isControllerEmulationExpanded ? "\uE70E" : "\uE70D";
            }

            UpdateControllerEmulationMouseSettingsVisibility();
            UpdateSystemControllerEmulationNavigation();
        }

        private void ControllerEmulationInputNotesExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isControllerEmulationInputNotesExpanded = !isControllerEmulationInputNotesExpanded;

            if (ControllerEmulationInputNotesContent != null)
            {
                ControllerEmulationInputNotesContent.Visibility = isControllerEmulationInputNotesExpanded
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (ControllerEmulationInputNotesExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ControllerEmulationInputNotesExpandIcon.Glyph = isControllerEmulationInputNotesExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void TDPSettingsExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isTDPSettingsExpanded = !isTDPSettingsExpanded;

            if (TDPSettingsContent != null)
            {
                TDPSettingsContent.Visibility = isTDPSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TDPSettingsExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                TDPSettingsExpandIcon.Glyph = isTDPSettingsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void SpecialRemappingExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isSpecialRemappingExpanded = !isSpecialRemappingExpanded;

            if (SpecialRemappingContent != null)
            {
                SpecialRemappingContent.Visibility = isSpecialRemappingExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (SpecialRemappingExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                SpecialRemappingExpandIcon.Glyph = isSpecialRemappingExpanded ? "\uE70E" : "\uE70D";
            }
        }
        private async void LoadPowerPlans()
        {
            isLoadingPowerPlans = true;

            try
            {
                // Request power plans from helper
                if (App.IsConnected)
                {
                    var request = new Windows.Foundation.Collections.ValueSet();
                    request.Add("GetPowerPlans", true);

                    var response = await App.SendMessageAsync(request);

                    if (response != null)
                    {
                        availablePowerPlans.Clear();

                        // Parse response: "GUID1|Name1;GUID2|Name2;..."
                        if (response.TryGetValue("PowerPlans", out object plansValue) && plansValue is string plansStr)
                        {
                            var planParts = plansStr.Split(';');
                            foreach (var part in planParts)
                            {
                                if (string.IsNullOrWhiteSpace(part)) continue;

                                var segments = part.Split('|');
                                if (segments.Length >= 2 && Guid.TryParse(segments[0], out Guid planGuid))
                                {
                                    availablePowerPlans.Add(new PowerPlanItem
                                    {
                                        Guid = planGuid,
                                        Name = segments[1]
                                    });
                                }
                            }
                        }

                        // Get currently active plan
                        if (response.TryGetValue("ActivePowerPlan", out object activeValue) && activeValue is string activeStr)
                        {
                            if (Guid.TryParse(activeStr, out Guid activeGuid))
                            {
                                // If no saved preferences, use current active plan as default
                                if (acPowerPlanGuid == Guid.Empty)
                                {
                                    acPowerPlanGuid = activeGuid;
                                }
                                if (dcPowerPlanGuid == Guid.Empty)
                                {
                                    dcPowerPlanGuid = activeGuid;
                                }
                            }
                        }

                        Logger.Info($"Received {availablePowerPlans.Count} power plans from helper");
                    }
                }

                // Fallback to well-known plans if helper didn't respond
                if (availablePowerPlans.Count == 0)
                {
                    Logger.Warn("No power plans received from helper, using defaults");
                    availablePowerPlans.Add(new PowerPlanItem
                    {
                        Guid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"),
                        Name = "Balanced"
                    });
                    availablePowerPlans.Add(new PowerPlanItem
                    {
                        Guid = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
                        Name = "High Performance"
                    });
                    availablePowerPlans.Add(new PowerPlanItem
                    {
                        Guid = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"),
                        Name = "Power Saver"
                    });
                }

                // Populate ComboBoxes
                if (ACPowerPlanComboBox != null)
                {
                    ACPowerPlanComboBox.Items.Clear();
                    foreach (var plan in availablePowerPlans)
                    {
                        ACPowerPlanComboBox.Items.Add(new ComboBoxItem { Content = plan.Name, Tag = plan.Guid.ToString() });
                    }

                    // Select saved or default
                    SelectPowerPlanInComboBox(ACPowerPlanComboBox, acPowerPlanGuid);
                }

                if (DCPowerPlanComboBox != null)
                {
                    DCPowerPlanComboBox.Items.Clear();
                    foreach (var plan in availablePowerPlans)
                    {
                        DCPowerPlanComboBox.Items.Add(new ComboBoxItem { Content = plan.Name, Tag = plan.Guid.ToString() });
                    }

                    // Select saved or default
                    SelectPowerPlanInComboBox(DCPowerPlanComboBox, dcPowerPlanGuid);
                }

                // Update toggle state
                if (PowerPlanAutoSwitchToggle != null)
                {
                    PowerPlanAutoSwitchToggle.IsOn = powerPlanAutoSwitch;
                }

                Logger.Info($"Loaded {availablePowerPlans.Count} power plans");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading power plans: {ex.Message}");
            }
            finally
            {
                isLoadingPowerPlans = false;
            }
        }

        private void SelectPowerPlanInComboBox(ComboBox comboBox, Guid planGuid)
        {
            if (comboBox == null) return;

            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item && item.Tag is string guidStr)
                {
                    if (Guid.TryParse(guidStr, out Guid itemGuid) && itemGuid == planGuid)
                    {
                        comboBox.SelectedIndex = i;
                        return;
                    }
                }
            }

            // Default to first item (Balanced) if not found
            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private void ACPowerPlanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingPowerPlans) return;

            if (ACPowerPlanComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string guidStr)
            {
                if (Guid.TryParse(guidStr, out Guid planGuid))
                {
                    acPowerPlanGuid = planGuid;
                    SavePowerPlanSettings();

                    // If currently on AC power, apply the plan immediately
                    if (powerPlanAutoSwitch && PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate)
                    {
                        ApplyPowerPlan(planGuid);
                    }

                    Logger.Info($"AC Power Plan set to: {selected.Content} ({planGuid})");
                }
            }
        }

        private void DCPowerPlanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingPowerPlans) return;

            if (DCPowerPlanComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string guidStr)
            {
                if (Guid.TryParse(guidStr, out Guid planGuid))
                {
                    dcPowerPlanGuid = planGuid;
                    SavePowerPlanSettings();

                    // If currently on battery, apply the plan immediately
                    if (powerPlanAutoSwitch && PowerManager.PowerSupplyStatus != PowerSupplyStatus.Adequate)
                    {
                        ApplyPowerPlan(planGuid);
                    }

                    Logger.Info($"DC Power Plan set to: {selected.Content} ({planGuid})");
                }
            }
        }

        private void PowerPlanAutoSwitchToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingPowerPlans) return;

            powerPlanAutoSwitch = PowerPlanAutoSwitchToggle?.IsOn ?? false;
            SavePowerPlanSettings();

            Logger.Info($"Power Plan auto-switch set to: {powerPlanAutoSwitch}");
        }

        private void AutoHibernateTimeoutSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (AutoHibernateTimeoutValue != null)
            {
                AutoHibernateTimeoutValue.Text = $"{(int)e.NewValue} min";
            }
        }

        private void ControllerEmulationMouseSensitivitySlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ControllerEmulationMouseSensitivityValue != null)
            {
                ControllerEmulationMouseSensitivityValue.Text = ((int)e.NewValue).ToString();
            }
        }

        private void ControllerEmulationMouseThresholdSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ControllerEmulationMouseThresholdValue != null)
            {
                ControllerEmulationMouseThresholdValue.Text = ((int)e.NewValue).ToString();
            }
        }

        private void ControllerEmulationMouseGainXSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ControllerEmulationMouseGainXValue != null)
            {
                ControllerEmulationMouseGainXValue.Text = ((int)e.NewValue).ToString();
            }
        }

        private void ControllerEmulationMouseGainYSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ControllerEmulationMouseGainYValue != null)
            {
                ControllerEmulationMouseGainYValue.Text = ((int)e.NewValue).ToString();
            }
        }

        private void GyroActivationExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isGyroActivationExpanded = !isGyroActivationExpanded;
            if (GyroActivationContent != null)
                GyroActivationContent.Visibility = isGyroActivationExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (GyroActivationExpandIcon != null)
                GyroActivationExpandIcon.Glyph = isGyroActivationExpanded ? "\uE70E" : "\uE70D";
        }

        private void FeaturesExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isFeaturesExpanded = !isFeaturesExpanded;
            if (FeaturesContent != null)
                FeaturesContent.Visibility = isFeaturesExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (FeaturesExpandIcon != null)
                FeaturesExpandIcon.Glyph = isFeaturesExpanded ? "\uE70E" : "\uE70D";
        }

        private void JoystickOutputExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isJoystickOutputExpanded = !isJoystickOutputExpanded;
            if (JoystickOutputContent != null)
                JoystickOutputContent.Visibility = isJoystickOutputExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (JoystickOutputExpandIcon != null)
                JoystickOutputExpandIcon.Glyph = isJoystickOutputExpanded ? "\uE70E" : "\uE70D";
        }

        private void StickSensitivityV2Slider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickSensitivityV2ValueText != null)
                StickSensitivityV2ValueText.Text = $"{(e.NewValue / 100.0):0.00}x";
        }

        private void StickMinGyroSpeedSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickMinGyroSpeedValueText != null)
                StickMinGyroSpeedValueText.Text = $"{(int)e.NewValue}\u00B0/s";
        }

        private void StickMaxGyroSpeedSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickMaxGyroSpeedValueText != null)
                StickMaxGyroSpeedValueText.Text = $"{(int)e.NewValue}\u00B0/s";
        }

        private void StickMinOutputSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickMinOutputValueText != null)
                StickMinOutputValueText.Text = $"{(int)e.NewValue}%";
        }

        private void StickMaxOutputSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickMaxOutputValueText != null)
                StickMaxOutputValueText.Text = $"{(int)e.NewValue}%";
        }

        private void StickPowerCurveSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickPowerCurveValueText != null)
                StickPowerCurveValueText.Text = $"{(e.NewValue / 100.0):0.0}";
        }

        private void StickDeadzoneSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickDeadzoneValueText != null)
                StickDeadzoneValueText.Text = $"{(int)e.NewValue}\u00B0/s";
        }

        private void StickPrecisionSpeedSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickPrecisionSpeedValueText != null)
            {
                int val = (int)e.NewValue;
                StickPrecisionSpeedValueText.Text = val == 0 ? "Off" : $"{val}\u00B0/s";
            }
        }

        private void StickOutputMixSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickOutputMixValueText != null)
            {
                int val = (int)e.NewValue;
                if (val > 0)
                    StickOutputMixValueText.Text = $"H+{val}";
                else if (val < 0)
                    StickOutputMixValueText.Text = $"V+{-val}";
                else
                    StickOutputMixValueText.Text = "Balanced";
            }
        }

        private async void AutoHibernateModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AutoHibernateModeComboBox?.SelectedItem == null) return;
            var selected = AutoHibernateModeComboBox.SelectedItem as ComboBoxItem;
            if (selected?.Tag == null) return;

            int mode = int.Parse(selected.Tag.ToString());
            try
            {
                if (App.IsConnected)
                {
                    var request = new Windows.Foundation.Collections.ValueSet
                    {
                        { "Command", (int)Shared.Enums.Command.Set },
                        { "Function", (int)Shared.Enums.Function.AutoHibernateMode },
                        { "Content", mode }
                    };
                    await App.SendMessageAsync(request);
                    Logger.Info($"Auto Hibernate mode set to: {selected.Content}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending Auto Hibernate mode: {ex.Message}");
            }
        }

        private void ApplyPowerPlan(Guid planGuid)
        {
            if (planGuid == Guid.Empty) return;

            // Send message to helper to apply the power plan
            // Format: "PowerPlan:GUID"
            try
            {
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("PowerPlan", planGuid.ToString());
                _ = SendHelperMessageAsync(message);
                Logger.Info($"Sent power plan change request: {planGuid}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying power plan: {ex.Message}");
            }
        }

        private async Task SendHelperMessageAsync(Windows.Foundation.Collections.ValueSet message)
        {
            if (App.IsConnected)
            {
                try
                {
                    await App.SendMessageAsync(message);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error sending message to helper: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Send a keyboard shortcut via the helper process.
        /// This is required because UWP apps cannot use SendInput directly due to sandboxing.
        /// </summary>
        private async Task SendKeyboardShortcutViaHelper(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut))
            {
                Logger.Warn("Empty shortcut string provided to SendKeyboardShortcutViaHelper");
                return;
            }

            try
            {
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("SendKeyboardShortcut", shortcut);
                await SendHelperMessageAsync(message);
                Logger.Info($"Sent keyboard shortcut request to helper: {shortcut}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending keyboard shortcut via helper: {ex.Message}");
            }
        }

        /// <summary>
        /// Request the helper to refresh display settings (resolution, refresh rate, HDR).
        /// Called when a game closes to ensure the resolution tile shows the correct value.
        /// </summary>
        private async Task RequestDisplaySettingsRefreshAsync()
        {
            try
            {
                Logger.Info("Requesting display settings refresh from helper");
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("RefreshDisplaySettings", true);
                await SendHelperMessageAsync(message);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error requesting display settings refresh: {ex.Message}");
            }
        }

        /// <summary>
        /// Send a custom shortcut by first closing Game Bar (if in widget mode), then sending the shortcut.
        /// Sequence: Win+G (close Game Bar) → Custom shortcut
        /// </summary>
        private async Task SendCustomShortcutAsync(string shortcut, string tileName)
        {
            try
            {
                Logger.Info($"Custom shortcut tile clicked: {tileName} -> {shortcut}");

                // Only close Game Bar if we're running as a widget
                if (widget != null)
                {
                    // First close Game Bar with Win+G
                    await SendKeyboardShortcutViaHelper("Win+G");
                    Logger.Debug("Win+G sent to close Game Bar");

                    // Wait for Game Bar to close
                    await Task.Delay(150);
                }

                // Now send the actual shortcut
                await SendKeyboardShortcutViaHelper(shortcut);
                Logger.Info($"Custom shortcut sent: {shortcut}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending custom shortcut '{shortcut}': {ex.Message}");
            }
        }

        private void SavePowerPlanSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["PowerPlan_AC"] = acPowerPlanGuid.ToString();
                settings.Values["PowerPlan_DC"] = dcPowerPlanGuid.ToString();
                settings.Values["PowerPlan_AutoSwitch"] = powerPlanAutoSwitch;
                Logger.Info("Power plan settings saved");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving power plan settings: {ex.Message}");
            }
        }

        private void LoadPowerPlanSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("PowerPlan_AC", out object acVal) && acVal is string acStr)
                {
                    if (Guid.TryParse(acStr, out Guid acGuid))
                    {
                        acPowerPlanGuid = acGuid;
                    }
                }

                if (settings.Values.TryGetValue("PowerPlan_DC", out object dcVal) && dcVal is string dcStr)
                {
                    if (Guid.TryParse(dcStr, out Guid dcGuid))
                    {
                        dcPowerPlanGuid = dcGuid;
                    }
                }

                if (settings.Values.TryGetValue("PowerPlan_AutoSwitch", out object autoVal))
                {
                    // Handle different possible types stored in settings
                    if (autoVal is bool autoSwitch)
                    {
                        powerPlanAutoSwitch = autoSwitch;
                    }
                    else if (autoVal is string autoStr)
                    {
                        powerPlanAutoSwitch = autoStr.Equals("True", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        Logger.Warn($"PowerPlan_AutoSwitch has unexpected type: {autoVal?.GetType().Name ?? "null"}");
                    }
                }
                else
                {
                    Logger.Info("PowerPlan_AutoSwitch not found in settings, using default (OFF)");
                }

                // Note: If GUIDs are empty, LoadPowerPlans() will use the current active plan as default

                Logger.Info($"Power plan settings loaded: AC={acPowerPlanGuid}, DC={dcPowerPlanGuid}, AutoSwitch={powerPlanAutoSwitch}");

                // Immediately sync the toggle UI to the loaded value
                // Use isLoadingPowerPlans flag to prevent Toggled event from triggering a save
                isLoadingPowerPlans = true;
                try
                {
                    if (PowerPlanAutoSwitchToggle != null)
                    {
                        PowerPlanAutoSwitchToggle.IsOn = powerPlanAutoSwitch;
                        Logger.Info($"PowerPlanAutoSwitchToggle UI synced to {powerPlanAutoSwitch}");
                    }
                }
                finally
                {
                    isLoadingPowerPlans = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading power plan settings: {ex.Message}");
            }
        }

        private void LoadForceDefaultGameProfileSetting()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("ForceDefaultGameProfile", out object val) && val is bool enabled)
                {
                    if (ForceDefaultGameProfileToggle != null)
                    {
                        ForceDefaultGameProfileToggle.IsOn = enabled;
                    }
                    // Send to helper on startup
                    forceDefaultGameProfile?.SetValue(enabled);
                    Logger.Info($"Loaded Force Default Game Profile setting: {enabled}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading Force Default Game Profile setting: {ex.Message}");
            }
        }

        private void ColorSettingsExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isColorSettingsExpanded = !isColorSettingsExpanded;

            if (ColorSettingsContent != null)
            {
                ColorSettingsContent.Visibility = isColorSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ColorSettingsExpandButton != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ColorSettingsExpandButton.Content = isColorSettingsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void TDPLimitsMinSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPLimits) return;
            if (TDPLimitsMinSlider == null || TDPLimitsMaxSlider == null) return;

            int minValue = (int)Math.Round(e.NewValue);

            // Ensure min doesn't exceed max
            if (minValue > TDPLimitsMaxSlider.Value)
            {
                TDPLimitsMinSlider.Value = TDPLimitsMaxSlider.Value;
                return;
            }

            deviceTDPMin = minValue;

            if (TDPLimitsMinValue != null)
            {
                TDPLimitsMinValue.Text = $"{minValue}W";
            }

            // Update TDP slider bounds immediately (for UI responsiveness)
            UpdateTDPSliderBounds();

            // Debounce save and send to helper
            StartTDPLimitsDebounce();
        }

        private void TDPLimitsMaxSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPLimits) return;
            if (TDPLimitsMinSlider == null || TDPLimitsMaxSlider == null) return;

            int maxValue = (int)Math.Round(e.NewValue);

            // Ensure max doesn't go below min
            if (maxValue < TDPLimitsMinSlider.Value)
            {
                TDPLimitsMaxSlider.Value = TDPLimitsMinSlider.Value;
                return;
            }

            deviceTDPMax = maxValue;

            if (TDPLimitsMaxValue != null)
            {
                TDPLimitsMaxValue.Text = $"{maxValue}W";
            }

            // Update TDP slider bounds immediately (for UI responsiveness)
            UpdateTDPSliderBounds();

            // Debounce save and send to helper
            StartTDPLimitsDebounce();
        }

        private void StartTDPLimitsDebounce()
        {
            // Initialize debounce timer if needed
            if (tdpLimitsDebounceTimer == null)
            {
                tdpLimitsDebounceTimer = new DispatcherTimer();
                tdpLimitsDebounceTimer.Interval = TimeSpan.FromMilliseconds(TDP_LIMITS_DEBOUNCE_MS);
                tdpLimitsDebounceTimer.Tick += TDPLimitsDebounceTimer_Tick;
            }

            // Restart the debounce timer
            tdpLimitsDebounceTimer.Stop();
            tdpLimitsDebounceTimer.Start();
        }

        private void TDPLimitsDebounceTimer_Tick(object sender, object e)
        {
            tdpLimitsDebounceTimer?.Stop();

            // Save and send to helper after debounce
            SaveTDPLimitsToStorage();
            SendTDPLimitsToHelper();
        }

        private void UpdateTDPSliderBounds()
        {
            // Update Performance tab TDP slider
            if (TDPSlider != null)
            {
                TDPSlider.Minimum = deviceTDPMin;
                TDPSlider.Maximum = deviceTDPMax;

                // Clamp current value if out of bounds
                if (TDPSlider.Value < deviceTDPMin)
                    TDPSlider.Value = deviceTDPMin;
                else if (TDPSlider.Value > deviceTDPMax)
                    TDPSlider.Value = deviceTDPMax;
            }

            // Update AutoTDP Min slider bounds
            if (AutoTDPMinSlider != null)
            {
                AutoTDPMinSlider.Minimum = deviceTDPMin;
                AutoTDPMinSlider.Maximum = deviceTDPMax;

                // Clamp current value if out of bounds
                if (AutoTDPMinSlider.Value < deviceTDPMin)
                    AutoTDPMinSlider.Value = deviceTDPMin;
                else if (AutoTDPMinSlider.Value > deviceTDPMax)
                    AutoTDPMinSlider.Value = deviceTDPMax;
            }

            // Update AutoTDP Max slider bounds
            if (AutoTDPMaxSlider != null)
            {
                AutoTDPMaxSlider.Minimum = deviceTDPMin;
                AutoTDPMaxSlider.Maximum = deviceTDPMax;

                // Clamp current value if out of bounds
                if (AutoTDPMaxSlider.Value < deviceTDPMin)
                    AutoTDPMaxSlider.Value = deviceTDPMin;
                else if (AutoTDPMaxSlider.Value > deviceTDPMax)
                    AutoTDPMaxSlider.Value = deviceTDPMax;
            }
        }

        private void ApplyTDPLimits()
        {
            // Update TDP slider bounds
            UpdateTDPSliderBounds();

            // Send limits to helper for AutoTDP
            SendTDPLimitsToHelper();
        }

        private void SendTDPLimitsToHelper()
        {
            try
            {
                string limitsString = $"{deviceTDPMin},{deviceTDPMax}";
                tdpLimits?.SetValue(limitsString);
                Logger.Info($"Sent TDP limits to helper: Min={deviceTDPMin}W, Max={deviceTDPMax}W");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send TDP limits to helper: {ex.Message}");
            }
        }

        private void SaveTDPLimitsToStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["DeviceTDPMin"] = deviceTDPMin;
                settings.Values["DeviceTDPMax"] = deviceTDPMax;
                Logger.Info($"Saved TDP limits: Min={deviceTDPMin}W, Max={deviceTDPMax}W");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save TDP limits: {ex.Message}");
            }
        }

        private void LoadTDPLimitsFromStorage()
        {
            isLoadingTDPLimits = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("DeviceTDPMin", out object minObj) && minObj is int min)
                {
                    deviceTDPMin = min;
                }

                if (settings.Values.TryGetValue("DeviceTDPMax", out object maxObj) && maxObj is int max)
                {
                    deviceTDPMax = max;
                }

                // Update UI
                if (TDPLimitsMinSlider != null)
                {
                    TDPLimitsMinSlider.Value = deviceTDPMin;
                    if (TDPLimitsMinValue != null)
                        TDPLimitsMinValue.Text = $"{deviceTDPMin}W";
                }

                if (TDPLimitsMaxSlider != null)
                {
                    TDPLimitsMaxSlider.Value = deviceTDPMax;
                    if (TDPLimitsMaxValue != null)
                        TDPLimitsMaxValue.Text = $"{deviceTDPMax}W";
                }

                // Apply to TDP slider
                ApplyTDPLimits();

                Logger.Info($"Loaded TDP limits: Min={deviceTDPMin}W, Max={deviceTDPMax}W");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load TDP limits: {ex.Message}");
            }
            finally
            {
                isLoadingTDPLimits = false;
            }
        }
        private void OSDLayoutOption_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            // Get text size (global setting)
            if (OSDTextSizeComboBox?.SelectedItem is ComboBoxItem sizeItem && sizeItem.Tag is string sizeTag)
            {
                if (int.TryParse(sizeTag, out int size))
                {
                    osdTextSize = size;
                }
            }

            // Columns are per-level, handled by SaveCurrentOSDConfig
            SaveCurrentOSDConfig();
        }

        private void OSDTextColorDynamic_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            bool isDynamic = OSDTextColorDynamicCheckBox?.IsChecked == true;
            if (isDynamic)
            {
                osdTextColor = "DYNAMIC";
                UpdateOSDTextColorPreview();
            }
            else
            {
                // Use current color picker color
                if (OSDTextColorPicker != null)
                {
                    var color = OSDTextColorPicker.Color;
                    osdTextColor = $"{color.R:X2}{color.G:X2}{color.B:X2}";
                }
                else
                {
                    osdTextColor = "FFFFFF";
                }
                UpdateOSDTextColorPreview();
            }

            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        private void OSDTextColorExpandButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OSDTextColorPicker != null)
                {
                    bool isExpanded = OSDTextColorPicker.Visibility == Visibility.Visible;
                    OSDTextColorPicker.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;

                    if (OSDTextColorExpandButton != null)
                    {
                        OSDTextColorExpandButton.Content = isExpanded ? "\uE70D" : "\uE70E";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDTextColorExpandButton_Click: {ex.Message}");
            }
        }

        private void OSDTextColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            if (isLoadingOSDConfig) return;

            try
            {
                // Update preview
                if (OSDTextColorPreview != null)
                {
                    OSDTextColorPreview.Background = new SolidColorBrush(args.NewColor);
                }

                // Only update color if not in Dynamic mode
                if (OSDTextColorDynamicCheckBox?.IsChecked != true)
                {
                    osdTextColor = $"{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDTextColorPicker_ColorChanged: {ex.Message}");
            }
        }

        private void OSDLabelColorDefault_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            bool isDefault = OSDLabelColorDefaultCheckBox?.IsChecked == true;
            if (isDefault)
            {
                osdLabelColor = "DEFAULT";
                UpdateOSDLabelColorPreview();
            }
            else
            {
                // Use current color picker color
                if (OSDLabelColorPicker != null)
                {
                    var color = OSDLabelColorPicker.Color;
                    osdLabelColor = $"{color.R:X2}{color.G:X2}{color.B:X2}";
                }
                else
                {
                    osdLabelColor = "00FFFF";  // Cyan default
                }
                UpdateOSDLabelColorPreview();
            }

            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        private void OSDLabelColorExpandButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OSDLabelColorPicker != null)
                {
                    bool isExpanded = OSDLabelColorPicker.Visibility == Visibility.Visible;
                    OSDLabelColorPicker.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;

                    if (OSDLabelColorExpandButton != null)
                    {
                        OSDLabelColorExpandButton.Content = isExpanded ? "\uE70D" : "\uE70E";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDLabelColorExpandButton_Click: {ex.Message}");
            }
        }

        private void OSDLabelColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            if (isLoadingOSDConfig) return;

            try
            {
                // Update preview
                if (OSDLabelColorPreview != null)
                {
                    OSDLabelColorPreview.Background = new SolidColorBrush(args.NewColor);
                }

                // Only update color if not in Default mode
                if (OSDLabelColorDefaultCheckBox?.IsChecked != true)
                {
                    osdLabelColor = $"{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDLabelColorPicker_ColorChanged: {ex.Message}");
            }
        }

        private void UpdateOSDTextColorPreview()
        {
            if (OSDTextColorPreview == null) return;

            try
            {
                if (osdTextColor == "DYNAMIC")
                {
                    // Show gradient for dynamic color preview (blue to green to yellow to red)
                    var gradient = new LinearGradientBrush();
                    gradient.StartPoint = new Windows.Foundation.Point(0, 0);
                    gradient.EndPoint = new Windows.Foundation.Point(1, 0);
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 128, 255), Offset = 0 });    // Blue (cold)
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 255, 0), Offset = 0.33 });   // Green (good)
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 255, 0), Offset = 0.66 }); // Yellow (warm)
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 0, 0), Offset = 1 });      // Red (hot)
                    OSDTextColorPreview.Background = gradient;
                }
                else if (osdTextColor.Length == 6)
                {
                    var color = Windows.UI.Color.FromArgb(255,
                        Convert.ToByte(osdTextColor.Substring(0, 2), 16),
                        Convert.ToByte(osdTextColor.Substring(2, 2), 16),
                        Convert.ToByte(osdTextColor.Substring(4, 2), 16));
                    OSDTextColorPreview.Background = new SolidColorBrush(color);
                }
            }
            catch { }
        }

        private void UpdateOSDLabelColorPreview()
        {
            if (OSDLabelColorPreview == null) return;

            try
            {
                if (osdLabelColor == "DEFAULT")
                {
                    // Show gradient to indicate default (each item has its own color)
                    var gradient = new LinearGradientBrush();
                    gradient.StartPoint = new Windows.Foundation.Point(0, 0);
                    gradient.EndPoint = new Windows.Foundation.Point(1, 0);
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 255, 255), Offset = 0 });    // Cyan
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 165, 0), Offset = 0.5 });  // Orange
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 255, 0), Offset = 1 });      // Green
                    OSDLabelColorPreview.Background = gradient;
                }
                else if (osdLabelColor.Length == 6)
                {
                    var color = Windows.UI.Color.FromArgb(255,
                        Convert.ToByte(osdLabelColor.Substring(0, 2), 16),
                        Convert.ToByte(osdLabelColor.Substring(2, 2), 16),
                        Convert.ToByte(osdLabelColor.Substring(4, 2), 16));
                    OSDLabelColorPreview.Background = new SolidColorBrush(color);
                }
            }
            catch { }
        }

        private void UpdateOSDLayoutUI()
        {
            isLoadingOSDConfig = true;
            try
            {
                // Set OSD provider combobox
                if (OSDProviderComboBox != null)
                {
                    foreach (ComboBoxItem item in OSDProviderComboBox.Items)
                    {
                        if (item.Tag is string tag && int.TryParse(tag, out int val) && val == osdProvider)
                        {
                            OSDProviderComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Update provider-specific UI visibility
                UpdateOSDProviderUI();

                // Columns are per-level, loaded in LoadOSDOptionsForLevel

                // Set text size combobox
                if (OSDTextSizeComboBox != null)
                {
                    foreach (ComboBoxItem item in OSDTextSizeComboBox.Items)
                    {
                        if (item.Tag is string tag && int.TryParse(tag, out int val) && val == osdTextSize)
                        {
                            OSDTextSizeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Set text color checkbox and color picker
                if (OSDTextColorDynamicCheckBox != null)
                {
                    OSDTextColorDynamicCheckBox.IsChecked = (osdTextColor == "DYNAMIC");
                }
                if (OSDTextColorPicker != null && osdTextColor != "DYNAMIC" && osdTextColor.Length == 6)
                {
                    try
                    {
                        var color = Windows.UI.Color.FromArgb(255,
                            Convert.ToByte(osdTextColor.Substring(0, 2), 16),
                            Convert.ToByte(osdTextColor.Substring(2, 2), 16),
                            Convert.ToByte(osdTextColor.Substring(4, 2), 16));
                        OSDTextColorPicker.Color = color;
                    }
                    catch { }
                }
                UpdateOSDTextColorPreview();

                // Set label color checkbox and color picker
                if (OSDLabelColorDefaultCheckBox != null)
                {
                    OSDLabelColorDefaultCheckBox.IsChecked = (osdLabelColor == "DEFAULT");
                }
                if (OSDLabelColorPicker != null && osdLabelColor != "DEFAULT" && osdLabelColor.Length == 6)
                {
                    try
                    {
                        var color = Windows.UI.Color.FromArgb(255,
                            Convert.ToByte(osdLabelColor.Substring(0, 2), 16),
                            Convert.ToByte(osdLabelColor.Substring(2, 2), 16),
                            Convert.ToByte(osdLabelColor.Substring(4, 2), 16));
                        OSDLabelColorPicker.Color = color;
                    }
                    catch { }
                }
                UpdateOSDLabelColorPreview();

            }
            finally
            {
                isLoadingOSDConfig = false;
            }
        }

    }
}
