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
        private void LegionNintendoLayout_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
                return;

            // Skip if a profile was just applied (prevents duplicate sends from queued UI events)
            if ((DateTime.Now - lastProfileApplyTime).TotalMilliseconds < 2000)
            {
                Logger.Info("Nintendo Layout toggled event skipped - profile was just applied");
                return;
            }

            bool enabled = LegionNintendoLayoutToggle?.IsOn ?? false;

            if (enabled)
            {
                // Apply Nintendo layout: swap A↔B and X↔Y
                ApplyNintendoLayoutMappings();
            }
            else
            {
                // Reset face buttons to default
                ClearNintendoLayoutMappings();
            }

            Logger.Info($"Nintendo Layout toggled: {enabled}");
        }

        private void ApplyNintendoLayoutMappings()
        {
            // GamepadAction uses dropdown index (from RemapActionHelper):
            // Index 15 = A (0x12), Index 16 = B (0x13), Index 17 = X (0x14), Index 18 = Y (0x15)
            // A → B: Type=0 (Gamepad), GamepadAction=16 (B)
            gamepadButtonMappings["A"] = new ButtonMapping { Type = 0, GamepadAction = 16 };
            // B → A: Type=0 (Gamepad), GamepadAction=15 (A)
            gamepadButtonMappings["B"] = new ButtonMapping { Type = 0, GamepadAction = 15 };
            // X → Y: Type=0 (Gamepad), GamepadAction=18 (Y)
            gamepadButtonMappings["X"] = new ButtonMapping { Type = 0, GamepadAction = 18 };
            // Y → X: Type=0 (Gamepad), GamepadAction=17 (X)
            gamepadButtonMappings["Y"] = new ButtonMapping { Type = 0, GamepadAction = 17 };

            SaveAndSendGamepadMappings();
            UpdateGamepadMappingSummary();

            Logger.Info("Applied Nintendo layout mappings: A→B, B→A, X→Y, Y→X");
        }

        private void ClearNintendoLayoutMappings()
        {
            var nintendoButtons = new[] { "A", "B", "X", "Y" };

            // Set each button to reset state (Type=0, GamepadAction=0) to trigger HID reset
            foreach (var button in nintendoButtons)
            {
                gamepadButtonMappings[button] = new ButtonMapping { Type = 0, GamepadAction = 0 };
            }
            SaveAndSendGamepadMappings();

            // Remove from dictionary after sending reset
            foreach (var button in nintendoButtons)
            {
                gamepadButtonMappings.Remove(button);
            }

            UpdateGamepadMappingSummary();

            Logger.Info("Cleared Nintendo layout mappings for A, B, X, Y");
        }

    }
}
