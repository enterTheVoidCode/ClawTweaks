namespace XboxGamingBarHelper
{
    /// <summary>
    /// Display names for TileActionType values — mirrors TileActionHelper.GetDisplayName()
    /// without pulling in the widget assembly.
    /// </summary>
    internal static class TileActionNames
    {
        public static string GetDisplayName(int actionType)
        {
            switch (actionType)
            {
                case 10: return "Brightness +5%";
                case 11: return "Brightness -5%";
                case 12: return "Window Overview (Alt+Tab)";
                case 13: return "Cycle Through Apps";
                case 14: return "Show Desktop (Win+D)";
                case 20: return "Cycle Overlay Mode";
                case 24: return "Cycle FPS Limit";
                case 25: return "TDP +1W";
                case 26: return "TDP -1W";
                case 27: return "Volume +5%";
                case 28: return "Volume -5%";
                case 29: return "Toggle Controller/Mouse";
                case 30: return "Next Track";
                case 31: return "Previous Track";
                case 32: return "Play / Pause";
                case 40: return "Steam Big Picture";
                case 41: return "Playnite";
                case 42: return "Xbox App";
                case 43: return "Open ClawTweaks Window";
                case 50: return "Open Default Browser";
                case 51: return "Open Windows Store";
                case 52: return "Open Chrome";
                case 53: return "Open Spotify";
                case 59: return "Program (User)";
                case 60: return "Open Exophase (Achievements)";
                case 61: return "Open Retro Achievements";
                case 62: return "Open Google";
                case 63: return "Open ClawTweaks Releases";
                case 64: return "Open ClawTweaks FAQ";
                case 65: return "Open YouTube";
                case 69: return "Website (User)";
                case 74: return "Steam BPM Left Menu";
                case 75: return "Steam BPM Right Quick Access";
                default: return $"Action {actionType}";
            }
        }
    }
}
