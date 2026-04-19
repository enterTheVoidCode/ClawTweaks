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

        // NOTE: Connection_RequestReceived (AppService handler) was removed - using Named Pipes only
        // See PipeServer_MessageReceived below for the pipe-based message handler

        // The following code was Connection_RequestReceived content - it has been removed
        // since we now use PipeServer_MessageReceived for all widget communication


        /// <summary>
        /// Handles messages received from the widget via Named Pipe
        /// </summary>
        private static async void PipeServer_MessageReceived(object sender, IPC.PipeMessageEventArgs e)
        {
            try
            {
                var pipeMsg = Shared.IPC.PipeMessage.FromJson(e.Message);
                Logger.Info($"Helper received pipe message: {pipeMsg}");

                // Convert to ValueSet for compatibility with existing handlers
                var valueSet = pipeMsg.ToValueSet();

                // Handle power plan change request
                if (pipeMsg.Extra.TryGetValue("PowerPlan", out object powerPlanValue) && powerPlanValue is string guidStr)
                {
                    if (Guid.TryParse(guidStr, out Guid planGuid))
                    {
                        Logger.Info($"Setting power plan to: {planGuid}");
                        Power.PowerManager.SetActivePowerPlan(planGuid);
                    }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Handle keyboard shortcut request
                if (pipeMsg.Extra.TryGetValue("SendKeyboardShortcut", out object shortcutValue) && shortcutValue is string shortcutStr)
                {
                    Logger.Info($"Sending keyboard shortcut via InputInjector: {shortcutStr}");
                    SendKeyboardShortcutViaInputInjector(shortcutStr);
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Handle close game request
                if (pipeMsg.Extra.ContainsKey("CloseGame"))
                {
                    Logger.Info("CloseGame request received - attempting to close foreground window");
                    bool success = Windows.User32.CloseForegroundWindow();
                    Logger.Info($"CloseGame result: {success}");
                    SendPipeAck(pipeMsg.RequestId, success);
                    return;
                }

                // Handle touch keyboard toggle request
                if (pipeMsg.Extra.ContainsKey("ToggleTouchKeyboard"))
                {
                    try
                    {
                        Logger.Info("Pipe: ToggleTouchKeyboard request received");
                        TouchKeyboardHelper.Toggle();
                        Logger.Info("Pipe: Touch keyboard toggled");
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to toggle touch keyboard: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle LaunchUrl request (for Donate button, etc.)
                if (pipeMsg.Extra.TryGetValue("LaunchUrl", out object urlValue) && urlValue is string url)
                {
                    try
                    {
                        Logger.Info($"Pipe: LaunchUrl request received: {url}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                        Logger.Info("Pipe: URL launched successfully");
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to launch URL: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle hibernate request
                if (pipeMsg.Extra.ContainsKey("Hibernate"))
                {
                    try
                    {
                        Logger.Info("Pipe: Hibernate request received - initiating system hibernation");
                        bool success = Windows.PowrProf.SetSuspendState(
                            bHibernate: true,      // Hibernate, not sleep
                            bForce: false,         // Don't force - let apps save work
                            bWakeupEventsDisabled: false);  // Allow wake events

                        if (success)
                        {
                            Logger.Info("Pipe: Hibernate initiated successfully");
                        }
                        else
                        {
                            int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                            Logger.Error($"Pipe: Failed to initiate hibernate, Win32 error: {error}");
                        }
                        SendPipeAck(pipeMsg.RequestId, success);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to hibernate: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle export logs request
                if (pipeMsg.Extra.ContainsKey("ExportLogs"))
                {
                    Logger.Info("Pipe: ExportLogs request received");
                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var exportFolder = Path.Combine(desktopPath, $"GoTweaks_Logs_{timestamp}");

                        // Create export folder
                        Directory.CreateDirectory(exportFolder);
                        var helperFolder = Path.Combine(exportFolder, "Helper");
                        var widgetFolder = Path.Combine(exportFolder, "Widget");
                        Directory.CreateDirectory(helperFolder);
                        Directory.CreateDirectory(widgetFolder);

                        // Get log paths from app package location
                        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        var packageFolder = Path.Combine(localAppData, "Packages", "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg");
                        var helperLogPath = localAppData; // Helper logs are in %LocalAppData% when elevated
                        var widgetLogPath = Path.Combine(packageFolder, "LocalState");

                        // Copy helper logs (last 2) - check both locations
                        var helperLogs = new List<string>();
                        if (Directory.Exists(helperLogPath))
                        {
                            helperLogs.AddRange(Directory.GetFiles(helperLogPath, "helper_*.log"));
                        }
                        var packageHelperLogPath = Path.Combine(packageFolder, "LocalCache", "Local");
                        if (Directory.Exists(packageHelperLogPath))
                        {
                            helperLogs.AddRange(Directory.GetFiles(packageHelperLogPath, "helper_*.log"));
                        }
                        foreach (var log in helperLogs.OrderByDescending(f => File.GetLastWriteTime(f)).Take(2))
                        {
                            var destPath = Path.Combine(helperFolder, Path.GetFileName(log));
                            File.Copy(log, destPath, true);
                            Logger.Info($"Copied: {Path.GetFileName(log)}");
                        }

                        // Copy widget logs (last 2)
                        if (Directory.Exists(widgetLogPath))
                        {
                            var widgetLogs = Directory.GetFiles(widgetLogPath, "widget_*.log")
                                .OrderByDescending(f => File.GetLastWriteTime(f))
                                .Take(2);

                            foreach (var log in widgetLogs)
                            {
                                var destPath = Path.Combine(widgetFolder, Path.GetFileName(log));
                                File.Copy(log, destPath, true);
                                Logger.Info($"Copied: {Path.GetFileName(log)}");
                            }
                        }

                        Logger.Info($"Pipe: Logs exported to: {exportFolder}");
                        response.Add("Success", true);
                        response.Add("Path", exportFolder);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to export logs: {ex.Message}");
                        response.Add("Success", false);
                        response.Add("Error", ex.Message);
                    }

                    // Send response
                    if (pipeServer != null && pipeServer.IsConnected)
                    {
                        var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                        responseMsg.RequestId = pipeMsg.RequestId;
                        pipeServer.SendMessage(responseMsg.ToJson());
                    }
                    return;
                }

                // Handle export profiles request
                if (pipeMsg.Extra.ContainsKey("ExportProfiles"))
                {
                    Logger.Info("Pipe: ExportProfiles request received");
                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var exportPath = Path.Combine(desktopPath, $"GoTweaks_Profiles_{timestamp}.xml");

                        // Collect all profiles for export
                        var gameProfilesList = new List<Shared.Data.GameProfile>();
                        foreach (var kvp in profileManager.GameProfiles)
                        {
                            gameProfilesList.Add(kvp.Value);
                        }

                        // Get app version from assembly
                        var appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

                        // Parse global widget settings from message (sent by widget as serialized GlobalWidgetSettings)
                        Shared.Data.GlobalWidgetSettings globalSettings = null;
                        if (pipeMsg.Extra.TryGetValue("GlobalSettings", out object gsXml) && gsXml is string gsXmlStr)
                        {
                            try
                            {
                                // Deserialize the GlobalWidgetSettings directly
                                globalSettings = Shared.Utilities.XmlHelper.FromXMLString<Shared.Data.GlobalWidgetSettings>(gsXmlStr);
                                if (globalSettings != null)
                                {
                                    Logger.Info("Pipe: Global widget settings included in export");
                                    Logger.Info($"  TDP Limits: Min={globalSettings.DeviceTDPMin}, Max={globalSettings.DeviceTDPMax}");
                                }
                            }
                            catch (Exception gsEx)
                            {
                                Logger.Warn($"Pipe: Failed to parse global settings: {gsEx.Message}");
                            }
                        }

                        // Create export container
                        var export = new Shared.Data.ProfileExport(
                            profileManager.GlobalProfile,
                            gameProfilesList,
                            appVersion,
                            globalSettings
                        );

                        // Serialize to XML file
                        if (Shared.Utilities.XmlHelper.ToXMLFile(export, exportPath))
                        {
                            Logger.Info($"Pipe: Profiles exported to: {exportPath}");
                            Logger.Info($"  Global profile + {gameProfilesList.Count} game profile(s) exported");
                            if (globalSettings != null)
                                Logger.Info("  Global widget settings (Legion buttons, scroll wheel, TDP limits, OSD) included");
                            response.Add("Success", true);
                            response.Add("Path", exportPath);
                            response.Add("ProfileCount", gameProfilesList.Count + 1); // +1 for global
                        }
                        else
                        {
                            throw new Exception("Failed to write XML file");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to export profiles: {ex.Message}");
                        response.Add("Success", false);
                        response.Add("Error", ex.Message);
                    }

                    // Send response
                    if (pipeServer != null && pipeServer.IsConnected)
                    {
                        var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                        responseMsg.RequestId = pipeMsg.RequestId;
                        pipeServer.SendMessage(responseMsg.ToJson());
                    }
                    return;
                }

                // Handle import profiles request
                if (pipeMsg.Extra.ContainsKey("ImportProfiles"))
                {
                    Logger.Info("Pipe: ImportProfiles request received");
                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        // Get import path from message
                        var importPath = pipeMsg.Extra["ImportProfiles"] as string;
                        if (string.IsNullOrEmpty(importPath) || !File.Exists(importPath))
                        {
                            throw new Exception($"Import file not found: {importPath}");
                        }

                        Logger.Info($"Pipe: Importing profiles from: {importPath}");

                        // Deserialize the export file
                        var import = Shared.Utilities.XmlHelper.FromXMLFile<Shared.Data.ProfileExport>(importPath);
                        if (import == null)
                        {
                            throw new Exception("Failed to parse import file");
                        }

                        Logger.Info($"Pipe: Import file version {import.Version}, created {import.ExportDate} by app v{import.AppVersion}");

                        int importedCount = 0;
                        int skippedCount = 0;

                        // Import global profile settings (merge into existing global)
                        var globalProfile = profileManager.GlobalProfile;
                        globalProfile.TDP = import.GlobalProfile.TDP;
                        globalProfile.CPUBoost = import.GlobalProfile.CPUBoost;
                        globalProfile.CPUEPP = import.GlobalProfile.CPUEPP;
                        globalProfile.MaxCPUState = import.GlobalProfile.MaxCPUState;
                        globalProfile.MinCPUState = import.GlobalProfile.MinCPUState;
                        globalProfile.TDPBoostEnabled = import.GlobalProfile.TDPBoostEnabled;
                        globalProfile.TDP_DC = import.GlobalProfile.TDP_DC;
                        globalProfile.CPUBoost_DC = import.GlobalProfile.CPUBoost_DC;
                        globalProfile.CPUEPP_DC = import.GlobalProfile.CPUEPP_DC;
                        globalProfile.MaxCPUState_DC = import.GlobalProfile.MaxCPUState_DC;
                        globalProfile.MinCPUState_DC = import.GlobalProfile.MinCPUState_DC;
                        globalProfile.FPSLimit = import.GlobalProfile.FPSLimit;
                        globalProfile.FPSLimit_DC = import.GlobalProfile.FPSLimit_DC;
                        globalProfile.OSPowerMode = import.GlobalProfile.OSPowerMode;
                        globalProfile.OSPowerMode_DC = import.GlobalProfile.OSPowerMode_DC;
                        Logger.Info("Global profile settings imported");
                        importedCount++;

                        // Import game profiles
                        var profilesFolder = Profile.ProfileManager.GetGameProfilesFolder();
                        foreach (var gameProfile in import.GameProfiles)
                        {
                            try
                            {
                                // Generate profile path from game executable name
                                var exeName = Path.GetFileNameWithoutExtension(gameProfile.GameId.Path);
                                if (string.IsNullOrEmpty(exeName))
                                {
                                    Logger.Warn($"Skipping profile with empty path: {gameProfile.GameId.Name}");
                                    skippedCount++;
                                    continue;
                                }

                                var profilePath = Path.Combine(profilesFolder, $"{exeName}.xml");

                                // Create a copy with the correct path set
                                var importedProfile = gameProfile;
                                importedProfile.Path = profilePath;

                                // Serialize directly to file
                                if (Shared.Utilities.XmlHelper.ToXMLFile(importedProfile, profilePath))
                                {
                                    Logger.Info($"Imported profile for: {gameProfile.GameId.Name}");
                                    importedCount++;
                                }
                                else
                                {
                                    Logger.Warn($"Failed to save profile for: {gameProfile.GameId.Name}");
                                    skippedCount++;
                                }
                            }
                            catch (Exception profileEx)
                            {
                                Logger.Warn($"Failed to import profile {gameProfile.GameId.Name}: {profileEx.Message}");
                                skippedCount++;
                            }
                        }

                        Logger.Info($"Pipe: Import complete - {importedCount} profile(s) imported, {skippedCount} skipped");
                        response.Add("Success", true);
                        response.Add("ImportedCount", importedCount);
                        response.Add("SkippedCount", skippedCount);
                        response.Add("Message", $"Imported {importedCount} profile(s). Restart the helper to load imported game profiles.");

                        // Return global widget settings to widget for restoration
                        if (import.GlobalSettings != null)
                        {
                            try
                            {
                                var globalSettingsXml = Shared.Utilities.XmlHelper.ToXMLString(import.GlobalSettings, true);
                                response.Add("GlobalSettings", globalSettingsXml);
                                Logger.Info("Pipe: Global widget settings returned to widget for restoration");
                            }
                            catch (Exception gsEx)
                            {
                                Logger.Warn($"Pipe: Failed to serialize global settings for response: {gsEx.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to import profiles: {ex.Message}");
                        response.Add("Success", false);
                        response.Add("Error", ex.Message);
                    }

                    // Send response
                    if (pipeServer != null && pipeServer.IsConnected)
                    {
                        var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                        responseMsg.RequestId = pipeMsg.RequestId;
                        pipeServer.SendMessage(responseMsg.ToJson());
                    }
                    return;
                }

                // Handle exit helper request (for version mismatch restart - legacy)
                if (pipeMsg.Extra.ContainsKey("ExitHelper"))
                {
                    Logger.Info("Pipe: ExitHelper request received - shutting down helper for restart");
                    LogManager.Flush(); // Ensure log is written before we start shutdown
                    SendPipeAck(pipeMsg.RequestId);
                    _isShuttingDown = true;
                    // Main loop will exit, Initialize() returns, finally block releases mutex naturally
                    // No Environment.Exit needed - process will exit cleanly via Main() return
                    return;
                }

                // Handle upgrade helper request (UAC-free upgrade - preferred)
                // The widget sends the MSIX source path so we can copy files after exit
                if (pipeMsg.Extra.ContainsKey("UpgradeHelper"))
                {
                    string msixSourcePath = pipeMsg.Extra["UpgradeHelper"]?.ToString();
                    if (!string.IsNullOrEmpty(msixSourcePath))
                    {
                        Logger.Info($"Pipe: UpgradeHelper request received - source: {msixSourcePath}");
                        LogManager.Flush(); // Ensure log is written before we start shutdown
                        SendPipeAck(pipeMsg.RequestId);

                        // Launch upgrade script that will copy files and restart after we exit
                        LaunchUpgradeScript(msixSourcePath);

                        _isShuttingDown = true;

                        // Schedule a forced exit
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(2000); // Give main loop time to exit gracefully
                            if (_isShuttingDown)
                            {
                                Logger.Info("Forcing exit for upgrade");
                                LogManager.Flush(); // Ensure log is written before exit

                                // Release mutex before exiting to ensure clean restart
                                try
                                {
                                    singleInstanceMutex?.ReleaseMutex();
                                    singleInstanceMutex?.Dispose();
                                }
                                catch { /* Ignore mutex errors during shutdown */ }

                                Environment.Exit(0);
                            }
                        });
                    }
                    else
                    {
                        Logger.Warn("Pipe: UpgradeHelper request missing source path");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle batch get request (for fast property sync)
                if (pipeMsg.Command == Shared.Enums.Command.BatchGet)
                {
                    await HandleBatchGetRequestViaPipe(pipeMsg);
                    return;
                }

                // Handle property requests via the properties system
                if (pipeMsg.Function != Shared.Enums.Function.None)
                {
                    HandlePipePropertyRequest(pipeMsg);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling pipe message: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Handles property Get/Set requests from the pipe
        /// </summary>
        private static void HandlePipePropertyRequest(Shared.IPC.PipeMessage request)
        {
            try
            {
                global::Windows.Foundation.Collections.ValueSet response = null;

                // Handle special functions that are not in FunctionalProperties
                int functionValue = (int)request.Function;

                // Labs: DAService Status request
                if (functionValue == (int)Function.Labs_DAServiceStatus)
                {
                    int status = GetDAServiceStatus();
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", status);
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    Logger.Info($"Pipe: Labs DAService status = {status}");
                }
                // Labs: DAService Control request (Start/Stop)
                else if (functionValue == (int)Function.Labs_DAServiceControl)
                {
                    if (request.Content != null)
                    {
                        int action = Convert.ToInt32(request.Content);
                        ControlDAService(action);
                        System.Threading.Thread.Sleep(500);
                        int status = GetDAServiceStatus();
                        response = new global::Windows.Foundation.Collections.ValueSet();
                        response.Add(nameof(Function), (int)Function.Labs_DAServiceStatus);
                        response.Add("Content", status);
                        response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                        Logger.Info($"Pipe: Labs DAService control action={action}, new status={status}");
                    }
                }
                // Labs: Legion Button Remap
                else if (functionValue == (int)Function.Labs_LegionButtonRemap)
                {
                    string button = "L";
                    bool enabled = false;
                    int actionType = 0;
                    string shortcut = "";

                    if (request.Extra.TryGetValue("Button", out object buttonObj))
                        button = buttonObj?.ToString() ?? "L";
                    if (request.Extra.TryGetValue("Enabled", out object enabledObj))
                        enabled = Convert.ToBoolean(enabledObj);
                    if (request.Extra.TryGetValue("Action", out object actionObj))
                        actionType = Convert.ToInt32(actionObj);
                    if (request.Extra.TryGetValue("Shortcut", out object shortcutObj))
                        shortcut = shortcutObj?.ToString() ?? "";

                    bool success = ConfigureLegionButtonRemap(button, enabled, actionType, shortcut);
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Success", success);
                    Logger.Info($"Pipe: Legion {button} Remap - Enabled: {enabled}, Success: {success}");
                }
                // Labs: Scroll Wheel Remap
                else if (functionValue == (int)Function.Labs_LegionScrollRemap)
                {
                    string direction = "Up";
                    bool enabled = false;
                    int actionType = 0;
                    string shortcut = "";

                    if (request.Extra.TryGetValue("Direction", out object directionObj))
                        direction = directionObj?.ToString() ?? "Up";
                    if (request.Extra.TryGetValue("Enabled", out object enabledObj))
                        enabled = Convert.ToBoolean(enabledObj);
                    if (request.Extra.TryGetValue("Action", out object actionObj))
                        actionType = Convert.ToInt32(actionObj);
                    if (request.Extra.TryGetValue("Shortcut", out object shortcutObj))
                        shortcut = shortcutObj?.ToString() ?? "";

                    bool success = ConfigureLegionScrollRemap(direction, enabled, actionType, shortcut);
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Success", success);
                    Logger.Info($"Pipe: Scroll {direction} Remap - Enabled: {enabled}, Success: {success}");
                }
                // Controller Hotkey Config: Receive hotkey settings from widget for XInput monitoring
                else if (functionValue == (int)Function.ControllerHotkeyConfig)
                {
                    if (request.Content != null)
                    {
                        string configJson = request.Content.ToString();
                        ApplyControllerHotkeyConfig(configJson);
                    }
                }
                // Quick Metrics: Enable/disable metrics push timer
                else if (functionValue == (int)Function.QuickMetricsEnabled)
                {
                    if (request.Content != null && performanceManager != null)
                    {
                        bool enabled = false;
                        if (bool.TryParse(request.Content.ToString(), out enabled) || request.Content.ToString() == "True" || request.Content.ToString() == "true")
                        {
                            enabled = request.Content.ToString().ToLower() == "true" || request.Content.ToString() == "True";
                        }
                        performanceManager.QuickMetricsEnabled = enabled;
                        Logger.Info($"Pipe: Quick Metrics enabled set to: {enabled}");
                    }
                }
                // Screen Saver: Enable/disable idle-triggered screen saver
                else if (functionValue == (int)Function.ScreenSaverEnabled)
                {
                    if (request.Content != null)
                    {
                        bool enabled = request.Content.ToString().ToLower() == "true";
                        SetScreenSaverEnabled(enabled);
                        Logger.Info($"Pipe: Screen Saver enabled set to: {enabled}");
                    }
                }
                // Sidebar Menu: Enable/disable sidebar overlay mode
                else if (functionValue == (int)Function.SidebarMenuEnabled)
                {
                    if (request.Content != null)
                    {
                        sidebarMenuEnabled = request.Content.ToString().ToLower() == "true";
                        Properties.Settings.Default.SidebarMenuEnabled = sidebarMenuEnabled;
                        Properties.Settings.Default.Save();
                        Logger.Info($"Pipe: Sidebar Menu enabled set to: {sidebarMenuEnabled}");
                    }
                }
                // Auto Hibernate Mode: 0=Always, 1=AC Only, 2=DC Only
                else if (functionValue == (int)Function.AutoHibernateMode)
                {
                    if (request.Content != null && int.TryParse(request.Content.ToString(), out int mode))
                    {
                        autoHibernateMode = mode;
                        Logger.Info($"Pipe: Auto Hibernate mode set to: {mode} ({(mode == 0 ? "Always" : mode == 1 ? "AC Only" : "DC Only")})");
                    }
                }
                // ViGEmBus: Check installed status
                else if (functionValue == (int)Function.ViGEmBusInstalled)
                {
                    bool installed = XboxGamingBarHelper.Labs.ViGEmBusHelper.IsInstalled();
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", installed);
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    Logger.Info($"Pipe: ViGEmBus installed status: {installed}");
                }
                // ViGEmBus: Install request - only install when explicitly requested with Set command and "install" content
                else if (functionValue == (int)Function.InstallViGEmBus)
                {
                    // Check if this is a Set command with "install" content
                    bool shouldInstall = request.Command == Shared.Enums.Command.Set && request.Content == "install";

                    if (!shouldInstall)
                    {
                        Logger.Debug("Pipe: InstallViGEmBus - Ignoring non-install request (Get or empty content)");
                        return;
                    }

                    Logger.Info("Pipe: ViGEmBus installation requested from widget");
                    _ = Task.Run(() =>
                    {
                        bool success = XboxGamingBarHelper.Labs.ViGEmBusHelper.Install();
                        bool installed = XboxGamingBarHelper.Labs.ViGEmBusHelper.IsInstalled();
                        // Send updated status via pipe
                        var updateMsg = new Shared.IPC.PipeMessage
                        {
                            Command = Shared.Enums.Command.Set,
                            Function = Function.ViGEmBusInstalled,
                            Content = installed.ToString()
                        };
                        SendPipeMessage(updateMsg);
                        Logger.Info($"Pipe: ViGEmBus installation complete, sent updated status: {installed}");
                    });
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true); // Acknowledge request started
                }
                // HidHide: Check installed status
                else if (functionValue == (int)Function.HidHideInstalled)
                {
                    bool installed = XboxGamingBarHelper.Labs.HidHideHelper.IsInstalled();
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", installed);
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    Logger.Info($"Pipe: HidHide installed status: {installed}");
                }
                // HidHide: Install request - only install when explicitly requested with Set command and "install" content
                else if (functionValue == (int)Function.InstallHidHide)
                {
                    bool shouldInstall = request.Command == Shared.Enums.Command.Set && request.Content == "install";

                    if (!shouldInstall)
                    {
                        Logger.Debug("Pipe: InstallHidHide - Ignoring non-install request (Get or empty content)");
                        return;
                    }

                    Logger.Info("Pipe: HidHide installation requested from widget");
                    _ = Task.Run(() =>
                    {
                        bool success = XboxGamingBarHelper.Labs.HidHideHelper.Install();
                        bool installed = XboxGamingBarHelper.Labs.HidHideHelper.IsInstalled();
                        var updateMsg = new Shared.IPC.PipeMessage
                        {
                            Command = Shared.Enums.Command.Set,
                            Function = Function.HidHideInstalled,
                            Content = installed.ToString()
                        };
                        SendPipeMessage(updateMsg);
                        Logger.Info($"Pipe: HidHide installation complete (success={success}), sent updated status: {installed}");
                    });

                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true); // Acknowledge request started
                }
                // Debug: Export Default Game Profiles
                else if (functionValue == (int)Function.Debug_ExportDGPs)
                {
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        string exportPath = ExportDefaultGameProfiles();
                        response.Add("ExportPath", exportPath);
                        Logger.Info($"Pipe: DGPs exported to {exportPath}");
                    }
                    catch (Exception ex)
                    {
                        response.Add("Error", ex.Message);
                        Logger.Error($"Pipe: Failed to export DGPs: {ex.Message}");
                    }
                }
                // Debug: Export Per-Game Profiles
                else if (functionValue == (int)Function.Debug_ExportProfiles)
                {
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        string exportPath = ExportProfiles();
                        response.Add("ExportPath", exportPath);
                        Logger.Info($"Pipe: Profiles exported to {exportPath}");
                    }
                    catch (Exception ex)
                    {
                        response.Add("Error", ex.Message);
                        Logger.Error($"Pipe: Failed to export profiles: {ex.Message}");
                    }
                }
                // Debug: Check for local update (AppPackages)
                else if (functionValue == (int)Function.CheckLocalUpdate)
                {
                    Logger.Info("Pipe: CheckLocalUpdate request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();

                    try
                    {
                        const string appPackagesPath = @"C:\Users\diego\OneDrive\Desktop\Diego\projects\XboxGamingBar\XboxGamingBarPackage\AppPackages";

                        if (!Directory.Exists(appPackagesPath))
                        {
                            response.Add("Error", $"AppPackages folder not found:\n{appPackagesPath}");
                        }
                        else
                        {
                            // Get all package folders and find the latest version
                            var packageFolders = Directory.GetDirectories(appPackagesPath)
                                .Where(d => Path.GetFileName(d).StartsWith("XboxGamingBarPackage_"))
                                .ToList();

                            if (packageFolders.Count == 0)
                            {
                                response.Add("Error", "No package folders found in AppPackages");
                            }
                            else
                            {
                                // Parse versions from folder names (e.g., XboxGamingBarPackage_0.3.98.0_Debug_Test)
                                string latestFolder = null;
                                string latestVersionStr = null;
                                Version latestVersion = null;

                                foreach (var folder in packageFolders)
                                {
                                    var folderName = Path.GetFileName(folder);
                                    var parts = folderName.Split('_');
                                    if (parts.Length >= 2)
                                    {
                                        var versionStr = parts[1];
                                        if (Version.TryParse(versionStr, out var version))
                                        {
                                            if (latestVersion == null || version > latestVersion)
                                            {
                                                latestVersion = version;
                                                latestVersionStr = versionStr;
                                                latestFolder = folder;
                                            }
                                        }
                                    }
                                }

                                if (latestFolder == null)
                                {
                                    response.Add("Error", "Could not parse version from folder names");
                                }
                                else
                                {
                                    // Find .msixbundle in the folder
                                    var msixbundleFiles = Directory.GetFiles(latestFolder, "*.msixbundle", SearchOption.AllDirectories);
                                    if (msixbundleFiles.Length == 0)
                                    {
                                        response.Add("Error", $"No .msixbundle found in:\n{Path.GetFileName(latestFolder)}");
                                    }
                                    else
                                    {
                                        var msixbundlePath = msixbundleFiles[0];
                                        Logger.Info($"Pipe: Found local update: version={latestVersionStr}, path={msixbundlePath}");

                                        response.Add("LatestVersion", latestVersionStr);
                                        response.Add("MsixbundlePath", msixbundlePath);
                                        response.Add("FolderName", Path.GetFileName(latestFolder));
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to check for local update: {ex.Message}");
                        response.Add("Error", $"Failed: {ex.Message}");
                    }
                }
                // Debug/Development: Install Update (download and install from URL or local path)
                else if (functionValue == (int)Function.InstallUpdate)
                {
                    var updatePath = request.Content;
                    Logger.Info($"Pipe: InstallUpdate request received: {updatePath}");
                    response = new global::Windows.Foundation.Collections.ValueSet();

                    if (string.IsNullOrEmpty(updatePath))
                    {
                        response.Add("UpdateStatus", "Error: No URL/path provided");
                    }
                    else
                    {
                        try
                        {
                            string msixbundlePath;

                            // Check if this is a local msixbundle path (debug mode)
                            if (updatePath.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) && File.Exists(updatePath))
                            {
                                // Direct path to local msixbundle - skip download/extract
                                Logger.Info($"Pipe: [DEBUG] Using local msixbundle: {updatePath}");
                                msixbundlePath = updatePath;
                            }
                            else
                            {
                                // Download and extract from URL
                                var tempFolder = Path.Combine(Path.GetTempPath(), "GoTweaks_Update");
                                var zipPath = Path.Combine(tempFolder, "update.zip");

                                // Clean up and create temp folder
                                if (Directory.Exists(tempFolder))
                                    Directory.Delete(tempFolder, true);
                                Directory.CreateDirectory(tempFolder);

                                // Download the zip file
                                Logger.Info($"Pipe: Downloading update from {updatePath}...");
                                using (var client = new WebClient())
                                {
                                    client.Headers.Add("User-Agent", "GoTweaks/1.0");
                                    client.DownloadFile(updatePath, zipPath);
                                }
                                Logger.Info($"Pipe: Downloaded to {zipPath}");

                                // Extract the zip
                                var extractFolder = Path.Combine(tempFolder, "extracted");
                                Directory.CreateDirectory(extractFolder);
                                ZipFile.ExtractToDirectory(zipPath, extractFolder);
                                Logger.Info($"Pipe: Extracted to {extractFolder}");

                                // Find the .msixbundle file
                                msixbundlePath = null;
                                foreach (var file in Directory.GetFiles(extractFolder, "*.msixbundle", SearchOption.AllDirectories))
                                {
                                    msixbundlePath = file;
                                    break;
                                }

                                if (string.IsNullOrEmpty(msixbundlePath))
                                {
                                    Logger.Error("Pipe: No .msixbundle file found in the update package");
                                    response.Add("UpdateStatus", "Error: No .msixbundle found in update");
                                    // Send response and return early
                                    if (pipeServer != null && pipeServer.IsConnected)
                                    {
                                        var errMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                                        errMsg.RequestId = request.RequestId;
                                        pipeServer.SendMessage(errMsg.ToJson());
                                    }
                                    return;
                                }
                            }

                            Logger.Info($"Pipe: Found msixbundle: {msixbundlePath}");

                            // Send response before launching installer
                            response.Add("UpdateStatus", "Installing");
                            if (pipeServer != null && pipeServer.IsConnected)
                            {
                                var successMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                                successMsg.RequestId = request.RequestId;
                                pipeServer.SendMessage(successMsg.ToJson());
                            }

                            // Launch the msixbundle installer
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = msixbundlePath,
                                UseShellExecute = true
                            };

                            Logger.Info("Pipe: Launching msixbundle installer...");
                            var installerProcess = Process.Start(startInfo);

                            // Wait for installer to fully load the package before exiting
                            Logger.Info("Pipe: Waiting for installer to load package...");
                            System.Threading.Thread.Sleep(5000); // 5 seconds for installer to fully open

                            // Exit helper so installer can replace files
                            Logger.Info("Pipe: Exiting helper for update installation...");
                            LogManager.Flush(); // Ensure log is written before exit

                            // Release mutex before exiting to ensure clean restart
                            try
                            {
                                singleInstanceMutex?.ReleaseMutex();
                                singleInstanceMutex?.Dispose();
                            }
                            catch { /* Ignore mutex errors during shutdown */ }

                            Environment.Exit(0);
                            return; // Won't reach this but for clarity
                        }
                        catch (WebException ex)
                        {
                            Logger.Error($"Pipe: Failed to download update: {ex.Message}");
                            response.Add("UpdateStatus", $"Error: Download failed - {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Pipe: Failed to install update: {ex.Message}");
                            response.Add("UpdateStatus", $"Error: {ex.Message}");
                        }
                    }
                }
                // System Restore: Prepare for Uninstall
                else if (functionValue == (int)Function.PrepareForUninstall)
                {
                    Logger.Info("Pipe: PrepareForUninstall request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();

                    try
                    {
                        string result = Services.SystemRestoreService.PrepareForUninstall();
                        response.Add(nameof(Function), functionValue);
                        response.Add("Content", result);
                        response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                        Logger.Info("Pipe: PrepareForUninstall completed successfully");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: PrepareForUninstall failed: {ex.Message}");
                        response.Add("Content", $"Error: {ex.Message}");
                    }
                }
                // System Restore: Get status of saved original values
                else if (functionValue == (int)Function.SystemRestoreStatus)
                {
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    string status = Services.SystemRestoreService.GetSavedValuesStatus();
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", status);
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    Logger.Info($"Pipe: SystemRestoreStatus requested");
                }
                // Export All Data (comprehensive backup)
                else if (functionValue == (int)Function.ExportAllData)
                {
                    Logger.Info("Pipe: ExportAllData request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();

                    try
                    {
                        // Widget settings may be passed in Content
                        string widgetSettings = request.Content;
                        string exportPath = ExportAllData(widgetSettings);
                        response.Add(nameof(Function), functionValue);
                        response.Add("Content", exportPath);
                        response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                        Logger.Info($"Pipe: ExportAllData completed: {exportPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: ExportAllData failed: {ex.Message}");
                        response.Add("Content", $"Error: {ex.Message}");
                    }
                }
                // Import All Data (restore from backup)
                else if (functionValue == (int)Function.ImportAllData)
                {
                    Logger.Info("Pipe: ImportAllData request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();

                    try
                    {
                        string importPath = request.Content;
                        if (string.IsNullOrEmpty(importPath))
                        {
                            response.Add("Content", "Error: No import path provided");
                        }
                        else
                        {
                            var (summary, widgetSettings) = ImportAllData(importPath);
                            response.Add(nameof(Function), functionValue);
                            response.Add("Content", summary);
                            if (!string.IsNullOrEmpty(widgetSettings))
                            {
                                response.Add("WidgetSettings", widgetSettings);
                            }
                            response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                            Logger.Info($"Pipe: ImportAllData completed from {importPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: ImportAllData failed: {ex.Message}");
                        response.Add("Content", $"Error: {ex.Message}");
                    }
                }
                // PawnIO Debug: Get CPU Info
                else if (functionValue == (int)Function.PawnIOGetCpuInfo)
                {
                    Logger.Info("Pipe: PawnIOGetCpuInfo request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        string cpuInfo = performanceManager?.GetPawnIOCpuInfo() ?? "PerformanceManager not initialized";
                        response.Add("Content", cpuInfo);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: PawnIOGetCpuInfo failed: {ex.Message}");
                        response.Add("Content", $"Error: {ex.Message}");
                    }
                }
                // PawnIO Debug: Apply Settings
                else if (functionValue == (int)Function.PawnIOApplySettings)
                {
                    Logger.Info("Pipe: PawnIOApplySettings request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    try
                    {
                        int coAll = 0, coGfx = 0, gfxClk = 0, tctlTemp = 0;
                        var valueSet = request.ToValueSet();
                        if (valueSet.TryGetValue("CoAll", out object coAllObj)) coAll = Convert.ToInt32(coAllObj);
                        if (valueSet.TryGetValue("CoGfx", out object coGfxObj)) coGfx = Convert.ToInt32(coGfxObj);
                        if (valueSet.TryGetValue("GfxClk", out object gfxClkObj)) gfxClk = Convert.ToInt32(gfxClkObj);
                        if (valueSet.TryGetValue("TctlTemp", out object tctlObj)) tctlTemp = Convert.ToInt32(tctlObj);

                        Logger.Info($"PawnIO Apply: CoAll={coAll}, CoGfx={coGfx}, GfxClk={gfxClk}, Tctl={tctlTemp}");
                        string result = performanceManager?.ApplyPawnIODebugSettings(coAll, coGfx, gfxClk, tctlTemp)
                            ?? "PerformanceManager not initialized";
                        response.Add("Content", result);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: PawnIOApplySettings failed: {ex.Message}");
                        response.Add("Content", $"Error: {ex.Message}");
                    }
                }
                else
                {
                    // Convert to ValueSet and use the existing property handling
                    var valueSet = request.ToValueSet();
                    response = properties.HandlePipeMessage(valueSet);
                }

                if (response != null && pipeServer != null && pipeServer.IsConnected)
                {
                    // Convert response to JSON and send back
                    // Echo the RequestId so client can correlate the response
                    var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                    responseMsg.RequestId = request.RequestId;
                    pipeServer.SendMessage(responseMsg.ToJson());
                    Logger.Debug($"Sent pipe response for {request.Function} (RequestId={request.RequestId})");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling pipe property request: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if the Named Pipe to the widget is connected.
        /// </summary>
        public static bool IsPipeConnected => pipeServer != null && pipeServer.IsConnected;

        /// <summary>
        /// Sends a message to the widget via Named Pipe
        /// </summary>
        public static bool SendPipeMessage(Shared.IPC.PipeMessage message)
        {
            if (pipeServer == null || !pipeServer.IsConnected)
            {
                Logger.Debug("Cannot send pipe message - not connected");
                return false;
            }
            return pipeServer.SendMessage(message.ToJson());
        }

        /// <summary>
        /// Handle batch get request via Named Pipe.
        /// Returns all requested property values in a single response.
        /// If managers aren't ready yet, returns a NotReady response so widget can retry.
        /// </summary>
        private static async Task HandleBatchGetRequestViaPipe(Shared.IPC.PipeMessage request)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Check if managers are ready - if not, tell widget to wait and retry
                if (!_managersReady)
                {
                    Logger.Info("BatchGet request received but managers not ready yet - sending NotReady response");
                    var notReadyResponse = new Shared.IPC.PipeMessage
                    {
                        Command = Shared.Enums.Command.BatchGet,
                        RequestId = request.RequestId,
                        Extra = new Dictionary<string, object>
                        {
                            ["NotReady"] = true,
                            ["Message"] = "Helper managers are still initializing, please retry"
                        }
                    };
                    pipeServer?.SendMessage(notReadyResponse.ToJson());
                    return;
                }

                if (!request.Extra.TryGetValue("Functions", out object functionsObj) || !(functionsObj is string functionsJson))
                {
                    Logger.Warn("BatchGet pipe request missing Functions");
                    return;
                }

                var functionIds = System.Text.Json.JsonSerializer.Deserialize<int[]>(functionsJson);
                if (functionIds == null || functionIds.Length == 0)
                {
                    Logger.Warn("BatchGet pipe request has empty Functions array");
                    return;
                }

                // Build batch response with all property values
                // Skip DefaultGameProfile properties - they're managed by ResyncCurrentState
                // Including them in batch causes race condition where stale values overwrite resync
                var helperManagedProperties = new HashSet<Shared.Enums.Function>
                {
                    Shared.Enums.Function.DefaultGameProfileAvailable,
                    Shared.Enums.Function.DefaultGameProfileData,
                    Shared.Enums.Function.DefaultGameProfileEnabled
                };

                var batchData = new Dictionary<string, object>();
                foreach (var funcId in functionIds)
                {
                    var func = (Shared.Enums.Function)funcId;

                    // Skip helper-managed properties that are resynced separately
                    if (helperManagedProperties.Contains(func))
                    {
                        Logger.Debug($"BatchGet: Skipping {func} - managed by ResyncCurrentState");
                        continue;
                    }

                    if (properties.TryGetProperty(func, out var property))
                    {
                        try
                        {
                            var value = property.GetValue();

                            // Structs must be serialized to XML to match individual property sync format
                            // Otherwise they serialize as "{}" in JSON which fails XML deserialization on widget
                            // Only serialize custom structs, not built-in value types like DateTime, TimeSpan, etc.
                            if (value != null)
                            {
                                var valueType = value.GetType();
                                if (valueType.IsValueType && !valueType.IsPrimitive && !valueType.IsEnum
                                    && valueType.Namespace != null && valueType.Namespace.StartsWith("Shared"))
                                {
                                    // Special handling for RunningGame/TrackedGame - check if valid before serializing
                                    // These structs have null strings when invalid/empty which causes XML serialization to fail
                                    bool shouldSerialize = true;
                                    if (value is Shared.Data.RunningGame rg)
                                    {
                                        // Must check ProcessId, GameId.Name AND GameId.Path - XML serializer fails on null strings
                                        shouldSerialize = rg.IsValid() && rg.GameId.IsValid() && !string.IsNullOrEmpty(rg.GameId.Path);
                                        if (!shouldSerialize)
                                        {
                                            Logger.Debug($"RunningGame not valid for XML: ProcessId={rg.ProcessId}, Name={rg.GameId.Name ?? "null"}, Path={rg.GameId.Path ?? "null"}");
                                        }
                                    }
                                    else if (value is Shared.Data.TrackedGame tg)
                                    {
                                        shouldSerialize = !string.IsNullOrEmpty(tg.DisplayName);
                                    }

                                    if (shouldSerialize)
                                    {
                                        value = Shared.Utilities.XmlHelper.ToXMLStringRuntime(value, true);
                                    }
                                    else
                                    {
                                        value = ""; // Return empty string for invalid/empty structs
                                    }
                                }
                            }

                            var propData = new Dictionary<string, object>
                            {
                                { "Content", value },
                                { "UpdatedTime", property.UpdatedTime }
                            };
                            batchData[funcId.ToString()] = propData;
                        }
                        catch (Exception propEx)
                        {
                            var innerMsg = propEx.InnerException?.Message ?? "no inner";
                            Logger.Warn($"BatchGet: Failed to serialize property {func}: {propEx.Message} (Inner: {innerMsg})");
                        }
                    }
                }

                // Send response via pipe with the same RequestId
                var response = new Shared.IPC.PipeMessage
                {
                    RequestId = request.RequestId,
                    Command = Shared.Enums.Command.Response,
                    Function = Shared.Enums.Function.None
                };
                response.Extra["BatchData"] = System.Text.Json.JsonSerializer.Serialize(batchData);

                if (pipeServer != null && pipeServer.IsConnected)
                {
                    pipeServer.SendMessage(response.ToJson());
                }

                timer.Stop();
                Logger.Info($"[TIMING] BatchGet via pipe {functionIds.Length} properties: {timer.ElapsedMilliseconds}ms");

                // After batch sync, resync DefaultGameProfile state to fix race condition
                // where widget may have received stale ProfileAvailable=false during sync
                defaultGameProfileManager?.ResyncCurrentState();
            }
            catch (Exception ex)
            {
                Logger.Error($"BatchGet via pipe failed: {ex.Message}");
            }
        }


        /// <summary>
        /// Sends a simple acknowledgment response to the widget via Named Pipe.
        /// Used for fire-and-forget messages that still need a response to avoid timeout.
        /// </summary>
        private static void SendPipeAck(int requestId, bool success = true)
        {
            try
            {
                if (pipeServer != null && pipeServer.IsConnected && requestId > 0)
                {
                    var response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Success", success);
                    var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                    responseMsg.RequestId = requestId;
                    pipeServer.SendMessage(responseMsg.ToJson());
                    Logger.Debug($"Sent pipe ack for RequestId={requestId}, Success={success}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send pipe ack: {ex.Message}");
            }
        }

    }
}
