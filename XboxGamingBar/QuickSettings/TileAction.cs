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
        CycleOverlayMode = 20,
        CycleTDPMode     = 21,
        TDPStepUp        = 22,
        TDPStepDown      = 23,
    }

    public static class TileActionHelper
    {
        public static string GetDisplayName(TileActionType action)
        {
            switch (action)
            {
                case TileActionType.KeyboardShortcut: return "Keyboard Shortcut";
                case TileActionType.BrightnessUp:     return "Helligkeit +5%";
                case TileActionType.BrightnessDown:   return "Helligkeit -5%";
                case TileActionType.AltTab:           return "Alt+Tab (Fensterübersicht)";
                case TileActionType.AltTabBack:       return "Alt+Tab (Vorherige App)";
                case TileActionType.GoToDesktop:      return "Desktop (Win+D)";
                case TileActionType.CycleOverlayMode: return "Overlay Modes durchschalten";
                case TileActionType.CycleTDPMode:     return "TDP Modes durchschalten";
                case TileActionType.TDPStepUp:        return "TDP +1 Stufe";
                case TileActionType.TDPStepDown:      return "TDP -1 Stufe";
                default: return "Unknown";
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
                default: return "";
            }
        }

        public static string GetGroupName(TileActionType action)
        {
            int v = (int)action;
            if (v >= 10 && v < 20) return "OS";
            if (v >= 20) return "App";
            return "";
        }

        /// <summary>Returns all user-selectable action types in display order.</summary>
        public static IEnumerable<TileActionType> GetAllActions()
        {
            // OS group
            yield return TileActionType.BrightnessUp;
            yield return TileActionType.BrightnessDown;
            yield return TileActionType.AltTab;
            yield return TileActionType.AltTabBack;
            yield return TileActionType.GoToDesktop;
            // App group
            yield return TileActionType.CycleOverlayMode;
            yield return TileActionType.CycleTDPMode;
            yield return TileActionType.TDPStepUp;
            yield return TileActionType.TDPStepDown;
        }
    }
}
