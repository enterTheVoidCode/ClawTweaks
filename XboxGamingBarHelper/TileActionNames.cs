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
                default: return $"Action {actionType}";
            }
        }
    }
}
