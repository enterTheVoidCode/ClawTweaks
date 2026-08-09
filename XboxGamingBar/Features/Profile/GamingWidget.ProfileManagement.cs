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

        // GetNewProfileDefaultTdp is GONE with its only caller (plan §5.4). It returned the PL1 max
        // (30 W on the Claw 8 AI+ / Lunar Lake — deliberately NOT the tdpLimits "max", which reports
        // the higher PL2/OverBoost ceiling) as the starting TDP for a brand-new per-game profile, on
        // the reasoning that users tune down more often than up. It only ever reached the widget's own
        // copy, so the intent never took effect: the helper creates the per-game profile and seeds it
        // from CurrentProfile. Recorded here rather than deleted silently — if the PL1-max start is
        // wanted, it belongs in the helper's profile creation, not in a widget field.

        private void LoadOrCreateGameProfiles()
        {
            if (!HasValidGame(currentGameName))
                return;

            var settings = ApplicationData.Current.LocalSettings;
            bool splitEnabled = GetPerGamePowerSourceProfileEnabled(currentGameName);
            bool hasSingle = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}");
            bool hasAC = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}_AC");
            bool hasDC = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}_DC");

            if (splitEnabled)
            {
                // Ensure AC/DC game profiles exist. If only a single profile exists, seed both from it.
                PerformanceProfile seedProfile = null;
                if (hasSingle)
                {
                    seedProfile = new PerformanceProfile();
                    LoadProfileFromStorage($"Game_{currentGameName}", seedProfile);
                }

                if (!hasAC)
                {
                    gameACProfile = (seedProfile ?? acProfile).Clone();
                    SaveProfileToStorage($"Game_{currentGameName}_AC", gameACProfile);
                    Logger.Info($"Initialized game AC profile for {currentGameName} (seed={(seedProfile != null ? "single profile" : "global AC")})");
                }
                else
                {
                    LoadProfileFromStorage($"Game_{currentGameName}_AC", gameACProfile);
                }

                if (!hasDC)
                {
                    gameDCProfile = (seedProfile ?? dcProfile).Clone();
                    SaveProfileToStorage($"Game_{currentGameName}_DC", gameDCProfile);
                    Logger.Info($"Initialized game DC profile for {currentGameName} (seed={(seedProfile != null ? "single profile" : "global DC")})");
                }
                else
                {
                    LoadProfileFromStorage($"Game_{currentGameName}_DC", gameDCProfile);
                }

                Logger.Info($"Loaded game per-power-state profiles for {currentGameName}");
                // The push that stood here is gone with plan §5.4. It synced these containers down to
                // the helper so it could apply them on a power-state change without the widget being
                // awake — the helper does that from its own profile store now, so it no longer needs
                // the widget to be awake at all, which was the point of the push in the first place.
            }
            else
            {
                // Ensure single game profile exists. If only AC/DC exists, seed from active power source profile.
                if (!hasSingle)
                {
                    PerformanceProfile seedProfile = null;
                    if (hasAC || hasDC)
                    {
                        var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                        bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;

                        string sourceProfileName;
                        if (isOnAC && hasAC)
                        {
                            sourceProfileName = $"Game_{currentGameName}_AC";
                        }
                        else if (!isOnAC && hasDC)
                        {
                            sourceProfileName = $"Game_{currentGameName}_DC";
                        }
                        else if (hasAC)
                        {
                            sourceProfileName = $"Game_{currentGameName}_AC";
                        }
                        else
                        {
                            sourceProfileName = $"Game_{currentGameName}_DC";
                        }

                        seedProfile = new PerformanceProfile();
                        LoadProfileFromStorage(sourceProfileName, seedProfile);
                        Logger.Info($"Seeding single game profile for {currentGameName} from {sourceProfileName}");
                    }

                    if (seedProfile == null && GetGlobalPowerSourceProfileEnabled())
                    {
                        var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                        bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;
                        seedProfile = (isOnAC ? acProfile : dcProfile).Clone();
                        Logger.Info($"Seeding single game profile for {currentGameName} from global {(isOnAC ? "AC" : "DC")} profile");
                    }

                    gameProfile = (seedProfile ?? globalProfile).Clone();
                    // The TDP seed that stood here is GONE (plan §5.4). It wrote the PL1 max into the
                    // WIDGET's copy, which no longer feeds anything — the helper creates its own
                    // per-game profile and seeds it from CurrentProfile (ProfileManager). So the
                    // "new per-game profile starts at PL1 max" intent was already not reaching the
                    // hardware; removing the write does not change that, it only stops pretending.
                    // If that intent should hold, it belongs in the helper's profile creation.
                    SaveProfileToStorage($"Game_{currentGameName}", gameProfile);
                    Logger.Info($"Initialized game profile UI state for {currentGameName} (seed={(seedProfile != null ? "active profile" : "global")})");
                }
                else
                {
                    LoadProfileFromStorage($"Game_{currentGameName}", gameProfile);
                    Logger.Info($"Loaded existing game profile for {currentGameName}");
                }
            }

            // Stamp the running game's exe path into every container we just created or
            // loaded for this title. Used by the Profiles tab to group multiple titles
            // that share an exe (e.g. emulators like Citron / RetroArch where each game
            // produces a different window title) under a single collapsed parent card.
            EnsureGameExePathStored(currentGameName);
        }

        /// <summary>
        /// Writes the current running game's full exe path into every Profile_Game_<name>
        /// container that exists for the given title (single, _AC, _DC). Idempotent —
        /// safe to call repeatedly. Skipped silently when the running game's path is
        /// not available (game closed mid-load, race during startup).
        /// </summary>
        private void EnsureGameExePathStored(string gameName)
        {
            try
            {
                if (runningGame == null) return;
                var rg = runningGame.Value; // RunningGame is a struct, can't ?.
                if (rg == null || !rg.IsValid() || rg.GameId == null) return;
                string exePath = rg.GameId.Path;
                if (string.IsNullOrEmpty(exePath)) return;
                if (string.IsNullOrEmpty(gameName)) return;

                // Guard against the start-up race where currentGameName (the Game Bar title)
                // has already switched to the new game but runningGame.GameId.Path still holds
                // the PREVIOUS game's exe (helper window-scan lag). Stamping then cross-stamps
                // the wrong exe path under this title, which collapses unrelated games into one
                // group in the Saved-Profiles list. Only stamp when the running game's identity
                // matches the title we're stamping; otherwise skip and let a later, consistent
                // tick do it.
                string runningName = rg.GameId.Name;
                if (string.IsNullOrEmpty(runningName)
                    || !string.Equals(runningName, gameName, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Debug($"EnsureGameExePathStored skipped: running '{runningName}' != target '{gameName}' (start-up race)");
                    return;
                }

                var settings = ApplicationData.Current.LocalSettings;
                foreach (var suffix in new[] { "", "_AC", "_DC" })
                {
                    var key = $"Profile_Game_{gameName}{suffix}";
                    if (settings.Containers.ContainsKey(key))
                    {
                        var existing = settings.Containers[key].Values.ContainsKey("GameExePath")
                            ? settings.Containers[key].Values["GameExePath"] as string
                            : null;
                        if (existing != exePath)
                        {
                            settings.Containers[key].Values["GameExePath"] = exePath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"EnsureGameExePathStored({gameName}) failed: {ex.Message}");
            }
        }

    }
}
