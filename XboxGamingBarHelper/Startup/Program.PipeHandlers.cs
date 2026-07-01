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
                // Routine IPC traffic (polling Get/Set/BatchGet) — the single biggest log noise
                // source (~1k lines/h). Keep at Debug so it's available when bumping the level,
                // but out of the normal Info-level logs.
                Logger.Debug($"Helper received pipe message: {pipeMsg}");

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

                // Run a Special-Controller-Button action requested from a Quick Settings TILE. Tiles
                // (unlike the front button, which the helper handles directly while Game Bar is closed)
                // are clicked with Game Bar OPEN, so the widget closes it first and then asks the helper
                // to execute. We route through the SAME ExecuteLeftClawAction dispatch the front button
                // uses, so the action's injection method is correct per type: Steam BPM (Ctrl+1/2) uses
                // the legacy keybd_event path (Big Picture ignores SendInput), the in-game overlay uses
                // Shift+Tab via the SendInput InputInjector, and action 77 picks BPM vs in-game by the
                // game-running state — all in one place.
                if (pipeMsg.Extra.TryGetValue("ExecuteControllerAction", out object ecaValue))
                {
                    int ecaId = -1;
                    try { ecaId = Convert.ToInt32(ecaValue); } catch { }
                    if (ecaId >= 0)
                    {
                        Logger.Info($"Pipe: ExecuteControllerAction {ecaId} (from tile)");
                        ExecuteLeftClawAction(ecaId, "");
                    }
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
                        // Tile intent is "open the keyboard": EnsureOpen avoids the blind-toggle
                        // race where the keyboard (already opened via the controller hotkey) would
                        // be closed by the tile click. The hotkey path keeps the raw Toggle.
                        TouchKeyboardHelper.EnsureOpen();
                        Logger.Info("Pipe: Touch keyboard ensure-open executed");
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to toggle touch keyboard: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle MSI Claw fan-control request (0/1/2 = preset, -1 = firmware)
                if (pipeMsg.Extra.TryGetValue("MsiFanControl", out object msiFanObj))
                {
                    try
                    {
                        int value = Convert.ToInt32(msiFanObj);
                        Logger.Info($"Pipe: MsiFanControl request received: {value}");
                        ApplyMsiFan(value);
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: MsiFanControl failed: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle MSI Claw fan verification request → read EC values and push status back
                if (pipeMsg.Extra.ContainsKey("MsiFanVerify"))
                {
                    try
                    {
                        Logger.Info("Pipe: MsiFanVerify request received");
                        ReportMsiFanStatus();
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: MsiFanVerify failed: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle MSI Claw custom fan curve (CSV of 11 values 0-100)
                if (pipeMsg.Extra.TryGetValue("MsiFanCurve", out object msiFanCurveObj) && msiFanCurveObj is string msiFanCsv)
                {
                    try
                    {
                        Logger.Info($"Pipe: MsiFanCurve request received: '{msiFanCsv}'");
                        ApplyMsiFanCurve(msiFanCsv);
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: MsiFanCurve failed: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Diagnostic fan-override probe: write a raw byte to an EC data block to hunt for a
                // proportional fan-duty register. Content "block,value" (decimal).
                if (pipeMsg.Extra.TryGetValue("MsiFanRegProbe", out object regProbeObj) && regProbeObj is string regProbeCmd)
                {
                    try
                    {
                        Logger.Info($"Pipe: MsiFanRegProbe request received: '{regProbeCmd}'");
                        var parts = regProbeCmd.Split(',');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int blk) && int.TryParse(parts[1], out int val))
                            ProbeFanRegister(blk, val);
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: MsiFanRegProbe failed: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Diagnostic: force the EC full-speed override (block 152.7) on/off to compare our table
                // max (=150) against the EC's true full-speed ceiling. "on"|"off".
                if (pipeMsg.Extra.TryGetValue("MsiFanFullBlast", out object fullBlastObj) && fullBlastObj is string fullBlastCmd)
                {
                    try
                    {
                        Logger.Info($"Pipe: MsiFanFullBlast request received: '{fullBlastCmd}'");
                        SetMsiFanFullBlast(fullBlastCmd == "on");
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: MsiFanFullBlast failed: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Experimental: stop / start / query the Intel thermal stack (IPF/DTT) that owns the
                // fan above the EC. "stop"|"start"|"status". Pushes back "IntelThermalStatus".
                if (pipeMsg.Extra.TryGetValue("IntelThermalCmd", out object intelThermalObj) && intelThermalObj is string intelThermalCmd)
                {
                    try
                    {
                        Logger.Info($"Pipe: IntelThermalCmd request received: '{intelThermalCmd}'");
                        switch (intelThermalCmd)
                        {
                            case "stop":  StopIntelThermal();  break;
                            case "start": StartIntelThermal(); break;
                            default:      ReportIntelThermalStatus(); break;
                        }
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: IntelThermalCmd failed: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle launcher open request (Steam Big Picture / Playnite / Xbox app)
                if (pipeMsg.Extra.TryGetValue("LaunchLauncher", out object launcherObj) && launcherObj is string launcherKey)
                {
                    try
                    {
                        Logger.Info($"Pipe: LaunchLauncher request received: {launcherKey}");
                        LaunchLauncher(launcherKey);
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to launch '{launcherKey}': {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle LaunchUrl request (Donate button, Launch Website actions, etc.)
                if (pipeMsg.Extra.TryGetValue("LaunchUrl", out object urlValue) && urlValue is string url)
                {
                    try
                    {
                        Logger.Info($"Pipe: LaunchUrl request received: {url}");
                        LaunchUrl(url);
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to launch URL: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle PowerAction request (Power tile): forced/immediate sleep/hibernate/reboot/
                // poweroff/bios. See ExecutePowerAction (Program.PowerActions.cs).
                if (pipeMsg.Extra.TryGetValue("PowerAction", out object powerValue) && powerValue is string powerAction)
                {
                    try
                    {
                        Logger.Info($"Pipe: PowerAction request received: {powerAction}");
                        SendPipeAck(pipeMsg.RequestId); // ack BEFORE acting — reboot/shutdown kills the pipe
                        ExecutePowerAction(powerAction);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to execute power action '{powerAction}': {ex.Message}");
                    }
                    return;
                }

                // Handle factory reset: the widget clears its own package data but the helper's
                // global controller profile (gyro etc.) lives in the helper's own profile store
                // (global.xml) and would otherwise survive — leaving gyro active after a reset.
                if (pipeMsg.Extra.TryGetValue("FactoryReset", out object _frObj))
                {
                    Logger.Info("Pipe: FactoryReset received — resetting global controller profile to defaults");

                    // Deactivate controller emulation FIRST (before anything else is reset):
                    // tear down the virtual ViGEm controller and ForceUnhideAll() so the physical
                    // MSI Claw controllers are restored via HidHide as the very first step.
                    // Reuses the existing working stop/unhide path unchanged.
                    try
                    {
                        if (Devices.DeviceDetector.DetectDevice().DeviceType == Shared.Enums.DeviceType.MSIClaw)
                            StopMSIClawButtonMonitor();
                    }
                    catch (Exception exFr)
                    {
                        Logger.Warn($"FactoryReset: controller emulation deactivate failed: {exFr.Message}");
                    }

                    // Persist controller emulation = OFF. The widget-side factory reset only clears
                    // the UWP ApplicationData store, but the helper also keeps a fallback copy
                    // (LocalState / %LocalAppData%\GoTweaks\settings.json) that survives the reset —
                    // so "ControllerEmulationEnabled" would otherwise re-load as its old value and
                    // auto-enable emulation after a reboot. Clear it explicitly in both stores.
                    try
                    {
                        controllerEmulationManager?.ControllerEmulationEnabled?.ForceSetValue(false);
                        Settings.LocalSettingsHelper.SetValue("ControllerEmulationEnabled", false);
                        Logger.Info("FactoryReset: ControllerEmulationEnabled persisted = false");
                    }
                    catch (Exception exCe)
                    {
                        Logger.Warn($"FactoryReset: reset ControllerEmulationEnabled failed: {exCe.Message}");
                    }

                    // Reset the emulation backend to the default (VIIPER). Same rationale as
                    // ControllerEmulationEnabled above: the widget reset clears only the UWP store,
                    // but the helper's LocalSettingsHelper fallback keeps "EmulationBackend" — so
                    // without writing it back here a user who had switched to Legacy would stay on
                    // Legacy after a factory reset. SetValue(true) also re-syncs the widget toggle
                    // and _viiperBackendActive; the LocalSettingsHelper write is belt-and-braces in
                    // case the value was already true (SetValue would then no-op).
                    try
                    {
                        settingsManager?.EmulationBackend?.SetValue(true);
                        Settings.LocalSettingsHelper.SetValue("EmulationBackend", (int)Shared.Enums.EmulationBackend.Viiper);
                        Logger.Info("FactoryReset: EmulationBackend reset to VIIPER (default)");
                    }
                    catch (Exception exEb)
                    {
                        Logger.Warn($"FactoryReset: reset EmulationBackend failed: {exEb.Message}");
                    }

                    FactoryResetGlobalControllerProfile();
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Handle Left MSI button double-click config update from the widget.
                if (pipeMsg.Extra.TryGetValue("LeftMsiDoubleClick", out object dcObj) && dcObj is string dcJson)
                {
                    try
                    {
                        bool enabled = System.Text.RegularExpressions.Regex.Match(dcJson, "\"enabled\"\\s*:\\s*(true|false)").Groups[1].Value == "true";
                        int delay = int.TryParse(System.Text.RegularExpressions.Regex.Match(dcJson, "\"delayMs\"\\s*:\\s*(\\d+)").Groups[1].Value, out var d) ? d : 300;
                        int action = int.TryParse(System.Text.RegularExpressions.Regex.Match(dcJson, "\"action\"\\s*:\\s*(-?\\d+)").Groups[1].Value, out var a) ? a : 0;
                        var pm = System.Text.RegularExpressions.Regex.Match(dcJson, "\"param\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
                        string param = pm.Success ? pm.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : "";
                        legionManager?.SetDoubleClickConfig(enabled, delay, action, param);
                        Logger.Info($"Pipe: LeftMsiDoubleClick updated (enabled={enabled}, delay={delay}, action={action})");
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: LeftMsiDoubleClick parse failed: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // [DISABLED — Game-Bar-only redesign] Step 1b controller-profile path map receiver.
                // if (pipeMsg.Extra.TryGetValue("ControllerProfilePaths", out object cppObj) && cppObj is string cppStr)
                // {
                //     try { settingsManager?.SetControllerProfileGames(cppStr); SendPipeAck(pipeMsg.RequestId); }
                //     catch (Exception ex) { Logger.Error($"Pipe: ControllerProfilePaths parse failed: {ex.Message}"); SendPipeAck(pipeMsg.RequestId, false); }
                //     return;
                // }

                // ── MSI Claw: LED color ──────────────────────────────────────────────────────
                // Payload: "MsiLedColor" = "R,G,B" or "R,G,B,Brightness" (bytes; brightness 0-100,
                // default 100 when absent — 0 turns the LED off for the LED on/off tile).
                if (pipeMsg.Extra.TryGetValue("MsiLedColor", out object ledColorObj) && ledColorObj is string ledColorStr)
                {
                    try
                    {
                        var parts = ledColorStr.Split(',');
                        if (parts.Length >= 3
                            && byte.TryParse(parts[0].Trim(), out byte lr)
                            && byte.TryParse(parts[1].Trim(), out byte lg)
                            && byte.TryParse(parts[2].Trim(), out byte lb))
                        {
                            byte lbright = 100;
                            if (parts.Length >= 4 && byte.TryParse(parts[3].Trim(), out byte parsedBright))
                                lbright = Math.Min((byte)100, parsedBright);
                            bool ok = Devices.MSIClaw.MsiClawLedController.TrySetLedColor(lr, lg, lb, lbright);
                            // Persist colour AND brightness so the helper can re-apply the exact state on
                            // its own at the next startup. Brightness MUST be stored — otherwise the LED
                            // on/off tile's "off" state (brightness 0) is lost on reboot and the LED comes
                            // back at full brightness. Its presence also authorises the helper to drive the LED.
                            Devices.MSIClaw.MsiLedColorStore.Save(lr, lg, lb, lbright);
                            Logger.Info($"Pipe: MsiLedColor R={lr} G={lg} B={lb} Brightness={lbright} → ok={ok}");
                            // If LED-by-SoC is active, the saved colour is kept (for restore) but the
                            // SoC tint owns the LED — reclaim it so a connect-time re-push of the user's
                            // colour doesn't leave the LED un-tinted until the next band crossing.
                            ReassertLedColorBySocIfActive();
                            SendPipeAck(pipeMsg.RequestId, ok);
                        }
                        else
                        {
                            Logger.Warn($"Pipe: MsiLedColor bad format: '{ledColorStr}'");
                            SendPipeAck(pipeMsg.RequestId, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: MsiLedColor failed: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // ── MSI Claw: startup LED colour cycle on/off ────────────────────────────────
                // Payload: "MsiLedBootCycle" = "1" (red→green→colour at boot) or "0" (just the colour)
                if (pipeMsg.Extra.TryGetValue("MsiLedBootCycle", out object ledCycleObj) && ledCycleObj is string ledCycleStr)
                {
                    try
                    {
                        bool on = ledCycleStr.Trim() != "0";
                        Devices.MSIClaw.MsiLedColorStore.SaveBootCycle(on);
                        Logger.Info($"Pipe: MsiLedBootCycle = {on}");
                        SendPipeAck(pipeMsg.RequestId, true);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: MsiLedBootCycle failed: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // ── MSI Claw: battery charge limit ───────────────────────────────────────────
                // Payload: "MsiChargeLimit" = "enabled:percent" e.g. "true:80" or "false:100"
                if (pipeMsg.Extra.TryGetValue("MsiChargeLimit", out object chgObj) && chgObj is string chgStr)
                {
                    try
                    {
                        var parts = chgStr.Split(':');
                        bool enabled = parts.Length > 0 && parts[0].Trim().ToLowerInvariant() == "true";
                        int  percent = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int p) ? p : 90;

                        bool ok1 = Devices.MSIClaw.MsiClawBatteryManager.SetPercent(percent);
                        bool ok2 = Devices.MSIClaw.MsiClawBatteryManager.SetEnabled(enabled);
                        // Persist helper-side so it can be re-applied on reboot / after an EC reset.
                        PersistMsiChargeLimit(enabled, percent);
                        Logger.Info($"Pipe: MsiChargeLimit enabled={enabled} percent={percent} → ok={ok1&&ok2}");
                        SendPipeAck(pipeMsg.RequestId, ok1 && ok2);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: MsiChargeLimit failed: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // ── MSI Claw: query current charge limit ─────────────────────────────────────
                // Widget sends "MsiChargeLimitGet" = true to read current ACPI state.
                // Response: "MsiChargeLimitStatus" = "enabled:percent" e.g. "true:80"
                if (pipeMsg.Extra.ContainsKey("MsiChargeLimitGet"))
                {
                    try
                    {
                        int percent = Devices.MSIClaw.MsiClawBatteryManager.GetConfig(out bool enabled, out bool readOk);
                        Logger.Info($"Pipe: MsiChargeLimitGet → enabled={enabled} percent={percent} readOk={readOk}");
                        // Format: "enabled:percent:readok" — readok=false means the EC read failed
                        // (device not ready), so the widget shows "unknown" rather than a stale guess.
                        var responseVs = new global::Windows.Foundation.Collections.ValueSet
                        {
                            { "MsiChargeLimitStatus", $"{enabled}:{percent}:{readOk}" }
                        };
                        if (pipeServer != null && pipeServer.IsConnected)
                        {
                            var responseMsg = Shared.IPC.PipeMessage.FromValueSet(responseVs);
                            responseMsg.RequestId = pipeMsg.RequestId;
                            pipeServer.SendMessage(responseMsg.ToJson());
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: MsiChargeLimitGet failed: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Handle LaunchProgram request (Program Actions: default targets + user exe/ps1)
                if (pipeMsg.Extra.TryGetValue("LaunchProgram", out object progValue) && progValue is string progTarget)
                {
                    try
                    {
                        Logger.Info($"Pipe: LaunchProgram request received: {progTarget}");
                        LaunchProgramTarget(progTarget);
                        SendPipeAck(pipeMsg.RequestId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Pipe: Failed to launch program: {ex.Message}");
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
                        var exportFolder = Path.Combine(desktopPath, $"ClawTweaks_Logs_{timestamp}");

                        // Create export folder
                        Directory.CreateDirectory(exportFolder);
                        var helperFolder = Path.Combine(exportFolder, "Helper");
                        var widgetFolder = Path.Combine(exportFolder, "Widget");
                        Directory.CreateDirectory(helperFolder);
                        Directory.CreateDirectory(widgetFolder);

                        // Helper logs are written to the package's LocalCache\Local folder. The exact
                        // directory is determined once at startup (Program.ConfigureLogDirectory) and
                        // stored in the NLog GDC, so read it back here instead of hardcoding a package
                        // name. The old code pointed at the upstream GoTweaks package + %LocalAppData%
                        // root, which only held stale logs -> exports picked up ancient files.
                        var helperLogPath = NLog.GlobalDiagnosticsContext.Get("LogDirectory") as string;
                        if (string.IsNullOrEmpty(helperLogPath) || !Directory.Exists(helperLogPath))
                        {
                            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                            helperLogPath = Path.Combine(localAppData, "Packages", "MSIClaw.ClawTweaks_7eszav2039cvc", "LocalCache", "Local");
                        }

                        // Widget logs live in the package's LocalState (sibling of LocalCache). Derive
                        // it from the helper log dir (...\<pkg>\LocalCache\Local) when possible.
                        string widgetLogPath = null;
                        try
                        {
                            var localCacheDir = Directory.GetParent(helperLogPath)?.FullName;   // ...\<pkg>\LocalCache
                            var packageRoot = localCacheDir != null ? Directory.GetParent(localCacheDir)?.FullName : null; // ...\<pkg>
                            if (packageRoot != null)
                                widgetLogPath = Path.Combine(packageRoot, "LocalState");
                        }
                        catch { /* fall through to default below */ }
                        if (string.IsNullOrEmpty(widgetLogPath) || !Directory.Exists(widgetLogPath))
                        {
                            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                            widgetLogPath = Path.Combine(localAppData, "Packages", "MSIClaw.ClawTweaks_7eszav2039cvc", "LocalState");
                        }

                        // Copy the most recent helper logs. Files rotate hourly (helper_yyyy-MM-dd_HH.log),
                        // so the newest 24 cover roughly the last day of activity (NLog keeps ~3 days).
                        if (Directory.Exists(helperLogPath))
                        {
                            foreach (var log in Directory.GetFiles(helperLogPath, "helper_*.log")
                                .OrderByDescending(f => File.GetLastWriteTime(f))
                                .Take(24))
                            {
                                var destPath = Path.Combine(helperFolder, Path.GetFileName(log));
                                File.Copy(log, destPath, true);
                                Logger.Info($"Copied helper log: {Path.GetFileName(log)}");
                            }
                        }
                        else
                        {
                            Logger.Warn($"Pipe: helper log dir not found: {helperLogPath}");
                        }

                        // Copy the most recent widget logs (also rotate hourly) — newest 24 ≈ last day.
                        if (Directory.Exists(widgetLogPath))
                        {
                            foreach (var log in Directory.GetFiles(widgetLogPath, "widget_*.log")
                                .OrderByDescending(f => File.GetLastWriteTime(f))
                                .Take(24))
                            {
                                var destPath = Path.Combine(widgetFolder, Path.GetFileName(log));
                                File.Copy(log, destPath, true);
                                Logger.Info($"Copied widget log: {Path.GetFileName(log)}");
                            }
                        }
                        else
                        {
                            Logger.Warn($"Pipe: widget log dir not found: {widgetLogPath}");
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

                // Handle Lenovo driver-update check. Widget button fires this, helper
                // resolves the machine type + BIOS version via WMI and best-effort-fetches
                // the live driver list from Lenovo's public pcsupport API. Response is
                // serialized JSON so the widget can render it without knowing the helper's
                // internal data model.
                if (pipeMsg.Extra.ContainsKey("CheckDriverUpdates"))
                {
                    try
                    {
                        // If the startup probe has already populated a result,
                        // and the widget's request carries no explicit "Force"
                        // hint, serve the cached result so the user sees the
                        // list instantly instead of waiting for another live
                        // Lenovo fetch.
                        bool force = false;
                        if (pipeMsg.Extra.TryGetValue("ForceRefresh", out var forceObj))
                        {
                            if (forceObj is bool fb) force = fb;
                            else if (forceObj is string fs) bool.TryParse(fs, out force);
                        }
                        // Device dispatch: on an MSI Claw use the MSI/Intel hybrid
                        // service (curated manifest + PnP + Intel-DSA deep-links),
                        // otherwise the Lenovo catalog service. Response key is the
                        // same so the widget renders both without changes.
                        string json;
                        if (Services.MsiClawDriverCheckService.IsClawHardware())
                        {
                            var probe = (!force && Services.MsiClawDriverCheckService.LastResult != null)
                                ? Services.MsiClawDriverCheckService.LastResult
                                : await Services.MsiClawDriverCheckService.CheckAsync();
                            Services.MsiClawDriverCheckService.ApplyMutes(probe); // reflect freshly-toggled mutes on cached serves
                            json = probe.ToJson();
                            Logger.Info($"Pipe: CheckDriverUpdates (Claw) — model={probe.ModelCode}, BIOS={probe.BiosVersion}, live={probe.LiveFetchSucceeded}, count={probe.Drivers.Count}");
                            foreach (var d in probe.Drivers)
                            {
                                if (d.UpdateStatus == Services.DriverUpdateStatus.UpdateAvailable)
                                    Logger.Info($"  Driver flagged update: name='{d.Name}', category='{d.Category}', installed='{d.InstalledVersion}', latest='{d.Version}', scope='{d.ProviderScope}'");
                            }
                        }
                        else
                        {
                            var probe = (!force && Services.LenovoDriverCheckService.LastResult != null)
                                ? Services.LenovoDriverCheckService.LastResult
                                : await Services.LenovoDriverCheckService.CheckAsync();
                            json = probe.ToJson();
                            Logger.Info($"Pipe: CheckDriverUpdates — MT={probe.MachineTypeCode}, BIOS={probe.BiosVersion}, live={probe.LiveFetchSucceeded}, count={probe.Drivers.Count}");
                            // Log each entry flagged UpdateAvailable so we can debug "up to date
                            // but flagged outdated" reports without Device Manager screenshots.
                            foreach (var d in probe.Drivers)
                            {
                                if (d.UpdateStatus == Services.DriverUpdateStatus.UpdateAvailable)
                                    Logger.Info($"  Driver flagged update: name='{d.Name}', category='{d.Category}', installed='{d.InstalledVersion}', catalog='{d.Version}', matchedDevice='{d.MatchedDeviceName}', matchedProvider='{d.MatchedProvider}', matchScore={d.MatchScore}");
                            }
                        }

                        var response = new global::Windows.Foundation.Collections.ValueSet
                        {
                            { "DriverUpdateResult", json },
                        };
                        if (pipeServer != null && pipeServer.IsConnected)
                        {
                            var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                            responseMsg.RequestId = pipeMsg.RequestId;
                            pipeServer.SendMessage(responseMsg.ToJson());
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: CheckDriverUpdates threw: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                // Persists the user's "Check for driver updates on start"
                // preference on the helper side. Widget writes here whenever
                // the checkbox flips; helper re-reads at next launch before
                // scheduling the Lenovo probe, so flipping the box off keeps
                // the helper from hitting pcsupport.lenovo.com on boot.
                // Open an external URL in the user's default browser. The widget runs
                // in the Xbox Game Bar AppContainer, where Launcher.LaunchUriAsync is
                // unreliable (returns false / "couldn't open the browser"). The helper
                // is full-trust, so it opens the link here — via explorer.exe so the
                // browser starts at the user's (medium) integrity even though the helper
                // is elevated. Only http/https is accepted.
                if (pipeMsg.Extra.ContainsKey("OpenExternalUrl"))
                {
                    bool opened = false;
                    try
                    {
                        string extUrl = pipeMsg.Extra["OpenExternalUrl"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(extUrl)
                            && Uri.TryCreate(extUrl, UriKind.Absolute, out var extUri)
                            && (extUri.Scheme == "http" || extUri.Scheme == "https"))
                        {
                            // cmd /c start is the reliable way to open a URL from an elevated
                            // process into the user's (non-elevated) default browser. Using
                            // explorer.exe directly fails when the URL contains query strings.
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = "/c start \"\" \"" + extUri.AbsoluteUri + "\"",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                            });
                            opened = true;
                            Logger.Info($"Pipe: OpenExternalUrl -> {extUri.Host}");
                        }
                        else { Logger.Warn($"Pipe: OpenExternalUrl rejected: '{extUrl}'"); }
                    }
                    catch (Exception ex) { Logger.Warn($"Pipe: OpenExternalUrl threw: {ex.Message}"); }
                    if (pipeServer != null && pipeServer.IsConnected)
                    {
                        var response = new global::Windows.Foundation.Collections.ValueSet { { "OpenExternalUrlResult", opened } };
                        var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                        responseMsg.RequestId = pipeMsg.RequestId;
                        pipeServer.SendMessage(responseMsg.ToJson());
                    }
                    return;
                }

                if (pipeMsg.Extra.ContainsKey("DisableGuideGameBar"))
                {
                    bool ok = false;
                    try { ok = DisableGuideButtonGameBar(); }
                    catch (Exception ex) { Logger.Warn($"Pipe: DisableGuideGameBar threw: {ex.Message}"); }
                    if (pipeServer != null && pipeServer.IsConnected)
                    {
                        var response = new global::Windows.Foundation.Collections.ValueSet { { "DisableGuideGameBarResult", ok } };
                        var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                        responseMsg.RequestId = pipeMsg.RequestId;
                        pipeServer.SendMessage(responseMsg.ToJson());
                    }
                    return;
                }

                if (pipeMsg.Extra.ContainsKey("SetDriverCheckOnStart"))
                {
                    try
                    {
                        bool val = true;
                        if (pipeMsg.Extra.TryGetValue("SetDriverCheckOnStart", out var v))
                        {
                            if (v is bool b) val = b;
                            else if (v is string s) bool.TryParse(s, out val);
                        }
                        Settings.LocalSettingsHelper.SetValue("DriverCheckOnStart", val);
                        Logger.Info($"Pipe: SetDriverCheckOnStart = {val}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: SetDriverCheckOnStart threw: {ex.Message}");
                    }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Persists the user's "Check for updates on start" preference
                // for the GoTweaks self-update probe. Mirrors SetDriverCheckOnStart.
                // Persist the user's opt-in for the third-party modded Wi-Fi driver.
                if (pipeMsg.Extra.ContainsKey("SetUseModdedWifi"))
                {
                    try
                    {
                        bool val = false;
                        if (pipeMsg.Extra.TryGetValue("SetUseModdedWifi", out var v))
                        {
                            if (v is bool b) val = b;
                            else if (v is string s) bool.TryParse(s, out val);
                        }
                        Settings.LocalSettingsHelper.SetValue("UseModdedWifiDriver", val);
                        Logger.Info($"Pipe: SetUseModdedWifi = {val}");
                    }
                    catch (Exception ex) { Logger.Warn($"Pipe: SetUseModdedWifi threw: {ex.Message}"); }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Assisted install of the modded Wi-Fi driver: download + extract + open
                // the folder (the user runs Setup.bat themselves).
                if (pipeMsg.Extra.ContainsKey("InstallModdedWifi"))
                {
                    Logger.Info("Pipe: InstallModdedWifi — starting download");
                    string resultJson;
                    try { resultJson = await Services.MsiClawDriverCheckService.InstallModdedWifiAsync(); }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: InstallModdedWifi threw: {ex.Message}");
                        resultJson = "{\"success\":false,\"message\":\"" + ex.Message.Replace("\"", "'") + "\"}";
                    }
                    // Driver changed on disk → drop the cached snapshot so the next check re-reads PnP.
                    Services.MsiClawDriverCheckService.InvalidateCache();
                    if (pipeServer != null && pipeServer.IsConnected)
                    {
                        var response = new global::Windows.Foundation.Collections.ValueSet { { "ModdedWifiInstallResult", resultJson } };
                        var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                        responseMsg.RequestId = pipeMsg.RequestId;
                        pipeServer.SendMessage(responseMsg.ToJson());
                    }
                    return;
                }

                // Mute/unmute a single driver update for its current latest version.
                if (pipeMsg.Extra.ContainsKey("SetDriverIgnore"))
                {
                    try
                    {
                        string key = pipeMsg.Extra["SetDriverIgnore"]?.ToString();
                        bool ignored = false;
                        if (pipeMsg.Extra.TryGetValue("IgnoreState", out var v))
                        {
                            if (v is bool b) ignored = b;
                            else if (v is string s) bool.TryParse(s, out ignored);
                        }
                        Services.MsiClawDriverCheckService.SetIgnore(key, ignored);
                    }
                    catch (Exception ex) { Logger.Warn($"Pipe: SetDriverIgnore threw: {ex.Message}"); }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                if (pipeMsg.Extra.ContainsKey("SetGoTweaksCheckOnStart"))
                {
                    try
                    {
                        bool val = true;
                        if (pipeMsg.Extra.TryGetValue("SetGoTweaksCheckOnStart", out var v))
                        {
                            if (v is bool b) val = b;
                            else if (v is string s) bool.TryParse(s, out val);
                        }
                        Settings.LocalSettingsHelper.SetValue("GoTweaksCheckOnStart", val);
                        Logger.Info($"Pipe: SetGoTweaksCheckOnStart = {val}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: SetGoTweaksCheckOnStart threw: {ex.Message}");
                    }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // CLAWTWEAKS: Update check/install pipe handlers disabled — re-enable
                // together with the startup probe in Program.cs once ClawTweaks has
                // its own GitHub releases and GoTweaksUpdateService.RepoPath is updated.
                /*
                // GoTweaks self-update check. Widget explicitly asks; helper
                // serves the cached startup-probe result unless ForceRefresh=true.
                if (pipeMsg.Extra.ContainsKey("CheckGoTweaksUpdate"))
                {
                    try
                    {
                        bool force = false;
                        if (pipeMsg.Extra.TryGetValue("ForceRefresh", out var forceObj))
                        {
                            if (forceObj is bool fb) force = fb;
                            else if (forceObj is string fs) bool.TryParse(fs, out force);
                        }
                        var result = (!force && Services.GoTweaksUpdateService.LastResult != null)
                            ? Services.GoTweaksUpdateService.LastResult
                            : await Services.GoTweaksUpdateService.CheckAsync(helperVersion);
                        if (pipeServer != null && pipeServer.IsConnected)
                        {
                            var response = new global::Windows.Foundation.Collections.ValueSet
                            {
                                { "GoTweaksUpdate", result.ToJson() },
                            };
                            var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                            responseMsg.RequestId = pipeMsg.RequestId;
                            pipeServer.SendMessage(responseMsg.ToJson());
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: CheckGoTweaksUpdate threw: {ex.Message}");
                        SendPipeAck(pipeMsg.RequestId, false);
                    }
                    return;
                }

                if (pipeMsg.Extra.ContainsKey("InstallGoTweaksUpdate"))
                {
                    string resultJson;
                    try
                    {
                        string gtUrl = null;
                        if (pipeMsg.Extra.TryGetValue("InstallGoTweaksUpdate", out var urlObj))
                            gtUrl = urlObj?.ToString();
                        resultJson = await Services.GoTweaksUpdateService.InstallAsync(gtUrl);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: InstallGoTweaksUpdate threw: {ex.Message}");
                        resultJson = "{\"success\":false,\"message\":\"" + ex.Message.Replace("\"", "'") + "\"}";
                    }
                    if (pipeServer != null && pipeServer.IsConnected)
                    {
                        var response = new global::Windows.Foundation.Collections.ValueSet
                        {
                            { "GoTweaksUpdateInstallResult", resultJson },
                        };
                        var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                        responseMsg.RequestId = pipeMsg.RequestId;
                        pipeServer.SendMessage(responseMsg.ToJson());
                    }
                    return;
                }
                */

                // Handle "update all" batch install. Widget sends an array of
                // Lenovo download URLs; helper downloads them in parallel (bounded)
                // and launches each installer sequentially so they don't fight
                // over the Windows Installer mutex.
                if (pipeMsg.Extra.ContainsKey("BatchInstallDrivers"))
                {
                    string batchResult;
                    try
                    {
                        var urls = new List<string>();
                        if (pipeMsg.Extra.TryGetValue("BatchInstallDrivers", out var urlsObj) && urlsObj is string joined)
                        {
                            // Widget serialises as newline-joined string — ValueSet doesn't
                            // support nested arrays cleanly across the pipe contract.
                            foreach (var line in joined.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                var trimmed = line.Trim();
                                if (trimmed.Length > 0) urls.Add(trimmed);
                            }
                        }
                        batchResult = Services.MsiClawDriverCheckService.IsClawHardware()
                            ? await Services.MsiClawDriverCheckService.BatchInstallAsync(urls)
                            : await Services.LenovoDriverCheckService.BatchInstallAsync(urls);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: BatchInstallDrivers threw: {ex.Message}");
                        batchResult = "{\"success\":false,\"message\":\"" + ex.Message.Replace("\"", "'") + "\"}";
                    }
                    if (pipeServer != null && pipeServer.IsConnected)
                    {
                        var response = new global::Windows.Foundation.Collections.ValueSet
                        {
                            { "DriverBatchInstallResult", batchResult },
                        };
                        var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                        responseMsg.RequestId = pipeMsg.RequestId;
                        pipeServer.SendMessage(responseMsg.ToJson());
                    }
                    return;
                }

                // Handle driver-install request. Widget sends a download URL; helper
                // downloads the file to a per-session temp folder and launches it elevated.
                // Intel CDN files (downloadmirror.intel.com) can be 800+ MB — running those
                // inline would block the pipe until the download finishes and the widget's
                // SendMessageAsync times out, causing the button to flicker back immediately.
                // For those large downloads: respond immediately with async:true, then finish
                // the download in the background and push DriverInstallComplete when done.
                if (pipeMsg.Extra.ContainsKey("InstallDriverUpdate"))
                {
                    string installUrl = null;
                    if (pipeMsg.Extra.TryGetValue("InstallDriverUpdate", out var urlObj))
                        installUrl = urlObj?.ToString();

                    bool isLargeDownload = !string.IsNullOrEmpty(installUrl) &&
                        (installUrl.IndexOf("downloadmirror.intel.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         installUrl.IndexOf("downloads.intel.com", StringComparison.OrdinalIgnoreCase) >= 0);

                    Logger.Info($"Pipe: InstallDriverUpdate url='{installUrl}' isLargeDownload={isLargeDownload}");

                    if (isLargeDownload)
                    {
                        // Acknowledge immediately so the pipe round-trip returns without timing out.
                        if (pipeServer != null && pipeServer.IsConnected)
                        {
                            var ack = new global::Windows.Foundation.Collections.ValueSet
                            {
                                { "DriverInstallResult", "{\"success\":true,\"message\":\"Downloading driver in background…\",\"async\":true}" },
                            };
                            var ackMsg = Shared.IPC.PipeMessage.FromValueSet(ack);
                            ackMsg.RequestId = pipeMsg.RequestId;
                            pipeServer.SendMessage(ackMsg.ToJson());
                        }

                        // Download + launch in the background; push completion when done.
                        var capturedUrl = installUrl;
                        _ = Task.Run(async () =>
                        {
                            string resultJson;
                            try
                            {
                                resultJson = Services.MsiClawDriverCheckService.IsClawHardware()
                                    ? await Services.MsiClawDriverCheckService.InstallDriverAsync(capturedUrl)
                                    : await Services.LenovoDriverCheckService.InstallDriverAsync(capturedUrl);
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Background driver install threw: {ex.Message}");
                                resultJson = "{\"success\":false,\"message\":\"" + ex.Message.Replace("\"", "'") + "\"}";
                            }
                            Logger.Info($"Background driver install finished: {resultJson}");
                            // Driver changed on disk → drop the cached snapshot so the next check re-reads PnP.
                            Services.MsiClawDriverCheckService.InvalidateCache();
                            if (pipeServer != null && pipeServer.IsConnected)
                            {
                                var push = new global::Windows.Foundation.Collections.ValueSet
                                {
                                    { "DriverInstallComplete", resultJson },
                                };
                                pipeServer.SendMessage(Shared.IPC.PipeMessage.FromValueSet(push).ToJson());
                            }
                            else
                            {
                                Logger.Warn("Background driver install: pipe not connected, cannot push DriverInstallComplete");
                            }
                        });
                    }
                    else
                    {
                        // Small/local download: synchronous path, respond after launch.
                        string resultJson;
                        try
                        {
                            resultJson = Services.MsiClawDriverCheckService.IsClawHardware()
                                ? await Services.MsiClawDriverCheckService.InstallDriverAsync(installUrl)
                                : await Services.LenovoDriverCheckService.InstallDriverAsync(installUrl);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Pipe: InstallDriverUpdate threw: {ex.Message}");
                            resultJson = "{\"success\":false,\"message\":\"" + ex.Message.Replace("\"", "'") + "\"}";
                        }
                        // Driver changed on disk → drop the cached snapshot so the next check re-reads PnP.
                        Services.MsiClawDriverCheckService.InvalidateCache();
                        if (pipeServer != null && pipeServer.IsConnected)
                        {
                            var response = new global::Windows.Foundation.Collections.ValueSet
                            {
                                { "DriverInstallResult", resultJson },
                            };
                            var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                            responseMsg.RequestId = pipeMsg.RequestId;
                            pipeServer.SendMessage(responseMsg.ToJson());
                        }
                    }
                    return;
                }

                // Manage installer files left on disk from earlier driver downloads:
                // list / delete / re-launch (downgrade). All file ops run elevated here and
                // are guarded to the two managed folders (see IsManagedInstallerPath).
                // Payload JSON: { "action":"list|delete|launch", "url":"<driver downloadUrl>", "path":"<full path>" }.
                if (pipeMsg.Extra.TryGetValue("ManageDriverInstaller", out object mgrObj))
                {
                    string resultJson = "{\"success\":false,\"message\":\"Bad request.\"}";
                    try
                    {
                        string payload = mgrObj?.ToString() ?? "";
                        string mgrAction = "", mgrUrl = "", mgrPath = "";
                        if (System.Text.Json.JsonDocument.Parse(payload).RootElement is var root)
                        {
                            if (root.TryGetProperty("action", out var a)) mgrAction = a.GetString() ?? "";
                            if (root.TryGetProperty("url", out var u)) mgrUrl = u.GetString() ?? "";
                            if (root.TryGetProperty("path", out var p)) mgrPath = p.GetString() ?? "";
                        }
                        Logger.Info($"Pipe: ManageDriverInstaller action='{mgrAction}' path='{mgrPath}'");

                        switch (mgrAction)
                        {
                            case "list":
                                resultJson = "{\"success\":true,\"installers\":" +
                                    Services.MsiClawDriverCheckService.CachedInstallersJson(mgrUrl) + "}";
                                break;
                            case "delete":
                                var del = Services.MsiClawDriverCheckService.DeleteCachedInstaller(mgrPath);
                                resultJson = "{\"success\":" + (del.Contains("\"success\":true") ? "true" : "false") +
                                    ",\"installers\":" + Services.MsiClawDriverCheckService.CachedInstallersJson(mgrUrl) + "}";
                                break;
                            case "launch":
                                resultJson = Services.MsiClawDriverCheckService.LaunchCachedInstaller(mgrPath);
                                break;
                            default:
                                resultJson = "{\"success\":false,\"message\":\"Unknown action.\"}";
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: ManageDriverInstaller threw: {ex.Message}");
                        resultJson = "{\"success\":false,\"message\":\"" + ex.Message.Replace("\"", "'") + "\"}";
                    }
                    if (pipeServer != null && pipeServer.IsConnected)
                    {
                        var response = new global::Windows.Foundation.Collections.ValueSet
                        {
                            { "DriverInstallerResult", resultJson },
                        };
                        var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                        responseMsg.RequestId = pipeMsg.RequestId;
                        pipeServer.SendMessage(responseMsg.ToJson());
                    }
                    return;
                }

                // Handle display brightness adjustment request (+/- percentage delta)
                if (pipeMsg.Extra.TryGetValue("AdjustBrightness", out object brightnessObj))
                {
                    try
                    {
                        int delta = 0;
                        if (brightnessObj is int di) delta = di;
                        else if (brightnessObj is long dl) delta = (int)dl;   // JSON numbers deserialize as long
                        else if (brightnessObj is double dd) delta = (int)dd;
                        else if (brightnessObj is string ds) int.TryParse(ds, out delta);

                        Logger.Info($"Pipe: AdjustBrightness delta={delta}");
                        AdjustBrightness(delta);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: AdjustBrightness threw: {ex.Message}");
                    }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Handle system volume adjustment request (+/- percentage delta)
                if (pipeMsg.Extra.TryGetValue("AdjustVolume", out object volumeObj))
                {
                    try
                    {
                        int delta = 0;
                        if (volumeObj is int vi) delta = vi;
                        else if (volumeObj is long vl) delta = (int)vl;
                        else if (volumeObj is double vd) delta = (int)vd;
                        else if (volumeObj is string vs) int.TryParse(vs, out delta);

                        Logger.Info($"Pipe: AdjustVolume delta={delta}");
                        AdjustVolume(delta);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: AdjustVolume threw: {ex.Message}");
                    }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Absolute set for the media-slider tile (brightness top, volume bottom).
                if (pipeMsg.Extra.TryGetValue("SetBrightnessLevel", out object setBrightObj))
                {
                    try
                    {
                        int level = ParsePipeInt(setBrightObj);
                        level = Math.Max(0, Math.Min(100, level));
                        Sidebar.BrightnessManager.SetBrightness(level);
                        Logger.Info($"Pipe: SetBrightnessLevel {level}%");
                    }
                    catch (Exception ex) { Logger.Warn($"Pipe: SetBrightnessLevel threw: {ex.Message}"); }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }
                if (pipeMsg.Extra.TryGetValue("SetVolumeLevel", out object setVolObj))
                {
                    try
                    {
                        int level = ParsePipeInt(setVolObj);
                        level = Math.Max(0, Math.Min(100, level));
                        using var mgr = new Sidebar.Audio.AudioManager();
                        mgr.SetVolume(level);
                        Logger.Info($"Pipe: SetVolumeLevel {level}%");
                    }
                    catch (Exception ex) { Logger.Warn($"Pipe: SetVolumeLevel threw: {ex.Message}"); }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }
                // Current brightness + volume for initialising the media-slider tile.
                if (pipeMsg.Extra.ContainsKey("GetMediaLevels"))
                {
                    int brightness = 50, volume = 50;
                    try { brightness = Sidebar.BrightnessManager.GetBrightness(); } catch (Exception ex) { Logger.Warn($"GetMediaLevels brightness: {ex.Message}"); }
                    try { using var mgr = new Sidebar.Audio.AudioManager(); volume = mgr.GetVolume(); } catch (Exception ex) { Logger.Warn($"GetMediaLevels volume: {ex.Message}"); }
                    if (pipeServer != null && pipeServer.IsConnected)
                    {
                        var response = new global::Windows.Foundation.Collections.ValueSet
                        {
                            { "MediaLevels", "{\"brightness\":" + brightness + ",\"volume\":" + volume + "}" },
                        };
                        var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                        responseMsg.RequestId = pipeMsg.RequestId;
                        pipeServer.SendMessage(responseMsg.ToJson());
                    }
                    return;
                }

                // Handle controller-button query from widget's binding flyout.
                // The widget's DispatcherTimer polls every ~100 ms while the binding UI is open.
                // Returns the current raw XInput button bitmask read by ControllerHotkeyMonitor
                // (which is in HidHide's allowlist, so it sees the physical MSI Claw controller).
                if (pipeMsg.Extra.ContainsKey("QueryControllerButtons"))
                {
                    uint buttons = controllerHotkeyMonitor?.CurrentButtons ?? 0;
                    var response = new global::Windows.Foundation.Collections.ValueSet
                    {
                        { "ControllerButtons", (int)buttons }
                    };
                    var responseMsg = Shared.IPC.PipeMessage.FromValueSet(response);
                    responseMsg.RequestId = pipeMsg.RequestId;
                    pipeServer?.SendMessage(responseMsg.ToJson());
                    Logger.Debug($"Pipe: QueryControllerButtons → 0x{buttons:X4}");
                    return;
                }

                // Handle tile hotkey registration from widget (controller button → tile action)
                if (pipeMsg.Extra.TryGetValue("UpdateTileHotkeys", out object tileHotkeysObj) && tileHotkeysObj is string tileHotkeysJson)
                {
                    try
                    {
                        Logger.Info($"Pipe: UpdateTileHotkeys json='{tileHotkeysJson}'");
                        ApplyTileHotkeys(tileHotkeysJson);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: UpdateTileHotkeys threw: {ex.Message}");
                    }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Handle RTSS OSD action notification request
                if (pipeMsg.Extra.TryGetValue("ShowOSDNotification", out object notifObj) && notifObj is string notifText)
                {
                    try
                    {
                        Logger.Info($"Pipe: ShowOSDNotification text='{notifText}'");
                        rtssManager?.ShowNotification(notifText);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: ShowOSDNotification threw: {ex.Message}");
                    }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Handle "reapply TDP to hardware" request. After a power source change or
                // Custom-mode re-entry, Windows/Legion firmware may silently reset TDP limits
                // even though our cached value hasn't changed. Widget asks the helper to
                // re-push the current TDP to hardware without going through the property
                // system's equality check. Must NOT modify the profile's saved TDP (the old
                // N-1/N trick did and corrupted global.xml by 1 W).
                if (pipeMsg.Extra.ContainsKey("ReapplyTDP"))
                {
                    try
                    {
                        if (performanceManager != null && !performanceManager.IsAutoTDPActive)
                        {
                            int currentTdp = performanceManager.TDP.Value;
                            Logger.Info($"Pipe: ReapplyTDP request — re-pushing current {currentTdp}W to hardware");
                            performanceManager.SetTDP(currentTdp);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: ReapplyTDP threw: {ex.Message}");
                    }
                    SendPipeAck(pipeMsg.RequestId);
                    return;
                }

                // Handle gyro-calibration request. Widget fires this as a one-shot while
                // the user holds the Legion controllers still; helper sends the HID
                // output report to the controllers' firmware to capture a fresh bias.
                if (pipeMsg.Extra.ContainsKey("CalibrateLegionGyro"))
                {
                    try
                    {
                        var monitor = legionButtonMonitor;
                        bool ok = monitor != null && monitor.CalibrateGyro(true, true);
                        Logger.Info($"Pipe: CalibrateLegionGyro request -> {(ok ? "sent" : "failed")}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: CalibrateLegionGyro threw: {ex.Message}");
                    }
                    SendPipeAck(pipeMsg.RequestId);
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
                // Controller-State diagnostic (Controller tab bottom card) — read-only inspection
                else if (functionValue == (int)Function.RequestControllerState)
                {
                    string controllerState = BuildControllerStateString();
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", controllerState);
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    Logger.Info($"Pipe: ControllerState = {controllerState}");
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
                // Profile Save Flags: which settings the widget wants captured per-game vs.
                // left as device-wide globals. Routes helper-side writes in the AutoTDP and
                // Legion controller setting handlers.
                else if (functionValue == (int)Function.ProfileSaveFlags)
                {
                    if (request.Content != null)
                    {
                        ApplyProfileSaveFlags(request.Content.ToString());
                    }
                }
                // Power Source Profile Config: mirror of the widget's AC/DC power-plan
                // auto-switch settings. Helper uses these on SystemManager.PowerSourceChanged
                // so the switch still happens while the widget is suspended (issue #72).
                else if (functionValue == (int)Function.PowerSourceProfileConfig)
                {
                    if (request.Content != null)
                    {
                        ApplyPowerSourceProfileConfig(request.Content.ToString());
                    }
                }
                // Per-state TDP / TDPBoost values from the widget. Cached so the helper
                // can apply them on AC/DC transitions independent of widget lifecycle.
                else if (functionValue == (int)Function.PowerSourceProfileValues)
                {
                    if (request.Content != null)
                    {
                        ApplyPowerSourceProfileValues(request.Content.ToString());
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
                // Per-game HW Controller Exception (MSI Claw): persist the flag for the running
                // game. No live controller swap — it applies at the next game start.
                else if (functionValue == (int)Function.HwControllerException)
                {
                    if (request.Content != null)
                    {
                        bool on = request.Content.ToString().ToLower() == "true";
                        string gameKey = systemManager?.RunningGame?.Value.IsValid() == true
                            ? systemManager.RunningGame.Value.GameId.Name
                            : null;
                        if (!string.IsNullOrEmpty(gameKey))
                        {
                            SetHwControllerException(gameKey, on);
                            Logger.Info($"Pipe: HW controller exception for '{gameKey}' = {on}");
                        }
                        else
                        {
                            Logger.Warn("Pipe: HwControllerException ignored — no valid running game");
                        }
                    }
                }
                // LED color based on battery SoC (MSI Claw): persist + apply/restore immediately.
                else if (functionValue == (int)Function.LedColorBySoc)
                {
                    if (request.Content != null)
                    {
                        bool on = request.Content.ToString().ToLower() == "true";
                        SetLedColorBySoc(on);
                        Logger.Info($"Pipe: LED color based on SoC set to: {on}");
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
                // usbip-win2: Install request — install the bundled MSI (VIIPER backend prerequisite).
                // Only when explicitly requested with a Set command and "install" content.
                else if (functionValue == (int)Function.InstallUsbip)
                {
                    bool shouldInstall = request.Command == Shared.Enums.Command.Set && request.Content == "install";
                    if (!shouldInstall)
                    {
                        Logger.Debug("Pipe: InstallUsbip - Ignoring non-install request (Get or empty content)");
                        return;
                    }

                    Logger.Info("Pipe: usbip-win2 installation requested from widget");
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            int code = XboxGamingBarHelper.Setup.UsbipInstaller.Run();
                            Logger.Info($"Pipe: usbip-win2 install finished (exit={code}); refreshing detection");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Pipe: usbip-win2 install failed: {ex.Message}");
                        }

                        // Re-detect + push the usbip status so the onboarding row/badge refreshes.
                        // (A reboot is usually required before detection flips to true.)
                        try { settingsManager?.UsbipInstalled?.Refresh(); }
                        catch (Exception ex) { Logger.Warn($"Pipe: usbip status refresh failed: {ex.Message}"); }
                    });
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true); // Acknowledge request started
                }
                // Steam Xbox extended controller driver conflict detection
                else if (functionValue == (int)Function.SteamXboxDriverDetected)
                {
                    bool detected = DetectSteamXboxDriver();
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", detected);
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    Logger.Info($"Pipe: Steam Xbox driver detected: {detected}");
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
                // RTSS: Install request - only install when explicitly requested with Set command and "install" content
                else if (functionValue == (int)Function.InstallRTSS)
                {
                    bool shouldInstall = request.Command == Shared.Enums.Command.Set && request.Content == "install";

                    if (!shouldInstall)
                    {
                        Logger.Debug("Pipe: InstallRTSS - Ignoring non-install request (Get or empty content)");
                        return;
                    }

                    Logger.Info("Pipe: RTSS installation requested from widget");
                    _ = Task.Run(() =>
                    {
                        bool success = XboxGamingBarHelper.Labs.RtssInstallHelper.Install();
                        bool installed = XboxGamingBarHelper.Labs.RtssInstallHelper.IsInstalled();
                        var updateMsg = new Shared.IPC.PipeMessage
                        {
                            Command = Shared.Enums.Command.Set,
                            Function = Function.RTSSInstalled,
                            Content = installed.ToString()
                        };
                        SendPipeMessage(updateMsg);
                        Logger.Info($"Pipe: RTSS installation complete (success={success}), sent updated status: {installed}");
                    });

                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true); // Acknowledge request started
                }
                // Controller: fire a short test rumble pulse on the physical Claw (no game needed).
                else if (functionValue == (int)Function.TestControllerVibration)
                {
                    if (request.Command != Shared.Enums.Command.Set) { return; }
                    Logger.Info("Pipe: test controller vibration requested from widget");
                    clawButtonMonitor?.TestVibration();
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true);
                }
                // Special Controller Buttons: fire a momentary Xbox Guide tap on the virtual ViGEm
                // controller (opens Steam Big Picture / in-game overlay). Used by the "Xbox Button"
                // app action assigned to a tile or front button. Fire-and-forget.
                else if (functionValue == (int)Function.EmulateXboxGuide)
                {
                    if (request.Command != Shared.Enums.Command.Set) { return; }
                    Logger.Info("Pipe: Xbox Guide tap requested from widget");
                    clawButtonMonitor?.TriggerGuideTap();
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true);
                }
                // Game Bar auto-nav: widget-bar position of ClawTweaks → RB hop count = position-1.
                else if (functionValue == (int)Function.GameBarWidgetPosition)
                {
                    if (request.Command != Shared.Enums.Command.Set) { return; }
                    if (int.TryParse(request.Content, out int pos))
                    {
                        if (pos < 1) pos = 1;
                        if (pos > 10) pos = 10;
                        GameBarAutoNavRbCount = pos - 1;
                        Logger.Info($"Pipe: ClawTweaks widget position = {pos} → RB hops = {GameBarAutoNavRbCount}");
                    }
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true);
                }
                // Standalone app-mode window reports its open/closed state so the "Open ClawTweaks
                // Window" action can toggle (second press closes it).
                else if (functionValue == (int)Function.AppModeWindowState)
                {
                    if (request.Command != Shared.Enums.Command.Set) { return; }
                    bool open = string.Equals(request.Content, "true", StringComparison.OrdinalIgnoreCase);
                    SetAppModeWindowOpen(open);
                }
                // Onboarding: run the proven prerequisite check/installer (embedded Setup-Tools.ps1)
                // which detects + installs the common tools (PawnIO, HidHide, RTSS) in one pass, then
                // installs the BACKEND-SPECIFIC emulation driver — usbip-win2 when VIIPER is active
                // (the default), or the legacy ViGEmBus when the user has switched to Legacy. ViGEm is
                // deliberately NOT installed in VIIPER mode. Finally push each *Installed status so the
                // onboarding rows + badge refresh.
                else if (functionValue == (int)Function.RunToolSetup)
                {
                    if (request.Command != Shared.Enums.Command.Set || request.Content != "install") { return; }
                    Logger.Info("Pipe: tool setup (Setup-Tools.ps1) requested from widget");
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            // Common tools only (PawnIO/HidHide/RTSS); ViGEm is excluded from the "all"
                            // run in Setup-Tools.ps1 and only installed on demand below for Legacy.
                            int code = XboxGamingBarHelper.Setup.ToolSetupRunner.Run();
                            Logger.Info($"Pipe: tool setup finished (exit={code}); pushing tool statuses");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Pipe: tool setup run failed: {ex.Message}");
                        }

                        // Backend-specific emulation driver: VIIPER → bundled usbip-win2 MSI;
                        // Legacy → ViGEmBus (via the per-tool winget path). Never install both.
                        bool viiperBackend = settingsManager?.EmulationBackend?.Value == true;
                        try
                        {
                            if (viiperBackend)
                            {
                                int uc = XboxGamingBarHelper.Setup.UsbipInstaller.Run();
                                Logger.Info($"Pipe: tool setup — VIIPER backend, usbip-win2 install (exit={uc})");
                            }
                            else
                            {
                                int vc = XboxGamingBarHelper.Setup.ToolSetupRunner.Run("vigem");
                                Logger.Info($"Pipe: tool setup — Legacy backend, ViGEmBus install (exit={vc})");
                            }
                        }
                        catch (Exception ex) { Logger.Error($"Pipe: tool setup emulation-driver install failed: {ex.Message}"); }

                        // Re-detect + push usbip status (VIIPER prerequisite).
                        try { settingsManager?.UsbipInstalled?.Refresh(); }
                        catch (Exception ex) { Logger.Warn($"Pipe: usbip status refresh failed: {ex.Message}"); }

                        // Re-detect + push each tool's status (best-effort).
                        try
                        {
                            bool vigem = XboxGamingBarHelper.Labs.ViGEmBusHelper.IsInstalled();
                            SendPipeMessage(new Shared.IPC.PipeMessage { Command = Shared.Enums.Command.Set, Function = Function.ViGEmBusInstalled, Content = vigem.ToString() });
                        }
                        catch (Exception ex) { Logger.Warn($"Pipe: ViGEm status push failed: {ex.Message}"); }
                        try
                        {
                            bool hid = XboxGamingBarHelper.Labs.HidHideHelper.IsInstalled();
                            SendPipeMessage(new Shared.IPC.PipeMessage { Command = Shared.Enums.Command.Set, Function = Function.HidHideInstalled, Content = hid.ToString() });
                        }
                        catch (Exception ex) { Logger.Warn($"Pipe: HidHide status push failed: {ex.Message}"); }
                        try
                        {
                            bool rtss = XboxGamingBarHelper.Labs.RtssInstallHelper.IsInstalled();
                            SendPipeMessage(new Shared.IPC.PipeMessage { Command = Shared.Enums.Command.Set, Function = Function.RTSSInstalled, Content = rtss.ToString() });
                        }
                        catch (Exception ex) { Logger.Warn($"Pipe: RTSS status push failed: {ex.Message}"); }
                        try
                        {
                            performanceManager?.RefreshPawnIOInstalledStatus();
                        }
                        catch (Exception ex) { Logger.Warn($"Pipe: PawnIO status refresh failed: {ex.Message}"); }
                    });
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true); // Acknowledge request started
                }
                // ViGEm: Uninstall request
                else if (functionValue == (int)Function.UninstallViGEm)
                {
                    if (request.Command != Shared.Enums.Command.Set || request.Content != "uninstall") { return; }
                    Logger.Info("Pipe: ViGEmBus uninstall requested from widget");
                    _ = Task.Run(() =>
                    {
                        XboxGamingBarHelper.Labs.ToolUninstaller.UninstallViaArp("ViGEm Bus Driver");
                        bool installed = XboxGamingBarHelper.Labs.ViGEmBusHelper.IsInstalled();
                        SendPipeMessage(new Shared.IPC.PipeMessage { Command = Shared.Enums.Command.Set, Function = Function.ViGEmBusInstalled, Content = installed.ToString() });
                        Logger.Info($"Pipe: ViGEmBus uninstall complete, sent updated status: {installed}");
                    });
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true);
                }
                // usbip-win2: Uninstall request (VIIPER backend driver)
                else if (functionValue == (int)Function.UninstallUsbip)
                {
                    if (request.Command != Shared.Enums.Command.Set || request.Content != "uninstall") { return; }
                    Logger.Info("Pipe: usbip-win2 uninstall requested from widget");
                    _ = Task.Run(() =>
                    {
                        try { XboxGamingBarHelper.Labs.ToolUninstaller.UninstallUsbip(); }
                        catch (Exception ex) { Logger.Warn($"Pipe: usbip-win2 uninstall failed: {ex.Message}"); }
                        // Push updated status (a reboot is usually required before detection flips).
                        try { settingsManager?.UsbipInstalled?.Refresh(); }
                        catch (Exception ex) { Logger.Warn($"Pipe: usbip status refresh failed: {ex.Message}"); }
                        Logger.Info("Pipe: usbip-win2 uninstall complete");
                    });
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true);
                }
                // HidHide: Uninstall request
                else if (functionValue == (int)Function.UninstallHidHide)
                {
                    if (request.Command != Shared.Enums.Command.Set || request.Content != "uninstall") { return; }
                    Logger.Info("Pipe: HidHide uninstall requested from widget");
                    _ = Task.Run(() =>
                    {
                        XboxGamingBarHelper.Labs.ToolUninstaller.UninstallHidHide();
                        bool installed = XboxGamingBarHelper.Labs.HidHideHelper.IsInstalled();
                        SendPipeMessage(new Shared.IPC.PipeMessage { Command = Shared.Enums.Command.Set, Function = Function.HidHideInstalled, Content = installed.ToString() });
                        Logger.Info($"Pipe: HidHide uninstall complete, sent updated status: {installed}");
                    });
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true);
                }
                // RTSS: Uninstall request
                else if (functionValue == (int)Function.UninstallRTSS)
                {
                    if (request.Command != Shared.Enums.Command.Set || request.Content != "uninstall") { return; }
                    Logger.Info("Pipe: RTSS uninstall requested from widget");
                    _ = Task.Run(() =>
                    {
                        XboxGamingBarHelper.Labs.ToolUninstaller.UninstallRtss();
                        bool installed = XboxGamingBarHelper.Labs.RtssInstallHelper.IsInstalled();
                        SendPipeMessage(new Shared.IPC.PipeMessage { Command = Shared.Enums.Command.Set, Function = Function.RTSSInstalled, Content = installed.ToString() });
                        Logger.Info($"Pipe: RTSS uninstall complete, sent updated status: {installed}");
                    });
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true);
                }
                // PawnIO: Uninstall request
                else if (functionValue == (int)Function.UninstallPawnIO)
                {
                    if (request.Command != Shared.Enums.Command.Set || request.Content != "uninstall") { return; }
                    Logger.Info("Pipe: PawnIO uninstall requested from widget");
                    _ = Task.Run(() =>
                    {
                        XboxGamingBarHelper.Labs.ToolUninstaller.UninstallViaArp("PawnIO");
                        performanceManager?.RefreshPawnIOInstalledStatus();
                        Logger.Info("Pipe: PawnIO uninstall complete, refreshed installed status");
                    });
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add("Content", true);
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
                        // Resolve the AppPackages probe directory in this order:
                        //   1. GOTWEAKS_APPPACKAGES_DIR env var (overrides everything — useful
                        //      for any developer with a non-default checkout location).
                        //   2. Walk up from the deployed helper's source-tree, looking for a
                        //      "XboxGamingBarPackage\AppPackages" directory. This works on the
                        //      author's machine and any contributor working from a clone.
                        // On a normal user's installed system, both will fail and we return a
                        // clear "Error" — the widget's debug-panel update probe handles that
                        // gracefully (this whole code path is debug-only UX).
                        string appPackagesPath = ResolveAppPackagesProbeDir();

                        if (string.IsNullOrEmpty(appPackagesPath) || !Directory.Exists(appPackagesPath))
                        {
                            response.Add("Error", $"AppPackages folder not found (set GOTWEAKS_APPPACKAGES_DIR env var to override).\nTried: {appPackagesPath ?? "<none resolved>"}");
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
                                // Parse versions from folder names (e.g., XboxGamingBarPackage_0.1.285.323_Debug_Test)
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
                // In-app update (Onboarding): list the most recent GitHub releases so the
                // widget can render "jump to / roll back" cards (latest + previous).
                else if (functionValue == (int)Function.ListAppReleases)
                {
                    Logger.Info("Pipe: ListAppReleases request received");
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    string json;
                    try
                    {
                        // This pipe handler is synchronous; block on the async fetch.
                        json = Services.GoTweaksUpdateService.CheckListAsync(2).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Pipe: ListAppReleases threw: {ex.Message}");
                        json = "[]";
                    }
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", json);
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                }
                // In-app update (Onboarding): poll the in-flight install progress (download %, phase).
                else if (functionValue == (int)Function.AppInstallStatus)
                {
                    response = new global::Windows.Foundation.Collections.ValueSet();
                    response.Add(nameof(Function), functionValue);
                    response.Add("Content", Services.GoTweaksUpdateService.GetInstallStatusJson());
                    response.Add("UpdatedTime", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                }
                // In-app update (Onboarding): install a chosen release by download URL.
                // AV-clean path — GoTweaksUpdateService downloads via HttpClient and installs
                // via the WinRT PackageManager (no PowerShell / Process.Start / runas).
                else if (functionValue == (int)Function.InstallAppRelease)
                {
                    if (request.Command != Shared.Enums.Command.Set) { return; }
                    string relUrl = request.Content;
                    Logger.Info($"Pipe: InstallAppRelease request received: {relUrl}");
                    response = new global::Windows.Foundation.Collections.ValueSet();

                    if (string.IsNullOrWhiteSpace(relUrl))
                    {
                        response.Add("UpdateStatus", "Error: No download URL provided");
                    }
                    else
                    {
                        // Ack first: AddPackageAsync(ForceApplicationShutdown) will close the
                        // widget mid-install, so push "Installing" before the connection drops.
                        response.Add("UpdateStatus", "Installing");
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                string r = await Services.GoTweaksUpdateService.InstallAsync(relUrl);
                                Logger.Info($"Pipe: InstallAppRelease finished: {r}");
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Pipe: InstallAppRelease failed: {ex.Message}");
                            }
                        });
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
                else
                {
                    // A TDP value may be persisted ONLY when the user moves the slider. An incoming
                    // Function.TDP Set is exactly that signal: the widget slider only emits on a real
                    // user ValueChanged (helper-sync pushes are suppressed via IsUpdatingUI/
                    // HelperSyncCount), so nothing else reaches here as a TDP Set. Mark the change as
                    // user-initiated for the synchronous duration of the property dispatch; TDP_-
                    // PropertyChanged reads this flag and persists only while it is set.
                    bool isUserTdpSet = functionValue == (int)Function.TDP
                                        && request.Command == Shared.Enums.Command.Set;
                    if (isUserTdpSet) tdpPersistFromUserSlider = true;
                    try
                    {
                        // Convert to ValueSet and use the existing property handling
                        var valueSet = request.ToValueSet();
                        response = properties.HandlePipeMessage(valueSet);
                    }
                    finally
                    {
                        if (isUserTdpSet) tdpPersistFromUserSlider = false;
                    }
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
        /// Pushes an unsolicited GoTweaks self-update payload to the widget.
        /// Widget renders a Quick-tab banner + GoTweaks Update card when
        /// IsUpdateAvailable is true (unless the user has set HideBanner).
        /// </summary>
        public static bool PushGoTweaksUpdate(Services.GoTweaksUpdateResult result)
        {
            try
            {
                if (pipeServer == null || !pipeServer.IsConnected) return false;
                if (result == null) return false;
                var payload = new global::Windows.Foundation.Collections.ValueSet
                {
                    { "GoTweaksUpdate", result.ToJson() },
                };
                var msg = Shared.IPC.PipeMessage.FromValueSet(payload);
                return pipeServer.SendMessage(msg.ToJson());
            }
            catch (Exception ex)
            {
                Logger.Debug($"PushGoTweaksUpdate failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Pushes an unsolicited "DriverUpdatesAvailable" message to the widget
        /// after the startup driver probe completes. The widget uses this to
        /// light up the Quick tab tile without having to ask. If the widget
        /// isn't connected yet (launch race), this is best-effort — we'll also
        /// push again if the widget later sends CheckDriverUpdates.
        /// </summary>
        public static bool PushDriverUpdatesAvailable(int count)
        {
            try
            {
                if (pipeServer == null || !pipeServer.IsConnected) return false;
                var payload = new global::Windows.Foundation.Collections.ValueSet
                {
                    { "DriverUpdatesAvailable", count },
                };
                var msg = Shared.IPC.PipeMessage.FromValueSet(payload);
                return pipeServer.SendMessage(msg.ToJson());
            }
            catch (Exception ex)
            {
                Logger.Debug($"PushDriverUpdatesAvailable failed: {ex.Message}");
                return false;
            }
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
        /// <summary>
        /// Turns off Windows' "controller Xbox/Guide button opens Game Bar" shortcuts so the virtual
        /// controller's Guide button is free for other apps (e.g. Steam Big Picture) to claim. Writes
        /// the three HKCU\Software\Microsoft\GameBar nexus DWORDs to 0. HKCU of this elevated helper is
        /// the same user hive, so the change applies to the logged-in user. Windows may only pick it up
        /// after a sign-out/in or reboot.
        /// </summary>
        private static bool DisableGuideButtonGameBar()
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar"))
            {
                if (key == null)
                {
                    Logger.Warn("DisableGuideButtonGameBar: could not open HKCU\\Software\\Microsoft\\GameBar");
                    return false;
                }
                key.SetValue("UseNexusForGameBarEnabled", 0, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("TaskSwitcherNexusInjectionEnabled", 0, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("GamepadNexusChordEnabled", 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            Logger.Info("DisableGuideButtonGameBar: set GameBar nexus reg values (UseNexus/TaskSwitcherNexusInjection/GamepadNexusChord) to 0");
            return true;
        }

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

        /// <summary>
        /// Adjust the display brightness by <paramref name="delta"/> percent.
        /// Delegates to BrightnessManager which uses WMI internally.
        /// </summary>
        /// <summary>
        /// Raise or lower the current PL1 TDP by <paramref name="delta"/> watts.
        /// Clamps PL1 to [4 W, 36 W] — ensures PL2 = PL1+1 ≤ 37 W (MSI Claw hardware limit).
        /// </summary>
        internal static void AdjustTDPByWatts(int delta)
        {
            try
            {
                if (performanceManager == null) { Logger.Warn("AdjustTDPByWatts: performanceManager not available"); return; }
                int current = performanceManager.CurrentSPL > 0 ? performanceManager.CurrentSPL : 15;
                const int TDP_MIN = 4;
                const int TDP_MAX = 36;  // PL2 = PL1+1 ≤ 37 W
                int next = Math.Max(TDP_MIN, Math.Min(TDP_MAX, current + delta));
                performanceManager.SetTDP(next);
                Logger.Info($"AdjustTDPByWatts: {current}W → {next}W (delta={delta})");
            }
            catch (Exception ex) { Logger.Warn($"AdjustTDPByWatts failed: {ex.Message}"); }
        }

        // Parses a pipe payload int that may arrive as int/long/double/string (JSON numbers deserialize as long).
        private static int ParsePipeInt(object o)
        {
            switch (o)
            {
                case int i: return i;
                case long l: return (int)l;
                case double d: return (int)d;
                case string s: return int.TryParse(s, out var v) ? v : 0;
                default: return 0;
            }
        }

        internal static void AdjustVolume(int delta)
        {
            try
            {
                using var mgr = new Sidebar.Audio.AudioManager();
                int current = mgr.GetVolume();
                int next = Math.Max(0, Math.Min(100, current + delta));
                mgr.SetVolume(next);
                Logger.Info($"AdjustVolume: {current}% → {next}% (delta={delta})");
            }
            catch (Exception ex) { Logger.Warn($"AdjustVolume failed: {ex.Message}"); }
        }

        private static void AdjustBrightness(int delta)
        {
            try
            {
                int current = Sidebar.BrightnessManager.GetBrightness();
                int next = Math.Max(0, Math.Min(100, current + delta));
                Sidebar.BrightnessManager.SetBrightness(next);
                Logger.Info($"AdjustBrightness: {current}% → {next}% (delta={delta})");
            }
            catch (Exception ex)
            {
                Logger.Warn($"AdjustBrightness failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Locates the AppPackages probe directory for the debug-panel "check local update"
        /// action. Strategy in order:
        ///   1. GOTWEAKS_APPPACKAGES_DIR env var — explicit override for any contributor.
        ///   2. Walk up from the helper exe — works only when the helper runs out of the
        ///      build output under the source tree (rare; the deployed helper lives in
        ///      LocalCache and is too far from the source for this to hit).
        ///   3. Probe a list of common developer checkout layouts under %USERPROFILE%.
        ///      This is the path that works for the deployed helper on a dev machine.
        /// On a normal user's installed system all three fail and we return null — the
        /// caller surfaces a clear error and the rest of the system is unaffected (this
        /// whole code path is debug-only UX behind the Debug panel).
        /// </summary>
        private static string ResolveAppPackagesProbeDir()
        {
            try
            {
                string envOverride = Environment.GetEnvironmentVariable("GOTWEAKS_APPPACKAGES_DIR");
                if (!string.IsNullOrEmpty(envOverride))
                {
                    return envOverride;
                }

                // Walk up from the helper exe — covers the rare case where the helper
                // is run directly from the source-tree build output without deploy.
                string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    var dir = new DirectoryInfo(exeDir);
                    for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                    {
                        string candidate = Path.Combine(dir.FullName, "XboxGamingBarPackage", "AppPackages");
                        if (Directory.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }

                // Probe common dev-checkout paths under the logged-in user's profile.
                // The scheduled-task helper runs elevated as the same user, so
                // Environment.SpecialFolder.UserProfile resolves to the developer's profile.
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    string[] roots =
                    {
                        Path.Combine(userProfile, "OneDrive", "Desktop", "Diego", "projects", "XboxGamingBar"),
                        Path.Combine(userProfile, "Desktop", "Diego", "projects", "XboxGamingBar"),
                        Path.Combine(userProfile, "source", "repos", "XboxGamingBar"),
                        Path.Combine(userProfile, "projects", "XboxGamingBar"),
                        Path.Combine(userProfile, "repos", "XboxGamingBar"),
                    };
                    foreach (var root in roots)
                    {
                        string candidate = Path.Combine(root, "XboxGamingBarPackage", "AppPackages");
                        if (Directory.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"ResolveAppPackagesProbeDir: {ex.Message}");
            }
            return null;
        }

    }
}
