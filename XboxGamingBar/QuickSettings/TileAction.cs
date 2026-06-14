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
        ShowKeyboard   = 15,   // toggle on-screen/touch keyboard

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
        OpenClawTweaksWindow = 43, // open ClawTweaks as a standalone desktop window (app mode), GameBar alternative

        // ── Program Actions (50–59) ────────────────────────────────────────
        OpenDefaultBrowser = 50,
        OpenWindowsStore   = 51,
        OpenChrome         = 52,
        OpenSpotify        = 53,
        LaunchUserProgram  = 59,  // generic: launches the exe/ps1 path carried in ActionParam

        // ── Special Controller Buttons (70–79) ─────────────────────────────
        EmulateXboxGuide       = 70,   // momentary Xbox Guide tap on the virtual ViGEm controller

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
                case TileActionType.ShowKeyboard:     return "Show Keyboard";
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
                case TileActionType.OpenClawTweaksWindow: return "Open ClawTweaks Window";
                case TileActionType.OpenDefaultBrowser: return "Open Default Browser";
                case TileActionType.OpenWindowsStore:   return "Open Windows Store";
                case TileActionType.OpenChrome:         return "Open Chrome";
                case TileActionType.OpenSpotify:        return "Open Spotify";
                case TileActionType.EmulateXboxGuide:       return "Xbox Button";
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
                case TileActionType.ShowKeyboard:     return "Keyboard";
                case TileActionType.MediaNextTrack:   return "Next";
                case TileActionType.MediaPrevTrack:   return "Prev";
                case TileActionType.MediaPlayPause:   return "Play | Pause";
                case TileActionType.CycleOverlayMode: return "CycleOSD";
                case TileActionType.CycleLimiterMode: return "CycleFPS";
                case TileActionType.TDPIncrBy1W:      return "TDP+1";
                case TileActionType.TDPDecrBy1W:      return "TDP-1";
                case TileActionType.ToggleControllerMouseMode: return "Ctrl/Mouse";
                case TileActionType.SteamBigPicture:  return "Steam";
                case TileActionType.Playnite:         return "Playnite";
                case TileActionType.XboxApp:          return "Xbox";
                case TileActionType.OpenClawTweaksWindow: return "Window";
                case TileActionType.OpenDefaultBrowser: return "Browser";
                case TileActionType.OpenWindowsStore:   return "Store";
                case TileActionType.OpenChrome:         return "Chrome";
                case TileActionType.OpenSpotify:        return "Spotify";
                case TileActionType.EmulateXboxGuide:       return "Xbox";
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
            if (v >= 70 && v < 80) return "Controller"; // Special Controller Buttons (Xbox Button)
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
                case "Controller": return "— Special Controller Buttons —";
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

        // Built-in (default) tile icons.
        private const string PathGauge    = "M12 22C17.5228 22 22 17.5228 22 12C22 6.47715 17.5228 2 12 2C6.47715 2 2 6.47715 2 12C2 17.5228 6.47715 22 12 22ZM15.8791 6.66732C16.1062 6.47297 16.439 6.46653 16.6734 6.65195C16.9078 6.83738 16.9781 7.16278 16.8412 7.42842L16.7119 7.67862C16.6295 7.83801 16.5113 8.06624 16.3681 8.34179C16.0818 8.89278 15.6954 9.63339 15.2955 10.3912C14.8959 11.1485 14.4815 11.9253 14.1395 12.5479C13.9686 12.8589 13.8142 13.1344 13.6879 13.3509C13.5703 13.5524 13.4548 13.7421 13.3688 13.8508C12.7263 14.6629 11.5471 14.8004 10.735 14.1579C9.92288 13.5154 9.78538 12.3362 10.4279 11.5241C10.5139 11.4154 10.672 11.2593 10.8409 11.0986C11.0226 10.9258 11.2552 10.7121 11.5185 10.4744C12.0457 9.9983 12.7063 9.41631 13.3514 8.85315C13.9969 8.28961 14.6288 7.74321 15.0991 7.33783C15.3343 7.1351 15.5292 6.96755 15.6654 6.85065L15.8791 6.66732ZM7.93413 17.1265C7.64124 17.4194 7.16637 17.4194 6.87347 17.1265C4.04217 14.2952 4.04217 9.70478 6.87347 6.87348C8.71833 5.02862 11.3099 4.38674 13.6723 4.94459C14.0755 5.03978 14.3251 5.44375 14.2299 5.84687C14.1347 6.25 13.7308 6.49963 13.3276 6.40444C11.45 5.96106 9.39622 6.47205 7.93413 7.93414C5.68862 10.1797 5.68862 13.8203 7.93413 16.0659C8.22703 16.3588 8.22703 16.8336 7.93413 17.1265ZM17.8879 9.1415C18.2789 9.00477 18.7067 9.21089 18.8435 9.60189C19.7333 12.1463 19.1624 15.0907 17.1265 17.1265C16.8336 17.4194 16.3588 17.4194 16.0659 17.1265C15.773 16.8336 15.773 16.3588 16.0659 16.0659C17.6791 14.4526 18.1344 12.1183 17.4276 10.097C17.2908 9.70604 17.4969 9.27824 17.8879 9.1415Z";
        private const string PathChip     = "M15.25 2C15.6297 2 15.9435 2.28215 15.9932 2.64823L16 2.75L16.0005 5.07512C17.471 5.37384 18.6289 6.53302 18.9258 8.00416L21.25 8.00493C21.6642 8.00493 22 8.34072 22 8.75493C22 9.13462 21.7178 9.44842 21.3518 9.49808L21.25 9.50493L19 9.504V11.254L21.25 11.2549C21.6297 11.2549 21.9435 11.5371 21.9932 11.9032L22 12.0049C22 12.3846 21.7178 12.6984 21.3518 12.7481L21.25 12.7549L19 12.754V14.504L21.25 14.5049C21.6297 14.5049 21.9435 14.7871 21.9932 15.1532L22 15.2549C22 15.6346 21.7178 15.9484 21.3518 15.9981L21.25 16.0049L18.924 16.0048C18.6242 17.4717 17.468 18.6268 16.0005 18.9249L16 21.25C16 21.6642 15.6642 22 15.25 22C14.8703 22 14.5565 21.7178 14.5068 21.3518L14.5 21.25V18.999H12.749L12.75 21.25C12.75 21.6297 12.4678 21.9435 12.1018 21.9932L12 22C11.6203 22 11.3065 21.7178 11.2568 21.3518L11.25 21.25L11.249 18.999H9.5V21.25C9.5 21.6297 9.21785 21.9435 8.85177 21.9932L8.75 22C8.3703 22 8.05651 21.7178 8.00685 21.3518L8 21.25L8.00046 18.9251C6.53088 18.627 5.37329 17.4695 5.07501 16L2.75 16C2.33579 16 2 15.6642 2 15.25C2 14.8703 2.28215 14.5565 2.64823 14.5068L2.75 14.5L5 14.499V12.749L2.75 12.75C2.3703 12.75 2.05651 12.4678 2.00685 12.1018L2 12C2 11.6203 2.28215 11.3065 2.64823 11.2568L2.75 11.25L5 11.249V9.499L2.75 9.5C2.3703 9.5 2.05651 9.21785 2.00685 8.85177L2 8.75C2 8.3703 2.28215 8.05651 2.64823 8.00685L2.75 8L5.07521 7.99904C5.37381 6.53 6.53121 5.37297 8.00046 5.07492L8 2.75C8 2.33579 8.33579 2 8.75 2C9.1297 2 9.44349 2.28215 9.49315 2.64823L9.5 2.75V4.999H11.249L11.25 2.75C11.25 2.3703 11.5322 2.05651 11.8982 2.00685L12 2C12.3797 2 12.6935 2.28215 12.7432 2.64823L12.75 2.75L12.749 4.999H14.5V2.75C14.5 2.40482 14.7332 2.11411 15.0506 2.02679L15.1482 2.00685L15.25 2ZM12.0049 9.00493C10.3481 9.00493 9.00493 10.3481 9.00493 12.0049C9.00493 13.6618 10.3481 15.0049 12.0049 15.0049C13.6618 15.0049 15.0049 13.6618 15.0049 12.0049C15.0049 10.3481 13.6618 9.00493 12.0049 9.00493ZM12.0049 10.5049C12.8334 10.5049 13.5049 11.1765 13.5049 12.0049C13.5049 12.8334 12.8334 13.5049 12.0049 13.5049C11.1765 13.5049 10.5049 12.8334 10.5049 12.0049C10.5049 11.1765 11.1765 10.5049 12.0049 10.5049Z";
        private const string PathFullscr  = "M5 6C5 5.44772 5.44772 5 6 5H8C8.55228 5 9 4.55228 9 4C9 3.44772 8.55228 3 8 3H6C4.34315 3 3 4.34315 3 6V8C3 8.55228 3.44772 9 4 9C4.55228 9 5 8.55228 5 8V6ZM5 18C5 18.5523 5.44772 19 6 19H8C8.55228 19 9 19.4477 9 20C9 20.5523 8.55228 21 8 21H6C4.34315 21 3 19.6569 3 18V16C3 15.4477 3.44772 15 4 15C4.55228 15 5 15.4477 5 16V18ZM18 5C18.5523 5 19 5.44772 19 6V8C19 8.55228 19.4477 9 20 9C20.5523 9 21 8.55228 21 8V6C21 4.34315 19.6569 3 18 3H16C15.4477 3 15 3.44772 15 4C15 4.55228 15.4477 5 16 5H18ZM19 18C19 18.5523 18.5523 19 18 19H16C15.4477 19 15 19.4477 15 20C15 20.5523 15.4477 21 16 21H18C19.6569 21 21 19.6569 21 18V16C21 15.4477 20.5523 15 20 15C19.4477 15 19 15.4477 19 16V18Z";
        private const string PathArrowMax = "M19.5 3.5C20.0523 3.5 20.5 3.94772 20.5 4.5V12C20.5 12.5523 20.0523 13 19.5 13C18.9477 13 18.5 12.5523 18.5 12V6.91406L6.91406 18.5H12C12.5523 18.5 13 18.9477 13 19.5C13 20.0523 12.5523 20.5 12 20.5H4.5C3.94772 20.5 3.5 20.0523 3.5 19.5V12C3.5 11.4477 3.94772 11 4.5 11C5.05228 11 5.5 11.4477 5.5 12V17.0859L17.0859 5.5H12C11.4477 5.5 11 5.05228 11 4.5C11 3.94772 11.4477 3.5 12 3.5H19.5Z";
        private const string PathKeyboard = "M19.7454 5C20.988 5 21.9954 6.00736 21.9954 7.25V16.7546C21.9954 17.9972 20.988 19.0046 19.7454 19.0046H4.25C3.00736 19.0046 2 17.9972 2 16.7546V7.25C2 6.00736 3.00736 5 4.25 5H19.7454ZM17.25 14.5H6.75L6.64823 14.5068C6.28215 14.5565 6 14.8703 6 15.25C6 15.6297 6.28215 15.9435 6.64823 15.9932L6.75 16H17.25L17.3518 15.9932C17.7178 15.9435 18 15.6297 18 15.25C18 14.8703 17.7178 14.5565 17.3518 14.5068L17.25 14.5ZM16.5 11C15.9477 11 15.5 11.4477 15.5 12C15.5 12.5523 15.9477 13 16.5 13C17.0523 13 17.5 12.5523 17.5 12C17.5 11.4477 17.0523 11 16.5 11ZM13.5049 11C12.9526 11 12.5049 11.4477 12.5049 12C12.5049 12.5523 12.9526 13 13.5049 13C14.0572 13 14.5049 12.5523 14.5049 12C14.5049 11.4477 14.0572 11 13.5049 11ZM10.5049 11C9.95259 11 9.50488 11.4477 9.50488 12C9.50488 12.5523 9.95259 13 10.5049 13C11.0572 13 11.5049 12.5523 11.5049 12C11.5049 11.4477 11.0572 11 10.5049 11ZM7.50488 11C6.95259 11 6.50488 11.4477 6.50488 12C6.50488 12.5523 6.95259 13 7.50488 13C8.05716 13 8.50488 12.5523 8.50488 12C8.50488 11.4477 8.05716 11 7.50488 11ZM6 8C5.44772 8 5 8.44772 5 9C5 9.55228 5.44772 10 6 10C6.55228 10 7 9.55228 7 9C7 8.44772 6.55228 8 6 8ZM8.99512 8C8.44284 8 7.99512 8.44772 7.99512 9C7.99512 9.55228 8.44284 10 8.99512 10C9.54741 10 9.99512 9.55228 9.99512 9C9.99512 8.44772 9.54741 8 8.99512 8ZM11.9951 8C11.4428 8 10.9951 8.44772 10.9951 9C10.9951 9.55228 11.4428 10 11.9951 10C12.5474 10 12.9951 9.55228 12.9951 9C12.9951 8.44772 12.5474 8 11.9951 8ZM14.9951 8C14.4428 8 13.9951 8.44772 13.9951 9C13.9951 9.55228 14.4428 10 14.9951 10C15.5474 10 15.9951 9.55228 15.9951 9C15.9951 8.44772 15.5474 8 14.9951 8ZM17.9951 8C17.4428 8 16.9951 8.44772 16.9951 9C16.9951 9.55228 17.4428 10 17.9951 10C18.5474 10 18.9951 9.55228 18.9951 9C18.9951 8.44772 18.5474 8 17.9951 8Z";
        private const string PathApps     = "M18.4923 2.33034L21.671 5.50911C22.5497 6.38779 22.5497 7.81241 21.671 8.69109L19.0866 11.275C20.1696 11.4375 21 12.3718 21 13.5V18.75C21 19.9926 19.9926 21 18.75 21H5.25C4.00736 21 3 19.9926 3 18.75V5.25001C3 4.00736 4.00736 3.00001 5.25 3.00001H10.5C11.6289 3.00001 12.5637 3.83146 12.7253 4.91541L15.3103 2.33034C16.189 1.45166 17.6136 1.45166 18.4923 2.33034ZM4.5 18.75C4.5 19.1642 4.83579 19.5 5.25 19.5L11.249 19.4993L11.25 12.75L4.5 12.7493V18.75ZM12.749 19.4993L18.75 19.5C19.1642 19.5 19.5 19.1642 19.5 18.75V13.5C19.5 13.0858 19.1642 12.75 18.75 12.75L12.749 12.7493V19.4993ZM10.5 4.50001H5.25C4.83579 4.50001 4.5 4.83579 4.5 5.25001V11.2493H11.25V5.25001C11.25 4.83579 10.9142 4.50001 10.5 4.50001ZM12.75 9.30933V11.25L14.69 11.2493L12.75 9.30933Z";
        private const string PathWindow   = "M3 6.25C3 4.45507 4.45507 3 6.25 3H17.75C19.5449 3 21 4.45507 21 6.25V17.75C21 19.5449 19.5449 21 17.75 21H6.25C4.45507 21 3 19.5449 3 17.75V6.25ZM4.5 17.75C4.5 18.7165 5.2835 19.5 6.25 19.5H17.75C18.7165 19.5 19.5 18.7165 19.5 17.75V8.5H4.5V17.75Z";
        private const string PathFolder   = "M2 8V6.25C2 4.45507 3.45507 3 5.25 3H8.12868C8.72542 3 9.29771 3.23705 9.71967 3.65901L11.25 5.18934L8.65901 7.78033C8.51836 7.92098 8.32759 8 8.12868 8H2ZM2 9.5V17.75C2 19.5449 3.45507 21 5.25 21H18.75C20.5449 21 22 19.5449 22 17.75V8.75C22 6.95507 20.5449 5.5 18.75 5.5H13.0607L9.71967 8.84099C9.29771 9.26295 8.72542 9.5 8.12868 9.5H2Z";
        private const string PathDismiss  = "M4.2097 4.3871L4.29289 4.29289C4.65338 3.93241 5.22061 3.90468 5.6129 4.2097L5.70711 4.29289L12 10.585L18.2929 4.29289C18.6834 3.90237 19.3166 3.90237 19.7071 4.29289C20.0976 4.68342 20.0976 5.31658 19.7071 5.70711L13.415 12L19.7071 18.2929C20.0676 18.6534 20.0953 19.2206 19.7903 19.6129L19.7071 19.7071C19.3466 20.0676 18.7794 20.0953 18.3871 19.7903L18.2929 19.7071L12 13.415L5.70711 19.7071C5.31658 20.0976 4.68342 20.0976 4.29289 19.7071C3.90237 19.3166 3.90237 18.6834 4.29289 18.2929L10.585 12L4.29289 5.70711C3.93241 5.34662 3.90468 4.77939 4.2097 4.3871L4.29289 4.29289L4.2097 4.3871Z";
        private const string PathBriHigh  = "M12.75 2.75C12.75 2.33579 12.4142 2 12 2C11.5858 2 11.25 2.33579 11.25 2.75V4.25C11.25 4.66421 11.5858 5 12 5C12.4142 5 12.75 4.66421 12.75 4.25V2.75ZM17 12C17 14.7614 14.7614 17 12 17C9.23858 17 7 14.7614 7 12C7 9.23858 9.23858 7 12 7C14.7614 7 17 9.23858 17 12ZM15.5 12C15.5 10.067 13.933 8.5 12 8.5V15.5C13.933 15.5 15.5 13.933 15.5 12ZM22 12C22 12.4142 21.6642 12.75 21.25 12.75H19.75C19.3358 12.75 19 12.4142 19 12C19 11.5858 19.3358 11.25 19.75 11.25H21.25C21.6642 11.25 22 11.5858 22 12ZM12.75 19.75C12.75 19.3358 12.4142 19 12 19C11.5858 19 11.25 19.3358 11.25 19.75V21.25C11.25 21.6642 11.5858 22 12 22C12.4142 22 12.75 21.6642 12.75 21.25V19.75ZM5 12C5 12.4142 4.66421 12.75 4.25 12.75H2.75C2.33579 12.75 2 12.4142 2 12C2 11.5858 2.33579 11.25 2.75 11.25H4.25C4.66421 11.25 5 11.5858 5 12ZM5.28033 4.22004C4.98744 3.92714 4.51256 3.92714 4.21967 4.22004C3.92678 4.51293 3.92678 4.9878 4.21967 5.2807L5.71967 6.7807C6.01256 7.07359 6.48744 7.07359 6.78033 6.7807C7.07322 6.4878 7.07322 6.01293 6.78033 5.72004L5.28033 4.22004ZM4.21967 19.7807C4.51256 20.0736 4.98744 20.0736 5.28033 19.7807L6.78033 18.2807C7.07322 17.9878 7.07322 17.5129 6.78033 17.22C6.48744 16.9271 6.01256 16.9271 5.71967 17.22L4.21967 18.72C3.92678 19.0129 3.92678 19.4878 4.21967 19.7807ZM18.7197 4.22004C19.0126 3.92714 19.4874 3.92714 19.7803 4.22004C20.0732 4.51293 20.0732 4.9878 19.7803 5.2807L18.2803 6.7807C17.9874 7.07359 17.5126 7.07359 17.2197 6.7807C16.9268 6.4878 16.9268 6.01293 17.2197 5.72004L18.7197 4.22004ZM19.7803 19.7807C19.4874 20.0736 19.0126 20.0736 18.7197 19.7807L17.2197 18.2807C16.9268 17.9878 16.9268 17.5129 17.2197 17.22C17.5126 16.9271 17.9874 16.9271 18.2803 17.22L19.7803 18.72C20.0732 19.0129 20.0732 19.4878 19.7803 19.7807Z";
        private const string PathBriLow   = "M12.75 4.25C12.75 3.83579 12.4142 3.5 12 3.5C11.5858 3.5 11.25 3.83579 11.25 4.25V5.25C11.25 5.66421 11.5858 6 12 6C12.4142 6 12.75 5.66421 12.75 5.25V4.25ZM17 12C17 14.7614 14.7614 17 12 17C9.23858 17 7 14.7614 7 12C7 9.23858 9.23858 7 12 7C14.7614 7 17 9.23858 17 12ZM15.5 12C15.5 10.067 13.933 8.5 12 8.5V15.5C13.933 15.5 15.5 13.933 15.5 12ZM20.5 12C20.5 12.4142 20.1642 12.75 19.75 12.75H18.75C18.3358 12.75 18 12.4142 18 12C18 11.5858 18.3358 11.25 18.75 11.25H19.75C20.1642 11.25 20.5 11.5858 20.5 12ZM12.75 18.75C12.75 18.3358 12.4142 18 12 18C11.5858 18 11.25 18.3358 11.25 18.75V19.75C11.25 20.1642 11.5858 20.5 12 20.5C12.4142 20.5 12.75 20.1642 12.75 19.75V18.75ZM6 12C6 12.4142 5.66421 12.75 5.25 12.75H4.25C3.83579 12.75 3.5 12.4142 3.5 12C3.5 11.5858 3.83579 11.25 4.25 11.25H5.25C5.66421 11.25 6 11.5858 6 12ZM7.28033 6.21967C6.98744 5.92678 6.51256 5.92678 6.21967 6.21967C5.92678 6.51256 5.92678 6.98744 6.21967 7.28033L6.71967 7.78033C7.01256 8.07322 7.48744 8.07322 7.78033 7.78033C8.07322 7.48744 8.07322 7.01256 7.78033 6.71967L7.28033 6.21967ZM6.21967 17.7803C6.51256 18.0732 6.98744 18.0732 7.28033 17.7803L7.78033 17.2803C8.07322 16.9874 8.07322 16.5126 7.78033 16.2197C7.48744 15.9268 7.01256 15.9268 6.71967 16.2197L6.21967 16.7197C5.92678 17.0126 5.92678 17.4874 6.21967 17.7803ZM16.7197 6.21967C17.0126 5.92678 17.4874 5.92678 17.7803 6.21967C18.0732 6.51256 18.0732 6.98744 17.7803 7.28033L17.2803 7.78033C16.9874 8.07322 16.5126 8.07322 16.2197 7.78033C15.9268 7.48744 15.9268 7.01256 16.2197 6.71967L16.7197 6.21967ZM17.7803 17.7803C17.4874 18.0732 17.0126 18.0732 16.7197 17.7803L16.2197 17.2803C15.9268 16.9874 15.9268 16.5126 16.2197 16.2197C16.5126 15.9268 16.9874 15.9268 17.2803 16.2197L17.7803 16.7197C18.0732 17.0126 18.0732 17.4874 17.7803 17.7803Z";
        private const string PathSpeaker2 = "M15 4.25049V19.7461C15 20.8247 13.7255 21.397 12.9194 20.6802L8.42793 16.6865C8.29063 16.5644 8.11329 16.497 7.92956 16.497H4.25C3.00736 16.497 2 15.4896 2 14.247V9.74907C2 8.50643 3.00736 7.49907 4.25 7.49907H7.92961C8.11333 7.49907 8.29065 7.43165 8.42794 7.30958L12.9195 3.31631C13.7255 2.59964 15 3.17187 15 4.25049ZM18.9916 5.89782C19.3244 5.65128 19.7941 5.72126 20.0407 6.05411C21.2717 7.71619 22 9.77439 22 12.0005C22 14.2266 21.2717 16.2848 20.0407 17.9469C19.7941 18.2798 19.3244 18.3497 18.9916 18.1032C18.6587 17.8567 18.5888 17.387 18.8353 17.0541C19.8815 15.6416 20.5 13.8943 20.5 12.0005C20.5 10.1067 19.8815 8.35945 18.8353 6.9469C18.5888 6.61404 18.6587 6.14435 18.9916 5.89782ZM17.143 8.36982C17.5072 8.17262 17.9624 8.30806 18.1596 8.67233C18.6958 9.66294 19 10.7973 19 12.0005C19 13.2037 18.6958 14.338 18.1596 15.3287C17.9624 15.6929 17.5072 15.8284 17.143 15.6312C16.7787 15.434 16.6432 14.9788 16.8404 14.6146C17.2609 13.8378 17.5 12.9482 17.5 12.0005C17.5 11.0528 17.2609 10.1632 16.8404 9.38642C16.6432 9.02216 16.7787 8.56701 17.143 8.36982Z";
        private const string PathSpeaker1 = "M14.7041 3.4425C14.8952 3.66821 15 3.95433 15 4.25003V19.7517C15 20.442 14.4404 21.0017 13.75 21.0017C13.4542 21.0017 13.168 20.8968 12.9423 20.7056L7.97513 16.4999H4.25C3.00736 16.4999 2 15.4925 2 14.2499V9.74985C2 8.50721 3.00736 7.49985 4.25 7.49985H7.97522L12.9425 3.29588C13.4694 2.84989 14.2582 2.91554 14.7041 3.4425ZM17.1035 8.64021C17.4571 8.42442 17.9187 8.5361 18.1344 8.88967C18.7083 9.8298 18.9957 10.8818 18.9957 12.0304C18.9957 13.1789 18.7083 14.231 18.1344 15.1711C17.9187 15.5247 17.4571 15.6364 17.1035 15.4206C16.75 15.2048 16.6383 14.7432 16.8541 14.3897C17.2822 13.6882 17.4957 12.9069 17.4957 12.0304C17.4957 11.1539 17.2822 10.3726 16.8541 9.67112C16.6383 9.31756 16.75 8.85601 17.1035 8.64021Z";
        private const string PathPerson   = "M17.7545 14.0002C18.9966 14.0002 20.0034 15.007 20.0034 16.2491V17.1675C20.0034 17.7409 19.8242 18.2999 19.4908 18.7664C17.9449 20.9296 15.4206 22.0013 12.0004 22.0013C8.5794 22.0013 6.05643 20.9292 4.51427 18.7648C4.18231 18.2989 4.00391 17.7411 4.00391 17.169V16.2491C4.00391 15.007 5.01076 14.0002 6.25278 14.0002H17.7545ZM12.0004 2.00488C14.7618 2.00488 17.0004 4.24346 17.0004 7.00488C17.0004 9.76631 14.7618 12.0049 12.0004 12.0049C9.23894 12.0049 7.00036 9.76631 7.00036 7.00488C7.00036 4.24346 9.23894 2.00488 12.0004 2.00488Z";
        private const string PathDesktop  = "M6.75 22.0004C6.33579 22.0004 6 21.6647 6 21.2504C6 20.8707 6.28215 20.557 6.64823 20.5073L6.75 20.5004L8.499 20.5V18.002L4.25 18.0023C3.05914 18.0023 2.08436 17.0771 2.00519 15.9063L2 15.7523V5.25C2 4.05914 2.92516 3.08436 4.09595 3.00519L4.25 3H19.7488C20.9397 3 21.9145 3.92516 21.9936 5.09595L21.9988 5.25V15.7523C21.9988 16.9431 21.0737 17.9179 19.9029 17.9971L19.7488 18.0023L15.499 18.002V20.5L17.25 20.5004C17.6642 20.5004 18 20.8362 18 21.2504C18 21.6301 17.7178 21.9439 17.3518 21.9936L17.25 22.0004H6.75ZM13.998 18.002H9.998L9.999 20.5004H13.999L13.998 18.002Z";
        private const string PathResize   = "M10.25 3H6.25C4.45507 3 3 4.45507 3 6.25V8.25C3 8.66421 3.33579 9 3.75 9C4.16421 9 4.5 8.66421 4.5 8.25V6.25C4.5 5.2835 5.2835 4.5 6.25 4.5H10.25C10.6642 4.5 11 4.16421 11 3.75C11 3.33579 10.6642 3 10.25 3ZM10.75 21C12.5449 21 14 19.5449 14 17.75V13.25C14 11.4551 12.5449 10 10.75 10H6.25C4.45507 10 3 11.4551 3 13.25V17.75C3 19.5449 4.45507 21 6.25 21H10.75ZM15.75 21C15.3358 21 15 20.6642 15 20.25C15 19.8358 15.3358 19.5 15.75 19.5H17.75C18.7165 19.5 19.5 18.7165 19.5 17.75V13.75C19.5 13.3358 19.8358 13 20.25 13C20.6642 13 21 13.3358 21 13.75V17.75C21 19.5449 19.5449 21 17.75 21H15.75ZM21 10.25V6.25C21 4.45507 19.5449 3 17.75 3H13.75C13.3358 3 13 3.33579 13 3.75C13 4.16421 13.3358 4.5 13.75 4.5H17.75C18.7165 4.5 19.5 5.2835 19.5 6.25V10.25C19.5 10.6642 19.8358 11 20.25 11C20.6642 11 21 10.6642 21 10.25Z";
        private const string PathColor    = "M3.83885 5.85764C6.77986 1.94203 12.8685 0.802644 17.2028 3.49752C21.4826 6.15853 23.0566 11.2746 21.3037 16.0749C19.6485 20.6075 15.2873 22.4033 12.144 20.1233C10.9666 19.2692 10.5101 18.1985 10.2895 16.4595L10.1841 15.4715L10.1387 15.0741C10.016 14.14 9.82762 13.7216 9.43435 13.5024C8.89876 13.2038 8.54213 13.1969 7.83887 13.4694L7.48775 13.615L7.30902 13.693C6.29524 14.1332 5.62085 14.2879 4.76786 14.1092L4.56761 14.062L4.40407 14.0154C1.61511 13.1512 1.20202 9.36827 3.83885 5.85764ZM16.7669 10.5797C16.9456 11.2465 17.631 11.6423 18.2978 11.4636C18.9646 11.2849 19.3604 10.5995 19.1817 9.93267C19.003 9.26583 18.3176 8.87011 17.6508 9.04878C16.9839 9.22746 16.5882 9.91288 16.7669 10.5797ZM17.2615 14.0684C17.4402 14.7352 18.1256 15.1309 18.7924 14.9523C19.4592 14.7736 19.855 14.0882 19.6763 13.4213C19.4976 12.7545 18.8122 12.3588 18.1454 12.5374C17.4785 12.7161 17.0828 13.4015 17.2615 14.0684ZM14.7884 7.57703C14.9671 8.24386 15.6525 8.63959 16.3193 8.46091C16.9861 8.28224 17.3819 7.59681 17.2032 6.92998C17.0245 6.26315 16.3391 5.86742 15.6723 6.0461C15.0054 6.22478 14.6097 6.9102 14.7884 7.57703ZM14.7599 16.5754C14.9386 17.2422 15.624 17.638 16.2908 17.4593C16.9577 17.2806 17.3534 16.5952 17.1747 15.9284C16.996 15.2615 16.3106 14.8658 15.6438 15.0445C14.9769 15.2232 14.5812 15.9086 14.7599 16.5754ZM11.263 6.60544C11.4416 7.27227 12.1271 7.668 12.7939 7.48932C13.4607 7.31064 13.8565 6.62522 13.6778 5.95839C13.4991 5.29156 12.8137 4.89583 12.1469 5.07451C11.48 5.25318 11.0843 5.9386 11.263 6.60544Z";
        private const string PathGames    = "M14.9979 5C18.8639 5 21.9979 8.13401 21.9979 12C21.9979 15.7855 18.9931 18.8691 15.2385 18.9959L14.9979 19H9.00211C5.13611 19 2.00211 15.866 2.00211 12C2.00211 8.21455 5.00689 5.1309 8.76146 5.00406L9.00211 5H14.9979ZM14.75 12.5C14.0596 12.5 13.5 13.0596 13.5 13.75C13.5 14.4404 14.0596 15 14.75 15C15.4403 15 16 14.4404 16 13.75C16 13.0596 15.4403 12.5 14.75 12.5ZM7.99999 9C7.62029 9 7.3065 9.28215 7.25684 9.64823L7.24999 9.75V11.248L5.74999 11.2487C5.33578 11.2487 4.99999 11.5845 4.99999 11.9987C4.99999 12.3784 5.28214 12.6922 5.64822 12.7419L5.74999 12.7487L7.24999 12.748V14.25C7.24999 14.6642 7.58578 15 7.99999 15C8.37969 15 8.69348 14.7178 8.74314 14.3518L8.74999 14.25V12.748L10.25 12.7487C10.6642 12.7487 11 12.413 11 11.9987C11 11.6191 10.7178 11.3053 10.3518 11.2556L10.25 11.2487L8.74999 11.248V9.75C8.74999 9.33579 8.4142 9 7.99999 9ZM16.75 9C16.0596 9 15.5 9.55964 15.5 10.25C15.5 10.9404 16.0596 11.5 16.75 11.5C17.4403 11.5 18 10.9404 18 10.25C18 9.55964 17.4403 9 16.75 9Z";

        // Launcher actions (Steam Big Picture, Xbox App, Playnite) — 2×2 grid of rounded tiles,
        // representing a game library / launcher screen. Fluent System Icons: grid_24_filled.
        private const string PathGameLibrary =
            "M3.5 5.75C3.5 4.50736 4.50736 3.5 5.75 3.5H9.25C10.4926 3.5 11.5 4.50736 11.5 5.75V9.25C11.5 10.4926 10.4926 11.5 9.25 11.5H5.75C4.50736 11.5 3.5 10.4926 3.5 9.25V5.75Z" +
            "M3.5 14.75C3.5 13.5074 4.50736 12.5 5.75 12.5H9.25C10.4926 12.5 11.5 13.5074 11.5 14.75V18.25C11.5 19.4926 10.4926 20.5 9.25 20.5H5.75C4.50736 20.5 3.5 19.4926 3.5 18.25V14.75Z" +
            "M12.5 5.75C12.5 4.50736 13.5074 3.5 14.75 3.5H18.25C19.4926 3.5 20.5 4.50736 20.5 5.75V9.25C20.5 10.4926 19.4926 11.5 18.25 11.5H14.75C13.5074 11.5 12.5 10.4926 12.5 9.25V5.75Z" +
            "M12.5 14.75C12.5 13.5074 13.5074 12.5 14.75 12.5H18.25C19.4926 12.5 20.5 13.5074 20.5 14.75V18.25C20.5 19.4926 19.4926 20.5 18.25 20.5H14.75C13.5074 20.5 12.5 19.4926 12.5 18.25V14.75Z";

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

                // OS-action tiles
                case TileActionType.BrightnessUp:   return PathBriHigh;
                case TileActionType.BrightnessDown: return PathBriLow;
                case TileActionType.VolumeUp:       return PathSpeaker2;
                case TileActionType.VolumeDown:     return PathSpeaker1;
                case TileActionType.GoToDesktop:    return PathDesktop;
                case TileActionType.ShowKeyboard:   return PathKeyboard;
                case TileActionType.CycleOverlayMode: return PathGauge;
                case TileActionType.ToggleControllerMouseMode: return PathGames;
                case TileActionType.EmulateXboxGuide:          return PathGames;  // Xbox/controller glyph

                // Launcher actions → game library grid icon
                case TileActionType.SteamBigPicture:
                case TileActionType.XboxApp:
                case TileActionType.Playnite:         return PathGameLibrary;

                case TileActionType.OpenClawTweaksWindow: return PathWindow;

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

        /// <summary>
        /// Fluent icon for a built-in (default) tile, keyed by its stable tile Id. These tiles have
        /// ActionType=None, so they can't be resolved via <see cref="GetFluentIconPath"/>.
        /// Returns null to fall back to the tile's Segoe glyph.
        /// </summary>
        public static string GetFluentIconPathForTileId(string tileId)
        {
            switch (tileId)
            {
                case "MSIClawDesktopMode":
                case "ControllerEmulation": return PathGames;     // Mode / Controller
                case "Overlay":             return PathGauge;      // performance overlay
                case "Profile":             return PathPerson;
                case "CPUBoost":            return PathChip;
                case "Resolution":          return PathDesktop;
                case "Fullscreen":          return PathFullscr;
                case "OptiScaler":          return PathResize;
                case "ReShade":             return PathColor;
                case "LosslessScaling":     return PathResize;
                case "Keyboard":            return PathKeyboard;
                case "MsiCenter":           return PathApps;
                case "ExternalGamepadMode": return PathDesktop;   // monitor/TV — external display setup (distinct from the controller Mode tile)
                case "ActionTaskManager":   return PathWindow;
                case "ActionExplorer":      return PathFolder;
                case "ActionEndTask":       return PathDismiss;
                default: return null;
            }
        }

        /// <summary>Returns true if this action type is only relevant for MSI Claw devices.
        /// ClawTweaks is always running on MSI Claw, so this always returns false.</summary>
        public static bool IsMsiClawOnly(TileActionType action) => false;

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
            yield return TileActionType.ShowKeyboard;
            // App group
            yield return TileActionType.CycleOverlayMode;
            yield return TileActionType.CycleLimiterMode;
            yield return TileActionType.TDPIncrBy1W;
            yield return TileActionType.TDPDecrBy1W;
            yield return TileActionType.ToggleControllerMouseMode;
            // Launcher group
            yield return TileActionType.SteamBigPicture;
            yield return TileActionType.Playnite;
            yield return TileActionType.XboxApp;
            yield return TileActionType.OpenClawTweaksWindow;
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
            // Special Controller Buttons group
            yield return TileActionType.EmulateXboxGuide;
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
