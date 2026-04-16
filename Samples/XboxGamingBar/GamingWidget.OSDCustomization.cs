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

        #region Display and OSD Settings Handlers

        private async void AdaptiveBrightnessToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingOLEDSettings) return;
            adaptiveBrightnessEnabled = AdaptiveBrightnessToggle.IsOn;
            SaveDisplayOSDSettingsToStorage();
            await SendDisplayOSDConfigToHelper();
        }

        private async void OSDPositionShiftToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingOLEDSettings) return;
            osdPositionShiftEnabled = OSDPositionShiftToggle.IsOn;
            SaveDisplayOSDSettingsToStorage();
            await SendDisplayOSDConfigToHelper();
        }

        private void FrametimeGraphPinnedToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;
            frametimeGraphPinned = FrametimeGraphPinnedToggle.IsOn;
            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        private void SaveDisplayOSDSettingsToStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["OLED_AdaptiveBrightness"] = adaptiveBrightnessEnabled;
                settings.Values["OLED_PositionShift"] = osdPositionShiftEnabled;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving display/OSD settings: {ex.Message}");
            }
        }

        private void LoadDisplayOSDSettingsFromStorage()
        {
            isLoadingOLEDSettings = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("OLED_AdaptiveBrightness", out object adaptiveBrightness) && adaptiveBrightness is bool ab)
                    adaptiveBrightnessEnabled = ab;
                if (settings.Values.TryGetValue("OLED_PositionShift", out object posShift) && posShift is bool ps)
                    osdPositionShiftEnabled = ps;
                if (settings.Values.TryGetValue("OSD_Opacity", out object opacity) && opacity is int op)
                    osdOpacity = op;

                // Update UI
                if (AdaptiveBrightnessToggle != null) AdaptiveBrightnessToggle.IsOn = adaptiveBrightnessEnabled;
                if (OSDPositionShiftToggle != null) OSDPositionShiftToggle.IsOn = osdPositionShiftEnabled;
                if (OSDOpacitySlider != null) OSDOpacitySlider.Value = osdOpacity;
                if (OSDOpacityValue != null) OSDOpacityValue.Text = $"{osdOpacity}%";
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading display/OSD settings: {ex.Message}");
            }
            finally
            {
                isLoadingOLEDSettings = false;
            }
        }

        private async Task SendDisplayOSDConfigToHelper()
        {
            try
            {
                if (!App.IsConnected) return;

                var configString = $"AdaptiveBrightness:{(adaptiveBrightnessEnabled ? 1 : 0)};" +
                                   $"PositionShift:{(osdPositionShiftEnabled ? 1 : 0)}";

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.OLEDConfig },
                    { "Content", configString },
                    { "UpdatedTime", DateTimeOffset.Now.Ticks }
                };
                await App.SendMessageAsync(request);

                Logger.Info($"Display/OSD config sent to helper: {configString}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending display/OSD config to helper: {ex.Message}");
            }
        }

        #endregion

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

        #region Quick Settings Action Helpers

        /// <summary>
        /// Toggles the Legion Power Light on/off.
        /// </summary>
        private void ToggleLegionPowerLight()
        {
            if (legionPowerLight == null) return;

            // Toggle the current state
            bool newState = !legionPowerLight.Value;
            legionPowerLight.SetValue(newState);

            Logger.Info($"Power Light toggled: {(newState ? "On" : "Off")}");
        }

        /// <summary>
        /// Toggles Fan Full Speed mode on/off for Legion or GPD devices.
        /// </summary>
        private bool gpdFanMaxActive = false;

        // GPD Software Fan Curve graph state
        private bool isGPDFanCurveExpanded = false;
        private bool gpdFanCurveGraphInitialized = false;
        private readonly Windows.UI.Xaml.Shapes.Ellipse[] gpdFanCurvePoints = new Windows.UI.Xaml.Shapes.Ellipse[10];
        private int[] currentGPDFanCurveValues = new int[10];
        private int gpdDraggedPointIndex = -1;
        private bool isGPDDraggingPoint = false;
        private static readonly int[] GPDFanCurveTemps = { 30, 38, 46, 54, 62, 70, 78, 86, 94, 100 };
        private static readonly Dictionary<string, int[]> GPDFanCurvePresets = new Dictionary<string, int[]>
        {
            { "Silent",      new int[] { 0, 0, 0, 30, 35, 45, 55, 65, 80, 100 } },
            { "Balanced",    new int[] { 0, 30, 35, 45, 55, 65, 75, 85, 95, 100 } },
            { "Performance", new int[] { 30, 40, 50, 55, 60, 70, 80, 90, 95, 100 } },
            { "MaxCooling",  new int[] { 40, 50, 60, 65, 70, 80, 85, 95, 100, 100 } }
        };
        private string currentGPDFanCurvePreset = "Custom";
        private bool isGPDFanCurvePresetLoading = false;

        private void ToggleLegionFanFullSpeed()
        {
            if (legionGoDetected?.Value == true && legionFanFullSpeed != null)
            {
                bool newState = !legionFanFullSpeed.Value;
                legionFanFullSpeed.SetValue(newState);

                if (LegionFanFullSpeedToggle != null)
                {
                    LegionFanFullSpeedToggle.IsOn = newState;
                }

                Logger.Info($"Fan Full Speed toggled (Legion): {(newState ? "On" : "Off")}");
            }
            else if (gpdDetected?.Value == true && gpdFanMode != null && gpdFanSpeed != null)
            {
                gpdFanMaxActive = !gpdFanMaxActive;
                if (gpdFanMaxActive)
                {
                    gpdFanMode.SetMode(1); // Manual
                    gpdFanSpeed.SetSpeed(100); // 100%
                    if (GPDFanModeToggle != null)
                    {
                        GPDFanModeToggle.Toggled -= GPDFanModeToggle_Toggled;
                        GPDFanModeToggle.IsOn = true;
                        GPDFanModeToggle.Toggled += GPDFanModeToggle_Toggled;
                    }
                    if (GPDFanSpeedSlider != null) GPDFanSpeedSlider.Value = 100;
                }
                else
                {
                    gpdFanMode.SetMode(0); // Auto
                    gpdFanSpeed.SetSpeed(0);
                    if (GPDFanModeToggle != null)
                    {
                        GPDFanModeToggle.Toggled -= GPDFanModeToggle_Toggled;
                        GPDFanModeToggle.IsOn = false;
                        GPDFanModeToggle.Toggled += GPDFanModeToggle_Toggled;
                    }
                }

                Logger.Info($"Fan Full Speed toggled (GPD): {(gpdFanMaxActive ? "On" : "Off")}");
            }
        }

        private async void ToggleScreenSaver()
        {
            screenSaverEnabled = !screenSaverEnabled;

            // Persist setting
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[ScreenSaverEnabledKey] = screenSaverEnabled;

            // Start or stop countdown timer
            if (screenSaverEnabled)
            {
                StartScreenSaverCountdown();
            }
            else
            {
                StopScreenSaverCountdown();
            }

            // Send to helper
            try
            {
                if (App.IsConnected)
                {
                    var request = new Windows.Foundation.Collections.ValueSet
                    {
                        { "Command", (int)Shared.Enums.Command.Set },
                        { "Function", (int)Shared.Enums.Function.ScreenSaverEnabled },
                        { "Content", screenSaverEnabled }
                    };
                    await App.SendMessageAsync(request);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending Screen Saver state: {ex.Message}");
            }

            Logger.Info($"Screen Saver toggled: {(screenSaverEnabled ? "On" : "Off")}");
        }

        private void StartScreenSaverCountdown()
        {
            if (screenSaverCountdownTimer == null)
            {
                screenSaverCountdownTimer = new DispatcherTimer();
                screenSaverCountdownTimer.Interval = TimeSpan.FromSeconds(1);
                screenSaverCountdownTimer.Tick += ScreenSaverCountdownTimer_Tick;
            }
            screenSaverCountdownTimer.Start();
            UpdateScreenSaverTileCountdown();
        }

        private void StopScreenSaverCountdown()
        {
            screenSaverCountdownTimer?.Stop();
            UpdateQuickSettingsTileStates();
        }

        private void ScreenSaverCountdownTimer_Tick(object sender, object e)
        {
            if (!screenSaverEnabled)
            {
                screenSaverCountdownTimer?.Stop();
                return;
            }
            UpdateScreenSaverTileCountdown();
        }

        private void UpdateScreenSaverTileCountdown()
        {
            if (qsTileMap == null || !qsTileMap.TryGetValue("ScreenSaver", out var tile) || tile.StateText == null)
                return;

            try
            {
                var lastInput = new LASTINPUTINFO();
                lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);

                if (GetLastInputInfo(ref lastInput))
                {
                    uint idleMs = (uint)Environment.TickCount - lastInput.dwTime;
                    int remaining = Math.Max(0, ScreenSaverTimeoutSeconds - (int)(idleMs / 1000));
                    tile.StateText.Text = $"{remaining}s";
                }
            }
            catch
            {
                tile.StateText.Text = $"{ScreenSaverTimeoutSeconds}s";
            }
        }

        /// <summary>
        /// Puts the system into hibernation via helper.
        /// </summary>
        private async void ExecuteHibernate()
        {
            Logger.Info("Hibernate action triggered");

            try
            {
                if (App.IsConnected)
                {
                    // Send hibernate request to helper (UWP can't execute shutdown directly)
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("Hibernate", true);
                    await App.SendMessageAsync(message);
                    Logger.Info("Hibernate request sent to helper");
                }
                else
                {
                    Logger.Warn("Cannot hibernate - helper not connected");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to hibernate: {ex.Message}");
            }
        }

        #endregion

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

        #region Fan Curve Graph

        private void InitializeFanCurveGraph()
        {
            if (FanCurveCanvas == null || fanCurveGraphInitialized)
                return;

            // Initialize with current values from property
            currentFanCurveValues = legionFanCurveGraph.GetCurveValues();

            // Create 10 control point ellipses
            for (int i = 0; i < 10; i++)
            {
                var ellipse = new Windows.UI.Xaml.Shapes.Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 0, 170, 255)),
                    Stroke = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White),
                    StrokeThickness = 2,
                    Tag = i
                };
                fanCurvePoints[i] = ellipse;
                FanCurveCanvas.Children.Add(ellipse);
            }

            fanCurveGraphInitialized = true;

            // Load saved preset selection
            LoadFanCurvePresetSetting();

            // Draw the graph
            DrawGridLines();
            UpdateFanCurveGraph();
        }

        private void FanCurvePresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isFanCurvePresetLoading) return;

            if (FanCurvePresetComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string presetName)
            {
                if (presetName == "Custom") return; // User manually selected Custom, no action needed

                if (FanCurvePresets.TryGetValue(presetName, out int[] presetValues))
                {
                    currentFanCurvePreset = presetName;
                    currentFanCurveValues = (int[])presetValues.Clone();
                    UpdateFanCurveGraph();

                    // Send to helper
                    legionFanCurveGraph?.SetCurveValuesDebounced(currentFanCurveValues);

                    // Save preset selection
                    SaveFanCurvePresetSetting(presetName);
                }
            }
        }

        private void SwitchToCustomPreset()
        {
            if (currentFanCurvePreset != "Custom")
            {
                currentFanCurvePreset = "Custom";
                isFanCurvePresetLoading = true;
                SelectPresetInComboBox("Custom");
                isFanCurvePresetLoading = false;
                SaveFanCurvePresetSetting("Custom");
            }
        }

        private void SelectPresetInComboBox(string presetName)
        {
            if (FanCurvePresetComboBox == null) return;
            foreach (ComboBoxItem item in FanCurvePresetComboBox.Items)
            {
                if (item.Tag is string tag && tag == presetName)
                {
                    FanCurvePresetComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void SaveFanCurvePresetSetting(string presetName)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["FanCurvePreset"] = presetName;
            }
            catch { }
        }

        private void LoadFanCurvePresetSetting()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("FanCurvePreset", out object saved) && saved is string presetName)
                {
                    currentFanCurvePreset = presetName;
                    isFanCurvePresetLoading = true;
                    SelectPresetInComboBox(presetName);
                    isFanCurvePresetLoading = false;
                }
            }
            catch { }
        }

        private void DrawGridLines()
        {
            if (FanCurveCanvas == null) return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Draw horizontal grid lines (at 25%, 50%, 75%)
            for (int i = 1; i <= 3; i++)
            {
                double y = height - (height * i * 0.25);
                var line = new Windows.UI.Xaml.Shapes.Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.ColorHelper.FromArgb(50, 255, 255, 255)),
                    StrokeThickness = 1
                };
                Canvas.SetZIndex(line, -1);
                FanCurveCanvas.Children.Add(line);
            }

            // Draw vertical grid lines (at 20%, 40%, 60%, 80%)
            for (int i = 1; i <= 4; i++)
            {
                double x = width * i * 0.2;
                var line = new Windows.UI.Xaml.Shapes.Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = height,
                    Stroke = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.ColorHelper.FromArgb(50, 255, 255, 255)),
                    StrokeThickness = 1
                };
                Canvas.SetZIndex(line, -1);
                FanCurveCanvas.Children.Add(line);
            }

            // Draw EC floor line after grid lines
            DrawECFloorLine();
        }

        private void DrawECFloorLine()
        {
            if (ECFloorPolyline == null || FanCurveCanvas == null) return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            var points = new Windows.UI.Xaml.Media.PointCollection();

            foreach (var (temp, floor) in ECFloorPoints)
            {
                // Map temperature to X position (10-100°C range)
                double x = (temp - 10.0) / 90.0 * width;
                // Map fan % to Y position (inverted)
                double y = height - (floor / 100.0 * height);
                points.Add(new Windows.Foundation.Point(x, y));
            }

            ECFloorPolyline.Points = points;
        }

        private void UpdateFanCurveGraph()
        {
            if (FanCurveCanvas == null || FanCurvePolyline == null || FanCurveFill == null)
                return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            var points = new Windows.UI.Xaml.Media.PointCollection();
            var fillPoints = new Windows.UI.Xaml.Media.PointCollection();

            // Legion Go temperature thresholds: 10, 20, 30, 40, 50, 60, 70, 80, 90, 100°C (FIXED by EC)
            // Map to 0-100% of width (10-100°C range = 90°C)
            for (int i = 0; i < 10; i++)
            {
                int temp = FanCurveTemperatures[i];
                double x = (temp - 10.0) / 90.0 * width; // Normalize 10-100 to 0-width
                double y = height - (currentFanCurveValues[i] / 100.0 * height);

                points.Add(new Windows.Foundation.Point(x, y));
                fillPoints.Add(new Windows.Foundation.Point(x, y));

                // Position control point
                if (fanCurvePoints[i] != null)
                {
                    Canvas.SetLeft(fanCurvePoints[i], x - 8); // Center the 16px ellipse
                    Canvas.SetTop(fanCurvePoints[i], y - 8);
                }
            }

            FanCurvePolyline.Points = points;

            // Add bottom corners for fill polygon
            fillPoints.Add(new Windows.Foundation.Point(width, height));
            fillPoints.Add(new Windows.Foundation.Point(0, height));
            FanCurveFill.Points = fillPoints;
        }

        private void UpdateTemperatureIndicator(int tempC)
        {
            if (TempIndicatorLine == null || FanCurveCanvas == null)
                return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Clamp temp to 10-100 range (Legion Go fan curve range, FIXED by EC)
            tempC = Math.Max(10, Math.Min(100, tempC));

            // Calculate X position (10-100°C range = 90°C span)
            double x = (tempC - 10.0) / 90.0 * width;

            TempIndicatorLine.X1 = x;
            TempIndicatorLine.X2 = x;
            TempIndicatorLine.Y1 = 0;
            TempIndicatorLine.Y2 = height;
            TempIndicatorLine.Visibility = Visibility.Visible;
        }

        private void OnFanCurveUpdated(int[] values)
        {
            if (values == null || values.Length != 10) return;

            currentFanCurveValues = values;
            UpdateFanCurveGraph();
        }

        private void OnCPUTempUpdated(int tempC)
        {
            // CPU temp is shown as reference only, fan sensor temp is used for graph indicator
            // (CPU temp is typically 10-17°C higher than fan sensor temp)
        }

        private void OnFanSensorTempUpdated(int tempC)
        {
            // Update temperature label (this is the temp the EC uses for fan curve)
            if (CurrentTempLabel != null)
            {
                CurrentTempLabel.Text = $"{tempC}°C";
            }
            // Update temperature indicator on graph (fan sensor temp matches the curve's X-axis)
            UpdateTemperatureIndicator(tempC);
        }

        private void OnFanRPMUpdated(int rpm)
        {
            if (FanRPMLabel != null)
            {
                FanRPMLabel.Text = $"{rpm} RPM";
            }

            // Update RPM indicator line on graph
            UpdateRPMIndicator(rpm);
        }

        private void UpdateRPMIndicator(int rpm)
        {
            if (RPMIndicatorLine == null || FanCurveCanvas == null)
                return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Convert RPM to percentage (max 7500 RPM for Legion Go EC scale)
            const int MAX_RPM = 7500;
            double percent = Math.Max(0, Math.Min(100, (double)rpm / MAX_RPM * 100));

            // Calculate Y position (inverted - 0% at bottom, 100% at top)
            double y = height - (percent / 100.0 * height);

            RPMIndicatorLine.X1 = 0;
            RPMIndicatorLine.X2 = width;
            RPMIndicatorLine.Y1 = y;
            RPMIndicatorLine.Y2 = y;
            RPMIndicatorLine.Visibility = Windows.UI.Xaml.Visibility.Visible;
        }

        private void FanCurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (fanCurveGraphInitialized)
            {
                // Clear old grid lines
                var toRemove = new System.Collections.Generic.List<Windows.UI.Xaml.UIElement>();
                foreach (var child in FanCurveCanvas.Children)
                {
                    if (child is Windows.UI.Xaml.Shapes.Line line && line != TempIndicatorLine && line != RPMIndicatorLine)
                    {
                        toRemove.Add(child);
                    }
                }
                foreach (var item in toRemove)
                {
                    FanCurveCanvas.Children.Remove(item);
                }

                DrawGridLines();
                UpdateFanCurveGraph();

                // Re-update temp indicator if we have a value (fan sensor temp is used for graph)
                if (legionFanSensorTemp != null && legionFanSensorTemp.Value > 0)
                {
                    UpdateTemperatureIndicator(legionFanSensorTemp.Value);
                }
            }
        }

        private void FanCurveCanvas_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (FanCurveCanvas == null) return;

            var point = e.GetCurrentPoint(FanCurveCanvas).Position;

            // Find the closest control point
            double minDist = double.MaxValue;
            int closestIndex = -1;

            for (int i = 0; i < 10; i++)
            {
                if (fanCurvePoints[i] == null) continue;

                double px = Canvas.GetLeft(fanCurvePoints[i]) + 8;
                double py = Canvas.GetTop(fanCurvePoints[i]) + 8;

                double dist = Math.Sqrt(Math.Pow(point.X - px, 2) + Math.Pow(point.Y - py, 2));
                if (dist < minDist && dist < 30) // 30px hit area
                {
                    minDist = dist;
                    closestIndex = i;
                }
            }

            if (closestIndex >= 0)
            {
                draggedPointIndex = closestIndex;
                isDraggingPoint = true;
                FanCurveCanvas.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void FanCurveCanvas_PointerMoved(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!isDraggingPoint || draggedPointIndex < 0 || FanCurveCanvas == null)
                return;

            var point = e.GetCurrentPoint(FanCurveCanvas).Position;
            double height = FanCurveCanvas.ActualHeight;

            // Calculate new fan speed (invert Y since 0 is at top)
            double fanSpeed = (1.0 - point.Y / height) * 100.0;

            // Enforce minimum fan speed for this temperature threshold
            int minSpeed = FanCurveMinSpeeds[draggedPointIndex];
            fanSpeed = Math.Max(minSpeed, Math.Min(100, fanSpeed));

            // Update the value
            currentFanCurveValues[draggedPointIndex] = (int)Math.Round(fanSpeed);

            // Redraw the graph
            UpdateFanCurveGraph();

            e.Handled = true;
        }

        private void FanCurveCanvas_PointerReleased(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (isDraggingPoint && FanCurveCanvas != null)
            {
                FanCurveCanvas.ReleasePointerCapture(e.Pointer);

                // Switch to Custom preset when manually dragging
                SwitchToCustomPreset();

                // Send the updated values to the helper (debounced)
                legionFanCurveGraph.SetCurveValuesDebounced(currentFanCurveValues);
            }

            draggedPointIndex = -1;
            isDraggingPoint = false;
            e.Handled = true;
        }

        #endregion

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

        #region TDP Custom Presets

        private void UseCustomTDPPresetsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (UseCustomTDPPresetsToggle == null) return;

            useCustomTDPPresets = UseCustomTDPPresetsToggle.IsOn;

            // Show/hide the presets list panel
            if (TDPPresetsListPanel != null)
            {
                TDPPresetsListPanel.Visibility = useCustomTDPPresets ? Visibility.Visible : Visibility.Collapsed;
            }

            // Update Lenovo modes panel visibility (only visible on Legion when custom presets enabled)
            UpdateUseLenovoModesPanelVisibility();

            // Save the setting
            SaveTdpPresetsSettings();

            // Rebuild the TDP Mode ComboBox with the appropriate items
            PopulateTdpModeComboBox();

            Logger.Info($"Custom TDP Presets: {(useCustomTDPPresets ? "enabled" : "disabled")}");
        }

        private void UseLenovoModesToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (UseLenovoModesToggle == null) return;

            useLenovoModes = UseLenovoModesToggle.IsOn;

            // Update built-in presets' LegionModeValue based on toggle
            // When "Use Lenovo modes" is ON: built-in presets use hardware modes (1, 2, 3)
            // When OFF: built-in presets use software TDP control (LegionModeValue = null)
            for (int i = 0; i < tdpPresets.Count; i++)
            {
                var preset = tdpPresets[i];
                if (preset.IsBuiltIn)
                {
                    if (useLenovoModes)
                    {
                        // Restore hardware mode values for built-in presets
                        // Quiet=1, Balanced=2, Performance=3
                        if (preset.Name == "Quiet") preset.LegionModeValue = 1;
                        else if (preset.Name == "Balanced") preset.LegionModeValue = 2;
                        else if (preset.Name == "Performance") preset.LegionModeValue = 3;
                    }
                    else
                    {
                        // Clear hardware mode - use software TDP control
                        preset.LegionModeValue = null;
                    }
                    tdpPresets[i] = preset;
                }
            }

            // Save the setting
            SaveTdpPresetsSettings();

            // Refresh the presets list to update edit button availability
            RefreshTdpPresetsList();

            // Rebuild the TDP Mode ComboBox
            PopulateTdpModeComboBox();

            Logger.Info($"Use Lenovo Modes: {(useLenovoModes ? "enabled" : "disabled")}");
        }

        /// <summary>
        /// Shows or hides the "Use Lenovo Modes" panel based on device type.
        /// Should be called after legionGoDetected is set.
        /// </summary>
        private void UpdateUseLenovoModesPanelVisibility()
        {
            if (UseLenovoModesPanel != null)
            {
                // Only show on Legion devices when custom presets are enabled
                bool isLegion = legionGoDetected?.Value == true;
                UseLenovoModesPanel.Visibility = isLegion && useCustomTDPPresets ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void LoadTdpPresetsSettings()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;

                // Load the use custom presets flag
                if (settings.Values.TryGetValue("TdpPresets_UseCustom", out object useCustomObj))
                {
                    useCustomTDPPresets = (bool)useCustomObj;
                }
                else
                {
                    useCustomTDPPresets = false;
                }

                // Load the use Lenovo modes flag (default: true for Legion devices)
                if (settings.Values.TryGetValue("TdpPresets_UseLenovoModes", out object useLenovoObj))
                {
                    useLenovoModes = (bool)useLenovoObj;
                }
                else
                {
                    useLenovoModes = true; // Default to using hardware modes
                }

                // Load the presets data
                if (settings.Values.TryGetValue("TdpPresets_Data", out object presetsJson) && presetsJson is string json)
                {
                    tdpPresets = Shared.Data.TdpPreset.FromJson(json);
                }

                // If no presets loaded or empty, use defaults
                if (tdpPresets == null || tdpPresets.Count == 0)
                {
                    tdpPresets = Shared.Data.TdpPreset.GetDefaultPresets();
                }

                // Update UI
                if (UseCustomTDPPresetsToggle != null)
                {
                    UseCustomTDPPresetsToggle.IsOn = useCustomTDPPresets;
                }

                if (UseLenovoModesToggle != null)
                {
                    UseLenovoModesToggle.IsOn = useLenovoModes;
                }

                if (TDPPresetsListPanel != null)
                {
                    TDPPresetsListPanel.Visibility = useCustomTDPPresets ? Visibility.Visible : Visibility.Collapsed;
                }

                // Update Lenovo modes panel visibility
                UpdateUseLenovoModesPanelVisibility();

                // Update the presets list display
                RefreshTdpPresetsList();

                Logger.Info($"Loaded TDP presets settings: useCustom={useCustomTDPPresets}, useLenovoModes={useLenovoModes}, presetCount={tdpPresets.Count}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading TDP presets settings: {ex.Message}");
                tdpPresets = Shared.Data.TdpPreset.GetDefaultPresets();
            }
        }

        private void SaveTdpPresetsSettings()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;

                settings.Values["TdpPresets_UseCustom"] = useCustomTDPPresets;
                settings.Values["TdpPresets_UseLenovoModes"] = useLenovoModes;
                settings.Values["TdpPresets_Data"] = Shared.Data.TdpPreset.ToJson(tdpPresets);

                Logger.Info($"Saved TDP presets settings: useCustom={useCustomTDPPresets}, useLenovoModes={useLenovoModes}, presetCount={tdpPresets.Count}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving TDP presets settings: {ex.Message}");
            }
        }

        private void RefreshTdpPresetsList()
        {
            if (TDPPresetsItemsControl != null)
            {
                TDPPresetsItemsControl.ItemsSource = null;
                TDPPresetsItemsControl.ItemsSource = tdpPresets;
            }
        }

        private void PopulateTdpModeComboBox()
        {
            if (TDPModeComboBox == null) return;

            // Remember current selection if possible
            int previousIndex = TDPModeComboBox.SelectedIndex;
            string previousName = null;
            if (previousIndex >= 0 && previousIndex < TDPModeComboBox.Items.Count)
            {
                var item = TDPModeComboBox.Items[previousIndex] as ComboBoxItem;
                previousName = item?.Content?.ToString();
            }

            TDPModeComboBox.Items.Clear();

            if (useCustomTDPPresets && tdpPresets != null && tdpPresets.Count > 0)
            {
                // Use custom presets
                for (int i = 0; i < tdpPresets.Count; i++)
                {
                    var preset = tdpPresets[i];
                    var item = new ComboBoxItem
                    {
                        Content = $"{preset.Name} ({preset.TdpWatts}W)",
                        Tag = i // Store index as tag for lookup
                    };
                    TDPModeComboBox.Items.Add(item);
                }

                // Add Custom mode at the end
                TDPModeComboBox.Items.Add(new ComboBoxItem { Content = "Custom", Tag = -1 });
            }
            else
            {
                // Use default hardcoded items
                TDPModeComboBox.Items.Add(new ComboBoxItem { Content = "Quiet", Tag = "1" });
                TDPModeComboBox.Items.Add(new ComboBoxItem { Content = "Balanced", Tag = "2" });
                TDPModeComboBox.Items.Add(new ComboBoxItem { Content = "Performance", Tag = "3" });
                TDPModeComboBox.Items.Add(new ComboBoxItem { Content = "Custom", Tag = "255" });
            }

            // Try to restore previous selection
            int newIndex = -1;
            if (!string.IsNullOrEmpty(previousName))
            {
                for (int i = 0; i < TDPModeComboBox.Items.Count; i++)
                {
                    var item = TDPModeComboBox.Items[i] as ComboBoxItem;
                    if (item?.Content?.ToString()?.StartsWith(previousName.Split(' ')[0]) == true)
                    {
                        newIndex = i;
                        break;
                    }
                }
            }

            // Default to Balanced (index 1) if no previous or couldn't find
            TDPModeComboBox.SelectedIndex = newIndex >= 0 ? newIndex : 1;
        }

        private void TDPPresetEditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Shared.Data.TdpPreset preset)
            {
                editingPreset = preset;
                editingPresetIndex = tdpPresets.IndexOf(preset);

                // Determine if TDP editing should be allowed
                // On Legion devices with "Use Lenovo modes" enabled, built-in presets use hardware modes
                // and TDP cannot be edited (hardware controls the TDP)
                bool isLegion = legionGoDetected?.Value == true;
                bool tdpEditingDisabled = isLegion && useLenovoModes && preset.IsBuiltIn;

                // Populate edit dialog
                if (EditPresetNameTextBox != null)
                {
                    EditPresetNameTextBox.Text = preset.Name;
                    EditPresetNameTextBox.IsEnabled = !preset.IsBuiltIn; // Can't rename built-in presets
                }

                if (EditPresetTDPNumberBox != null)
                {
                    EditPresetTDPNumberBox.Value = preset.TdpWatts;
                    EditPresetTDPNumberBox.Minimum = deviceTDPMin;
                    EditPresetTDPNumberBox.Maximum = deviceTDPMax;
                    EditPresetTDPNumberBox.IsEnabled = !tdpEditingDisabled;
                }

                // Show/hide TDP panel based on whether editing is allowed
                if (EditPresetTDPPanel != null)
                {
                    EditPresetTDPPanel.Visibility = tdpEditingDisabled ? Visibility.Collapsed : Visibility.Visible;
                }

                // TDP Boost checkbox
                if (EditPresetTDPBoostCheckBox != null)
                {
                    EditPresetTDPBoostCheckBox.IsChecked = preset.TdpBoostEnabled;
                    // TDP Boost only available when TDP is editable (software control)
                    EditPresetTDPBoostCheckBox.IsEnabled = !tdpEditingDisabled;
                }

                // Show/hide TDP Boost panel
                if (EditPresetTDPBoostPanel != null)
                {
                    EditPresetTDPBoostPanel.Visibility = tdpEditingDisabled ? Visibility.Collapsed : Visibility.Visible;
                }

                // Show edit dialog
                if (EditPresetDialog != null)
                {
                    EditPresetDialog.Visibility = Visibility.Visible;
                }

                Logger.Info($"Editing preset: {preset.Name} ({preset.TdpWatts}W, Boost={preset.TdpBoostEnabled}, TDPEditable={!tdpEditingDisabled})");
            }
        }

        private void TDPPresetDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Shared.Data.TdpPreset preset)
            {
                if (preset.IsBuiltIn)
                {
                    Logger.Warn($"Cannot delete built-in preset: {preset.Name}");
                    return;
                }

                tdpPresets.Remove(preset);
                SaveTdpPresetsSettings();
                RefreshTdpPresetsList();
                PopulateTdpModeComboBox();

                Logger.Info($"Deleted preset: {preset.Name}");
            }
        }

        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (NewPresetNameTextBox == null || NewPresetTDPNumberBox == null) return;

            string name = NewPresetNameTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                Logger.Warn("Cannot add preset: name is empty");
                return;
            }

            // Check for duplicate names
            string baseName = name;
            int suffix = 2;
            while (tdpPresets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{baseName} {suffix}";
                suffix++;
            }

            int tdpWatts = (int)NewPresetTDPNumberBox.Value;

            // Clamp to device limits
            tdpWatts = Math.Max(deviceTDPMin, Math.Min(deviceTDPMax, tdpWatts));

            // Get TDP Boost setting
            bool tdpBoostEnabled = NewPresetTDPBoostCheckBox?.IsChecked ?? false;

            var newPreset = new Shared.Data.TdpPreset(name, tdpWatts, null, false, tdpBoostEnabled);
            tdpPresets.Add(newPreset);

            SaveTdpPresetsSettings();
            RefreshTdpPresetsList();
            PopulateTdpModeComboBox();

            // Clear the input fields
            NewPresetNameTextBox.Text = "";
            NewPresetTDPNumberBox.Value = 30;
            if (NewPresetTDPBoostCheckBox != null)
            {
                NewPresetTDPBoostCheckBox.IsChecked = false;
            }

            Logger.Info($"Added new preset: {name} ({tdpWatts}W, Boost={tdpBoostEnabled})");
        }

        private void EditPresetCancelButton_Click(object sender, RoutedEventArgs e)
        {
            editingPreset = null;
            editingPresetIndex = -1;

            if (EditPresetDialog != null)
            {
                EditPresetDialog.Visibility = Visibility.Collapsed;
            }
        }

        private void EditPresetSaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (editingPreset == null || editingPresetIndex < 0 || editingPresetIndex >= tdpPresets.Count)
            {
                EditPresetCancelButton_Click(sender, e);
                return;
            }

            string newName = EditPresetNameTextBox?.Text?.Trim();
            int newTdp = (int)(EditPresetTDPNumberBox?.Value ?? editingPreset.TdpWatts);

            // Clamp to device limits
            newTdp = Math.Max(deviceTDPMin, Math.Min(deviceTDPMax, newTdp));

            // Update the preset
            var updatedPreset = tdpPresets[editingPresetIndex];
            if (!updatedPreset.IsBuiltIn && !string.IsNullOrEmpty(newName))
            {
                updatedPreset.Name = newName;
            }

            // Only update TDP and Boost if editing was enabled (not using Lenovo hardware modes for built-in)
            bool isLegion = legionGoDetected?.Value == true;
            bool tdpEditingDisabled = isLegion && useLenovoModes && updatedPreset.IsBuiltIn;
            if (!tdpEditingDisabled)
            {
                updatedPreset.TdpWatts = newTdp;
                updatedPreset.TdpBoostEnabled = EditPresetTDPBoostCheckBox?.IsChecked ?? false;
            }

            tdpPresets[editingPresetIndex] = updatedPreset;

            SaveTdpPresetsSettings();
            RefreshTdpPresetsList();
            PopulateTdpModeComboBox();

            Logger.Info($"Updated preset: {updatedPreset.Name} ({updatedPreset.TdpWatts}W, Boost={updatedPreset.TdpBoostEnabled})");

            // Close dialog
            EditPresetCancelButton_Click(sender, e);
        }

        private void ResetTDPPresetsButton_Click(object sender, RoutedEventArgs e)
        {
            tdpPresets = Shared.Data.TdpPreset.GetDefaultPresets();
            SaveTdpPresetsSettings();
            RefreshTdpPresetsList();
            PopulateTdpModeComboBox();

            Logger.Info("Reset TDP presets to defaults");
        }

        /// <summary>
        /// Gets the TDP value for the currently selected preset mode.
        /// Returns -1 if in Custom mode (slider controlled).
        /// </summary>
        private int GetCurrentPresetTdpValue()
        {
            if (TDPModeComboBox == null) return -1;

            int selectedIndex = TDPModeComboBox.SelectedIndex;
            if (selectedIndex < 0) return -1;

            if (useCustomTDPPresets && tdpPresets != null)
            {
                // Last item is always "Custom" mode
                if (selectedIndex >= tdpPresets.Count)
                {
                    return -1; // Custom mode
                }

                if (selectedIndex < tdpPresets.Count)
                {
                    return tdpPresets[selectedIndex].TdpWatts;
                }
            }
            else
            {
                // Default hardcoded values
                int[] defaultTdpValues = { 8, 15, 25 }; // Quiet, Balanced, Performance
                if (selectedIndex < defaultTdpValues.Length)
                {
                    return defaultTdpValues[selectedIndex];
                }
            }

            return -1; // Custom mode
        }

        /// <summary>
        /// Gets the Legion hardware mode value for the currently selected preset.
        /// Returns 255 (Custom) if no hardware mode mapping exists.
        /// </summary>
        private int GetCurrentPresetLegionMode()
        {
            if (TDPModeComboBox == null) return 255;

            int selectedIndex = TDPModeComboBox.SelectedIndex;
            if (selectedIndex < 0) return 255;

            if (useCustomTDPPresets && tdpPresets != null)
            {
                // Last item is always "Custom" mode
                if (selectedIndex >= tdpPresets.Count)
                {
                    return 255; // Custom mode
                }

                if (selectedIndex < tdpPresets.Count)
                {
                    var preset = tdpPresets[selectedIndex];
                    return preset.LegionModeValue ?? 255;
                }
            }
            else
            {
                // Default hardcoded Legion mode values
                int[] defaultLegionModes = { 1, 2, 3, 255 }; // Quiet, Balanced, Performance, Custom
                if (selectedIndex < defaultLegionModes.Length)
                {
                    return defaultLegionModes[selectedIndex];
                }
            }

            return 255; // Custom mode
        }

        /// <summary>
        /// Gets the TDP Boost setting for the currently selected preset.
        /// Returns null if in Custom mode (user controls TDP Boost toggle directly).
        /// </summary>
        private bool? GetCurrentPresetTdpBoost()
        {
            if (TDPModeComboBox == null) return null;

            int selectedIndex = TDPModeComboBox.SelectedIndex;
            if (selectedIndex < 0) return null;

            if (useCustomTDPPresets && tdpPresets != null)
            {
                // Last item is always "Custom" mode
                if (selectedIndex >= tdpPresets.Count)
                {
                    return null; // Custom mode - user controls TDP Boost directly
                }

                if (selectedIndex < tdpPresets.Count)
                {
                    return tdpPresets[selectedIndex].TdpBoostEnabled;
                }
            }

            // Default presets don't have TDP Boost setting
            return null;
        }

        /// <summary>
        /// Checks if the current TDP Mode selection is "Custom" (slider-controlled).
        /// </summary>
        private bool IsCustomTdpModeSelected()
        {
            if (TDPModeComboBox == null) return true;
            return IsCustomTdpModeIndex(TDPModeComboBox.SelectedIndex);
        }

        /// <summary>
        /// Checks if a given TDP mode index is "Custom" (slider-controlled).
        /// </summary>
        private bool IsCustomTdpModeIndex(int index)
        {
            if (index < 0) return true;

            if (useCustomTDPPresets && tdpPresets != null)
            {
                // Last item is always "Custom" mode
                return index >= tdpPresets.Count;
            }
            else
            {
                // Custom is the last item (index 3)
                return index == 3;
            }
        }

        /// <summary>
        /// Gets the TDPModeComboBox index for Custom mode.
        /// </summary>
        private int GetCustomTdpModeIndex()
        {
            if (useCustomTDPPresets && tdpPresets != null)
            {
                // Custom is after all presets
                return tdpPresets.Count;
            }
            else
            {
                // Custom is index 3 (Quiet=0, Balanced=1, Performance=2, Custom=3)
                return 3;
            }
        }

        #endregion TDP Custom Presets

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

        #region TDP Boost Handlers

        private void TDPBoostToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (TDPBoostToggle == null) return;
            if (isApplyingHelperUpdate) return;
            // Skip during mode changes - don't save forced-off state
            if (isUpdatingTDPMode) return;

            Logger.Info($"TDP Boost toggled to: {TDPBoostToggle.IsOn}");

            // Send to helper
            tdpBoostEnabled?.SetValue(TDPBoostToggle.IsOn);

            // Save to local settings for persistence across widget restarts
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostEnabled"] = TDPBoostToggle.IsOn;

            // When enabling boost, also send current SPPT/FPPT values to ensure helper has them
            if (TDPBoostToggle.IsOn)
            {
                int spptBoost = (int)(TDPBoostSPPTSlider?.Value ?? 1);
                int fpptBoost = (int)(TDPBoostFPPTSlider?.Value ?? 3);
                tdpBoostSPPT?.SetValue(spptBoost);
                tdpBoostFPPT?.SetValue(fpptBoost);
                Logger.Info($"TDP Boost enabled - sent SPPT={spptBoost}W, FPPT={fpptBoost}W to helper");
            }

            // Save to profile if not loading
            if (!isLoadingProfile && !isSwitchingProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        private void TDPBoostSPPTSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPBoostSettings) return;
            if (TDPBoostSPPTSlider == null) return;

            int spptBoost = (int)Math.Round(e.NewValue);
            Logger.Info($"TDP Boost SPPT changed to: {spptBoost}W");

            if (TDPBoostSPPTValue != null)
            {
                TDPBoostSPPTValue.Text = $"{spptBoost}W";
            }

            // Send to helper
            tdpBoostSPPT?.SetValue(spptBoost);

            // Save to local settings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostSPPT"] = spptBoost;
        }

        private void TDPBoostFPPTSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPBoostSettings) return;
            if (TDPBoostFPPTSlider == null) return;

            int fpptBoost = (int)Math.Round(e.NewValue);
            Logger.Info($"TDP Boost FPPT changed to: {fpptBoost}W");

            if (TDPBoostFPPTValue != null)
            {
                TDPBoostFPPTValue.Text = $"{fpptBoost}W";
            }

            // Send to helper
            tdpBoostFPPT?.SetValue(fpptBoost);

            // Save to local settings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostFPPT"] = fpptBoost;
        }

        private void LoadTDPBoostSettings()
        {
            isLoadingTDPBoostSettings = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load TDP Boost enabled state (default OFF)
                if (settings.Values.TryGetValue("TDPBoostEnabled", out object enabledObj) && enabledObj is bool enabled)
                {
                    if (TDPBoostToggle != null)
                    {
                        TDPBoostToggle.IsOn = enabled;
                    }
                    tdpBoostEnabled?.SetValue(enabled);
                    Logger.Info($"TDP Boost enabled state loaded from settings: {enabled}");
                }

                // Load SPPT boost (default 1W)
                int spptBoost = 1; // Default
                if (settings.Values.TryGetValue("TDPBoostSPPT", out object spptObj) && spptObj != null)
                {
                    try
                    {
                        spptBoost = Convert.ToInt32(spptObj);
                    }
                    catch
                    {
                        spptBoost = 1;
                    }
                }
                if (TDPBoostSPPTSlider != null)
                {
                    TDPBoostSPPTSlider.Value = spptBoost;
                }
                if (TDPBoostSPPTValue != null)
                {
                    TDPBoostSPPTValue.Text = $"{spptBoost}W";
                }
                tdpBoostSPPT?.SetValue(spptBoost);
                // Ensure value is saved (in case it was missing or converted)
                settings.Values["TDPBoostSPPT"] = spptBoost;

                // Load FPPT boost (default 3W)
                int fpptBoost = 3; // Default
                if (settings.Values.TryGetValue("TDPBoostFPPT", out object fpptObj) && fpptObj != null)
                {
                    try
                    {
                        fpptBoost = Convert.ToInt32(fpptObj);
                    }
                    catch
                    {
                        fpptBoost = 3;
                    }
                }
                if (TDPBoostFPPTSlider != null)
                {
                    TDPBoostFPPTSlider.Value = fpptBoost;
                }
                if (TDPBoostFPPTValue != null)
                {
                    TDPBoostFPPTValue.Text = $"{fpptBoost}W";
                }
                tdpBoostFPPT?.SetValue(fpptBoost);
                // Ensure value is saved (in case it was missing or converted)
                settings.Values["TDPBoostFPPT"] = fpptBoost;

                Logger.Info($"TDP Boost settings loaded - SPPT: {spptBoost}W, FPPT: {fpptBoost}W");
            }
            finally
            {
                isLoadingTDPBoostSettings = false;
            }
        }

        private void TDPBoostEnabled_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // NOTE: This callback is triggered when helper syncs TDPBoostEnabled.
            // We do NOT update the toggle from this callback because:
            // 1. The widget (LocalSettings) is the source of truth for this setting
            // 2. The helper doesn't persist TDPBoostEnabled, so it always sends False on fresh start
            // 3. Profile loading explicitly sets the toggle in LoadProfileSettings()
            //
            // If boost is enabled, we just need to ensure SPPT/FPPT values are sent to helper.
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (TDPBoostToggle == null || tdpBoostEnabled == null) return;

                // Only send SPPT/FPPT to helper if boost is currently enabled in the UI
                // (regardless of what the helper sent us)
                if (TDPBoostToggle.IsOn)
                {
                    int spptBoost = (int)(TDPBoostSPPTSlider?.Value ?? 1);
                    int fpptBoost = (int)(TDPBoostFPPTSlider?.Value ?? 3);
                    tdpBoostSPPT?.SetValue(spptBoost);
                    tdpBoostFPPT?.SetValue(fpptBoost);
                    Logger.Debug($"TDP Boost PropertyChanged - ensuring SPPT={spptBoost}W, FPPT={fpptBoost}W sent to helper");
                }
            });
        }

        #endregion

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

        #region Advanced (Core Parking & Affinity)

        private bool isAdvancedExpanded = false;
        private bool isLoadingCPUCoreConfig = false;
        private int totalPCores = 3;  // Default for Z2E
        private int totalECores = 5;  // Default for Z2E
        private int totalCores = 8;   // Total logical cores
        private int activePCores = 3;
        private int activeECores = 5;
        private int parkedCores = 0;  // Number of cores to park (0 = all active)
        private bool isHybridCPU = false;
        private bool isLoadingCoreParking = false;

        private void AdvancedExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isAdvancedExpanded = !isAdvancedExpanded;

            if (AdvancedContent != null)
            {
                AdvancedContent.Visibility = isAdvancedExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (AdvancedExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                AdvancedExpandIcon.Glyph = isAdvancedExpanded ? "\uE70E" : "\uE70D";
            }

            // Load power plans when expanding for the first time
            if (isAdvancedExpanded && availablePowerPlans.Count == 0)
            {
                LoadPowerPlans();
            }
        }

        private void CoreParkingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingCoreParking) return;
            if (CoreParkingComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int activeCores))
                {
                    parkedCores = totalCores - activeCores;
                    UpdateCoreParkingDescription(activeCores);
                    UpdateCPUCoreConfigSummary();
                    SaveCoreParkingToStorage();
                    SendCoreParkingToHelper(activeCores);
                    Logger.Info($"Core parking changed to: {activeCores} active cores ({parkedCores} parked)");
                }
            }
        }

        private void UpdateCoreParkingDescription(int activeCores)
        {
            if (CoreParkingDescription != null)
            {
                if (activeCores >= totalCores)
                {
                    CoreParkingDescription.Text = "All cores active";
                }
                else
                {
                    CoreParkingDescription.Text = $"{totalCores - activeCores} cores parked";
                }
            }
        }

        private void SetupCoreParkingUI()
        {
            isLoadingCoreParking = true;
            try
            {
                // Get total logical processor count
                totalCores = Environment.ProcessorCount;

                if (CoreParkingComboBox != null)
                {
                    CoreParkingComboBox.Items.Clear();

                    // Add "All" option first
                    var allItem = new ComboBoxItem { Content = $"All ({totalCores})", Tag = totalCores.ToString() };
                    CoreParkingComboBox.Items.Add(allItem);

                    // Add options for reducing cores (by 2s for larger counts)
                    int step = totalCores > 8 ? 2 : 1;
                    for (int i = totalCores - step; i >= 2; i -= step)
                    {
                        var item = new ComboBoxItem { Content = i.ToString(), Tag = i.ToString() };
                        CoreParkingComboBox.Items.Add(item);
                    }

                    // Load saved setting
                    LoadCoreParkingFromStorage();
                }

                Logger.Info($"Core Parking UI setup: {totalCores} total cores");
            }
            finally
            {
                isLoadingCoreParking = false;
            }
        }

        private void SaveCoreParkingToStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["CoreParkingActiveCores"] = totalCores - parkedCores;
                Logger.Info($"Saved core parking: {totalCores - parkedCores} active");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save core parking: {ex.Message}");
            }
        }

        private void LoadCoreParkingFromStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                int activeCores = totalCores; // Default to all active

                if (settings.Values.TryGetValue("CoreParkingActiveCores", out object val) && val is int saved)
                {
                    activeCores = Math.Min(saved, totalCores); // Clamp to current max
                }

                parkedCores = totalCores - activeCores;

                // Select the matching item
                if (CoreParkingComboBox != null)
                {
                    foreach (ComboBoxItem item in CoreParkingComboBox.Items)
                    {
                        if (item.Tag is string tagStr && int.TryParse(tagStr, out int tagVal) && tagVal == activeCores)
                        {
                            CoreParkingComboBox.SelectedItem = item;
                            break;
                        }
                    }

                    // If no match, select first (all cores)
                    if (CoreParkingComboBox.SelectedItem == null && CoreParkingComboBox.Items.Count > 0)
                    {
                        CoreParkingComboBox.SelectedIndex = 0;
                    }
                }

                UpdateCoreParkingDescription(activeCores);
                Logger.Info($"Loaded core parking: {activeCores} active");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load core parking: {ex.Message}");
            }
        }

        private void SendCoreParkingToHelper(int activeCores)
        {
            // Calculate percentage for CPMAXCORES
            // activeCores / totalCores * 100 = percentage of cores that can be unparked
            int percent = (int)Math.Ceiling((double)activeCores / totalCores * 100);
            percent = Math.Clamp(percent, 1, 100); // At least 1%, max 100%

            if (coreParkingPercent != null)
            {
                coreParkingPercent.SetValue(percent);
                Logger.Info($"Core parking: set {percent}% ({activeCores}/{totalCores} cores)");
            }
        }

        private void PCoreCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingCPUCoreConfig) return;
            if (PCoreCountComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int count))
                {
                    // Prevent both P-Cores and E-Cores from being 0
                    if (count == 0 && activeECores == 0)
                    {
                        Logger.Warn("Cannot disable both P-Cores and E-Cores, reverting selection");
                        // Revert to previous value
                        isLoadingCPUCoreConfig = true;
                        UpdatePCoreComboBox();
                        isLoadingCPUCoreConfig = false;
                        return;
                    }

                    activePCores = count;
                    UpdateCPUCoreConfigSummary();
                    SaveCPUCoreConfigToStorage();
                    SendCPUCoreConfigToHelper();
                    if (SaveCPUAffinity)
                    {
                        SaveCurrentSettingsToProfile(currentProfileName);
                    }
                    Logger.Info($"P-Core count changed to: {activePCores}");
                }
            }
        }

        private void ECoreCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingCPUCoreConfig) return;
            if (ECoreCountComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int count))
                {
                    // Prevent both P-Cores and E-Cores from being 0
                    if (count == 0 && activePCores == 0)
                    {
                        Logger.Warn("Cannot disable both P-Cores and E-Cores, reverting selection");
                        // Revert to previous value
                        isLoadingCPUCoreConfig = true;
                        UpdateECoreComboBox();
                        isLoadingCPUCoreConfig = false;
                        return;
                    }

                    activeECores = count;
                    UpdateCPUCoreConfigSummary();
                    SaveCPUCoreConfigToStorage();
                    SendCPUCoreConfigToHelper();
                    if (SaveCPUAffinity)
                    {
                        SaveCurrentSettingsToProfile(currentProfileName);
                    }
                    Logger.Info($"E-Core count changed to: {activeECores}");
                }
            }
        }

        private void SendCPUCoreConfigToHelper()
        {
            if (cpuCoreActiveConfig != null && isHybridCPU)
            {
                // Send affinity config
                string configString = $"{activePCores},{activeECores}";
                cpuCoreActiveConfig.SetValue(configString);
                Logger.Info($"Sent CPU core config to helper: {configString}");

                // Also send core parking percentage based on total active cores
                // For hybrid: active cores = activePCores threads + activeECores threads
                // Assuming SMT: P-Cores have 2 threads, E-Cores have 1 thread (AMD Z2E)
                int activeThreads = (activePCores * 2) + activeECores;
                int percent = (int)Math.Ceiling((double)activeThreads / totalCores * 100);
                percent = Math.Clamp(percent, 1, 100);

                if (coreParkingPercent != null)
                {
                    coreParkingPercent.SetValue(percent);
                    Logger.Info($"Core parking: set {percent}% ({activeThreads}/{totalCores} threads)");
                }
            }
        }

        private void ForceParkModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (ForceParkModeToggle == null) return;
            if (isLoadingCPUCoreConfig) return;

            bool enabled = ForceParkModeToggle.IsOn;
            Logger.Info($"Force Park Mode toggled to: {enabled}");

            // Send to helper
            forceParkMode?.SetValue(enabled);

            // Save to local settings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["ForceParkMode"] = enabled;
        }

        private void ForceDefaultGameProfileToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (ForceDefaultGameProfileToggle == null) return;

            bool enabled = ForceDefaultGameProfileToggle.IsOn;
            Logger.Info($"Force Default Game Profile toggled to: {enabled}");

            // Send to helper
            forceDefaultGameProfile?.SetValue(enabled);

            // Save to local settings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["ForceDefaultGameProfile"] = enabled;
        }

        #region Debug Panel Handlers

        private void DebugExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isDebugExpanded = !isDebugExpanded;

            if (DebugContent != null)
            {
                DebugContent.Visibility = isDebugExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (DebugExpandIcon != null)
            {
                DebugExpandIcon.Glyph = isDebugExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private bool isThemeInitialized = false;

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isThemeInitialized) return; // Don't save until initial load completes

            if (ThemeComboBox?.SelectedItem is ComboBoxItem item)
            {
                string themeName = item.Content?.ToString() ?? "Default";
                ApplyTheme(themeName);
                SaveThemeSetting(themeName);
            }
        }

        private void ApplyTheme(string themeName)
        {
            if (!WidgetThemes.TryGetValue(themeName, out var theme))
            {
                Logger.Warn($"Theme '{themeName}' not found, using Default");
                theme = WidgetThemes["Default"];
                themeName = "Default";
            }

            currentThemeName = themeName;
            Logger.Info($"Applying theme: {themeName}");

            // Update page background
            this.Background = new SolidColorBrush(theme.PageBackground);
            widgetDarkThemeBrush = new SolidColorBrush(theme.PageBackground);

            // Update resource brushes (for new elements)
            try
            {
                Resources["PageBackgroundBrush"] = new SolidColorBrush(theme.PageBackground);
                Resources["CardBackgroundBrush"] = new SolidColorBrush(theme.CardBackground);
                Resources["CardBorderBrush"] = new SolidColorBrush(theme.CardBorder);
                Resources["ButtonBackground"] = new SolidColorBrush(theme.ButtonBackground);
                Resources["ButtonBorderBrush"] = new SolidColorBrush(theme.ButtonBorder);
                Resources["TileOffBackground"] = new SolidColorBrush(theme.TileOff);
                Resources["TileOnBackground"] = new SolidColorBrush(theme.TileOn);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error updating theme resources: {ex.Message}");
            }

            // Manually update existing elements (StaticResource doesn't update at runtime)
            try
            {
                var cardBgBrush = new SolidColorBrush(theme.CardBackground);
                var cardBorderBrush = new SolidColorBrush(theme.CardBorder);
                var accentBrush = new SolidColorBrush(theme.AccentColor);
                var textSecondaryBrush = new SolidColorBrush(theme.TextSecondary);

                // Update all Border elements (cards)
                ApplyThemeToVisualTree(this, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);

                Logger.Info($"Theme '{themeName}' applied to visual tree");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying theme to visual tree: {ex.Message}");
            }
        }

        private void ApplyThemeToVisualTree(DependencyObject parent, ThemeColors theme,
            SolidColorBrush cardBgBrush, SolidColorBrush cardBorderBrush,
            SolidColorBrush accentBrush, SolidColorBrush textSecondaryBrush)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // Update Border elements (cards use CardStyle with specific properties)
                if (child is Border border)
                {
                    // Check if this looks like a card (has corner radius and padding typical of CardStyle)
                    // Skip borders with LinearGradientBrush backgrounds (custom gradients for "smart" features like DGP card)
                    if (border.CornerRadius.TopLeft == 8 && border.Padding.Left == 12 &&
                        !(border.Background is LinearGradientBrush))
                    {
                        border.Background = cardBgBrush;
                        border.BorderBrush = cardBorderBrush;
                    }
                }

                // Update accent-colored TextBlocks (section headers, card values)
                if (child is TextBlock textBlock)
                {
                    if (textBlock.Foreground is SolidColorBrush brush)
                    {
                        // Check for cyan accent color (#00C8FF) - update to new accent
                        if (brush.Color.R == 0 && brush.Color.G == 200 && brush.Color.B == 255)
                        {
                            textBlock.Foreground = accentBrush;
                        }
                        // Check for secondary text color (#A0A0A0)
                        else if (brush.Color.R == 160 && brush.Color.G == 160 && brush.Color.B == 160)
                        {
                            textBlock.Foreground = textSecondaryBrush;
                        }
                    }
                }

                // Recurse into children
                ApplyThemeToVisualTree(child, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            }
        }

        private async Task ApplyThemeOnLoadAsync(string themeName)
        {
            // Wait for UI to fully initialize
            await Task.Delay(100);

            try
            {
                // Set ComboBox selection (isThemeInitialized is still false, so this won't trigger save)
                if (ThemeComboBox != null)
                {
                    for (int i = 0; i < ThemeComboBox.Items.Count; i++)
                    {
                        if (ThemeComboBox.Items[i] is ComboBoxItem item && item.Content?.ToString() == themeName)
                        {
                            ThemeComboBox.SelectedIndex = i;
                            break;
                        }
                    }
                }

                ApplyTheme(themeName);

                // Apply to all tabs to prevent flash when switching
                ApplyThemeToCurrentTab();
            }
            finally
            {
                // Now allow saves on future changes
                isThemeInitialized = true;
            }
        }

        private void SaveThemeSetting(string themeName)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values["WidgetTheme"] = themeName;
                Logger.Info($"Theme setting saved: {themeName}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save theme setting: {ex.Message}");
            }
        }

        private void LoadThemeSetting()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("WidgetTheme", out var saved) && saved is string themeName)
                {
                    currentThemeName = themeName;
                    Logger.Info($"Theme loaded from settings: {themeName}");

                    // Defer visual updates until UI is fully ready
                    _ = ApplyThemeOnLoadAsync(themeName);
                }
                else
                {
                    // No saved theme - mark as initialized so user can save their choice
                    _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                    {
                        isThemeInitialized = true;
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load theme setting: {ex.Message}");
                isThemeInitialized = true; // Allow saves even on error
            }
        }

        private bool isAboutExpanded = false;

        private void AboutExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isAboutExpanded = !isAboutExpanded;

            if (AboutContent != null)
            {
                AboutContent.Visibility = isAboutExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (AboutExpandIcon != null)
            {
                AboutExpandIcon.Glyph = isAboutExpanded ? "\uE70E" : "\uE70D";
            }

            // Update version text dynamically
            if (isAboutExpanded && AboutVersionText != null)
            {
                try
                {
                    var version = Windows.ApplicationModel.Package.Current.Id.Version;
                    AboutVersionText.Text = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                }
                catch
                {
                    // Keep default version text
                }
            }
        }

        private async void DonateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Send message to helper to launch URL (Game Bar blocks direct URL launching)
                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("LaunchUrl", "https://paypal.me/corando98");
                    await App.SendMessageAsync(message);
                    Logger.Info("Sent LaunchUrl request to helper");
                }
                else
                {
                    Logger.Warn("Cannot launch donate URL - no connection to helper");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send donate link request: {ex.Message}");
            }
        }

        private async void RestartHelperButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RestartHelperButton.IsEnabled = false;
                RestartHelperButton.Content = "Restarting...";

                // Send exit command to helper via IPC
                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("ExitHelper", true);

                    Logger.Info("Sending ExitHelper command to helper");
                    var response = await App.SendMessageAsync(message);

                    if (response != null)
                    {
                        Logger.Info("Helper acknowledged exit command");
                    }

                    // Disconnect the pipe so we can detect when helper is truly gone
                    App.PipeClient?.Disconnect();
                }

                // Wait for helper to exit and release mutex
                // Helper waits 3 seconds before force-killing, so we wait 4 seconds to be safe
                Logger.Info("Waiting for helper to exit...");
                await Task.Delay(4000);

                // Verify helper has disconnected
                if (App.IsConnected)
                {
                    Logger.Warn("Helper still connected after exit command - forcing disconnect");
                    App.PipeClient?.Disconnect();
                    await Task.Delay(1000);
                }

                // Launch new helper instance
                Logger.Info("Launching new helper instance");
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();

                // Give the helper a moment to start its pipe server, then try to reconnect
                await Task.Delay(1000);
                Logger.Info("Attempting to reconnect to helper via Named Pipe");
                _ = TryConnectPipeAsync();

                await Task.Delay(1500);
                RestartHelperButton.Content = "Restart Helper";
                RestartHelperButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to restart helper: {ex.Message}");
                RestartHelperButton.Content = "Restart Helper";
                RestartHelperButton.IsEnabled = true;
            }
        }

        private async void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportLogsButton.IsEnabled = false;
                ExportLogsButton.Content = "Exporting...";

                // Send export logs command to helper via IPC
                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("ExportLogs", true);

                    Logger.Info("Sending ExportLogs command to helper");
                    var response = await App.SendMessageAsync(message);

                    if (response != null)
                    {
                        bool success = false;
                        if (response.TryGetValue("Success", out object successObj) && successObj is bool successVal)
                            success = successVal;

                        if (success)
                        {
                            var path = response.TryGetValue("Path", out object pathObj) ? pathObj as string : "Desktop";
                            Logger.Info($"Logs exported successfully to: {path}");
                            ExportLogsButton.Content = "Exported!";
                        }
                        else
                        {
                            var error = response.TryGetValue("Error", out object errorObj) ? errorObj as string : "Unknown error";
                            Logger.Error($"Export logs failed: {error}");
                            ExportLogsButton.Content = "Export Failed";
                        }
                    }
                    else
                    {
                        Logger.Error("Export logs request failed - no response");
                        ExportLogsButton.Content = "Export Failed";
                    }
                }
                else
                {
                    Logger.Error("Cannot export logs - no connection to helper");
                    ExportLogsButton.Content = "No Helper";
                }

                await Task.Delay(2000);
                ExportLogsButton.Content = "Export Logs";
                ExportLogsButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export logs: {ex.Message}");
                ExportLogsButton.Content = "Export Failed";
                await Task.Delay(2000);
                ExportLogsButton.Content = "Export Logs";
                ExportLogsButton.IsEnabled = true;
            }
        }

        private async void KillGoTweaksButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Kill GoTweaks requested by user");

                // Send exit command to helper using all available methods
                bool exitSent = false;

                // Try via Named Pipe
                if (App.PipeClient?.IsConnected == true)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("ExitHelper", true);
                    Logger.Info("Sending ExitHelper via Named Pipe");
                    await App.SendMessageAsync(message);
                    exitSent = true;
                }
                // Not connected - try temporary pipe connection
                else
                {
                    Logger.Info("Not connected - attempting temporary pipe connection for ExitHelper");
                    try
                    {
                        using (var tempPipe = new System.IO.Pipes.NamedPipeClientStream(".", "GoTweaksHelper", System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous))
                        {
                            var connectTask = tempPipe.ConnectAsync(2000);
                            if (await Task.WhenAny(connectTask, Task.Delay(2500)) == connectTask)
                            {
                                using (var writer = new System.IO.StreamWriter(tempPipe, System.Text.Encoding.UTF8, 4096, leaveOpen: true))
                                {
                                    writer.AutoFlush = true;
                                    await writer.WriteLineAsync("{\"RequestId\":0,\"ExitHelper\":true}");
                                }
                                Logger.Info("Sent ExitHelper via temporary pipe connection");
                                exitSent = true;
                            }
                        }
                    }
                    catch (Exception pipeEx)
                    {
                        Logger.Warn($"Temporary pipe connection failed: {pipeEx.Message}");
                    }
                }

                if (exitSent)
                {
                    // Give helper time to exit (helper waits 3 seconds before force-killing)
                    Logger.Info("Waiting for helper to exit...");
                    await Task.Delay(4000);
                }
                else
                {
                    Logger.Warn("Could not send ExitHelper - helper may still be running");
                }

                // Exit the widget application
                Application.Current.Exit();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to kill GoTweaks: {ex.Message}");
                // Still try to exit even if helper communication failed
                Application.Current.Exit();
            }
        }

        /// <summary>
        /// Compares two version strings (e.g., "v0.3.902" vs "v0.3.1001.0").
        /// Returns true if latestVersion is newer than currentVersion.
        /// </summary>
        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            // Strip 'v' prefix if present
            var latest = latestVersion.TrimStart('v', 'V');
            var current = currentVersion.TrimStart('v', 'V');

            // Split into parts
            var latestParts = latest.Split('.');
            var currentParts = current.Split('.');

            // Compare each part numerically
            int maxLength = Math.Max(latestParts.Length, currentParts.Length);
            for (int i = 0; i < maxLength; i++)
            {
                int latestNum = 0;
                int currentNum = 0;

                if (i < latestParts.Length && int.TryParse(latestParts[i], out int lp))
                    latestNum = lp;
                if (i < currentParts.Length && int.TryParse(currentParts[i], out int cp))
                    currentNum = cp;

                if (latestNum > currentNum)
                    return true;
                if (latestNum < currentNum)
                    return false;
            }

            return false; // Versions are equal
        }

        private async void CheckForUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckForUpdateButton.IsEnabled = false;
                CheckForUpdateButton.Content = "Checking...";
                UpdateStatusText.Visibility = Visibility.Visible;
                UpdateStatusText.Text = "Checking for updates...";
                UpdateButton.Visibility = Visibility.Collapsed;
                _pendingUpdateZipUrl = null;
                _pendingUpdateVersion = null;

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "GoTweaks-UpdateChecker");
                    var response = await httpClient.GetStringAsync("https://api.github.com/repos/corando98/GoTweaks/releases/latest");

                    // Parse JSON response using Windows.Data.Json
                    var jsonObject = Windows.Data.Json.JsonObject.Parse(response);
                    var latestVersion = jsonObject.GetNamedString("tag_name", "");

                    // Get current version from package
                    var packageVersion = Package.Current.Id.Version;
                    var currentVersion = $"v{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";

                    Logger.Info($"Update check: current={currentVersion}, latest={latestVersion}");

                    if (!string.IsNullOrEmpty(latestVersion) && IsNewerVersion(latestVersion, currentVersion))
                    {
                        // Find the .zip asset download URL
                        string zipUrl = null;
                        if (jsonObject.ContainsKey("assets"))
                        {
                            var assets = jsonObject.GetNamedArray("assets");
                            foreach (var asset in assets)
                            {
                                var assetObj = asset.GetObject();
                                var name = assetObj.GetNamedString("name", "");
                                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    zipUrl = assetObj.GetNamedString("browser_download_url", "");
                                    break;
                                }
                            }
                        }

                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.LimeGreen);
                        UpdateStatusText.Text = $"New version available: {latestVersion}\nCurrent: {currentVersion}";

                        if (!string.IsNullOrEmpty(zipUrl))
                        {
                            _pendingUpdateZipUrl = zipUrl;
                            _pendingUpdateVersion = latestVersion;
                            UpdateButton.Visibility = Visibility.Visible;
                            Logger.Info($"Update zip URL found: {zipUrl}");
                        }
                        else
                        {
                            UpdateStatusText.Text += "\n(No zip asset found in release)";
                            Logger.Warn("No zip asset found in latest release");
                        }
                    }
                    else
                    {
                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
                        UpdateStatusText.Text = $"You're up to date! ({currentVersion})";
                    }
                }

                CheckForUpdateButton.Content = "Check for Update";
                CheckForUpdateButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to check for update: {ex.Message}");
                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                UpdateStatusText.Text = $"Failed to check for updates: {ex.Message}";
                CheckForUpdateButton.Content = "Check for Update";
                CheckForUpdateButton.IsEnabled = true;
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pendingUpdateZipUrl))
            {
                Logger.Warn("Update clicked but no pending update URL");
                return;
            }

            try
            {
                UpdateButton.IsEnabled = false;
                UpdateButton.Content = "Downloading...";
                UpdateStatusText.Text = $"Downloading {_pendingUpdateVersion}...";

                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("Command", (int)Shared.Enums.Command.Set);
                    message.Add("Function", (int)Shared.Enums.Function.InstallUpdate);
                    message.Add("Content", _pendingUpdateZipUrl);
                    var result = await App.SendMessageAsync(message);

                    if (result != null)
                    {
                        if (result.TryGetValue("UpdateStatus", out object status))
                        {
                            var statusStr = status?.ToString() ?? "";
                            if (statusStr == "Installing")
                            {
                                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.LimeGreen);
                                UpdateStatusText.Text = "Installing update... Please follow the installer prompts.";
                                UpdateButton.Content = "Installing...";
                            }
                            else if (statusStr.StartsWith("Error"))
                            {
                                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                                UpdateStatusText.Text = statusStr;
                                UpdateButton.Content = "Update";
                                UpdateButton.IsEnabled = true;
                            }
                        }
                    }
                    else
                    {
                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                        UpdateStatusText.Text = "Failed to communicate with helper";
                        UpdateButton.Content = "Update";
                        UpdateButton.IsEnabled = true;
                    }
                }
                else
                {
                    UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                    UpdateStatusText.Text = "Helper not connected";
                    UpdateButton.Content = "Update";
                    UpdateButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start update: {ex.Message}");
                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                UpdateStatusText.Text = $"Update failed: {ex.Message}";
                UpdateButton.Content = "Update";
                UpdateButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Automatically checks for updates on startup if the setting is enabled.
        /// Shows a banner if an update is available.
        /// </summary>
        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                // Check if auto-update check is enabled (default: true)
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                bool autoCheckEnabled = true;
                if (settings.Values.TryGetValue("AutoUpdateCheckEnabled", out object val) && val is bool b)
                {
                    autoCheckEnabled = b;
                }

                // Update the toggle to match saved setting
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (AutoUpdateCheckToggle != null)
                    {
                        AutoUpdateCheckToggle.IsOn = autoCheckEnabled;
                    }
                });

                if (!autoCheckEnabled)
                {
                    Logger.Info("Auto-update check is disabled, skipping startup check");
                    return;
                }

                Logger.Info("Checking for updates on startup...");

                // Small delay to let the UI settle first
                await Task.Delay(2000);

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "GoTweaks-UpdateChecker");
                    var response = await httpClient.GetStringAsync("https://api.github.com/repos/corando98/GoTweaks/releases/latest");

                    // Parse JSON response
                    var jsonObject = Windows.Data.Json.JsonObject.Parse(response);
                    var latestVersion = jsonObject.GetNamedString("tag_name", "");

                    // Get current version
                    var packageVersion = Package.Current.Id.Version;
                    var currentVersion = $"v{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";

                    Logger.Info($"Startup update check: current={currentVersion}, latest={latestVersion}");

                    if (!string.IsNullOrEmpty(latestVersion) && IsNewerVersion(latestVersion, currentVersion))
                    {
                        // Find the .zip asset download URL
                        string zipUrl = null;
                        if (jsonObject.ContainsKey("assets"))
                        {
                            var assets = jsonObject.GetNamedArray("assets");
                            foreach (var asset in assets)
                            {
                                var assetObj = asset.GetObject();
                                var name = assetObj.GetNamedString("name", "");
                                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    zipUrl = assetObj.GetNamedString("browser_download_url", "");
                                    break;
                                }
                            }
                        }

                        // Show update banner
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            _pendingUpdateZipUrl = zipUrl;
                            _pendingUpdateVersion = latestVersion;
                            ShowUpdateBanner(latestVersion);
                        });

                        Logger.Info($"Update available: {latestVersion}, zip URL: {zipUrl ?? "not found"}");
                    }
                    else
                    {
                        Logger.Info("No update available");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to check for updates on startup: {ex.Message}");
                // Silently fail - don't show error to user for automatic check
            }
        }

        /// <summary>
        /// Shows the update available banner with the new version.
        /// </summary>
        private void ShowUpdateBanner(string newVersion)
        {
            if (UpdateAvailableBanner != null && UpdateAvailableText != null)
            {
                UpdateAvailableText.Text = $"Update Available: {newVersion}";
                UpdateAvailableBanner.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Hides the update available banner.
        /// </summary>
        private void HideUpdateBanner()
        {
            if (UpdateAvailableBanner != null)
            {
                UpdateAvailableBanner.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Handles the Update button click on the update banner.
        /// </summary>
        private async void UpdateBannerButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pendingUpdateZipUrl))
            {
                Logger.Warn("Update banner clicked but no pending update URL");
                return;
            }

            try
            {
                UpdateBannerButton.IsEnabled = false;
                UpdateBannerButton.Content = "Updating...";

                if (App.IsConnected)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("Command", (int)Shared.Enums.Command.Set);
                    message.Add("Function", (int)Shared.Enums.Function.InstallUpdate);
                    message.Add("Content", _pendingUpdateZipUrl);
                    var result = await App.SendMessageAsync(message);

                    if (result != null && result.TryGetValue("UpdateStatus", out object status))
                    {
                        var statusStr = status?.ToString() ?? "";
                        if (statusStr == "Installing")
                        {
                            UpdateBannerButton.Content = "Installing...";
                            Logger.Info("Update installation started from banner");
                        }
                        else if (statusStr.StartsWith("Error"))
                        {
                            Logger.Error($"Update failed: {statusStr}");
                            UpdateBannerButton.Content = "Failed";
                            await Task.Delay(2000);
                            UpdateBannerButton.Content = "Update";
                            UpdateBannerButton.IsEnabled = true;
                        }
                    }
                    else
                    {
                        UpdateBannerButton.Content = "Failed";
                        await Task.Delay(2000);
                        UpdateBannerButton.Content = "Update";
                        UpdateBannerButton.IsEnabled = true;
                    }
                }
                else
                {
                    Logger.Warn("Helper not connected for update");
                    UpdateBannerButton.Content = "No Helper";
                    await Task.Delay(2000);
                    UpdateBannerButton.Content = "Update";
                    UpdateBannerButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start update from banner: {ex.Message}");
                UpdateBannerButton.Content = "Update";
                UpdateBannerButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Handles the dismiss button click on the update banner.
        /// </summary>
        private void DismissUpdateBannerButton_Click(object sender, RoutedEventArgs e)
        {
            HideUpdateBanner();
        }

        /// <summary>
        /// Handles the auto-update check toggle change.
        /// </summary>
        private void AutoUpdateCheckToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (AutoUpdateCheckToggle == null)
                return;

            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            settings.Values["AutoUpdateCheckEnabled"] = AutoUpdateCheckToggle.IsOn;
            Logger.Info($"Auto-update check setting changed to: {AutoUpdateCheckToggle.IsOn}");
        }

        private async void CheckForUpdateDebugButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckForUpdateDebugButton.IsEnabled = false;
                CheckForUpdateDebugButton.Content = "Checking...";
                UpdateStatusText.Visibility = Visibility.Visible;
                UpdateStatusText.Text = "Checking local AppPackages...";
                UpdateButton.Visibility = Visibility.Collapsed;
                _pendingUpdateZipUrl = null;
                _pendingUpdateVersion = null;

                if (!App.IsConnected)
                {
                    UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                    UpdateStatusText.Text = "Helper not connected";
                    CheckForUpdateDebugButton.Content = "Check for Update (Debug)";
                    CheckForUpdateDebugButton.IsEnabled = true;
                    return;
                }

                // Ask helper to check for local updates (helper has file system access)
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Get);
                message.Add("Function", (int)Shared.Enums.Function.CheckLocalUpdate);
                var result = await App.SendMessageAsync(message);

                if (result != null)
                {
                    if (result.TryGetValue("Error", out object errorObj))
                    {
                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                        UpdateStatusText.Text = errorObj?.ToString() ?? "Unknown error";
                    }
                    else if (result.TryGetValue("LatestVersion", out object versionObj) &&
                             result.TryGetValue("MsixbundlePath", out object pathObj))
                    {
                        var foundVersionStr = versionObj?.ToString();
                        var msixbundlePath = pathObj?.ToString();
                        var folderName = result.TryGetValue("FolderName", out object folderObj) ? folderObj?.ToString() : "";

                        // Get current version
                        var packageVersion = Package.Current.Id.Version;
                        var currentVersion = $"v{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";
                        var foundVersion = $"v{foundVersionStr}";

                        Logger.Info($"Debug update check: current={currentVersion}, found={foundVersion}, path={msixbundlePath}");

                        // Compare versions
                        var currentVer = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
                        if (Version.TryParse(foundVersionStr, out var latestVersion) && latestVersion > currentVer)
                        {
                            UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.LimeGreen);
                            UpdateStatusText.Text = $"[DEBUG] New version found: {foundVersion}\nCurrent: {currentVersion}\n{folderName}";
                            _pendingUpdateZipUrl = msixbundlePath; // Local path to msixbundle
                            _pendingUpdateVersion = foundVersion;
                            UpdateButton.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
                            UpdateStatusText.Text = $"[DEBUG] You're up to date! ({currentVersion})\nLatest in AppPackages: {foundVersion}";
                        }
                    }
                    else
                    {
                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                        UpdateStatusText.Text = "Invalid response from helper";
                    }
                }
                else
                {
                    UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                    UpdateStatusText.Text = "Failed to communicate with helper";
                }

                CheckForUpdateDebugButton.Content = "Check for Update (Debug)";
                CheckForUpdateDebugButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to check for debug update: {ex.Message}");
                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                UpdateStatusText.Text = $"Failed: {ex.Message}";
                CheckForUpdateDebugButton.Content = "Check for Update (Debug)";
                CheckForUpdateDebugButton.IsEnabled = true;
            }
        }

        private async void ExportDGPsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportDGPsButton.IsEnabled = false;
                ExportDGPsButton.Content = "Exporting...";

                if (!App.IsConnected)
                {
                    ExportDGPsButton.Content = "Helper not connected";
                    await Task.Delay(2000);
                    ExportDGPsButton.Content = "Export DGPs (Desktop)";
                    ExportDGPsButton.IsEnabled = true;
                    return;
                }

                // Send request to helper to export DGPs
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Set);
                message.Add("Function", (int)Shared.Enums.Function.Debug_ExportDGPs);
                var result = await App.SendMessageAsync(message);

                if (result != null)
                {
                    if (result.TryGetValue("ExportPath", out object pathObj))
                    {
                        ExportDGPsButton.Content = $"Exported!";
                        Logger.Info($"DGPs exported to: {pathObj}");
                    }
                    else if (result.TryGetValue("Error", out object errorObj))
                    {
                        ExportDGPsButton.Content = $"Error: {errorObj}";
                    }
                }
                else
                {
                    ExportDGPsButton.Content = "Failed";
                }

                await Task.Delay(2000);
                ExportDGPsButton.Content = "Export DGPs (Desktop)";
                ExportDGPsButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export DGPs: {ex.Message}");
                ExportDGPsButton.Content = "Export DGPs (Desktop)";
                ExportDGPsButton.IsEnabled = true;
            }
        }

        private async void ExportAllDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportAllDataButton.IsEnabled = false;
                ExportAllDataButton.Content = "Exporting...";

                if (!App.IsConnected)
                {
                    ExportAllDataButton.Content = "Helper not connected";
                    await Task.Delay(2000);
                    ExportAllDataButton.Content = "Export All Data";
                    ExportAllDataButton.IsEnabled = true;
                    return;
                }

                // Gather widget LocalSettings to include in export
                string widgetSettingsJson = GatherWidgetSettingsForExport();

                // Send request to helper to export all data
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Set);
                message.Add("Function", (int)Shared.Enums.Function.ExportAllData);
                message.Add("Content", widgetSettingsJson);
                var result = await App.SendMessageAsync(message);

                if (result != null && result.TryGetValue("Content", out object contentObj))
                {
                    string resultText = contentObj?.ToString() ?? "";
                    if (resultText.StartsWith("Error:"))
                    {
                        ExportAllDataButton.Content = "Failed";
                        Logger.Error($"Export failed: {resultText}");
                    }
                    else
                    {
                        ExportAllDataButton.Content = "Exported!";
                        Logger.Info($"All data exported to: {resultText}");
                    }
                }
                else
                {
                    ExportAllDataButton.Content = "Failed";
                }

                await Task.Delay(2000);
                ExportAllDataButton.Content = "Export All Data";
                ExportAllDataButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export all data: {ex.Message}");
                ExportAllDataButton.Content = "Export All Data";
                ExportAllDataButton.IsEnabled = true;
            }
        }

        private async void ImportAllDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open folder picker to select backup folder
                var folderPicker = new Windows.Storage.Pickers.FolderPicker();
                folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                folderPicker.FileTypeFilter.Add("*");

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder == null)
                    return; // User cancelled

                // Check if this looks like a valid backup folder
                var manifestFile = await folder.TryGetItemAsync("manifest.json");
                if (manifestFile == null)
                {
                    var warningDialog = new Windows.UI.Popups.MessageDialog(
                        "The selected folder doesn't appear to be a valid GoTweaks backup.\n\n" +
                        "Please select a folder created by 'Export All Data' (e.g., GoTweaks_Backup_2024-...).",
                        "Invalid Backup Folder");
                    await warningDialog.ShowAsync();
                    return;
                }

                // Show confirmation dialog
                var dialog = new Windows.UI.Popups.MessageDialog(
                    $"Import data from:\n{folder.Name}\n\n" +
                    "This will:\n" +
                    "• Import all per-game profiles\n" +
                    "• Import global settings\n" +
                    "• Import AutoTDP Q-learning model\n" +
                    "• Import helper settings\n" +
                    "• Apply widget settings\n\n" +
                    "Existing data will be overwritten. Continue?",
                    "Import All Data");

                dialog.Commands.Add(new Windows.UI.Popups.UICommand("Import"));
                dialog.Commands.Add(new Windows.UI.Popups.UICommand("Cancel"));
                dialog.DefaultCommandIndex = 1;
                dialog.CancelCommandIndex = 1;

                var confirmResult = await dialog.ShowAsync();
                if (confirmResult.Label == "Cancel")
                    return;

                ImportAllDataButton.IsEnabled = false;
                ImportAllDataButton.Content = "Importing...";

                if (!App.IsConnected)
                {
                    ImportAllDataButton.Content = "Helper not connected";
                    await Task.Delay(2000);
                    ImportAllDataButton.Content = "Import All Data";
                    ImportAllDataButton.IsEnabled = true;
                    return;
                }

                // Send request to helper to import all data
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Set);
                message.Add("Function", (int)Shared.Enums.Function.ImportAllData);
                message.Add("Content", folder.Path);
                var result = await App.SendMessageAsync(message);

                if (result != null && result.TryGetValue("Content", out object contentObj))
                {
                    string summary = contentObj?.ToString() ?? "Import completed";

                    // Check if widget settings were returned
                    if (result.TryGetValue("WidgetSettings", out object widgetSettingsObj))
                    {
                        string widgetSettingsJson = widgetSettingsObj?.ToString();
                        if (!string.IsNullOrEmpty(widgetSettingsJson))
                        {
                            ApplyImportedWidgetSettings(widgetSettingsJson);
                            summary += "\n\nWidget settings have been applied.";
                        }
                    }

                    // Show result dialog
                    var resultDialog = new Windows.UI.Popups.MessageDialog(summary, "Import Complete");
                    await resultDialog.ShowAsync();

                    ImportAllDataButton.Content = "Imported!";
                }
                else
                {
                    ImportAllDataButton.Content = "Failed";
                }

                await Task.Delay(2000);
                ImportAllDataButton.Content = "Import All Data";
                ImportAllDataButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to import all data: {ex.Message}");
                ImportAllDataButton.Content = "Import All Data";
                ImportAllDataButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Gathers widget LocalSettings as JSON for export.
        /// </summary>
        private string GatherWidgetSettingsForExport()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var jsonObj = new Windows.Data.Json.JsonObject();

                // Export all known settings keys
                var keysToExport = new[]
                {
                    // AutoTDP settings
                    "AutoTDPEnabled", "AutoTDPTargetFPS", "AutoTDPMinTDP", "AutoTDPMaxTDP",
                    "AutoTDPUseMLMode", "AutoTDPPauseWhenUnfocused",
                    // TDP Boost settings
                    "TDPBoostEnabled", "TDPBoostSPPT", "TDPBoostFPPT",
                    // OSD settings
                    "OSDConfig", "OLEDConfig",
                    // Profile settings
                    "ProfileMatchByExe", "ProfileGamesOnly", "ProfileCustomGamePath", "ProfileBlacklistPaths",
                    // Legion settings
                    "LegionL_Action", "LegionL_Shortcut", "LegionL_Command",
                    "LegionR_Action", "LegionR_Shortcut", "LegionR_Command",
                    "LegionTouchpadVibration", "LegionDesktopControls",
                    // Controller hotkey settings
                    "ControllerHotkeyConfig",
                    // Display settings
                    "RefreshRateProfile",
                    // Other settings
                    "TdpMethod", "ForceDefaultGameProfile"
                };

                foreach (var key in keysToExport)
                {
                    if (settings.Values.ContainsKey(key))
                    {
                        var value = settings.Values[key];
                        if (value is bool boolVal)
                            jsonObj[key] = Windows.Data.Json.JsonValue.CreateBooleanValue(boolVal);
                        else if (value is int intVal)
                            jsonObj[key] = Windows.Data.Json.JsonValue.CreateNumberValue(intVal);
                        else if (value is double doubleVal)
                            jsonObj[key] = Windows.Data.Json.JsonValue.CreateNumberValue(doubleVal);
                        else if (value is string strVal)
                            jsonObj[key] = Windows.Data.Json.JsonValue.CreateStringValue(strVal);
                    }
                }

                return jsonObj.Stringify();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to gather widget settings for export: {ex.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// Applies imported widget settings from JSON.
        /// </summary>
        private void ApplyImportedWidgetSettings(string json)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;

                if (!Windows.Data.Json.JsonObject.TryParse(json, out Windows.Data.Json.JsonObject jsonObj))
                {
                    Logger.Error("Failed to parse imported widget settings JSON");
                    return;
                }

                int importedCount = 0;
                foreach (var key in jsonObj.Keys)
                {
                    try
                    {
                        var jsonValue = jsonObj[key];
                        object value = null;

                        switch (jsonValue.ValueType)
                        {
                            case Windows.Data.Json.JsonValueType.Boolean:
                                value = jsonValue.GetBoolean();
                                break;
                            case Windows.Data.Json.JsonValueType.Number:
                                // Try to preserve int vs double
                                double numVal = jsonValue.GetNumber();
                                if (numVal == Math.Floor(numVal) && numVal >= int.MinValue && numVal <= int.MaxValue)
                                    value = (int)numVal;
                                else
                                    value = numVal;
                                break;
                            case Windows.Data.Json.JsonValueType.String:
                                value = jsonValue.GetString();
                                break;
                        }

                        if (value != null)
                        {
                            settings.Values[key] = value;
                            importedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to import setting '{key}': {ex.Message}");
                    }
                }

                Logger.Info($"Applied {importedCount} widget settings from import");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to apply imported widget settings: {ex.Message}");
            }
        }

        private async void PrepareForUninstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show confirmation dialog
                var dialog = new Windows.UI.Popups.MessageDialog(
                    "This will:\n\n" +
                    "• Remove the scheduled task\n" +
                    "• Restore original CPU Boost settings\n" +
                    "• Restore original EPP settings\n" +
                    "• Re-enable Legion Space service (if disabled)\n\n" +
                    "After this, you can safely uninstall the app.",
                    "Prepare for Uninstall");

                dialog.Commands.Add(new Windows.UI.Popups.UICommand("Continue"));
                dialog.Commands.Add(new Windows.UI.Popups.UICommand("Cancel"));
                dialog.DefaultCommandIndex = 1;
                dialog.CancelCommandIndex = 1;

                var result = await dialog.ShowAsync();
                if (result.Label == "Cancel")
                    return;

                PrepareForUninstallButton.IsEnabled = false;
                PrepareForUninstallButton.Content = "Restoring...";

                if (!App.IsConnected)
                {
                    PrepareForUninstallButton.Content = "Helper not connected";
                    await Task.Delay(2000);
                    PrepareForUninstallButton.Content = "Prepare for Uninstall";
                    PrepareForUninstallButton.IsEnabled = true;
                    return;
                }

                // Send request to helper to prepare for uninstall
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Set);
                message.Add("Function", (int)Shared.Enums.Function.PrepareForUninstall);
                var response = await App.SendMessageAsync(message);

                if (response != null && response.TryGetValue("Content", out object contentObj))
                {
                    string resultText = contentObj?.ToString() ?? "Completed";
                    Logger.Info($"PrepareForUninstall result:\n{resultText}");

                    // Show result in a dialog
                    var resultDialog = new Windows.UI.Popups.MessageDialog(
                        resultText,
                        "Uninstall Preparation Complete");
                    await resultDialog.ShowAsync();

                    PrepareForUninstallButton.Content = "Done!";
                }
                else
                {
                    PrepareForUninstallButton.Content = "Failed";
                }

                await Task.Delay(2000);
                PrepareForUninstallButton.Content = "Prepare for Uninstall";
                PrepareForUninstallButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to prepare for uninstall: {ex.Message}");
                PrepareForUninstallButton.Content = "Prepare for Uninstall";
                PrepareForUninstallButton.IsEnabled = true;
            }
        }

        #region PawnIO Debug Tools

        private int _pawnIOCoAllValue = 0;
        private int _pawnIOCoGfxValue = 0;
        private int _pawnIOGfxClkValue = 800;
        private int _pawnIOTctlValue = 95;

        private void EnableDebugToolsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (PawnIODebugTools != null)
            {
                PawnIODebugTools.Visibility = EnableDebugToolsToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;

                if (EnableDebugToolsToggle.IsOn)
                {
                    // Request CPU info from helper
                    _ = UpdatePawnIOCpuInfo();
                }
            }
        }

        private async Task UpdatePawnIOCpuInfo()
        {
            try
            {
                if (!App.IsConnected)
                {
                    PawnIOCpuInfoText.Text = "CPU: Helper not connected";
                    return;
                }

                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Get);
                message.Add("Function", (int)Shared.Enums.Function.PawnIOGetCpuInfo);
                var response = await App.SendMessageAsync(message);

                if (response != null && response.TryGetValue("Content", out object contentObj))
                {
                    PawnIOCpuInfoText.Text = $"CPU: {contentObj}";
                }
                else
                {
                    PawnIOCpuInfoText.Text = "CPU: PawnIO not available";
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get PawnIO CPU info: {ex.Message}");
                PawnIOCpuInfoText.Text = "CPU: Error";
            }
        }

        private void PawnIOCoAllMinus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOCoAllValue = Math.Max(-30, _pawnIOCoAllValue - 1);
            PawnIOCoAllValue.Text = _pawnIOCoAllValue.ToString();
        }

        private void PawnIOCoAllPlus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOCoAllValue = Math.Min(30, _pawnIOCoAllValue + 1);
            PawnIOCoAllValue.Text = _pawnIOCoAllValue.ToString();
        }

        private void PawnIOCoGfxMinus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOCoGfxValue = Math.Max(-30, _pawnIOCoGfxValue - 1);
            PawnIOCoGfxValue.Text = _pawnIOCoGfxValue.ToString();
        }

        private void PawnIOCoGfxPlus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOCoGfxValue = Math.Min(30, _pawnIOCoGfxValue + 1);
            PawnIOCoGfxValue.Text = _pawnIOCoGfxValue.ToString();
        }

        private void PawnIOGfxClkMinus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOGfxClkValue = Math.Max(100, _pawnIOGfxClkValue - 50);
            PawnIOGfxClkValue.Text = _pawnIOGfxClkValue.ToString();
        }

        private void PawnIOGfxClkPlus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOGfxClkValue = Math.Min(3000, _pawnIOGfxClkValue + 50);
            PawnIOGfxClkValue.Text = _pawnIOGfxClkValue.ToString();
        }

        private void PawnIOTctlMinus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOTctlValue = Math.Max(60, _pawnIOTctlValue - 1);
            PawnIOTctlValue.Text = _pawnIOTctlValue.ToString();
        }

        private void PawnIOTctlPlus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOTctlValue = Math.Min(105, _pawnIOTctlValue + 1);
            PawnIOTctlValue.Text = _pawnIOTctlValue.ToString();
        }

        private async void PawnIOApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PawnIOApplyButton.IsEnabled = false;
                PawnIODebugStatusText.Text = "Applying...";

                if (!App.IsConnected)
                {
                    PawnIODebugStatusText.Text = "Helper not connected";
                    PawnIOApplyButton.IsEnabled = true;
                    return;
                }

                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Set);
                message.Add("Function", (int)Shared.Enums.Function.PawnIOApplySettings);
                message.Add("CoAll", _pawnIOCoAllValue);
                message.Add("CoGfx", _pawnIOCoGfxValue);
                message.Add("GfxClk", _pawnIOGfxClkValue);
                message.Add("TctlTemp", _pawnIOTctlValue);

                var response = await App.SendMessageAsync(message);

                if (response != null && response.TryGetValue("Content", out object contentObj))
                {
                    PawnIODebugStatusText.Text = contentObj?.ToString() ?? "Applied";
                }
                else
                {
                    PawnIODebugStatusText.Text = "No response from helper";
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"PawnIO apply failed: {ex.Message}");
                PawnIODebugStatusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                PawnIOApplyButton.IsEnabled = true;
            }
        }

        private void PawnIOResetButton_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOCoAllValue = 0;
            _pawnIOCoGfxValue = 0;
            _pawnIOGfxClkValue = 800;
            _pawnIOTctlValue = 95;

            PawnIOCoAllValue.Text = "0";
            PawnIOCoGfxValue.Text = "0";
            PawnIOGfxClkValue.Text = "800";
            PawnIOTctlValue.Text = "95";
            PawnIODebugStatusText.Text = "Values reset (not applied)";
        }

        #endregion

        #endregion

        private void UpdateCPUCoreConfigSummary()
        {
            // Update the Advanced card summary with current settings
            if (AdvancedSummary != null)
            {
                int activeCoresParking = totalCores - parkedCores;
                if (isHybridCPU)
                {
                    if (parkedCores > 0)
                    {
                        AdvancedSummary.Text = $"Parking: {activeCoresParking}/{totalCores} cores | Affinity: {activePCores}P + {activeECores}E";
                    }
                    else
                    {
                        AdvancedSummary.Text = $"Affinity: {activePCores}P + {activeECores}E cores";
                    }
                }
                else
                {
                    if (parkedCores > 0)
                    {
                        AdvancedSummary.Text = $"Core parking: {activeCoresParking}/{totalCores} cores active";
                    }
                    else
                    {
                        AdvancedSummary.Text = "Core parking and affinity settings";
                    }
                }
            }
        }

        private void SaveCPUCoreConfigToStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["ActivePCores"] = activePCores;
                settings.Values["ActiveECores"] = activeECores;
                Logger.Info($"Saved CPU core config: P={activePCores}, E={activeECores}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save CPU core config: {ex.Message}");
            }
        }

        private void LoadCPUCoreConfigFromStorage()
        {
            isLoadingCPUCoreConfig = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("ActivePCores", out object pObj) && pObj is int pCores)
                {
                    activePCores = pCores;
                }

                if (settings.Values.TryGetValue("ActiveECores", out object eObj) && eObj is int eCores)
                {
                    activeECores = eCores;
                }

                // Load Force Park Mode setting
                if (settings.Values.TryGetValue("ForceParkMode", out object fpObj) && fpObj is bool fpEnabled)
                {
                    if (ForceParkModeToggle != null)
                    {
                        ForceParkModeToggle.IsOn = fpEnabled;
                    }
                    // Send to helper on startup
                    forceParkMode?.SetValue(fpEnabled);
                    Logger.Info($"Loaded Force Park Mode: {fpEnabled}");
                }

                // Update UI
                UpdatePCoreComboBox();
                UpdateECoreComboBox();
                UpdateCPUCoreConfigSummary();

                Logger.Info($"Loaded CPU core config: P={activePCores}, E={activeECores}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load CPU core config: {ex.Message}");
            }
            finally
            {
                isLoadingCPUCoreConfig = false;
            }
        }

        private void UpdatePCoreComboBox()
        {
            if (PCoreCountComboBox == null) return;

            foreach (ComboBoxItem item in PCoreCountComboBox.Items)
            {
                if (item.Tag is string tagStr && int.TryParse(tagStr, out int val) && val == activePCores)
                {
                    PCoreCountComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void UpdateECoreComboBox()
        {
            if (ECoreCountComboBox == null) return;

            foreach (ComboBoxItem item in ECoreCountComboBox.Items)
            {
                if (item.Tag is string tagStr && int.TryParse(tagStr, out int val) && val == activeECores)
                {
                    ECoreCountComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void SetupCPUCoreConfigUI(int pCoreCount, int eCoreCount)
        {
            isLoadingCPUCoreConfig = true;
            try
            {
                totalPCores = pCoreCount;
                totalECores = eCoreCount;
                isHybridCPU = pCoreCount > 0 && eCoreCount > 0;

                // For hybrid CPUs: show affinity section, hide core parking dropdown
                // For non-hybrid: show core parking dropdown, hide affinity section
                if (CoreAffinitySection != null)
                {
                    CoreAffinitySection.Visibility = isHybridCPU ? Visibility.Visible : Visibility.Collapsed;
                }
                if (CoreParkingSection != null)
                {
                    CoreParkingSection.Visibility = isHybridCPU ? Visibility.Collapsed : Visibility.Visible;
                }

                // Setup core parking UI for non-hybrid CPUs
                if (!isHybridCPU)
                {
                    SetupCoreParkingUI();
                }

                if (!isHybridCPU) return;

                // Populate P-Core combobox
                if (PCoreCountComboBox != null)
                {
                    PCoreCountComboBox.Items.Clear();
                    for (int i = 0; i <= pCoreCount; i++)
                    {
                        var item = new ComboBoxItem { Content = i.ToString(), Tag = i.ToString() };
                        PCoreCountComboBox.Items.Add(item);
                    }
                }

                // Populate E-Core combobox
                if (ECoreCountComboBox != null)
                {
                    ECoreCountComboBox.Items.Clear();
                    for (int i = 0; i <= eCoreCount; i++)
                    {
                        var item = new ComboBoxItem { Content = i.ToString(), Tag = i.ToString() };
                        ECoreCountComboBox.Items.Add(item);
                    }
                }

                // Load saved config or use defaults (all cores active)
                LoadCPUCoreConfigFromStorage();

                // Ensure at least 1 core total is active
                if (activePCores == 0 && activeECores == 0)
                {
                    activePCores = pCoreCount;
                    activeECores = eCoreCount;
                }

                UpdatePCoreComboBox();
                UpdateECoreComboBox();
                UpdateCPUCoreConfigSummary();

                // Send the saved config to helper to apply on startup
                SendCPUCoreConfigToHelper();

                Logger.Info($"CPU Core Config UI setup: {pCoreCount}P + {eCoreCount}E cores (hybrid={isHybridCPU})");
            }
            finally
            {
                isLoadingCPUCoreConfig = false;
            }
        }

        #endregion

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
