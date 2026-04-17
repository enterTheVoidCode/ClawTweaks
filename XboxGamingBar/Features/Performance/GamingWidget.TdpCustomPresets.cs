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

    }
}
