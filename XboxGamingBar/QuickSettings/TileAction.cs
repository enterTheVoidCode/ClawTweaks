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
                case TileActionType.CycleOverlayMode: return "CycleOSD";
                case TileActionType.CycleLimiterMode: return "CycleFPS";
                case TileActionType.TDPIncrBy1W:      return "TDP+1";
                case TileActionType.TDPDecrBy1W:      return "TDP-1";
                case TileActionType.ToggleControllerMouseMode: return "Ctrl/Mouse";
                case TileActionType.SteamBigPicture:  return "Steam";
                case TileActionType.Playnite:         return "Playnite";
                case TileActionType.XboxApp:          return "Xbox";
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
            if (v == 27 || v == 28) return "OS";       // VolumeUp / VolumeDown
            if (v >= 40 && v < 50) return "Launcher";  // Launcher actions (Steam BP, Playnite, Xbox)
            if (v >= 20) return "App";
            return "";
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
        }
    }
}
