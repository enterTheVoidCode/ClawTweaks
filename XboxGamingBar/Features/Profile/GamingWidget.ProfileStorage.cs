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

        private void SaveProfileToStorage(string profileName, PerformanceProfile profile)
        {
            // Never save to "No game detected" profile (case-insensitive check)
            if (profileName.IndexOf("No game detected", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Logger.Warn($"Attempted to save to storage with invalid profile name: {profileName}, skipping");
                return;
            }

            var settings = ApplicationData.Current.LocalSettings;
            var container = settings.CreateContainer($"Profile_{profileName}", ApplicationDataCreateDisposition.Always);

            // Group C only (plan §5.4). The hardware and OS values that used to be written here belong
            // to the helper's profile store; a copy would only be a second truth to drift. Leftover
            // keys from older builds are simply never read again — see LoadProfileFromStorage.
            container.Values["FluidMotionFrames"] = profile.FluidMotionFrames;
            container.Values["RadeonSuperResolution"] = profile.RadeonSuperResolution;
            container.Values["RadeonSuperResolutionSharpness"] = profile.RadeonSuperResolutionSharpness;
            container.Values["ImageSharpening"] = profile.ImageSharpening;
            container.Values["ImageSharpeningSharpness"] = profile.ImageSharpeningSharpness;
            container.Values["RadeonAntiLag"] = profile.RadeonAntiLag;
            container.Values["RadeonBoost"] = profile.RadeonBoost;
            container.Values["RadeonBoostResolution"] = profile.RadeonBoostResolution;
            container.Values["RadeonChill"] = profile.RadeonChill;
            container.Values["RadeonChillMinFPS"] = profile.RadeonChillMinFPS;
            container.Values["RadeonChillMaxFPS"] = profile.RadeonChillMaxFPS;
            container.Values["LegionPerformanceMode"] = profile.LegionPerformanceMode;
            container.Values["TDPModeIndex"] = profile.TDPModeIndex;
            container.Values["OverlayLevel"] = profile.OverlayLevel;
            // The MsiFanPreset / MsiFanCurve keys are no longer written (2026-08-02): the per-game fan
            // curve belongs to the helper's GameProfile now, like TDP. Existing containers keep their old
            // keys and are simply never read, the same way the group-A/B keys were left behind in §5.4.
            // Last-saved timestamp drives the "modified Nm/h/d ago" line on the profile
            // card and the "Last Modified" sort option in the Profiles tab. Stored as
            // UTC ticks so it survives timezone changes.
            container.Values["LastModifiedUtc"] = DateTime.UtcNow.Ticks;
        }

        private void LoadProfileFromStorage(string profileName, PerformanceProfile profile)
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Containers.ContainsKey($"Profile_{profileName}"))
            {
                var container = settings.Containers[$"Profile_{profileName}"];

                // Group C only (plan §5.4). The TDP, CPU, Intel-display, FPS, power-mode and HDR keys
                // that used to be read here belong to the helper's store and reach the widget through
                // ProfileSnapshot. Old containers still carry those keys; they are simply never read.
                profile.FluidMotionFrames = container.Values.ContainsKey("FluidMotionFrames") ? (bool)container.Values["FluidMotionFrames"] : false;
                profile.RadeonSuperResolution = container.Values.ContainsKey("RadeonSuperResolution") ? (bool)container.Values["RadeonSuperResolution"] : false;
                profile.RadeonSuperResolutionSharpness = container.Values.ContainsKey("RadeonSuperResolutionSharpness") ? (double)container.Values["RadeonSuperResolutionSharpness"] : 80;
                profile.ImageSharpening = container.Values.ContainsKey("ImageSharpening") ? (bool)container.Values["ImageSharpening"] : false;
                profile.ImageSharpeningSharpness = container.Values.ContainsKey("ImageSharpeningSharpness") ? (double)container.Values["ImageSharpeningSharpness"] : 80;
                profile.RadeonAntiLag = container.Values.ContainsKey("RadeonAntiLag") ? (bool)container.Values["RadeonAntiLag"] : false;
                profile.RadeonBoost = container.Values.ContainsKey("RadeonBoost") ? (bool)container.Values["RadeonBoost"] : false;
                profile.RadeonBoostResolution = container.Values.ContainsKey("RadeonBoostResolution") ? (double)container.Values["RadeonBoostResolution"] : 0;
                profile.RadeonChill = container.Values.ContainsKey("RadeonChill") ? (bool)container.Values["RadeonChill"] : false;
                profile.RadeonChillMinFPS = container.Values.ContainsKey("RadeonChillMinFPS") ? (double)container.Values["RadeonChillMinFPS"] : 30;
                profile.RadeonChillMaxFPS = container.Values.ContainsKey("RadeonChillMaxFPS") ? (double)container.Values["RadeonChillMaxFPS"] : 60;
                // Only load LegionPerformanceMode if it exists in storage - keep profile's existing value otherwise
                // This preserves the default (Balanced=2) for new profiles but doesn't override if storage key is missing
                if (container.Values.ContainsKey("LegionPerformanceMode"))
                {
                    profile.LegionPerformanceMode = (int)container.Values["LegionPerformanceMode"];
                }
                // Load TDPModeIndex for custom presets (-1 means use LegionPerformanceMode to determine index)
                profile.TDPModeIndex = container.Values.ContainsKey("TDPModeIndex") ? (int)container.Values["TDPModeIndex"] : -1;
                profile.OverlayLevel = container.Values.ContainsKey("OverlayLevel") ? (int)container.Values["OverlayLevel"] : 0;

                Logger.Info($"Loaded {profileName} profile from storage");
            }
        }

    }
}
