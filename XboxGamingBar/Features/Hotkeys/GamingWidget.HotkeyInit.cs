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

        /// <summary>
        /// Apply default hotkey settings if not already configured
        /// </summary>
        private void ApplyHotkeyDefaults()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Check if defaults have already been applied
                if (settings.Values.ContainsKey("Hotkey_DefaultsApplied"))
                    return;

                // Apply defaults:
                // View + A: Disabled (0) - was Ctrl+Alt+Del but that cannot be simulated
                // View + B: Open Virtual Keyboard (7)
                // View + X: Screenshot (4)
                // View + Y: Focus GoTweaks (10)
                settings.Values["Hotkey_MenuA_Action"] = (int)HotkeyAction.Disabled;
                settings.Values["Hotkey_MenuB_Action"] = (int)HotkeyAction.OpenKeyboard;
                settings.Values["Hotkey_MenuX_Action"] = (int)HotkeyAction.Screenshot;
                settings.Values["Hotkey_MenuY_Action"] = (int)HotkeyAction.FocusGoTweaks;
                settings.Values["Hotkey_DefaultsApplied"] = true;

                Logger.Info("Hotkey defaults applied: A=Disabled, B=OpenKeyboard, X=Screenshot, Y=FocusGoTweaks");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying hotkey defaults: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize hotkey watchers for Xbox controller button combinations.
        /// These work even when the widget is not visible.
        /// </summary>
        private void InitializeHotkeyWatchers()
        {
            if (widget == null)
            {
                Logger.Warn("Cannot initialize hotkey watchers - widget is null");
                return;
            }

            // Skip if already initialized
            if (hotkeyMenuA != null)
            {
                Logger.Info("Hotkey watchers already initialized, skipping");
                return;
            }

            // Apply default hotkey settings if not already set
            ApplyHotkeyDefaults();

            try
            {
                // Menu+A
                var keysA = new List<VirtualKey> { VirtualKey.GamepadView, VirtualKey.GamepadA };
                hotkeyMenuA = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysA);
                hotkeyMenuA.HotkeySetStateChanged += HotkeyMenuA_StateChanged;
                hotkeyMenuA.Start();

                // Menu+B
                var keysB = new List<VirtualKey> { VirtualKey.GamepadView, VirtualKey.GamepadB };
                hotkeyMenuB = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysB);
                hotkeyMenuB.HotkeySetStateChanged += HotkeyMenuB_StateChanged;
                hotkeyMenuB.Start();

                // Menu+X
                var keysX = new List<VirtualKey> { VirtualKey.GamepadView, VirtualKey.GamepadX };
                hotkeyMenuX = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysX);
                hotkeyMenuX.HotkeySetStateChanged += HotkeyMenuX_StateChanged;
                hotkeyMenuX.Start();

                // Menu+Y
                var keysY = new List<VirtualKey> { VirtualKey.GamepadView, VirtualKey.GamepadY };
                hotkeyMenuY = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysY);
                hotkeyMenuY.HotkeySetStateChanged += HotkeyMenuY_StateChanged;
                hotkeyMenuY.Start();

                // Menu+DpadUp
                var keysDpadUp = new List<VirtualKey> { VirtualKey.GamepadMenu, VirtualKey.GamepadDPadUp };
                hotkeyMenuDpadUp = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysDpadUp);
                hotkeyMenuDpadUp.HotkeySetStateChanged += HotkeyMenuDpadUp_StateChanged;
                hotkeyMenuDpadUp.Start();

                // Menu+DpadDown
                var keysDpadDown = new List<VirtualKey> { VirtualKey.GamepadMenu, VirtualKey.GamepadDPadDown };
                hotkeyMenuDpadDown = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysDpadDown);
                hotkeyMenuDpadDown.HotkeySetStateChanged += HotkeyMenuDpadDown_StateChanged;
                hotkeyMenuDpadDown.Start();

                // Menu+DpadLeft
                var keysDpadLeft = new List<VirtualKey> { VirtualKey.GamepadMenu, VirtualKey.GamepadDPadLeft };
                hotkeyMenuDpadLeft = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysDpadLeft);
                hotkeyMenuDpadLeft.HotkeySetStateChanged += HotkeyMenuDpadLeft_StateChanged;
                hotkeyMenuDpadLeft.Start();

                // Menu+DpadRight
                var keysDpadRight = new List<VirtualKey> { VirtualKey.GamepadMenu, VirtualKey.GamepadDPadRight };
                hotkeyMenuDpadRight = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysDpadRight);
                hotkeyMenuDpadRight.HotkeySetStateChanged += HotkeyMenuDpadRight_StateChanged;
                hotkeyMenuDpadRight.Start();

                Logger.Info("Hotkey watchers initialized for View+A/B/X/Y and Menu+Dpad");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize hotkey watchers: {ex.Message}");
            }
        }

    }
}
