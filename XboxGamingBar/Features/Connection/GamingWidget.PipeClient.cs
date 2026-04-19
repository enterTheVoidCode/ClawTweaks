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

                Logger.Debug($"Widget received pipe message: Function={message["Function"]}");

                // Check for focus widget request from helper
                if (message.TryGetValue("Function", out object funcObj) &&
                    Convert.ToInt32(funcObj) == (int)Shared.Enums.Function.Labs_FocusWidget)
                {
                    Logger.Info("Focus widget request received from helper via pipe");
                    await FocusThisWidgetAsync();
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

                // Set flag to prevent auto-save when helper updates slider values
                isApplyingHelperUpdate = true;
                try
                {
                    // Handle the message via the properties system
                    properties.HandlePipeMessage(message);
                    await Task.Delay(50);
                }
                finally
                {
                    isApplyingHelperUpdate = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing pipe message from helper: {ex.Message}");
            }
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
                        result[key] = value.Substring(1, value.Length - 2)
                            .Replace("\\\"", "\"").Replace("\\\\", "\\")
                            .Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
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

            // Show reconnecting state and trigger guarded reconnect flow.
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                ShowConnectionBanner(BannerState.Reconnecting);
            });

            Logger.Info("Pipe disconnected - starting automatic helper reconnection");
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                _ = LaunchHelperWithGuardsAsync("Pipe disconnected");
            });
        }

    }
}
