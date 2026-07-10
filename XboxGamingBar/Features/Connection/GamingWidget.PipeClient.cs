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
        /// Handles messages received from helper via Named Pipe.
        /// </summary>
        private async void PipeClient_MessageReceived(object sender, IPC.PipeMessageEventArgs e)
        {
            try
            {
                // Only process messages if this is the active widget instance
                var activeWidget = App.GetActiveGamingWidget();
                if (activeWidget != null && activeWidget != this)
                {
                    Logger.Debug("Widget received pipe message but this is NOT the active instance. Ignoring.");
                    return;
                }

                // Parse the JSON message to ValueSet
                var message = ParsePipeMessageToValueSet(e.Message);
                if (message == null)
                {
                    Logger.Warn("Failed to parse pipe message");
                    return;
                }

                // Use TryGetValue — not every helper→widget message carries a "Function" key
                // (e.g. TileHotkeyFired, DriverUpdatesAvailable, GoTweaksUpdate). Indexing a
                // missing key threw KeyNotFoundException here, which aborted the whole handler
                // (logged as "Error processing pipe message from helper: key") and silently
                // dropped those messages.
                object funcForLog = message.TryGetValue("Function", out var funcLogVal) ? funcLogVal : "(none)";
                Logger.Debug($"Widget received pipe message: Function={funcForLog}");

                // Check for focus widget request from helper
                if (message.TryGetValue("Function", out object funcObj) &&
                    Convert.ToInt32(funcObj) == (int)Shared.Enums.Function.Labs_FocusWidget)
                {
                    Logger.Info("Focus widget request received from helper via pipe");
                    await FocusThisWidgetAsync();
                    return;
                }

                // Toggle: helper asks the standalone app-mode window to close (second press of the
                // "Open ClawTweaks Window" action / front MSI button).
                if (message.TryGetValue("Function", out object closeFuncObj) &&
                    Convert.ToInt32(closeFuncObj) == (int)Shared.Enums.Function.CloseAppModeWindow)
                {
                    Logger.Info("CloseAppModeWindow request received from helper via pipe");
                    App.CloseStandaloneAppModeWindow();
                    return;
                }

                // Check for Quick Metrics push from helper
                if (message.TryGetValue("Function", out object qmFuncObj) &&
                    Convert.ToInt32(qmFuncObj) == (int)Shared.Enums.Function.QuickMetrics)
                {
                    if (message.TryGetValue("Content", out object content) && content is string metricsJson)
                    {
                        UpdateQuickMetrics(metricsJson);
                    }
                    return;
                }

                // Helper fires a tile hotkey that can't be executed directly (widget-side action)
                if (message.TryGetValue("TileHotkeyFired", out object tileHotkeyIdObj)
                    && tileHotkeyIdObj is string tileHotkeyId)
                {
                    Logger.Info($"TileHotkeyFired received for tile '{tileHotkeyId}'");
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { SimulateTileHotkeyFired(tileHotkeyId); }
                        catch (Exception ex) { Logger.Error($"TileHotkeyFired dispatch error: {ex.Message}"); }
                    });
                    return;
                }

                // Helper pushes MSI fan EC read-back in response to a verify request.
                if (message.TryGetValue("MsiFanStatus", out object msiFanStatusObj) && msiFanStatusObj is string msiFanStatus)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { OnMsiFanStatus(msiFanStatus); }
                        catch (Exception ex) { Logger.Error($"OnMsiFanStatus dispatch error: {ex.Message}"); }
                    });
                    return;
                }

                // Helper pushes the Intel thermal stack (IPF/DTT) status for the experimental Fan-tab controls.
                if (message.TryGetValue("IntelThermalStatus", out object intelThermalObj) && intelThermalObj is string intelThermalStatus)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { OnIntelThermalStatus(intelThermalStatus); }
                        catch (Exception ex) { Logger.Error($"OnIntelThermalStatus dispatch error: {ex.Message}"); }
                    });
                    return;
                }

                // Helper pushes the fan-override probe read-back for the diagnostic register hunt.
                if (message.TryGetValue("MsiFanRegStatus", out object fanRegObj) && fanRegObj is string fanRegStatus)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { OnFanRegStatus(fanRegStatus); }
                        catch (Exception ex) { Logger.Error($"OnFanRegStatus dispatch error: {ex.Message}"); }
                    });
                    return;
                }

                // Helper pushes the experimental controller-HID probe response (hex report).
                if (message.TryGetValue("ClawHidProbeResult", out object hidProbeObj) && hidProbeObj is string hidProbeResult)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { if (ClawHidProbeResult != null) ClawHidProbeResult.Text = hidProbeResult; }
                        catch (Exception ex) { Logger.Error($"ClawHidProbeResult dispatch error: {ex.Message}"); }
                    });
                    return;
                }

                // Helper pushes the live firmware button→keyboard map (Controller-status refresh).
                if (message.TryGetValue("FwButtonMapResult", out object fwMapObj) && fwMapObj is string fwMapResult)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { OnFirmwareButtonMapResult(fwMapResult); }
                        catch (Exception ex) { Logger.Error($"FwButtonMapResult dispatch error: {ex.Message}"); }
                    });
                    return;
                }

                // Helper pushes the experimental native-fan detection report.
                if (message.TryGetValue("MsiFanDetectResult", out object fanDetObj) && fanDetObj is string fanDetResult)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { if (MsiFanDetectResult != null) MsiFanDetectResult.Text = fanDetResult; }
                        catch (Exception ex) { Logger.Error($"MsiFanDetectResult dispatch error: {ex.Message}"); }
                    });
                    return;
                }

                // Helper reports a resolved Left-MSI button click ("single"/"double") so the
                // double-click settings UI can flash a detection indicator for fine-tuning.
                if (message.TryGetValue("ClawClick", out object clawClickObj) && clawClickObj is string clawClick)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { OnClawClickDetected(clawClick); }
                        catch (Exception ex) { Logger.Error($"OnClawClickDetected dispatch error: {ex.Message}"); }
                    });
                    return;
                }

                // Helper pushes the persisted MSI fan preset/curve on connect (helper is the
                // single source of truth). The widget reflects it WITHOUT echoing back.
                if (message.TryGetValue("MsiFanState", out object msiFanStateObj) && msiFanStateObj is string msiFanState)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { OnMsiFanState(msiFanState); }
                        catch (Exception ex) { Logger.Error($"OnMsiFanState dispatch error: {ex.Message}"); }
                    });
                    return;
                }

                // Helper pushes DriverUpdatesAvailable as an unsolicited message
                // after its startup driver probe completes. Light up the Quick
                // tab tile; no other state needs updating yet.
                if (message.TryGetValue("DriverUpdatesAvailable", out object countObj))
                {
                    int count = 0;
                    try { count = Convert.ToInt32(countObj); } catch { }
                    UpdateDriverUpdatesTile(count);
                    return;
                }

                // Helper pushes GoTweaksUpdate (JSON blob) after startup
                // self-update probe. Also delivered as a response to an
                // explicit CheckGoTweaksUpdate request.
                if (message.TryGetValue("GoTweaksUpdate", out object goTweaksPayload)
                    && goTweaksPayload is string gtJson)
                {
                    HandleGoTweaksUpdatePush(gtJson);
                    return;
                }

                // Helper pushes DriverInstallComplete after a large background
                // download (e.g. Intel Arc 824 MB) finishes. Re-enables the
                // install button and shows the final status message.
                if (message.TryGetValue("DriverInstallComplete", out object driverCompleteObj)
                    && driverCompleteObj is string driverCompleteJson)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { OnDriverInstallComplete(driverCompleteJson); }
                        catch (Exception ex) { Logger.Error($"OnDriverInstallComplete dispatch error: {ex.Message}"); }
                    });
                    return;
                }

                // Skip TDP and CurrentTDP updates during Sticky TDP reapply
                if (isStickyTDPReapplying && message.ContainsKey("Function"))
                {
                    var function = Convert.ToInt32(message["Function"]);
                    if (function == (int)Shared.Enums.Function.TDP || function == (int)Shared.Enums.Function.CurrentTDP)
                    {
                        Logger.Debug("Skipping TDP/CurrentTDP pipe update during Sticky TDP reapply");
                        return;
                    }
                }

                // Dispatch the whole property-update path to the UI thread. NamedPipeClient
                // delivers MessageReceived on a background reader thread; GenericProperty.SetValue
                // fires InvokePropertyChanged synchronously, and several subscribers (color picker
                // sync, visibility toggles, ComboBox.SelectedIndex=...) touch XAML. Those throw
                // RPC_E_WRONG_THREAD when invoked off the UI apartment (seen consistently in
                // live logs around TDP / LightBrightness debounce-cancel paths). Dispatching
                // here keeps the whole property tree on the UI thread.
                if (Dispatcher == null)
                {
                    // Fallback: no dispatcher available (widget torn down); skip.
                    return;
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    isApplyingHelperUpdate = true;
                    try
                    {
                        properties.HandlePipeMessage(message);
                    }
                    finally
                    {
                        isApplyingHelperUpdate = false;
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing pipe message from helper: {ex.Message}");
            }
        }

        /// <summary>
        /// Unescapes a JSON string value in a single pass. A chained .Replace() is WRONG:
        /// un-doubling "\\" -> "\" first re-creates literal "\r"/"\n"/"\t" sequences from paths
        /// like "...\re2.exe", which a later .Replace("\\r", "\r") then mangles into a control
        /// character (this corrupted per-game controller-profile GameExePath values). Walking the
        /// string once and consuming each escape exactly once avoids the ambiguity.
        /// </summary>
        private static string UnescapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char next = s[++i];
                    switch (next)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(next); break; // unknown escape: keep the char, drop backslash
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Parses a pipe JSON message into a ValueSet.
        /// </summary>
        private Windows.Foundation.Collections.ValueSet ParsePipeMessageToValueSet(string json)
        {
            try
            {
                var result = new Windows.Foundation.Collections.ValueSet();
                json = json.Trim();
                if (!json.StartsWith("{") || !json.EndsWith("}"))
                    return null;

                // Simple JSON parsing
                var matches = System.Text.RegularExpressions.Regex.Matches(json,
                    @"""(\w+)""\s*:\s*(""[^""\\]*(\\.[^""\\]*)*""|-?\d+\.?\d*|true|false|null|\{[^{}]*\}|\[[^\[\]]*\])");

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var key = match.Groups[1].Value;
                    var value = match.Groups[2].Value;

                    if (value.StartsWith("\"") && value.EndsWith("\""))
                    {
                        result[key] = UnescapeJsonString(value.Substring(1, value.Length - 2));
                    }
                    else if (value == "true")
                    {
                        result[key] = true;
                    }
                    else if (value == "false")
                    {
                        result[key] = false;
                    }
                    else if (value == "null")
                    {
                        result[key] = null;
                    }
                    else if (value.StartsWith("{") || value.StartsWith("["))
                    {
                        result[key] = value;
                    }
                    else if (value.Contains("."))
                    {
                        if (double.TryParse(value, out var d))
                            result[key] = d;
                    }
                    else
                    {
                        if (int.TryParse(value, out var i))
                            result[key] = i;
                        else if (long.TryParse(value, out var l))
                            result[key] = l;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error parsing pipe message JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Handles Named Pipe disconnection from the helper.
        /// </summary>
        private async void PipeClient_Disconnected(object sender, EventArgs e)
        {
            Logger.Info("Named pipe disconnected from helper");

            // Ignore disconnects from inactive/unloading widget instances.
            // A new active instance will own reconnection.
            if (isUnloading || App.GetActiveGamingWidget() != this)
            {
                Logger.Info($"Skipping reconnect handling (isUnloading={isUnloading}, isActive={App.GetActiveGamingWidget() == this})");
                return;
            }

            // Unregister handlers
            App.PipeMessageReceived -= PipeClient_MessageReceived;
            App.PipeDisconnected -= PipeClient_Disconnected;

            // If the active mode had EC override on, the helper was driving 0xC6C8 every
            // 3s. With the helper gone (crash or kill), 0xC6C8 holds whatever RPM we last
            // wrote — fan stuck at that value until the helper reconnects or reboot.
            // Surface a warning in the fan card so the user isn't left wondering why the
            // fan won't ramp. Cleared on next successful pipe-connect (OnPipeConnectedAsync).
            bool activeUnlockWasOn = legionUnlockFanCurve != null && legionUnlockFanCurve.Value;

            // Show reconnecting state and trigger guarded reconnect flow.
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                ShowConnectionBanner(BannerState.Reconnecting);
                if (activeUnlockWasOn && FanCurveHelperDisconnectedWarning != null)
                {
                    FanCurveHelperDisconnectedWarning.Visibility = Windows.UI.Xaml.Visibility.Visible;
                }
            });

            Logger.Info("Pipe disconnected - starting automatic helper reconnection");
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                _ = LaunchHelperWithGuardsAsync("Pipe disconnected");
            });
        }

    }
}
