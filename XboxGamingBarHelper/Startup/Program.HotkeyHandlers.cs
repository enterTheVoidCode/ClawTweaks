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

                // HC pattern: if ClawButtonMonitor was started before us (it is — see Initialize order),
                // wire the HotkeyFeed now so the MonitorLoop never opens a second DirectInput instance.
                // FeedButtons(0) pre-sets _externalFeedActive=true before Start() launches the thread.
                lock (clawButtonMonitorLock)
                {
                    if (clawButtonMonitor != null)
                    {
                        clawButtonMonitor.HotkeyFeed = controllerHotkeyMonitor.FeedButtons;
                        controllerHotkeyMonitor.FeedButtons(0); // pre-arm: disables DI path from first tick
                        Logger.Info("ControllerHotkeyMonitor: HotkeyFeed wired to existing ClawButtonMonitor (late-wire, HC single-reader pattern)");
                    }
                }

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
        /// <summary>
        /// Cycle the OSD overlay level 0→1→2→3→0. Returns the new level, or -1 on failure.
        /// </summary>
        private static int ToggleOSD()
        {
            try
            {
                if (onScreenDisplay == null)
                {
                    Logger.Warn("ToggleOSD: OSD property not initialized");
                    return -1;
                }

                // Cycle through levels: 0 -> 1 -> 2 -> 3 -> 0
                int currentLevel = onScreenDisplay.Value;
                int newLevel = (currentLevel + 1) % 4;  // 0, 1, 2, 3, then back to 0

                onScreenDisplay.SetValue(newLevel);
                Logger.Info($"ToggleOSD: OSD level changed from {currentLevel} to {newLevel}");
                return newLevel;
            }
            catch (Exception ex)
            {
                Logger.Error($"ToggleOSD: {ex.Message}");
                return -1;
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
        /// Parse the tile-hotkey JSON received from the widget and register combos
        /// in the ControllerHotkeyMonitor so they fire actions system-wide.
        ///
        /// JSON format: [{"id":"...","name":"...","mask":4112,"actionType":10,"shortcut":"..."}]
        ///
        /// TileActionType values:
        ///   0=None, 1=KeyboardShortcut, 10=BrightnessUp, 11=BrightnessDown,
        ///   12=AltTab, 13=AltTabBack, 14=GoToDesktop,
        ///   20=CycleOverlayMode, 21=CycleTDPMode, 22=TDPStepUp, 23=TDPStepDown
        /// </summary>
        private static void ApplyTileHotkeys(string json)
        {
            try
            {
                if (controllerHotkeyMonitor == null) return;

                controllerHotkeyMonitor.ClearTileHotkeys();

                if (string.IsNullOrEmpty(json) || json == "[]") return;

                var entries = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(json);
                if (entries == null) return;

                int registered = 0;
                foreach (var entry in entries)
                {
                    uint mask = 0;
                    int actionType = 0;
                    string shortcut = "";
                    string name = "Tile";
                    string id = "";

                    if (entry.TryGetProperty("id", out var idEl))
                        id = idEl.GetString() ?? "";
                    if (entry.TryGetProperty("mask", out var maskEl))
                        mask = (uint)maskEl.GetInt64(); // uint to support M1/M2 bits (0x10000, 0x20000)
                    if (entry.TryGetProperty("actionType", out var atEl))
                        actionType = atEl.GetInt32();
                    if (entry.TryGetProperty("shortcut", out var scEl))
                        shortcut = scEl.GetString() ?? "";
                    if (entry.TryGetProperty("name", out var nameEl))
                        name = nameEl.GetString() ?? "Tile";

                    Logger.Debug($"ApplyTileHotkeys: Entry id='{id}' name='{name}' mask=0x{mask:X5} action={actionType} shortcut='{shortcut}'");
                    if (mask == 0) { Logger.Warn("ApplyTileHotkeys: Entry skipped — mask is 0"); continue; }

                    // Capture for lambda
                    int capturedActionType = actionType;
                    string capturedShortcut = shortcut;
                    string capturedName = name;
                    string capturedId = id;

                    Action callback = () =>
                    {
                        Logger.Info($"ApplyTileHotkeys: Tile '{capturedName}' (id='{capturedId}') triggered (action={capturedActionType}, shortcut='{capturedShortcut}')");
                        bool skipGenericNotification = false;
                        switch (capturedActionType)
                        {
                            case 10: // BrightnessUp
                                AdjustBrightness(5);
                                break;
                            case 11: // BrightnessDown
                                AdjustBrightness(-5);
                                break;
                            case 12: // AltTab
                                ExecuteKeyboardShortcut("Alt+Tab");
                                break;
                            case 13: // AltTabBack — cycle to previous app
                                ExecuteKeyboardShortcut("Alt+Tab");
                                break;
                            case 14: // GoToDesktop
                                ExecuteKeyboardShortcut("Win+D");
                                break;
                            case 20: // CycleOverlayMode — can run locally on helper side
                                ToggleOSD();
                                break;
                            case 25: // TDPIncrBy1W — adjust current TDP +1 W via PerformanceManager
                                AdjustTDPByWatts(+1);
                                break;
                            case 26: // TDPDecrBy1W
                                AdjustTDPByWatts(-1);
                                break;
                            case 27: // VolumeUp
                                AdjustVolume(+5);
                                break;
                            case 28: // VolumeDown
                                AdjustVolume(-5);
                                break;
                            case 29: // ToggleControllerMouseMode — direct toggle in helper, no pipe roundtrip needed
                                ToggleControllerMouseModeInHelper(); // shows its own "Hotkey: Mouse/Controller" notification
                                skipGenericNotification = true;
                                break;
                            case 1:  // KeyboardShortcut (custom)
                                if (!string.IsNullOrEmpty(capturedShortcut))
                                    ExecuteKeyboardShortcut(capturedShortcut);
                                break;
                            case 0:  // None — use shortcut field; if empty, forward to widget
                                // Helper fires directly — no Win+G, no GameBar interaction.
                                // Key goes straight to the foreground game window.
                                //
                                // !! DO NOT CHANGE TO User32.SendKeyboardShortcut !!
                                // InputInjector MUST be used here. Switching to Win32 SendInput
                                // broke OptiScaler (Insert key stopped reaching the game).
                                // InputInjector correctly delivers extended nav keys (Home/Insert)
                                // to in-process overlays. Win32 path had timing/flag differences
                                // that OptiScaler's hook rejected. Tested: v0.1.197.234.
                                if (!string.IsNullOrEmpty(capturedShortcut))
                                {
                                    ExecuteKeyboardShortcut(capturedShortcut);
                                }
                                else
                                {
                                    // Built-in tiles that have no keyboard shortcut need a helper-side
                                    // action: when the Game Bar is closed the widget is suspended, so
                                    // FireTileHotkeyToWidget reaches nothing. Run their action directly
                                    // in the helper instead. (Tiles WITH a shortcut — OptiScaler/ReShade —
                                    // never enter this branch and stay untouched.)
                                    switch (capturedId)
                                    {
                                        case "Keyboard":
                                            // Toggle the on-screen / touch keyboard (same as the tile click).
                                            TouchKeyboardHelper.Toggle();
                                            break;
                                        case "Overlay":
                                            // Cycle the OSD overlay levels 0→1→2→3→0 (same states the
                                            // tile dropdown exposes) and show the level we switched to —
                                            // including "Off" (level 0) — instead of the generic name.
                                            int overlayLevel = ToggleOSD();
                                            if (overlayLevel >= 0)
                                            {
                                                // Map the level to the same friendly names the widget's
                                                // overlay dropdown uses, so the OSD shows e.g. "Overlay: Horizontal"
                                                // instead of a bare level code.
                                                string[] overlayNames = { "Off", "Basic", "Horizontal", "H. Detailed", "Full" };
                                                string overlayLabel = (overlayLevel >= 0 && overlayLevel < overlayNames.Length)
                                                    ? overlayNames[overlayLevel]
                                                    : overlayLevel.ToString();
                                                rtssManager?.ShowNotification($"Overlay: {overlayLabel}", 4000);
                                                skipGenericNotification = true;
                                            }
                                            break;
                                        default:
                                            FireTileHotkeyToWidget(capturedId, capturedName);
                                            break;
                                    }
                                }
                                break;
                            default:
                                // Widget-side app actions (21=CycleTDPMode, 22=TDPStepUp, 23=TDPStepDown,
                                // 24=CycleLimiterMode) and any other unknown action: forward to widget.
                                // If a shortcut is also set, execute it in addition.
                                if (!string.IsNullOrEmpty(capturedShortcut))
                                    ExecuteKeyboardShortcut(capturedShortcut);
                                FireTileHotkeyToWidget(capturedId, capturedName);
                                break;
                        }
                        // Show RTSS notification — "Hotkey: <name>" for 4 seconds
                        // (skipped for case 29 which shows its own "Hotkey: Mouse/Controller" notification)
                        if (!skipGenericNotification)
                            rtssManager?.ShowNotification($"Hotkey: {capturedName}", 4000);
                    };

                    controllerHotkeyMonitor.RegisterTileHotkey(mask, callback, name);
                    registered++;
                }

                Logger.Info($"ApplyTileHotkeys: Registered {registered} tile hotkeys from widget");
            }
            catch (Exception ex)
            {
                Logger.Error($"ApplyTileHotkeys: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a TileHotkeyFired notification back to the widget so it can execute
        /// widget-side actions (TDP cycle, FPS cycle, Desktop Mode, etc.) that the
        /// helper cannot perform directly.
        /// </summary>
        private static void FireTileHotkeyToWidget(string tileId, string tileName)
        {
            try
            {
                if (pipeServer == null || !pipeServer.IsConnected)
                {
                    Logger.Warn($"FireTileHotkeyToWidget: pipe not connected, cannot fire '{tileName}' (id='{tileId}')");
                    return;
                }

                // Build a minimal JSON message the widget handles in PipeClient_MessageReceived
                string json = $"{{\"TileHotkeyFired\":\"{tileId.Replace("\"", "\\\"")}\",\"TileName\":\"{tileName.Replace("\"", "\\\"")}\"}}";
                pipeServer.SendMessage(json);
                Logger.Info($"FireTileHotkeyToWidget: sent TileHotkeyFired for '{tileName}' (id='{tileId}')");
            }
            catch (Exception ex)
            {
                Logger.Error($"FireTileHotkeyToWidget: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggles the MSI Claw Controller/Mouse mode directly in the helper.
        ///
        /// Reads the current mode from MsiClawControllerModeProperty, inverts it,
        /// calls OnMsiClawControllerModeChanged (same path as a widget tile tap), and
        /// optionally notifies the widget so the tile icon updates.
        ///
        /// Works even when the pipe is disconnected — the local toggle is authoritative.
        /// </summary>
        private static void ToggleControllerMouseModeInHelper()
        {
            try
            {
                if (msiClawControllerModeManager == null)
                {
                    Logger.Warn("ToggleControllerMouseModeInHelper: msiClawControllerModeManager not available");
                    return;
                }

                bool current = msiClawControllerModeManager.MsiClawControllerMode?.Value ?? true;
                bool newMode = !current;

                // Apply locally — identical to what the widget would trigger via pipe
                msiClawControllerModeManager.MsiClawControllerMode?.SetValue(newMode);

                string modeName = newMode ? "Controller" : "Mouse";
                Logger.Info($"ToggleControllerMouseModeInHelper: toggled → {modeName}");

                // Show OSD notification
                rtssManager?.ShowNotification($"Hotkey: {modeName}", 4000);

                // Notify widget so tile icon updates (best-effort — works when pipe connected)
                FireTileHotkeyToWidget("MSIClawDesktopMode", $"Mode: {modeName}");
            }
            catch (Exception ex)
            {
                Logger.Error($"ToggleControllerMouseModeInHelper: {ex.Message}");
            }
        }

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


        /// <summary>
        /// Parse and send a keyboard shortcut using InputInjector (works in widget context unlike SendInput)
        /// </summary>
        internal static void SendKeyboardShortcut(string shortcut) => SendKeyboardShortcutViaInputInjector(shortcut);

        /// <summary>
        /// Parse and send a keyboard shortcut using InputInjector (works in widget context unlike SendInput)
        /// </summary>
        private static void SendKeyboardShortcutViaInputInjector(string shortcut)
        {
            if (inputInjector == null)
            {
                Logger.Error("InputInjector not available - falling back to User32.SendKeyboardShortcut");
                Windows.User32.SendKeyboardShortcut(shortcut);
                return;
            }

            if (string.IsNullOrWhiteSpace(shortcut))
            {
                Logger.Warn("Empty shortcut string provided");
                return;
            }

            try
            {
                var parts = shortcut.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
                var keyInfos = new List<InjectedInputKeyboardInfo>();
                var modifierKeys = new List<ushort>();
                var mainKeys = new List<ushort>(); // Support multiple non-modifier keys

                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    var upper = trimmed.ToUpperInvariant();
                    ushort vk = 0;

                    if (upper == "CTRL" || upper == "CONTROL" || upper == "LCTRL" || upper == "LCONTROL")
                        vk = (ushort)VirtualKey.LeftControl;
                    else if (upper == "RCTRL" || upper == "RCONTROL")
                        vk = (ushort)VirtualKey.RightControl;
                    else if (upper == "ALT" || upper == "LALT")
                        vk = (ushort)VirtualKey.LeftMenu;
                    else if (upper == "RALT")
                        vk = (ushort)VirtualKey.RightMenu;
                    else if (upper == "SHIFT" || upper == "LSHIFT")
                        vk = (ushort)VirtualKey.LeftShift;
                    else if (upper == "RSHIFT")
                        vk = (ushort)VirtualKey.RightShift;
                    else if (upper == "WIN" || upper == "WINDOWS" || upper == "LWIN" || upper == "LMETA" || upper == "META")
                        vk = (ushort)VirtualKey.LeftWindows;
                    else if (upper == "RWIN" || upper == "RMETA")
                        vk = (ushort)VirtualKey.RightWindows;
                    else if (upper == "TAB")
                        vk = (ushort)VirtualKey.Tab;
                    else if (upper == "ENTER" || upper == "RETURN")
                        vk = (ushort)VirtualKey.Enter;
                    else if (upper == "ESCAPE" || upper == "ESC")
                        vk = (ushort)VirtualKey.Escape;
                    else if (upper == "SPACE")
                        vk = (ushort)VirtualKey.Space;
                    else if (upper == "BACKSPACE" || upper == "BACK")
                        vk = (ushort)VirtualKey.Back;
                    else if (upper == "DELETE" || upper == "DEL")
                        vk = (ushort)VirtualKey.Delete;
                    else if (upper == "HOME")
                        vk = (ushort)VirtualKey.Home;
                    else if (upper == "END")
                        vk = (ushort)VirtualKey.End;
                    else if (upper == "PGUP" || upper == "PAGEUP")
                        vk = (ushort)VirtualKey.PageUp;
                    else if (upper == "PGDN" || upper == "PAGEDOWN")
                        vk = (ushort)VirtualKey.PageDown;
                    else if (upper == "INSERT" || upper == "INS")
                        vk = (ushort)VirtualKey.Insert;
                    else if (upper == "UP")
                        vk = (ushort)VirtualKey.Up;
                    else if (upper == "DOWN")
                        vk = (ushort)VirtualKey.Down;
                    else if (upper == "LEFT")
                        vk = (ushort)VirtualKey.Left;
                    else if (upper == "RIGHT")
                        vk = (ushort)VirtualKey.Right;
                    else if (upper == "PAUSE")
                        vk = (ushort)VirtualKey.Pause;
                    else if (upper == "PRINTSCREEN" || upper == "PRTSC")
                        vk = (ushort)VirtualKey.Snapshot;
                    else if (upper == "VOLUME_UP" || upper == "VOLUMEUP")
                        vk = 0xAF; // VK_VOLUME_UP
                    else if (upper == "VOLUME_DOWN" || upper == "VOLUMEDOWN")
                        vk = 0xAE; // VK_VOLUME_DOWN
                    else if (upper == "VOLUME_MUTE" || upper == "VOLUMEMUTE" || upper == "MUTE")
                        vk = 0xAD; // VK_VOLUME_MUTE
                    else if (trimmed == "[" || upper == "LEFTBRACKET")
                        vk = 0xDB; // VK_OEM_4 (left bracket)
                    else if (trimmed == "]" || upper == "RIGHTBRACKET")
                        vk = 0xDD; // VK_OEM_6 (right bracket)
                    else if (upper.Length == 1)
                    {
                        char c = upper[0];
                        if (c >= 'A' && c <= 'Z')
                            vk = (ushort)(VirtualKey.A + (c - 'A'));
                        else if (c >= '0' && c <= '9')
                            vk = (ushort)(VirtualKey.Number0 + (c - '0'));
                    }
                    else if (upper.StartsWith("F") && upper.Length <= 3)
                    {
                        if (int.TryParse(upper.Substring(1), out int fNum) && fNum >= 1 && fNum <= 24)
                            vk = (ushort)(VirtualKey.F1 + (fNum - 1));
                    }

                    if (vk == 0)
                    {
                        Logger.Warn($"Unknown key in shortcut: {trimmed}");
                        continue;
                    }

                    // Check if modifier
                    if (upper == "CTRL" || upper == "CONTROL" || upper == "LCTRL" || upper == "LCONTROL" ||
                        upper == "RCTRL" || upper == "RCONTROL" ||
                        upper == "ALT" || upper == "LALT" || upper == "RALT" ||
                        upper == "SHIFT" || upper == "LSHIFT" || upper == "RSHIFT" ||
                        upper == "WIN" || upper == "WINDOWS" || upper == "LWIN" || upper == "RWIN" ||
                        upper == "LMETA" || upper == "RMETA" || upper == "META")
                    {
                        modifierKeys.Add(vk);
                    }
                    else
                    {
                        mainKeys.Add(vk); // Add to list instead of overwriting
                    }
                }

                // Build key sequence: press modifiers, press all main keys, release all main keys in reverse, release modifiers
                // Press modifiers
                foreach (var mod in modifierKeys)
                {
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = mod, KeyOptions = InjectedInputKeyOptions.None });
                }

                // Extended navigation keys require InjectedInputKeyOptions.ExtendedKey so they
                // are not misinterpreted as numpad keys by the OS or low-level hooks.
                var extendedVKs = new HashSet<ushort> {
                    0x21, 0x22, 0x23, 0x24, // PageUp, PageDown, End, Home
                    0x25, 0x26, 0x27, 0x28, // Arrow keys Left/Up/Right/Down
                    0x2D, 0x2E,             // Insert, Delete
                    0x5B, 0x5C,             // Win keys
                    0x2C,                   // PrintScreen
                };

                // Press all main keys
                foreach (var key in mainKeys)
                {
                    var opts = extendedVKs.Contains(key)
                        ? InjectedInputKeyOptions.ExtendedKey
                        : InjectedInputKeyOptions.None;
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = key, KeyOptions = opts });
                }

                // Release all main keys in reverse order
                for (int i = mainKeys.Count - 1; i >= 0; i--)
                {
                    var opts = extendedVKs.Contains(mainKeys[i])
                        ? InjectedInputKeyOptions.ExtendedKey | InjectedInputKeyOptions.KeyUp
                        : InjectedInputKeyOptions.KeyUp;
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = mainKeys[i], KeyOptions = opts });
                }

                // Release modifiers in reverse order
                for (int i = modifierKeys.Count - 1; i >= 0; i--)
                {
                    keyInfos.Add(new InjectedInputKeyboardInfo { VirtualKey = modifierKeys[i], KeyOptions = InjectedInputKeyOptions.KeyUp });
                }

                inputInjector.InjectKeyboardInput(keyInfos);
                Logger.Info($"Sent keyboard shortcut via InputInjector: {shortcut} (modifiers: {modifierKeys.Count}, keys: {mainKeys.Count})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending keyboard shortcut '{shortcut}': {ex.Message}");
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

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // Touch keyboard host window class. When the on-screen keyboard is shown this
        // window is visible; when hidden it isn't (or doesn't exist yet).
        private const string TouchKeyboardWindowClass = "IPTip_Main_Window";

        /// <summary>True when the on-screen / touch keyboard is currently shown.</summary>
        private static bool IsKeyboardVisible()
        {
            try
            {
                IntPtr hwnd = FindWindow(TouchKeyboardWindowClass, null);
                return hwnd != IntPtr.Zero && IsWindowVisible(hwnd);
            }
            catch { return false; }
        }

        /// <summary>
        /// Reliably SHOW the keyboard. Used by the Quick Settings "Keyboard" tile, whose
        /// intent is "open the keyboard" — unlike the controller hotkey which keeps the raw
        /// COM toggle. Because <see cref="Toggle"/> is a blind toggle, clicking the tile while
        /// the keyboard is already up (e.g. opened earlier via the hotkey) would close it and
        /// look broken. Here we only toggle when it isn't already visible, so the tile always
        /// ends with the keyboard shown.
        /// </summary>
        public static void EnsureOpen()
        {
            try
            {
                if (IsKeyboardVisible())
                {
                    Logger.Info("Touch keyboard already visible — tile open is a no-op");
                    return;
                }
                Toggle(); // closed → open
            }
            catch (Exception ex)
            {
                Logger.Error($"EnsureOpen error: {ex.Message}");
                TryLaunchTabTip();
            }
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
