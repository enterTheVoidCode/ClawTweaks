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
        OpenYouTube            = 65,
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
                case TileActionType.OpenYouTube:            return "Open YouTube";
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
                case TileActionType.OpenYouTube:            return "YouTube";
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
                case TileActionType.OpenYouTube:            return "https://www.youtube.com/";
                default: return null;
            }
        }

        // Fluent System Icons (MIT, microsoft/fluentui-system-icons, 24px filled) for the new
        // action tiles, so they get a crisp dedicated icon instead of the generic fallback glyph.
        private const string PathGlobe    = "M8.90435 16.5008H15.0958C14.4759 19.7722 13.2345 21.999 12.0001 21.999C10.8031 21.999 9.59951 19.9051 8.9624 16.7953L8.90435 16.5008H15.0958H8.90435ZM3.06589 16.501L7.37153 16.5008C7.7363 18.583 8.35458 20.3545 9.16372 21.5942C6.60088 20.8373 4.46722 19.0825 3.21224 16.7799L3.06589 16.501ZM16.6286 16.5008L20.9343 16.501C19.703 18.9406 17.5018 20.8071 14.8375 21.5939C15.592 20.4362 16.1807 18.8162 16.5524 16.9129L16.6286 16.5008L20.9343 16.501L16.6286 16.5008ZM16.9315 10.0008L21.8016 10.0002C21.9328 10.6465 22.0016 11.3155 22.0016 12.0005C22.0016 13.0458 21.8413 14.0537 21.5438 15.0009H16.8412C16.9465 14.0433 17.0016 13.0372 17.0016 12.0005C17.0016 11.5462 16.991 11.0977 16.9703 10.6567L16.9315 10.0008L21.8016 10.0002L16.9315 10.0008ZM2.1986 10.0002L7.06862 10.0008C7.02238 10.6508 6.99854 11.319 6.99854 12.0005C6.99854 12.8299 7.03385 13.6396 7.10188 14.4207L7.159 15.0009H2.45637C2.1589 14.0537 1.99854 13.0458 1.99854 12.0005C1.99854 11.3155 2.0674 10.6465 2.1986 10.0002ZM8.57558 10.0002H15.4246C15.4748 10.6459 15.5016 11.3147 15.5016 12.0005C15.5016 12.8381 15.4617 13.6505 15.3878 14.4262L15.3261 15.0009H8.67404C8.56099 14.0551 8.49853 13.0476 8.49853 12.0005C8.49853 11.4862 8.51361 10.9814 8.54234 10.4887L8.57558 10.0002H15.4246H8.57558ZM14.9444 2.57707L14.8365 2.40684C17.8548 3.29781 20.2788 5.57442 21.372 8.50016L16.7811 8.50045C16.4656 6.08353 15.8246 4.00785 14.9444 2.57707L14.8365 2.40684L14.9444 2.57707ZM9.04186 2.44365L9.16364 2.40688C8.28288 3.75639 7.62827 5.736 7.28061 8.06062L7.21905 8.50045L2.62816 8.50016C3.70663 5.6139 6.08022 3.35936 9.04186 2.44365L9.16364 2.40688L9.04186 2.44365ZM12.0001 2.00195C13.3189 2.00195 14.6457 4.5437 15.2141 8.1854L15.2609 8.5002H8.73926C9.27867 4.69102 10.6436 2.00195 12.0001 2.00195Z";
        private const string PathPlay     = "M5 5.27368C5 3.56682 6.82609 2.48151 8.32538 3.2973L20.687 10.0235C22.2531 10.8756 22.2531 13.124 20.687 13.9762L8.32538 20.7024C6.82609 21.5181 5 20.4328 5 18.726V5.27368Z";
        private const string PathNext     = "M3.0001 4.753C3.0001 3.34519 4.57791 2.51363 5.73926 3.30937L16.2377 10.5028C17.2479 11.1949 17.2531 12.6839 16.2478 13.3831L5.7493 20.6847C4.58897 21.4917 3.0001 20.6613 3.0001 19.248V4.753ZM21 3.75C21 3.33579 20.6642 3 20.25 3C19.8358 3 19.5 3.33579 19.5 3.75V20.25C19.5 20.6642 19.8358 21 20.25 21C20.6642 21 21 20.6642 21 20.25V3.75Z";
        private const string PathPrevious = "M3 3.75C3 3.33579 3.33579 3 3.75 3C4.16421 3 4.5 3.33579 4.5 3.75V20.25C4.5 20.6642 4.16421 21 3.75 21C3.33579 21 3 20.6642 3 20.25V3.75ZM20.9999 4.753C20.9999 3.34519 19.4221 2.51363 18.2608 3.30937L7.76231 10.5028C6.75214 11.1949 6.74694 12.6839 7.75226 13.3831L18.2507 20.6847C19.4111 21.4917 20.9999 20.6613 20.9999 19.248V4.753Z";
        private const string PathApp      = "M6.25 3C4.45507 3 3 4.45507 3 6.25V17.75C3 19.5449 4.45507 21 6.25 21H17.75C19.5449 21 21 19.5449 21 17.75V6.25C21 4.45507 19.5449 3 17.75 3H6.25ZM4.5 8H19.5V17.75C19.5 18.7165 18.7165 19.5 17.75 19.5H6.25C5.2835 19.5 4.5 18.7165 4.5 17.75V8ZM6 10.35C6 9.88056 6.38056 9.5 6.85 9.5H10.15C10.6194 9.5 11 9.88056 11 10.35V17.15C11 17.6194 10.6194 18 10.15 18H6.85C6.38056 18 6 17.6194 6 17.15V10.35ZM7.5 11V16.5H9.5V11H7.5ZM12.75 9.5H17.25C17.6642 9.5 18 9.83579 18 10.25C18 10.6642 17.6642 11 17.25 11H12.75C12.3358 11 12 10.6642 12 10.25C12 9.83579 12.3358 9.5 12.75 9.5ZM12 13.25C12 12.8358 12.3358 12.5 12.75 12.5H16.25C16.6642 12.5 17 12.8358 17 13.25C17 13.6642 16.6642 14 16.25 14H12.75C12.3358 14 12 13.6642 12 13.25Z";
        private const string PathMusic    = "M20 2.75001C20 2.51293 19.8879 2.28981 19.6977 2.14829C19.5075 2.00677 19.2616 1.96351 19.0345 2.03164L9.03449 5.03164C8.71725 5.12681 8.5 5.4188 8.5 5.75001V15.6273C7.93308 15.2319 7.24362 15 6.5 15C4.567 15 3 16.567 3 18.5C3 20.433 4.567 22 6.5 22C8.433 22 10 20.433 10 18.5C10 18.4426 9.99862 18.3856 9.99589 18.3289C9.99861 18.303 10 18.2766 10 18.25V10.308L18.5 7.75803V13.6273C17.9331 13.2319 17.2436 13 16.5 13C14.567 13 13 14.567 13 16.5C13 18.433 14.567 20 16.5 20C18.433 20 20 18.433 20 16.5C20 16.4427 19.9986 16.3856 19.9959 16.329C19.9986 16.303 20 16.2767 20 16.25V2.75001Z";
        private const string PathStore    = "M8 3.75V6H2.75C2.33579 6 2 6.33579 2 6.75V18.25C2 19.7688 3.23122 21 4.75 21H19.25C20.7688 21 22 19.7688 22 18.25V6.75C22 6.33579 21.6642 6 21.25 6H16V3.75C16 2.7835 15.2165 2 14.25 2H9.75C8.7835 2 8 2.7835 8 3.75ZM9.75 3.5H14.25C14.3881 3.5 14.5 3.61193 14.5 3.75V6H9.5V3.75C9.5 3.61193 9.61193 3.5 9.75 3.5ZM8 13V9.5H11.5V13H8ZM8 17.5V14H11.5V17.5H8ZM16 13H12.5V9.5H16V13ZM12.5 17.5V14H16V17.5H12.5Z";

        /// <summary>
        /// Returns a Fluent (SVG) geometry path for action types that should render a crisp
        /// PathIcon on their tile, or null to fall back to the Segoe glyph. Covers the new
        /// Program / Website / media actions; users asked specifically for a browser icon on URL
        /// actions and dedicated play / skip icons on the media actions.
        /// </summary>
        public static string GetFluentIconPath(TileActionType action)
        {
            switch (action)
            {
                case TileActionType.MediaPlayPause: return PathPlay;
                case TileActionType.MediaNextTrack: return PathNext;
                case TileActionType.MediaPrevTrack: return PathPrevious;

                case TileActionType.OpenWindowsStore: return PathStore;
                case TileActionType.OpenSpotify:      return PathMusic;
                case TileActionType.LaunchUserProgram: return PathApp;
                case TileActionType.OpenDefaultBrowser:
                case TileActionType.OpenChrome:       return PathGlobe;

                // All website actions (defaults + user) → browser/globe
                case TileActionType.OpenExophase:
                case TileActionType.OpenRetroAchievements:
                case TileActionType.OpenGoogle:
                case TileActionType.OpenClawTweaksReleases:
                case TileActionType.OpenClawTweaksFaq:
                case TileActionType.OpenYouTube:
                case TileActionType.OpenUserWebsite:  return PathGlobe;

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
            yield return TileActionType.OpenYouTube;
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
