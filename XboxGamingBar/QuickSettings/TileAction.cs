using System.Collections.Generic;

namespace XboxGamingBar.QuickSettings
{
    /// <summary>
    /// Predefined action types for custom tiles.
    /// Tiles can either send a keyboard shortcut (KeyboardShortcut)
    /// or execute a built-in OS / app action.
    /// </summary>
    public enum TileActionType
    {
        None = 0,

        // ── Keyboard shortcut (stored separately via CustomShortcut field) ──
        KeyboardShortcut = 1,

        // ── OS Actions (10–19) ─────────────────────────────────────────────
        BrightnessUp   = 10,
        BrightnessDown = 11,
        AltTab         = 12,
        AltTabBack     = 13,
        GoToDesktop    = 14,

        // ── OS Actions: media keys (30–39) ─────────────────────────────────
        MediaNextTrack = 30,   // VK_MEDIA_NEXT_TRACK (0xB0)
        MediaPrevTrack = 31,   // VK_MEDIA_PREV_TRACK (0xB1)
        MediaPlayPause = 32,   // VK_MEDIA_PLAY_PAUSE (0xB3) — toggles play/pause

        // ── App Actions (20–29) ────────────────────────────────────────────
        CycleOverlayMode  = 20,
        CycleTDPMode      = 21,
        TDPStepUp         = 22,
        TDPStepDown       = 23,
        CycleLimiterMode  = 24,   // cycle FPS limiter values for current mode (RTSS or Intel)
        TDPIncrBy1W       = 25,   // raise current TDP by exactly 1 W (PL1 max 36 W → PL2 = 37 W)
        TDPDecrBy1W       = 26,   // lower current TDP by exactly 1 W
        VolumeUp          = 27,   // system volume +5 %
        VolumeDown        = 28,   // system volume -5 %
        ToggleControllerMouseMode = 29, // MSI Claw: toggle Controller ↔ Mouse mode

        // ── Launcher Actions (40–49) ───────────────────────────────────────
        SteamBigPicture   = 40,   // open Steam Big Picture (steam://open/bigpicture)
        Playnite          = 41,   // open Playnite Fullscreen
        XboxApp           = 42,   // open the Xbox (Game Pass) app

        // ── Program Actions (50–59) ────────────────────────────────────────
        OpenDefaultBrowser = 50,
        OpenWindowsStore   = 51,
        OpenChrome         = 52,
        OpenSpotify        = 53,
        LaunchUserProgram  = 59,  // generic: launches the exe/ps1 path carried in ActionParam

        // ── Launch Website (60–69) ─────────────────────────────────────────
        OpenExophase           = 60,
        OpenRetroAchievements  = 61,
        OpenGoogle             = 62,
        OpenClawTweaksReleases = 63,
        OpenClawTweaksFaq      = 64,
        OpenUserWebsite        = 69,  // generic: opens the URL carried in ActionParam
    }

    public static class TileActionHelper
    {
        public static string GetDisplayName(TileActionType action)
        {
            switch (action)
            {
                case TileActionType.KeyboardShortcut: return "Keyboard Shortcut";
                case TileActionType.BrightnessUp:     return "Brightness +5%";
                case TileActionType.BrightnessDown:   return "Brightness -5%";
                case TileActionType.AltTab:           return "Window Overview (Alt+Tab)";
                case TileActionType.AltTabBack:       return "Cycle Through Apps";
                case TileActionType.GoToDesktop:      return "Show Desktop (Win+D)";
                case TileActionType.MediaNextTrack:   return "Next Track";
                case TileActionType.MediaPrevTrack:   return "Previous Track";
                case TileActionType.MediaPlayPause:   return "Play / Pause";
                case TileActionType.CycleOverlayMode: return "Cycle Overlay Mode";
                case TileActionType.CycleTDPMode:     return "Cycle TDP Mode";
                case TileActionType.TDPStepUp:        return "TDP Step Up";
                case TileActionType.TDPStepDown:      return "TDP Step Down";
                case TileActionType.CycleLimiterMode: return "Cycle FPS Limit";
                case TileActionType.TDPIncrBy1W:      return "TDP +1W";
                case TileActionType.TDPDecrBy1W:      return "TDP -1W";
                case TileActionType.VolumeUp:         return "Volume +5%";
                case TileActionType.VolumeDown:       return "Volume -5%";
                case TileActionType.ToggleControllerMouseMode: return "Toggle Controller/Mouse";
                case TileActionType.SteamBigPicture:  return "Steam Big Picture";
                case TileActionType.Playnite:         return "Playnite";
                case TileActionType.XboxApp:          return "Xbox App";
                case TileActionType.OpenDefaultBrowser: return "Open Default Browser";
                case TileActionType.OpenWindowsStore:   return "Open Windows Store";
                case TileActionType.OpenChrome:         return "Open Chrome";
                case TileActionType.OpenSpotify:        return "Open Spotify";
                case TileActionType.OpenExophase:           return "Open Exophase (Achievements)";
                case TileActionType.OpenRetroAchievements:  return "Open Retro Achievements";
                case TileActionType.OpenGoogle:             return "Open Google";
                case TileActionType.OpenClawTweaksReleases: return "Open ClawTweaks Releases";
                case TileActionType.OpenClawTweaksFaq:      return "Open ClawTweaks FAQ";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Short tile name (≤6 chars) used as the tile label when a predefined action tile is created.
        /// Distinct per action so tiles are immediately recognisable on the Quick Settings grid.
        /// </summary>
        public static string GetShortName(TileActionType action)
        {
            switch (action)
            {
                case TileActionType.BrightnessUp:     return "Bri+";
                case TileActionType.BrightnessDown:   return "Bri-";
                case TileActionType.VolumeUp:         return "Vol+";
                case TileActionType.VolumeDown:       return "Vol-";
                case TileActionType.AltTab:           return "AltTb";
                case TileActionType.AltTabBack:       return "CycleApps";
                case TileActionType.GoToDesktop:      return "Desk";
                case TileActionType.MediaNextTrack:   return "Next";
                case TileActionType.MediaPrevTrack:   return "Prev";
                case TileActionType.MediaPlayPause:   return "Play";
                case TileActionType.CycleOverlayMode: return "CycleOSD";
                case TileActionType.CycleLimiterMode: return "CycleFPS";
                case TileActionType.TDPIncrBy1W:      return "TDP+1";
                case TileActionType.TDPDecrBy1W:      return "TDP-1";
                case TileActionType.ToggleControllerMouseMode: return "Ctrl/Mouse";
                case TileActionType.SteamBigPicture:  return "Steam";
                case TileActionType.Playnite:         return "Playnite";
                case TileActionType.XboxApp:          return "Xbox";
                case TileActionType.OpenDefaultBrowser: return "Browser";
                case TileActionType.OpenWindowsStore:   return "Store";
                case TileActionType.OpenChrome:         return "Chrome";
                case TileActionType.OpenSpotify:        return "Spotify";
                case TileActionType.OpenExophase:           return "Exophase";
                case TileActionType.OpenRetroAchievements:  return "RetroAch";
                case TileActionType.OpenGoogle:             return "Google";
                case TileActionType.OpenClawTweaksReleases: return "Releases";
                case TileActionType.OpenClawTweaksFaq:      return "FAQ";
                default: return GetDisplayName(action);
            }
        }

        public static string GetGlyph(TileActionType action)
        {
            switch (action)
            {
                case TileActionType.BrightnessUp:     return "";  // Brightness
                case TileActionType.BrightnessDown:   return "";  // Brightness lower
                case TileActionType.AltTab:           return "";  // Switch windows
                case TileActionType.AltTabBack:       return "";  // Back
                case TileActionType.GoToDesktop:      return "";  // Desktop
                case TileActionType.CycleOverlayMode: return "";  // Monitor
                case TileActionType.CycleTDPMode:     return "";  // Performance
                case TileActionType.TDPStepUp:        return "";  // Up
                case TileActionType.TDPStepDown:      return "";  // Down
                case TileActionType.TDPIncrBy1W:      return "";  // Add
                case TileActionType.TDPDecrBy1W:      return "";  // Remove
                case TileActionType.VolumeUp:         return "";  // Volume 2
                case TileActionType.VolumeDown:       return "";  // Volume 0
                case TileActionType.ToggleControllerMouseMode: return "";  // Game controller
                default: return "";
            }
        }

        public static string GetGroupName(TileActionType action)
        {
            int v = (int)action;
            if (v >= 10 && v < 20) return "OS";
            if (v >= 30 && v < 40) return "OS";        // media keys
            if (v == 27 || v == 28) return "OS";       // VolumeUp / VolumeDown
            if (v >= 40 && v < 50) return "Launcher";  // Launcher actions (Steam BP, Playnite, Xbox)
            if (v >= 50 && v < 60) return "Program";   // Program Actions (browser/store/chrome/spotify/user)
            if (v >= 60 && v < 70) return "Website";   // Launch Website (defaults + user URLs)
            if (v >= 20) return "App";
            return "";
        }

        /// <summary>Human-readable header shown above a group in the action dropdown.</summary>
        public static string GetGroupHeader(string group)
        {
            switch (group)
            {
                case "OS":       return "— OS Actions —";
                case "Launcher": return "— Launcher Actions —";
                case "Program":  return "— Program Actions —";
                case "Website":  return "— Launch Website —";
                default:         return "— ClawTweaks Actions —";
            }
        }

        /// <summary>
        /// For a built-in Program-Action enum, the launch target the helper understands
        /// (a URI scheme or the @DefaultBrowser token). Null for non-program / user actions.
        /// </summary>
        public static string GetDefaultProgramTarget(TileActionType action)
        {
            switch (action)
            {
                case TileActionType.OpenDefaultBrowser: return "@DefaultBrowser";
                case TileActionType.OpenWindowsStore:   return "ms-windows-store:";
                case TileActionType.OpenChrome:         return "chrome";
                case TileActionType.OpenSpotify:        return "spotify:";
                default: return null;
            }
        }

        /// <summary>For a built-in Launch-Website enum, the URL to open. Null for non-website / user actions.</summary>
        public static string GetDefaultWebsiteUrl(TileActionType action)
        {
            switch (action)
            {
                case TileActionType.OpenExophase:           return "https://www.exophase.com/";
                case TileActionType.OpenRetroAchievements:  return "https://retroachievements.org/";
                case TileActionType.OpenGoogle:             return "https://www.google.com/";
                case TileActionType.OpenClawTweaksReleases: return "https://github.com/enterTheVoidCode/ClawTweaks/releases";
                case TileActionType.OpenClawTweaksFaq:      return "https://github.com/enterTheVoidCode/ClawTweaks";
                default: return null;
            }
        }

        /// <summary>Returns true if this action type is only relevant for MSI Claw devices.</summary>
        public static bool IsMsiClawOnly(TileActionType action)
        {
            return action == TileActionType.ToggleControllerMouseMode;
        }

        /// <summary>Returns all user-selectable action types in display order.</summary>
        public static IEnumerable<TileActionType> GetAllActions()
        {
            // OS group
            yield return TileActionType.BrightnessUp;
            yield return TileActionType.BrightnessDown;
            yield return TileActionType.VolumeUp;
            yield return TileActionType.VolumeDown;
            yield return TileActionType.MediaPrevTrack;
            yield return TileActionType.MediaPlayPause;
            yield return TileActionType.MediaNextTrack;
            yield return TileActionType.AltTab;
            yield return TileActionType.AltTabBack;
            yield return TileActionType.GoToDesktop;
            // App group
            yield return TileActionType.CycleOverlayMode;
            yield return TileActionType.CycleLimiterMode;
            yield return TileActionType.TDPIncrBy1W;
            yield return TileActionType.TDPDecrBy1W;
            // MSI Claw only
            yield return TileActionType.ToggleControllerMouseMode;
            // Launcher group
            yield return TileActionType.SteamBigPicture;
            yield return TileActionType.Playnite;
            yield return TileActionType.XboxApp;
            // Program group (defaults; user programs appended dynamically by BuildChoices)
            yield return TileActionType.OpenDefaultBrowser;
            yield return TileActionType.OpenWindowsStore;
            yield return TileActionType.OpenChrome;
            yield return TileActionType.OpenSpotify;
            // Website group (defaults; user URLs appended dynamically by BuildChoices)
            yield return TileActionType.OpenExophase;
            yield return TileActionType.OpenRetroAchievements;
            yield return TileActionType.OpenGoogle;
            yield return TileActionType.OpenClawTweaksReleases;
            yield return TileActionType.OpenClawTweaksFaq;
        }
    }

    /// <summary>
    /// One selectable entry in an action dropdown. Carries the action type plus an optional
    /// payload (the exe/ps1 path for user programs, the URL for user websites) and the label to
    /// show. Set as a ComboBoxItem.Tag so both the Custom-Tile and Front-Button pickers read the
    /// same shape.
    /// </summary>
    public sealed class ActionChoice
    {
        public TileActionType Type { get; set; }
        public string Param { get; set; }   // null for built-ins
        public string Display { get; set; }

        public ActionChoice(TileActionType type, string param, string display)
        {
            Type = type;
            Param = param;
            Display = display;
        }
    }

    public static class ActionChoiceBuilder
    {
        /// <summary>
        /// Builds the full ordered list of action choices: all built-ins (from GetAllActions),
        /// with the user's programs appended right after the Program defaults and the user's URLs
        /// appended right after the Website defaults. User entries get a " (User)" suffix.
        /// MSI-Claw-only actions are filtered out when <paramref name="isMsiClaw"/> is false.
        /// </summary>
        public static System.Collections.Generic.List<ActionChoice> Build(
            System.Collections.Generic.IEnumerable<Shared.Data.ProgramAction> userPrograms,
            System.Collections.Generic.IEnumerable<Shared.Data.UrlAction> userUrls,
            bool isMsiClaw = true)
        {
            var list = new System.Collections.Generic.List<ActionChoice>();
            foreach (var action in TileActionHelper.GetAllActions())
            {
                if (!isMsiClaw && TileActionHelper.IsMsiClawOnly(action)) continue;
                list.Add(new ActionChoice(action, null, TileActionHelper.GetDisplayName(action)));

                // Append user entries directly after the last default of their group.
                if (action == TileActionType.OpenSpotify && userPrograms != null)
                {
                    foreach (var p in userPrograms)
                    {
                        if (p == null || string.IsNullOrWhiteSpace(p.Path)) continue;
                        list.Add(new ActionChoice(TileActionType.LaunchUserProgram, p.Path, $"{p.DisplayName} (User)"));
                    }
                }
                else if (action == TileActionType.OpenClawTweaksFaq && userUrls != null)
                {
                    foreach (var u in userUrls)
                    {
                        if (u == null || string.IsNullOrWhiteSpace(u.Url)) continue;
                        list.Add(new ActionChoice(TileActionType.OpenUserWebsite, u.Url, $"{u.DisplayName} (User)"));
                    }
                }
            }
            return list;
        }
    }
}
