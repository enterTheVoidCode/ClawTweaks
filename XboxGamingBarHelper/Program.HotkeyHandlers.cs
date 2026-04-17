using NLog;
using Shared.Constants;
using Shared.Data;
using Shared.IPC;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI.Input.Preview.Injection;
using XboxGamingBarHelper.AMD;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.ControllerEmulation;
using XboxGamingBarHelper.Devices.Libraries.GPD;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.LosslessScaling;
using XboxGamingBarHelper.OnScreenDisplay;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.Power;
using XboxGamingBarHelper.Profile;
using XboxGamingBarHelper.RTSS;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Systems;
using XboxGamingBarHelper.AutoTDP;
using XboxGamingBarHelper.DefaultGameProfiles;
using XboxGamingBarHelper.Labs;
using Shared.Enums;

namespace XboxGamingBarHelper
{
    internal partial class Program
    {

        /// <summary>
        /// Initializes the hotkey manager and registers global hotkeys
        /// </summary>
        private static void InitializeHotkeyManager()
        {
            try
            {
                hotkeyManager = new HotkeyManager();

                // Register Ctrl+Shift+D for Desktop Controls toggle
                int hotkeyId = hotkeyManager.RegisterHotkey(
                    HotkeyManager.MOD_CONTROL | HotkeyManager.MOD_SHIFT,
                    HotkeyManager.VK_D,
                    ToggleDesktopControls);

                if (hotkeyId > 0)
                {
                    Logger.Info("Registered global hotkey Ctrl+Shift+D for Desktop Controls toggle");
                }
                else
                {
                    Logger.Warn("Failed to register Ctrl+Shift+D hotkey - may be in use by another application");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize hotkey manager: {ex.Message}");
            }

            // Initialize controller hotkey monitor for XInput-based button combos
            InitializeControllerHotkeyMonitor();
        }

        /// <summary>
        /// Initializes the controller hotkey monitor for XInput-based button combos.
        /// This allows detection of Menu+DPad and View+ABXY combos in games.
        /// </summary>
        private static void InitializeControllerHotkeyMonitor()
        {
            try
            {
                controllerHotkeyMonitor = new ControllerHotkeyMonitor();
                controllerHotkeyMonitor.LoadSettings();

                // Set up callbacks for each combo
                // These will execute the same actions as the Xbox Game Bar hotkey watchers
                // Names must match widget's LocalSettings keys (without "Hotkey_" prefix)
                controllerHotkeyMonitor.OnMenuDPadUp = () => ExecuteControllerHotkeyAction("MenuDpadUp");
                controllerHotkeyMonitor.OnMenuDPadDown = () => ExecuteControllerHotkeyAction("MenuDpadDown");
                controllerHotkeyMonitor.OnMenuDPadLeft = () => ExecuteControllerHotkeyAction("MenuDpadLeft");
                controllerHotkeyMonitor.OnMenuDPadRight = () => ExecuteControllerHotkeyAction("MenuDpadRight");
                controllerHotkeyMonitor.OnViewA = () => ExecuteControllerHotkeyAction("MenuA");
                controllerHotkeyMonitor.OnViewB = () => ExecuteControllerHotkeyAction("MenuB");
                controllerHotkeyMonitor.OnViewX = () => ExecuteControllerHotkeyAction("MenuX");
                controllerHotkeyMonitor.OnViewY = () => ExecuteControllerHotkeyAction("MenuY");

                controllerHotkeyMonitor.Start();
                Logger.Info("Controller hotkey monitor initialized for XInput-based button combos");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize controller hotkey monitor: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes the configured action for a controller hotkey combo.
        /// Uses cached config from Named Pipe, falls back to LocalSettings.
        /// </summary>
        private static void ExecuteControllerHotkeyAction(string hotkeyName)
        {
            try
            {
                int action = 0;
                string keyParam = "";
                bool foundInConfig = false;

                // Try cached config first (received from widget via pipe)
                if (_controllerHotkeyConfig != null)
                {
                    if (_controllerHotkeyConfig.TryGetValue($"{hotkeyName}_Action", out var actionElement))
                    {
                        action = actionElement.GetInt32();
                        foundInConfig = true;
                    }
                    if (_controllerHotkeyConfig.TryGetValue($"{hotkeyName}_Key", out var keyElement))
                        keyParam = keyElement.GetString() ?? "";
                }

                // Fallback to LocalSettings if not in cached config
                // Widget saves as "Hotkey_{hotkeyName}_Action" and "Hotkey_{hotkeyName}_Key"
                if (!foundInConfig)
                {
                    if (LocalSettingsHelper.TryGetValue<int>($"Hotkey_{hotkeyName}_Action", out var localAction))
                        action = localAction;
                    if (LocalSettingsHelper.TryGetValue<string>($"Hotkey_{hotkeyName}_Key", out var localKey))
                        keyParam = localKey ?? "";
                    Logger.Debug($"ExecuteControllerHotkeyAction: Using LocalSettings fallback for {hotkeyName}");
                }

                Logger.Info($"ExecuteControllerHotkeyAction: {hotkeyName} action={action} key={keyParam}");

                // HotkeyAction enum from widget:
                // 0=Disabled, 1=KeyboardKey, 2=KeyboardShortcut, 3=ToggleOSD, 4=Screenshot,
                // 5=AltTab, 6=AltF4, 7=OpenKeyboard, 8=CtrlAltDel, 9=TaskManager, 10=FocusGoTweaks
                switch (action)
                {
                    case 0: // Disabled
                        break;
                    case 1: // KeyboardKey - single key press
                        if (!string.IsNullOrEmpty(keyParam))
                        {
                            ExecuteKeyboardShortcut(keyParam);
                        }
                        break;
                    case 2: // KeyboardShortcut - combo like Ctrl+Alt+X
                        if (!string.IsNullOrEmpty(keyParam))
                        {
                            ExecuteKeyboardShortcut(keyParam);
                        }
                        break;
                    case 3: // Toggle OSD
                        ToggleOSD();
                        break;
                    case 4: // Screenshot (Win+Shift+S)
                        ExecuteKeyboardShortcut("Win+Shift+S");
                        break;
                    case 5: // Alt+Tab
                        ExecuteKeyboardShortcut("Alt+Tab");
                        break;
                    case 6: // Alt+F4
                        ExecuteKeyboardShortcut("Alt+F4");
                        break;
                    case 7: // Open On-Screen Keyboard
                        OpenOnScreenKeyboard();
                        break;
                    case 8: // Ctrl+Alt+Del
                        ExecuteKeyboardShortcut("Ctrl+Alt+Delete");
                        break;
                    case 9: // Task Manager (Ctrl+Shift+Esc)
                        ExecuteKeyboardShortcut("Ctrl+Shift+Escape");
                        break;
                    case 10: // Focus GoTweaks widget
                        FocusGoTweaksWidget();
                        break;
                    default:
                        Logger.Warn($"ExecuteControllerHotkeyAction: Unknown action {action} for {hotkeyName}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ExecuteControllerHotkeyAction: Error executing {hotkeyName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes a keyboard shortcut string (e.g., "Ctrl+Alt+Delete")
        /// Uses InputInjector which works properly from elevated helper context
        /// </summary>
        private static void ExecuteKeyboardShortcut(string shortcut)
        {
            try
            {
                Logger.Info($"ExecuteKeyboardShortcut: {shortcut}");
                // Use InputInjector (same as widget's SendKeyboardShortcutViaHelper)
                SendKeyboardShortcutViaInputInjector(shortcut);
            }
            catch (Exception ex)
            {
                Logger.Error($"ExecuteKeyboardShortcut: Failed to send {shortcut}: {ex.Message}");
            }
        }

        /// <summary>
        /// Opens the Windows on-screen keyboard using TouchKeyboardHelper
        /// </summary>
        private static void OpenOnScreenKeyboard()
        {
            try
            {
                Logger.Info("OpenOnScreenKeyboard: Toggling touch keyboard");
                TouchKeyboardHelper.Toggle();
                Logger.Info("OpenOnScreenKeyboard: Touch keyboard toggled");
            }
            catch (Exception ex)
            {
                Logger.Error($"OpenOnScreenKeyboard: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggles OSD visibility by cycling through OSD levels (0=Off, 1, 2, 3)
        /// </summary>
        private static void ToggleOSD()
        {
            try
            {
                if (onScreenDisplay == null)
                {
                    Logger.Warn("ToggleOSD: OSD property not initialized");
                    return;
                }

                // Cycle through levels: 0 -> 1 -> 2 -> 3 -> 0
                int currentLevel = onScreenDisplay.Value;
                int newLevel = (currentLevel + 1) % 4;  // 0, 1, 2, 3, then back to 0

                onScreenDisplay.SetValue(newLevel);
                Logger.Info($"ToggleOSD: OSD level changed from {currentLevel} to {newLevel}");
            }
            catch (Exception ex)
            {
                Logger.Error($"ToggleOSD: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply controller hotkey configuration received from the widget.
        /// Enables/disables XInput-based button combo detection.
        /// </summary>
        private static void ApplyControllerHotkeyConfig(string configJson)
        {
            try
            {
                Logger.Info($"Applying controller hotkey config from widget");

                var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(configJson);
                if (config == null || controllerHotkeyMonitor == null)
                {
                    Logger.Warn("ApplyControllerHotkeyConfig: Config is null or monitor not initialized");
                    return;
                }

                // Apply View + ABXY settings (View button = Back/two-squares button in XInput)
                // Widget saves as "Hotkey_MenuA_Action", etc.
                if (config.TryGetValue("MenuA_Action", out var menuAAction))
                    controllerHotkeyMonitor.ViewAEnabled = menuAAction.GetInt32() > 0;
                if (config.TryGetValue("MenuB_Action", out var menuBAction))
                    controllerHotkeyMonitor.ViewBEnabled = menuBAction.GetInt32() > 0;
                if (config.TryGetValue("MenuX_Action", out var menuXAction))
                    controllerHotkeyMonitor.ViewXEnabled = menuXAction.GetInt32() > 0;
                if (config.TryGetValue("MenuY_Action", out var menuYAction))
                    controllerHotkeyMonitor.ViewYEnabled = menuYAction.GetInt32() > 0;

                // Apply Menu + DPad settings (Menu button = Start/three-lines button in XInput)
                // Widget saves as "Hotkey_MenuDpadUp_Action", etc.
                if (config.TryGetValue("MenuDpadUp_Action", out var dpadUpAction))
                    controllerHotkeyMonitor.MenuDPadUpEnabled = dpadUpAction.GetInt32() > 0;
                if (config.TryGetValue("MenuDpadDown_Action", out var dpadDownAction))
                    controllerHotkeyMonitor.MenuDPadDownEnabled = dpadDownAction.GetInt32() > 0;
                if (config.TryGetValue("MenuDpadLeft_Action", out var dpadLeftAction))
                    controllerHotkeyMonitor.MenuDPadLeftEnabled = dpadLeftAction.GetInt32() > 0;
                if (config.TryGetValue("MenuDpadRight_Action", out var dpadRightAction))
                    controllerHotkeyMonitor.MenuDPadRightEnabled = dpadRightAction.GetInt32() > 0;

                // Store config for action execution
                _controllerHotkeyConfig = config;

                Logger.Info($"Controller hotkey config applied - Menu+DPad: Up={controllerHotkeyMonitor.MenuDPadUpEnabled}, Down={controllerHotkeyMonitor.MenuDPadDownEnabled}, Left={controllerHotkeyMonitor.MenuDPadLeftEnabled}, Right={controllerHotkeyMonitor.MenuDPadRightEnabled}");
                Logger.Info($"Controller hotkey config applied - View+ABXY: A={controllerHotkeyMonitor.ViewAEnabled}, B={controllerHotkeyMonitor.ViewBEnabled}, X={controllerHotkeyMonitor.ViewXEnabled}, Y={controllerHotkeyMonitor.ViewYEnabled}");
            }
            catch (Exception ex)
            {
                Logger.Error($"ApplyControllerHotkeyConfig: {ex.Message}");
            }
        }

        // Cached controller hotkey config for action execution
        private static Dictionary<string, System.Text.Json.JsonElement> _controllerHotkeyConfig;

        /// <summary>
        /// Toggles Desktop Controls preset via global hotkey (Ctrl+Shift+D)
        /// Applies Joystick-as-Mouse and button mappings for desktop navigation
        /// </summary>
        private static void ToggleDesktopControls()
        {
            try
            {
                if (legionManager == null || !legionManager.LegionGoDetected.Value)
                {
                    Logger.Warn("ToggleDesktopControls: Legion Go not detected, skipping");
                    return;
                }

                // Toggle the state
                bool newState = !legionManager.LegionDesktopControls.Value;
                Logger.Info($"ToggleDesktopControls: Hotkey pressed, toggling from {!newState} to {newState}");

                if (newState)
                {
                    // Enable Desktop Controls:
                    // 1. Set Right Stick as Mouse (mode 2)
                    legionManager.LegionJoystickAsMouseMode.ForceSetValue(2);

                    // 2. Apply desktop button mappings JSON
                    // Format: {"ButtonName":{"Type":X,"GamepadAction":Y,"KeyboardKeys":[...],"MouseButton":Z},...}
                    // Desktop Controls preset: DPAD/LS→Arrows, LSClick→Win, A→Enter, B→Esc, LB→LClick, LT→RClick
                    string desktopMappingsJson = @"{
                        ""DPadUp"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[82],""MouseButton"":0},
                        ""DPadDown"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[81],""MouseButton"":0},
                        ""DPadLeft"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[80],""MouseButton"":0},
                        ""DPadRight"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[79],""MouseButton"":0},
                        ""LSUp"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[82],""MouseButton"":0},
                        ""LSDown"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[81],""MouseButton"":0},
                        ""LSClick"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[227],""MouseButton"":0},
                        ""A"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[40],""MouseButton"":0},
                        ""B"":{""Type"":1,""GamepadAction"":0,""KeyboardKeys"":[41],""MouseButton"":0},
                        ""LB"":{""Type"":2,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LT"":{""Type"":2,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":1}
                    }";
                    legionManager.LegionGamepadMapping.ForceSetValue(desktopMappingsJson);
                }
                else
                {
                    // Disable Desktop Controls:
                    // 1. Disable Joystick as Mouse (mode 0)
                    legionManager.LegionJoystickAsMouseMode.ForceSetValue(0);

                    // 2. Clear button mappings (empty JSON resets to defaults)
                    string resetMappingsJson = @"{
                        ""DPadUp"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""DPadDown"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""DPadLeft"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""DPadRight"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LSUp"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LSDown"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LSClick"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""A"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""B"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LB"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0},
                        ""LT"":{""Type"":0,""GamepadAction"":0,""KeyboardKeys"":[],""MouseButton"":0}
                    }";
                    legionManager.LegionGamepadMapping.ForceSetValue(resetMappingsJson);
                }

                // Update the Desktop Controls property (syncs to widget UI)
                legionManager.LegionDesktopControls.ForceSetValue(newState);

                Logger.Info($"ToggleDesktopControls: Desktop Controls now {(newState ? "ENABLED" : "DISABLED")}");
            }
            catch (Exception ex)
            {
                Logger.Error($"ToggleDesktopControls: Error toggling desktop controls: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Helper class to toggle the Windows touch keyboard via COM interop
    /// </summary>
    internal static class TouchKeyboardHelper
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376")]
        private class UIHostNoLaunch { }

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("37c994e7-432b-4834-a2f7-dce1f13b834b")]
        [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITipInvocation
        {
            void Toggle(IntPtr hwnd);
        }

        public static void Toggle()
        {
            try
            {
                var uiHostNoLaunch = new UIHostNoLaunch();
                var tipInvocation = (ITipInvocation)uiHostNoLaunch;
                tipInvocation.Toggle(IntPtr.Zero);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(uiHostNoLaunch);
                Logger.Info("Touch keyboard toggle executed via COM");
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Logger.Error($"COM error toggling touch keyboard: {ex.Message}");
                // Fallback: try launching TabTip.exe
                TryLaunchTabTip();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error toggling touch keyboard: {ex.Message}");
                TryLaunchTabTip();
            }
        }

        private static void TryLaunchTabTip()
        {
            try
            {
                var tabtipPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                    "microsoft shared", "ink", "TabTip.exe");

                if (System.IO.File.Exists(tabtipPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = tabtipPath,
                        UseShellExecute = true
                    });
                    Logger.Info("Launched TabTip.exe as fallback");
                }
                else
                {
                    Logger.Warn("TabTip.exe not found for fallback");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to launch TabTip.exe: {ex.Message}");
            }
        }
    }
}
