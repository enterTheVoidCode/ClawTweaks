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
        private void GPDFanModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (GPDFanModeToggle != null && gpdFanMode != null)
            {
                int mode = GPDFanModeToggle.IsOn ? 1 : 0;
                gpdFanMode.SetMode(mode);

                // Update mode text
                if (GPDFanModeText != null)
                {
                    GPDFanModeText.Text = GPDFanModeToggle.IsOn ? "Manual" : "Auto";
                }

                // Show/hide speed slider
                if (GPDFanSpeedSection != null)
                {
                    GPDFanSpeedSection.Visibility = GPDFanModeToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
                }

                // If switching to auto, send 0 for auto speed
                if (!GPDFanModeToggle.IsOn && gpdFanSpeed != null)
                {
                    gpdFanSpeed.SetSpeed(0);
                }

                Logger.Info($"GPD fan mode toggled to: {(GPDFanModeToggle.IsOn ? "Manual" : "Auto")}");
            }
        }

        private void GPDFanSpeedSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (GPDFanSpeedSlider != null && gpdFanSpeed != null)
            {
                int speed = (int)GPDFanSpeedSlider.Value;
                gpdFanSpeed.SetSpeed(speed);

                // Update value text
                if (GPDFanSpeedValueText != null)
                {
                    GPDFanSpeedValueText.Text = $"{speed}%";
                }

                Logger.Info($"GPD fan speed set to: {speed}%");
            }
        }
        private void GPDButtonRemapExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            if (GPDButtonRemapExpandToggle != null && GPDButtonRemapContent != null && GPDButtonRemapExpandIcon != null)
            {
                bool isExpanded = GPDButtonRemapExpandToggle.IsChecked == true;
                GPDButtonRemapContent.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
                GPDButtonRemapExpandIcon.Glyph = isExpanded ? "\uE70E" : "\uE70D"; // Down or Right chevron
            }
        }

        private void GPDL4PaddleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isApplyingGpdRestoreDefaults) return;
            if (GPDL4PaddleComboBox?.SelectedItem == null) return;

            string selected = GPDL4PaddleComboBox.SelectedItem.ToString();
            ushort keycode = MapGPDKeyNameToKeycode(selected);

            Logger.Info($"GPD L4 paddle staged: {selected} (keycode: 0x{keycode:X4})");

            if (gpdButtonL4 != null)
            {
                gpdButtonL4.SetKeycode(keycode);
            }
        }

        private void GPDR4PaddleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isApplyingGpdRestoreDefaults) return;
            if (GPDR4PaddleComboBox?.SelectedItem == null) return;

            string selected = GPDR4PaddleComboBox.SelectedItem.ToString();
            ushort keycode = MapGPDKeyNameToKeycode(selected);

            Logger.Info($"GPD R4 paddle staged: {selected} (keycode: 0x{keycode:X4})");

            if (gpdButtonR4 != null)
            {
                gpdButtonR4.SetKeycode(keycode);
            }
        }

        private void GPDButtonComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isApplyingGpdRestoreDefaults) return;
            if (!(sender is ComboBox comboBox) || comboBox.SelectedItem == null) return;

            string tag = comboBox.Tag?.ToString();
            string selected = comboBox.SelectedItem.ToString();
            ushort keycode = MapGPDKeyNameToKeycode(selected);

            Logger.Info($"GPD button {tag} staged: {selected} (keycode: 0x{keycode:X4})");

            GPDButtonProperty prop = null;
            switch (tag)
            {
                case "A": prop = gpdButtonA; break;
                case "B": prop = gpdButtonB; break;
                case "X": prop = gpdButtonX; break;
                case "Y": prop = gpdButtonY; break;
                case "DPadUp": prop = gpdButtonDPadUp; break;
                case "DPadDown": prop = gpdButtonDPadDown; break;
                case "DPadLeft": prop = gpdButtonDPadLeft; break;
                case "DPadRight": prop = gpdButtonDPadRight; break;
                case "L3": prop = gpdButtonL3; break;
                case "R3": prop = gpdButtonR3; break;
                case "LSLeft": prop = gpdButtonLSLeft; break;
                case "LSRight": prop = gpdButtonLSRight; break;
            }

            prop?.SetKeycode(keycode);
        }

        /// <summary>
        /// Maps a key name to a GPD Win 5 USB HID keycode.
        /// </summary>
        private ushort MapGPDKeyNameToKeycode(string keyName)
        {
            switch (keyName)
            {
                // Disabled
                case "Disabled": return 0x0000;

                // Function keys F1-F12
                case "F1": return 0x003A;
                case "F2": return 0x003B;
                case "F3": return 0x003C;
                case "F4": return 0x003D;
                case "F5": return 0x003E;
                case "F6": return 0x003F;
                case "F7": return 0x0040;
                case "F8": return 0x0041;
                case "F9": return 0x0042;
                case "F10": return 0x0043;
                case "F11": return 0x0044;
                case "F12": return 0x0045;

                // Function keys F13-F17 (extended)
                case "F13": return 0x0068;
                case "F14": return 0x0069;
                case "F15": return 0x006A;
                case "F16": return 0x006B;
                case "F17": return 0x006C;

                // Control keys
                case "Enter": return 0x0028;
                case "Escape": return 0x0029;
                case "Space": return 0x002C;
                case "Tab": return 0x002B;
                case "Backspace": return 0x002A;

                // Arrow keys
                case "Up": return 0x0052;
                case "Down": return 0x0051;
                case "Left": return 0x0050;
                case "Right": return 0x004F;

                // Navigation keys
                case "Home": return 0x004A;
                case "End": return 0x004D;
                case "PageUp": return 0x004B;
                case "PageDown": return 0x004E;
                case "Insert": return 0x0049;
                case "Delete": return 0x004C;

                // Modifier keys
                case "Left Ctrl": return 0x00E0;
                case "Left Shift": return 0x00E1;
                case "Left Alt": return 0x00E2;
                case "Left Win": return 0x00E3;
                case "Right Ctrl": return 0x00E4;
                case "Right Shift": return 0x00E5;
                case "Right Alt": return 0x00E6;

                // Mouse buttons (GPD custom codes)
                case "Mouse Left": return 0x00EA;
                case "Mouse Right": return 0x00EB;
                case "Mouse Middle": return 0x00EC;
                case "Mouse Wheel Up": return 0x00E8;
                case "Mouse Wheel Down": return 0x00E9;

                default: return 0x0000; // Disabled
            }
        }

        private void GPDRestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("GPD restore defaults button clicked");

            // Reset all button ComboBoxes to Disabled (index 0)
            var comboBoxes = new ComboBox[]
            {
                GPDButtonAComboBox, GPDButtonBComboBox, GPDButtonXComboBox, GPDButtonYComboBox,
                GPDButtonDPadUpComboBox, GPDButtonDPadDownComboBox, GPDButtonDPadLeftComboBox, GPDButtonDPadRightComboBox,
                GPDButtonL3ComboBox, GPDButtonR3ComboBox,
                GPDButtonLSLeftComboBox, GPDButtonLSRightComboBox,
                GPDL4PaddleComboBox, GPDR4PaddleComboBox
            };

            isApplyingGpdRestoreDefaults = true;
            foreach (var comboBox in comboBoxes)
            {
                if (comboBox != null)
                    comboBox.SelectedIndex = 0;
            }
            isApplyingGpdRestoreDefaults = false;

            // Trigger helper-side default restore in one transaction.
            gpdRestoreDefaults?.Trigger();
        }

        private void GPDApplyMappingsButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("GPD apply mappings button clicked");
            gpdApplyMappings?.Trigger();
        }

    }
}
