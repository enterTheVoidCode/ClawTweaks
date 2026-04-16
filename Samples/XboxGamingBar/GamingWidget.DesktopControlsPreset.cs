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
        private void LegionDesktopControls_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
                return;

            // Skip if a profile was just applied (prevents duplicate sends from queued UI events)
            if ((DateTime.Now - lastProfileApplyTime).TotalMilliseconds < 2000)
            {
                Logger.Info("Desktop Controls toggled event skipped - profile was just applied");
                return;
            }

            bool enabled = LegionDesktopControlsToggle?.IsOn ?? false;

            if (enabled)
            {
                // Apply desktop controls preset
                // 1. Set Right Stick as Mouse (for cursor movement)
                if (LegionJoystickAsMouseComboBox != null)
                    LegionJoystickAsMouseComboBox.SelectedIndex = 2; // Right Stick

                // 2. Apply button mappings (DPAD, LS scroll, LB/LT clicks)
                ApplyDesktopControlMappings();
            }
            else
            {
                // Reset to defaults
                if (LegionJoystickAsMouseComboBox != null)
                    LegionJoystickAsMouseComboBox.SelectedIndex = 0; // Disabled

                // Clear the desktop control button mappings
                ClearDesktopControlMappings();
            }

            Logger.Info($"Desktop Controls toggled: {enabled}");

            // Save the updated profile
            if (!isLoadingControllerProfile && !isSwitchingControllerProfile)
            {
                if (LegionControllerProfileToggle?.IsOn == true && HasValidGame(currentGameName))
                {
                    gameControllerProfile = GetCurrentControllerProfileFromUI();
                    SaveControllerProfileToStorage($"Game_{currentGameName}", gameControllerProfile);
                    Logger.Info($"Saved Desktop Controls state to game profile: {currentGameName}");
                }
                else
                {
                    globalControllerProfile = GetCurrentControllerProfileFromUI();
                    SaveControllerProfileToStorage("Global", globalControllerProfile);
                    Logger.Info("Saved Desktop Controls state to global profile");
                }
            }
        }

        private void ApplyDesktopControlMappings()
        {
            // Desktop Controls preset - uses LB/LT for clicks to avoid firmware drag-drop bug with triggers
            // HID key codes: Up=0x52, Down=0x51, Left=0x50, Right=0x4F, Enter=0x28, Escape=0x29, LeftGUI(Win)=0xE3
            // MouseButton dropdown index: 0=Left, 1=Right, 2=Middle, 3=ScrollUp, 4=ScrollDown

            // DPAD → Arrow keys (Type=1 Keyboard)
            gamepadButtonMappings["DPadUp"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x52 } };
            gamepadButtonMappings["DPadDown"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x51 } };
            gamepadButtonMappings["DPadLeft"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x50 } };
            gamepadButtonMappings["DPadRight"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x4F } };

            // Left Stick Up/Down → Arrow Up/Down (Type=1 Keyboard)
            gamepadButtonMappings["LSUp"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x52 } };    // Up Arrow
            gamepadButtonMappings["LSDown"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x51 } }; // Down Arrow

            // LSClick → Windows Key (Type=1 Keyboard)
            gamepadButtonMappings["LSClick"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0xE3 } }; // Left GUI (Win)

            // A → Enter, B → Escape (Type=1 Keyboard)
            gamepadButtonMappings["A"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x28 } };  // Enter
            gamepadButtonMappings["B"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x29 } };  // Escape

            // LB → Left Click, LT → Right Click (Type=2 Mouse)
            gamepadButtonMappings["LB"] = new ButtonMapping { Type = 2, MouseButton = 0 };     // Left Click
            gamepadButtonMappings["LT"] = new ButtonMapping { Type = 2, MouseButton = 1 };     // Right Click

            // During profile loading, just update the dictionary - SendButtonMappingsToHelper will send once at the end
            if (!isLoadingControllerProfile)
            {
                SaveAndSendGamepadMappings();
            }
            UpdateGamepadMappingSummary();

            Logger.Info("Applied desktop control mappings: DPAD/LS→Arrows, LSClick→Win, A→Enter, B→Esc, LB→LClick, LT→RClick");
        }

        private void ClearDesktopControlMappings()
        {
            var desktopButtons = new[] { "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "LSUp", "LSDown", "LSClick", "A", "B", "LB", "LT" };

            // Set each button to reset state (Type=0, GamepadAction=0) to trigger HID reset
            foreach (var button in desktopButtons)
            {
                gamepadButtonMappings[button] = new ButtonMapping { Type = 0, GamepadAction = 0 };
            }

            // During profile loading, just update the dictionary - SendButtonMappingsToHelper will send once at the end
            if (!isLoadingControllerProfile)
            {
                SaveAndSendGamepadMappings();

                // Remove from dictionary after sending reset (only when not loading profile)
                foreach (var button in desktopButtons)
                {
                    gamepadButtonMappings.Remove(button);
                }
            }
            // When loading profile, keep Type=0 entries in dictionary so they get sent with other mappings

            UpdateGamepadMappingSummary();

            Logger.Info("Cleared desktop control mappings for DPAD, LS, A, B, LB, LT");
        }

    }
}
