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
                Logger.Info("OpenOnScreenKeyboard: opening keyboard (smart: modern, or OSK over browsers)");
                TouchKeyboardHelper.OpenSmart(false); // controller shortcut → Game Bar already closed
                Logger.Info("OpenOnScreenKeyboard: keyboard open requested");
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

                // Cycle through levels: 0 (off) -> 1 (Basic) -> 2 (Horizontal) -> 3 (Detailed) -> 4 (Full) -> 0
                int currentLevel = onScreenDisplay.Value;
                int newLevel = (currentLevel + 1) % 5;  // include level 4 (Full)

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
        /// Opens a game launcher in the user session. Works whether the launcher is already
        /// running (foreground or background) or not yet started.
        ///   - SteamBigPicture: steam://open/bigpicture URI — handled by the user-session Steam
        ///     (running or not) and forces Big Picture; using the URI (not an elevated child
        ///     process) keeps Steam out of an elevated context.
        ///   - Playnite: launches %LOCALAPPDATA%\Playnite\Playnite.FullscreenApp.exe (single-instance,
        ///     brings itself to the foreground if already running).
        ///   - XboxApp: launches the Xbox (Game Pass) app via its AppsFolder AUMID.
        /// </summary>
        // Tracks whether the standalone "app mode" ClawTweaks window is currently open (reported by the
        // widget via Function.AppModeWindowState). Drives the toggle below.
        private static volatile bool _appModeWindowOpen;

        /// <summary>Updates the tracked app-mode window open/closed state (from the widget notification).</summary>
        internal static void SetAppModeWindowOpen(bool open)
        {
            _appModeWindowOpen = open;
            Logger.Info($"App-mode window state: {(open ? "open" : "closed")}");
        }

        /// <summary>
        /// Toggle for the "Open ClawTweaks Window" action / front MSI button: if the standalone app-mode
        /// window is open, ask the widget to close it; otherwise launch it. Pessimistically flips the
        /// tracked state on close so a missed notification can't wedge the toggle.
        /// </summary>
        internal static void ToggleClawTweaksWindow()
        {
            if (_appModeWindowOpen)
            {
                Logger.Info("ToggleClawTweaksWindow: window open → requesting close");
                _appModeWindowOpen = false;
                try
                {
                    SendPipeMessage(new Shared.IPC.PipeMessage
                    {
                        Command = Shared.Enums.Command.Set,
                        Function = Shared.Enums.Function.CloseAppModeWindow,
                        Content = "close"
                    });
                }
                catch (Exception ex) { Logger.Warn($"ToggleClawTweaksWindow close failed: {ex.Message}"); }
            }
            else
            {
                Logger.Info("ToggleClawTweaksWindow: window closed → launching");
                LaunchLauncher("ClawTweaksWindow");
            }
        }

        internal static async void LaunchLauncher(string which)
        {
            try
            {
                switch (which)
                {
                    case "SteamBigPicture":
                        await global::Windows.System.Launcher.LaunchUriAsync(new Uri("steam://open/bigpicture"));
                        Logger.Info("LaunchLauncher: Steam Big Picture requested (steam://open/bigpicture)");
                        break;

                    case "Playnite":
                        {
                            string playnite = System.IO.Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "Playnite", "Playnite.FullscreenApp.exe");
                            if (System.IO.File.Exists(playnite))
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = playnite,
                                    WorkingDirectory = System.IO.Path.GetDirectoryName(playnite),
                                    UseShellExecute = true
                                });
                                Logger.Info($"LaunchLauncher: Playnite Fullscreen launched from {playnite}");
                            }
                            else
                            {
                                Logger.Warn($"LaunchLauncher: Playnite not found at {playnite}");
                            }
                        }
                        break;

                    case "XboxApp":
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = @"shell:AppsFolder\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App",
                            UseShellExecute = true
                        });
                        Logger.Info("LaunchLauncher: Xbox app launched via AppsFolder AUMID");
                        break;

                    case "ClawTweaksWindow":
                        // Open ClawTweaks as a standalone desktop window (UWP "app mode", App.OnLaunched) —
                        // an alternative to the Game Bar widget. The helper runs elevated and Windows refuses
                        // to activate a packaged app directly from an elevated process, so we delegate to
                        // explorer.exe (de-elevates) with the app's AppsFolder AUMID, exactly like XboxApp.
                        // PFN is the fixed package identity (CN=ClawTweaks Dev, O=MSIClaw).
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = @"shell:AppsFolder\MSIClaw.ClawTweaks_7eszav2039cvc!App",
                            UseShellExecute = true
                        });
                        Logger.Info("LaunchLauncher: ClawTweaks standalone window launched via AppsFolder AUMID");
                        break;

                    default:
                        Logger.Warn($"LaunchLauncher: unknown launcher '{which}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"LaunchLauncher({which}): {ex.Message}");
            }
        }

        /// <summary>
        /// Launches a "Program Action" target. Handles:
        ///  - "@DefaultBrowser": resolves the registered http handler and launches it
        ///  - "*.ps1": runs via powershell.exe -File
        ///  - a URI scheme (ms-windows-store:, spotify:, http(s)://): LaunchUriAsync
        ///  - "chrome" or any other token/path: Process.Start with UseShellExecute (App Paths / PATH)
        /// Runs in the user session, so it works whether the Game Bar is open or not.
        /// </summary>
        internal static async void LaunchProgramTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            try
            {
                target = target.Trim().Trim('"');

                if (string.Equals(target, "@DefaultBrowser", StringComparison.OrdinalIgnoreCase))
                {
                    LaunchDefaultBrowser();
                    return;
                }

                if (string.Equals(target, "@ClawTweaksCenter", StringComparison.OrdinalIgnoreCase))
                {
                    string centerExe = ResolveClawTweaksCenterExe();
                    if (centerExe == null)
                    {
                        Logger.Warn("LaunchProgramTarget: ClawTweaks Center is not installed — nothing to launch.");
                        return;
                    }
                    target = centerExe;   // fall through to the normal exe path below
                }

                string ext = "";
                try { ext = System.IO.Path.GetExtension(target).ToLowerInvariant(); } catch { }

                if (ext == ".ps1")
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{target}\"",
                        UseShellExecute = true,
                        WorkingDirectory = SafeDir(target)
                    });
                    Logger.Info($"LaunchProgramTarget: ran PowerShell script {target}");
                    return;
                }

                // URI scheme (contains "://" or "scheme:") and not a drive path like C:\...
                int colon = target.IndexOf(':');
                bool looksLikeScheme = target.Contains("://") ||
                    (colon > 1 && !(colon == 1 && target.Length > 2 && (target[2] == '\\' || target[2] == '/')));
                if (looksLikeScheme && ext != ".exe")
                {
                    await global::Windows.System.Launcher.LaunchUriAsync(new Uri(target));
                    Logger.Info($"LaunchProgramTarget: launched URI {target}");
                    return;
                }

                // Plain exe path or a bare command (e.g. "chrome") resolved via App Paths / PATH.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true,
                    WorkingDirectory = SafeDir(target)
                });
                Logger.Info($"LaunchProgramTarget: started process {target}");
            }
            catch (Exception ex)
            {
                Logger.Error($"LaunchProgramTarget({target}): {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves the installed ClawTweaks Center executable, or null when Center is not installed.
        /// Prefers the Add/Remove-Programs registry entry that Center's own SelfInstaller writes
        /// (Doku/PLAN_Center_Helper_Integration.md 3b) - that is the same mechanism Windows uses, so it
        /// survives a non-default install location. The fixed Program Files path is only the fallback.
        /// The registry read lives here, in the full-trust helper: the widget is a sandboxed UWP process
        /// and cannot read HKLM at all.
        /// </summary>
        internal static string ResolveClawTweaksCenterExe()
        {
            const string ExeName = "CTW_Center.exe";
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                           @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ClawTweaksCenter"))
                {
                    if (key != null)
                    {
                        // DisplayIcon is the full exe path; InstallLocation is the folder.
                        if (key.GetValue("DisplayIcon") is string icon &&
                            !string.IsNullOrWhiteSpace(icon) && System.IO.File.Exists(icon))
                            return icon;

                        if (key.GetValue("InstallLocation") is string dir && !string.IsNullOrWhiteSpace(dir))
                        {
                            string fromDir = System.IO.Path.Combine(dir, ExeName);
                            if (System.IO.File.Exists(fromDir)) return fromDir;
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.Debug($"ResolveClawTweaksCenterExe: registry lookup failed: {ex.Message}"); }

            try
            {
                string fallback = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "ClawTweaks Center", ExeName);
                if (System.IO.File.Exists(fallback)) return fallback;
            }
            catch (Exception ex) { Logger.Debug($"ResolveClawTweaksCenterExe: fallback probe failed: {ex.Message}"); }

            return null;
        }

        /// <summary>Opens a URL in the default browser. Works whether the Game Bar is open or not.</summary>
        internal static void LaunchUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                Logger.Info($"LaunchUrl: {url}");
            }
            catch (Exception ex) { Logger.Error($"LaunchUrl({url}): {ex.Message}"); }
        }

        /// <summary>Maps a built-in Program-Action id to its launch target, or returns the user param.</summary>
        internal static string ResolveProgramTargetHelper(int actionType, string param)
        {
            switch (actionType)
            {
                case 50: return "@DefaultBrowser";
                case 51: return "ms-windows-store:";
                case 52: return "chrome";
                case 53: return "spotify:";
                default: return param; // 59 = LaunchUserProgram
            }
        }

        /// <summary>Maps a built-in Launch-Website id to its URL, or returns the user param.</summary>
        internal static string ResolveWebsiteUrlHelper(int actionType, string param)
        {
            switch (actionType)
            {
                case 60: return "https://www.exophase.com/";
                case 61: return "https://retroachievements.org/";
                case 62: return "https://www.google.com/";
                case 63: return "https://github.com/enterTheVoidCode/ClawTweaks/releases";
                case 64: return "https://github.com/enterTheVoidCode/ClawTweaks";
                case 65: return "https://www.youtube.com/";
                default: return param; // 69 = OpenUserWebsite
            }
        }

        private static string SafeDir(string path)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                return System.IO.Directory.Exists(dir) ? dir : "";
            }
            catch { return ""; }
        }

        /// <summary>Resolves the user's default browser from the registry and launches it (no page).</summary>
        private static void LaunchDefaultBrowser()
        {
            try
            {
                string progId = null;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"))
                {
                    progId = key?.GetValue("ProgId") as string;
                }
                string command = null;
                if (!string.IsNullOrEmpty(progId))
                {
                    using (var cmdKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command"))
                    {
                        command = cmdKey?.GetValue(null) as string;
                    }
                }
                if (!string.IsNullOrEmpty(command))
                {
                    // command is like: "C:\...\app.exe" --args "%1"
                    string exe = command;
                    if (command.StartsWith("\""))
                    {
                        int end = command.IndexOf('"', 1);
                        if (end > 1) exe = command.Substring(1, end - 1);
                    }
                    else
                    {
                        int sp = command.IndexOf(' ');
                        if (sp > 0) exe = command.Substring(0, sp);
                    }
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exe,
                        UseShellExecute = true,
                        WorkingDirectory = SafeDir(exe)
                    });
                    Logger.Info($"LaunchDefaultBrowser: launched {exe} (ProgId={progId})");
                    return;
                }
                // Fallback: open about:blank, which the default browser handles.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://www.google.com/",
                    UseShellExecute = true
                });
                Logger.Info("LaunchDefaultBrowser: fallback via http URL");
            }
            catch (Exception ex)
            {
                Logger.Error($"LaunchDefaultBrowser: {ex.Message}");
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
                    string param = "";
                    if (entry.TryGetProperty("param", out var paramEl))
                        param = paramEl.GetString() ?? "";

                    Logger.Debug($"ApplyTileHotkeys: Entry id='{id}' name='{name}' mask=0x{mask:X5} action={actionType} shortcut='{shortcut}'");
                    if (mask == 0) { Logger.Warn("ApplyTileHotkeys: Entry skipped — mask is 0"); continue; }

                    // Capture for lambda
                    int capturedActionType = actionType;
                    string capturedShortcut = shortcut;
                    string capturedName = name;
                    string capturedId = id;
                    string capturedParam = param;

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
                            case 71: // ToggleHwMouse — force firmware HW mouse (UAC-clickable) / back to controller
                                ToggleHwMouseInHelper(); // shows its own "Hotkey: HW Mouse/Controller" notification
                                skipGenericNotification = true;
                                break;
                            case 40: // SteamBigPicture — launcher action, runs helper-side (GameBar closed)
                                LaunchLauncher("SteamBigPicture");
                                break;
                            case 41: // Playnite
                                LaunchLauncher("Playnite");
                                break;
                            case 42: // XboxApp
                                LaunchLauncher("XboxApp");
                                break;
                            case 43: // OpenClawTweaksWindow — toggle the standalone app-mode window (GameBar alternative)
                                ToggleClawTweaksWindow();
                                break;
                            case 30: // MediaNextTrack
                                ExecuteKeyboardShortcut("MEDIA_NEXT_TRACK");
                                break;
                            case 31: // MediaPrevTrack
                                ExecuteKeyboardShortcut("MEDIA_PREV_TRACK");
                                break;
                            case 32: // MediaPlayPause
                                ExecuteKeyboardShortcut("MEDIA_PLAY_PAUSE");
                                break;
                            case 50: // OpenDefaultBrowser
                            case 51: // OpenWindowsStore
                            case 52: // OpenChrome
                            case 53: // OpenSpotify
                            case 59: // LaunchUserProgram (param = exe/ps1 path)
                                LaunchProgramTarget(ResolveProgramTargetHelper(capturedActionType, capturedParam));
                                break;
                            case 60: // OpenExophase
                            case 61: // OpenRetroAchievements
                            case 62: // OpenGoogle
                            case 63: // OpenClawTweaksReleases
                            case 64: // OpenClawTweaksFaq
                            case 69: // OpenUserWebsite (param = url)
                                LaunchUrl(ResolveWebsiteUrlHelper(capturedActionType, capturedParam));
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
                                            // Open the keyboard (smart: modern, or OSK over windowed
                                            // browsers/Electron). Game Bar is closed here → no dismiss.
                                            TouchKeyboardHelper.OpenSmart(false);
                                            break;
                                        case "FpsLimiter":
                                            // Cycle FPS cap in the current mode (RTSS/Intel) — helper-side
                                            // so it works with the Game Bar closed. Shows its own notification.
                                            CycleFpsLimitFromHotkey();
                                            skipGenericNotification = true;
                                            break;
                                        case "ChargeLimiter":
                                            // Toggle battery charge limit on/off using the persisted value.
                                            ToggleMsiChargeLimitFromHotkey();
                                            skipGenericNotification = true;
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

                // NOTE: do NOT FireTileHotkeyToWidget here. The helper already toggled the mode
                // authoritatively above; the widget's MsiClawControllerMode property is synced
                // from that SetValue and refreshes the tile icon via QuickSettingsProperty_Changed.
                // Firing TileHotkeyFired would make the widget re-run the tile's toggle action
                // (SimulateTileHotkeyFired → QuickSettingsTile_Click), double-toggling the mode
                // back so it appeared to never change.
            }
            catch (Exception ex)
            {
                Logger.Error($"ToggleControllerMouseModeInHelper: {ex.Message}");
            }
        }

        /// <summary>
        /// HW-mouse killswitch toggle from a controller hotkey. Sets the helper-authoritative property,
        /// which routes to Enter/ExitHwMouseKillswitch and syncs the tile. Note: while HW mouse is ON the
        /// controller buttons drive the OS cursor, so a CONTROLLER hotkey can reliably turn it ON but not
        /// OFF — the tile (clickable with the HW mouse) or a keyboard combo is the way back.
        /// </summary>
        private static void ToggleHwMouseInHelper()
        {
            try
            {
                if (msiClawHwMouseManager == null)
                {
                    Logger.Warn("ToggleHwMouseInHelper: msiClawHwMouseManager not available");
                    return;
                }

                bool current = msiClawHwMouseManager.MsiClawHwMouse?.Value ?? false;
                bool newState = !current;
                msiClawHwMouseManager.MsiClawHwMouse?.SetValue(newState);

                string label = newState ? "HW Mouse" : "Controller";
                Logger.Info($"ToggleHwMouseInHelper: toggled → {label}");
                // OSD is shown by Enter/ExitHwMouseKillswitch (covers all trigger paths); no duplicate here.
            }
            catch (Exception ex)
            {
                Logger.Error($"ToggleHwMouseInHelper: {ex.Message}");
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
        // Staged-injection timing for modifier combos (see SendKeyboardShortcutViaInputInjector).
        // Long enough that in-game overlay hooks reliably observe the modifier as held before the
        // main key arrives; short enough to feel instant.
        private const int ShortcutModifierHoldMs = 25;
        private const int ShortcutKeyPressMs = 25;

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

            // A lone navigation/edit key (no modifier) — Home/End/Insert/Delete/PageUp/PageDown — is
            // the toggle key of in-game post-processing overlays (ReShade=Home, OptiScaler=Insert).
            // Those overlays react ONLY to the legacy keybd_event API, not to InputInjector or SendInput
            // (proven on-device: Diagnostics/Test-ReShadeHome.ps1). Route just these keys through
            // keybd_event so EVERY caller of this method — Quick Settings tiles fired by a controller
            // combo, M1/M2 keyboard remaps, the front-button/hotkey paths — can open the overlay. All
            // other shortcuts (Tab, Win+D, Shift+Tab for the Steam overlay, media keys, etc.) keep the
            // existing InputInjector path untouched.
            {
                var lone = shortcut.Trim();
                if (lone.IndexOf('+') < 0)
                {
                    switch (lone.ToUpperInvariant())
                    {
                        case "HOME":
                        case "END":
                        case "INSERT":
                        case "INS":
                        case "DELETE":
                        case "DEL":
                        case "PGUP":
                        case "PAGEUP":
                        case "PGDN":
                        case "PAGEDOWN":
                            Logger.Info($"Routing lone overlay-toggle key '{lone}' via keybd_event (ReShade/OptiScaler compatible)");
                            Windows.User32.SendKeyboardShortcutViaKeybdEvent(lone);
                            return;
                    }
                }
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
                    else if (upper == "MEDIA_NEXT_TRACK" || upper == "MEDIANEXTTRACK")
                        vk = 0xB0; // VK_MEDIA_NEXT_TRACK
                    else if (upper == "MEDIA_PREV_TRACK" || upper == "MEDIAPREVTRACK")
                        vk = 0xB1; // VK_MEDIA_PREV_TRACK
                    else if (upper == "MEDIA_PLAY_PAUSE" || upper == "MEDIAPLAYPAUSE")
                        vk = 0xB3; // VK_MEDIA_PLAY_PAUSE
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

                // NOTE: do NOT set InjectedInputKeyOptions.ExtendedKey here. We previously flagged
                // nav keys (Insert/Home/PgUp/PgDn/arrows/Delete) as extended, believing it was
                // required so they aren't read as numpad keys — but an on-device SendInput probe
                // (Diagnostics/Test-InsertInjection.ps1) proved the OPPOSITE on the MSI Claw: an
                // Insert event carrying the extended flag is silently DROPPED (never reaches the OS,
                // invisible even to a global AHK keyboard hook), while a plain VK-only Insert
                // registers correctly as VK 0x2D. That's exactly why the OptiScaler (Insert) and
                // ReShade (Home) tiles fired nothing. InputInjector is VirtualKey-based and derives
                // the extended bit from the VK itself, so an explicit ExtendedKey flag is both
                // unnecessary and harmful. Plain VirtualKey down/up for every key.
                var modsDown = new List<InjectedInputKeyboardInfo>();
                var mainsDown = new List<InjectedInputKeyboardInfo>();
                var mainsUp = new List<InjectedInputKeyboardInfo>();
                var modsUp = new List<InjectedInputKeyboardInfo>();

                foreach (var mod in modifierKeys)
                    modsDown.Add(new InjectedInputKeyboardInfo { VirtualKey = mod, KeyOptions = InjectedInputKeyOptions.None });
                foreach (var key in mainKeys)
                    mainsDown.Add(new InjectedInputKeyboardInfo { VirtualKey = key, KeyOptions = InjectedInputKeyOptions.None });
                for (int i = mainKeys.Count - 1; i >= 0; i--)
                    mainsUp.Add(new InjectedInputKeyboardInfo { VirtualKey = mainKeys[i], KeyOptions = InjectedInputKeyOptions.KeyUp });
                for (int i = modifierKeys.Count - 1; i >= 0; i--)
                    modsUp.Add(new InjectedInputKeyboardInfo { VirtualKey = modifierKeys[i], KeyOptions = InjectedInputKeyOptions.KeyUp });

                keyInfos.AddRange(modsDown);
                keyInfos.AddRange(mainsDown);
                keyInfos.AddRange(mainsUp);
                keyInfos.AddRange(modsUp);

                if (modifierKeys.Count == 0)
                {
                    // No modifier → a single atomic batch is fine (e.g. F12/Tab/Insert/Home).
                    inputInjector.InjectKeyboardInput(keyInfos);
                }
                else
                {
                    // Modifier combo (e.g. Shift+Tab for the Steam in-game overlay): a single atomic
                    // batch sends modifier-down and key-down effectively simultaneously. In-game overlay
                    // hooks that sample modifier state on the key-down via GetAsyncKeyState then miss the
                    // modifier (it hasn't propagated yet) — so the combo does nothing in-game, even though
                    // VK-based detectors like Steam's settings "detect hotkey" UI see it fine. Press the
                    // modifiers first, hold briefly, then the main key, mimicking real hardware timing.
                    // Run off-thread so a caller like the ClawButtonMonitor poll loop is never stalled.
                    var injector = inputInjector;
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            injector.InjectKeyboardInput(modsDown);
                            System.Threading.Thread.Sleep(ShortcutModifierHoldMs);
                            injector.InjectKeyboardInput(mainsDown);
                            System.Threading.Thread.Sleep(ShortcutKeyPressMs);
                            injector.InjectKeyboardInput(mainsUp);
                            injector.InjectKeyboardInput(modsUp);
                        }
                        catch (Exception ex) { Logger.Error($"Staged shortcut injection failed '{shortcut}': {ex.Message}"); }
                    });
                }

                // Diagnostic: a hotkey that "does nothing" is almost always one of — wrong VK
                // parse, the injector path not taken, the target filtering injected input
                // (many games / anti-cheat ignore InjectedInput), or the wrong window having
                // focus. Logging the decoded VK sequence (XX = vk, v=down ^=up) plus the
                // foreground window lets all of those be told apart from the log alone.
                string vkDump = string.Join(" ", keyInfos.Select(k =>
                    $"{k.VirtualKey:X2}{((k.KeyOptions & InjectedInputKeyOptions.KeyUp) != 0 ? "^" : "v")}"));
                Logger.Info($"Injected shortcut '{shortcut}' [{vkDump}] (mods={modifierKeys.Count}, keys={mainKeys.Count}) → foreground: {Windows.User32.GetForegroundWindowDescription()}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending keyboard shortcut '{shortcut}': {ex.Message}");
            }
        }

        /// <summary>True while the Game Bar overlay is foreground, per the widget's own pushed
        /// foreground signal (settingsManager.IsForeground, Function.Foreground — same source
        /// GameBarAutoNav and ControllerEmulationManager rely on). This reflects the UWP widget's
        /// own activation state, so unlike matching GetForegroundWindow()'s owning process by name
        /// it isn't thrown off by which process actually owns the overlay's top-level window.</summary>
        internal static bool IsGameBarWidgetForeground()
        {
            try { return settingsManager?.IsForeground?.Value ?? false; }
            catch { return false; }
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
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // Process names whose foreground window means the Game Bar overlay is up (mirrors
        // ControllerEmulationManager.GameBarForegroundProcessNames).
        private static readonly System.Collections.Generic.HashSet<string> GameBarProcNames =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "GameBar", "GameBarFTServer", "GameBarElevatedFT", "XboxGameBarWidgets", "XboxGamingBar", "XboxGameBar" };

        /// <summary>True when the Game Bar overlay is up right now. Prefers the widget's own pushed
        /// foreground signal (reliable — reflects the UWP widget's real activation state) and falls
        /// back to matching GetForegroundWindow()'s owning process by name (belt-and-suspenders for
        /// callers where the signal hasn't synced yet).</summary>
        private static bool IsGameBarForeground()
        {
            if (Program.IsGameBarWidgetForeground()) return true;
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return false;
                GetWindowThreadProcessId(h, out uint pid);
                string pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
                return GameBarProcNames.Contains(pn);
            }
            catch { return false; }
        }

        // Touch keyboard host window class. When the on-screen keyboard is shown this
        // window is visible; when hidden it isn't (or doesn't exist yet).
        private const string TouchKeyboardWindowClass = "IPTip_Main_Window";
        // Classic On-Screen Keyboard (osk.exe) main window class — used for the fallback.
        private const string OskWindowClass = "OSKMainClass";

        /// <summary>True when the modern touch keyboard (TabTip) is currently shown.</summary>
        private static bool IsKeyboardVisible()
        {
            try
            {
                IntPtr hwnd = FindWindow(TouchKeyboardWindowClass, null);
                return hwnd != IntPtr.Zero && IsWindowVisible(hwnd);
            }
            catch { return false; }
        }

        /// <summary>True when the classic OSK (osk.exe) is currently shown.</summary>
        private static bool IsOskVisible()
        {
            try
            {
                IntPtr hwnd = FindWindow(OskWindowClass, null);
                return hwnd != IntPtr.Zero && IsWindowVisible(hwnd);
            }
            catch { return false; }
        }

        /// <summary>Fallback: launch the classic On-Screen Keyboard (osk.exe). Unlike the modern touch
        /// keyboard, it reliably shows over ANY window (browsers/Electron included) and is a signed
        /// Windows accessibility tool. No-op if it's already up.</summary>
        private static void LaunchOsk()
        {
            try
            {
                if (IsOskVisible()) { Logger.Info("OSK already visible — skipping fallback launch"); return; }
                string osk = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "osk.exe");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = osk, UseShellExecute = true });
                Logger.Info("Fallback: launched osk.exe (classic on-screen keyboard)");
            }
            catch (Exception ex) { Logger.Error($"LaunchOsk failed: {ex.Message}"); }
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
                if (IsKeyboardVisible()) { Logger.Info("Touch keyboard already visible — tile open is a no-op"); return; }
                Toggle();
            }
            catch (Exception ex)
            {
                Logger.Error($"EnsureOpen error: {ex.Message}");
                TryLaunchTabTip();
            }
        }

        // Window classes of Chromium/Electron desktop apps (browsers, Claude Desktop, Discord, VS Code…)
        // and Firefox — where the modern touch keyboard won't force-show on demand. Over a WINDOWED one
        // of these we go straight to the classic OSK. Fullscreen contexts (Steam Big Picture, games)
        // behave tablet-like and keep the modern keyboard.
        private static bool ShouldUseOskForForeground()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return false;
                var cn = new System.Text.StringBuilder(128);
                GetClassName(h, cn, 128);
                string cls = cn.ToString();
                bool problematic = cls == "Chrome_WidgetWin_1" || cls == "MozillaWindowClass";
                if (!problematic) return false;
                if (GetWindowRect(h, out RECT r))
                {
                    int sw = GetSystemMetrics(0 /*SM_CXSCREEN*/), sh = GetSystemMetrics(1 /*SM_CYSCREEN*/);
                    if ((r.Right - r.Left) >= sw && (r.Bottom - r.Top) >= sh) return false; // fullscreen → modern
                }
                Logger.Info($"Touch keyboard: foreground '{cls}' is a windowed browser/Electron app → using classic OSK");
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Smart open used by the Quick Settings "Keyboard" tile AND the controller shortcut. Picks the
        /// modern touch keyboard where it works, or the classic OSK over windowed browsers/Electron apps
        /// where the modern one won't stay. When invoked from the Game Bar tile (dismissGameBar=true) it
        /// closes Game Bar first so the app BEHIND it becomes foreground and the decision targets it —
        /// exactly like the controller shortcut (Game Bar already closed). Runs off the pipe thread.
        /// </summary>
        public static void OpenSmart(bool dismissGameBar)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // Only dismiss Game Bar if it is ACTUALLY the foreground right now. Otherwise Win+G
                    // would OPEN it — the bug where a controller/button binding (Game Bar already closed)
                    // spuriously popped Game Bar. When it's not up, the app is already foreground and no
                    // dismiss is needed.
                    if (dismissGameBar && IsGameBarForeground())
                    {
                        try { Program.SendKeyboardShortcut("Win+G"); } catch (Exception ex) { Logger.Debug($"OpenSmart Win+G: {ex.Message}"); }
                        System.Threading.Thread.Sleep(500); // let the app behind Game Bar regain foreground
                    }
                    if (ShouldUseOskForForeground()) { LaunchOsk(); return; }
                    Toggle(); // modern touch keyboard — same raw toggle the controller shortcut uses
                }
                catch (Exception ex)
                {
                    Logger.Error($"OpenSmart error: {ex.Message}");
                    LaunchOsk();
                }
            });
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
