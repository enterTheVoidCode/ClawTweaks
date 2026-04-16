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
        private void HotkeyMenuA_StateChanged(XboxGameBarHotkeyWatcher sender, HotkeySetStateChangedArgs args)
        {
            if (!args.HotkeySetDown) return; // Only trigger on press, not release
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ExecuteHotkeyAction("MenuA"));
        }

        private void HotkeyMenuB_StateChanged(XboxGameBarHotkeyWatcher sender, HotkeySetStateChangedArgs args)
        {
            if (!args.HotkeySetDown) return;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ExecuteHotkeyAction("MenuB"));
        }

        private void HotkeyMenuX_StateChanged(XboxGameBarHotkeyWatcher sender, HotkeySetStateChangedArgs args)
        {
            if (!args.HotkeySetDown) return;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ExecuteHotkeyAction("MenuX"));
        }

        private void HotkeyMenuY_StateChanged(XboxGameBarHotkeyWatcher sender, HotkeySetStateChangedArgs args)
        {
            if (!args.HotkeySetDown) return;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ExecuteHotkeyAction("MenuY"));
        }

        private void HotkeyMenuDpadUp_StateChanged(XboxGameBarHotkeyWatcher sender, HotkeySetStateChangedArgs args)
        {
            if (!args.HotkeySetDown) return;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ExecuteHotkeyAction("MenuDpadUp"));
        }

        private void HotkeyMenuDpadDown_StateChanged(XboxGameBarHotkeyWatcher sender, HotkeySetStateChangedArgs args)
        {
            if (!args.HotkeySetDown) return;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ExecuteHotkeyAction("MenuDpadDown"));
        }

        private void HotkeyMenuDpadLeft_StateChanged(XboxGameBarHotkeyWatcher sender, HotkeySetStateChangedArgs args)
        {
            if (!args.HotkeySetDown) return;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ExecuteHotkeyAction("MenuDpadLeft"));
        }

        private void HotkeyMenuDpadRight_StateChanged(XboxGameBarHotkeyWatcher sender, HotkeySetStateChangedArgs args)
        {
            if (!args.HotkeySetDown) return;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ExecuteHotkeyAction("MenuDpadRight"));
        }

        /// <summary>
        /// Execute the configured action for a hotkey combo.
        /// </summary>
        private void ExecuteHotkeyAction(string hotkeyName)
        {
            try
            {
                // Debounce check - prevent rapid repeated execution
                var now = DateTime.UtcNow;
                if (hotkeyLastExecuted.TryGetValue(hotkeyName, out DateTime lastExec))
                {
                    var elapsed = (now - lastExec).TotalMilliseconds;
                    if (elapsed < HotkeyDebounceMs)
                    {
                        return; // Skip - too soon since last execution
                    }
                }
                hotkeyLastExecuted[hotkeyName] = now;

                var settings = ApplicationData.Current.LocalSettings;
                int action = (int)(settings.Values[$"Hotkey_{hotkeyName}_Action"] ?? 0);
                string customKey = settings.Values[$"Hotkey_{hotkeyName}_Key"] as string ?? "";

                Logger.Info($"Executing hotkey action: {hotkeyName}, Action={(HotkeyAction)action}, CustomKey={customKey}");

                switch ((HotkeyAction)action)
                {
                    case HotkeyAction.Disabled:
                        // Do nothing
                        break;

                    case HotkeyAction.KeyboardKey:
                    case HotkeyAction.KeyboardShortcut:
                        if (!string.IsNullOrWhiteSpace(customKey))
                        {
                            _ = SendKeyboardShortcutViaHelper(customKey);
                        }
                        break;

                    case HotkeyAction.ToggleOSD:
                        // Toggle RTSS overlay between off and last used level
                        ToggleRTSSOsd();
                        break;

                    case HotkeyAction.Screenshot:
                        _ = SendKeyboardShortcutViaHelper("Win+Shift+S");
                        break;

                    case HotkeyAction.AltTab:
                        _ = SendKeyboardShortcutViaHelper("Alt+Tab");
                        break;

                    case HotkeyAction.AltF4:
                        _ = SendKeyboardShortcutViaHelper("Alt+F4");
                        break;

                    case HotkeyAction.OpenKeyboard:
                        _ = ToggleTouchKeyboard();
                        break;

                    case HotkeyAction.CtrlAltDel:
                        _ = SendKeyboardShortcutViaHelper("Ctrl+Alt+Delete");
                        break;

                    case HotkeyAction.TaskManager:
                        _ = SendKeyboardShortcutViaHelper("Ctrl+Shift+Escape");
                        break;

                    case HotkeyAction.FocusGoTweaks:
                        _ = FocusThisWidgetAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error executing hotkey action for {hotkeyName}: {ex.Message}");
            }
        }

    }
}
