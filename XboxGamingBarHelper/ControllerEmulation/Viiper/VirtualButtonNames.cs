namespace XboxGamingBarHelper.ControllerEmulation.Viiper
{
    /// <summary>
    /// Per-device virtual button names used to populate remapping dropdowns.
    /// </summary>
    internal static class VirtualButtonNames
    {
        public const string None = "(none)";

        private static readonly string[] Xbox360Buttons = new[]
        {
            None, "A", "B", "X", "Y", "LB", "RB", "Back", "Start", "LS", "RS", "Guide",
            "DPad Up", "DPad Down", "DPad Left", "DPad Right",
        };

        private static readonly string[] XboxElite2Buttons = new[]
        {
            None, "A", "B", "X", "Y", "LB", "RB", "Back", "Start", "LS", "RS", "Guide",
            "DPad Up", "DPad Down", "DPad Left", "DPad Right",
            "P1", "P2", "P3", "P4",
        };

        private static readonly string[] DualShock4Buttons = new[]
        {
            None, "Cross", "Circle", "Square", "Triangle", "L1", "R1",
            "Share", "Options", "L3", "R3", "PS", "Touchpad",
            "DPad Up", "DPad Down", "DPad Left", "DPad Right",
        };

        private static readonly string[] DualSenseEdgeButtons = new[]
        {
            None, "Cross", "Circle", "Square", "Triangle", "L1", "R1",
            "Create", "Options", "L3", "R3", "PS", "Touchpad",
            "DPad Up", "DPad Down", "DPad Left", "DPad Right",
            "PaddleL1", "PaddleL2", "PaddleR1", "PaddleL3",
        };

        private static readonly string[] SteamButtons = new[]
        {
            None, "A", "B", "X", "Y", "L", "R", "ZL", "ZR",
            "Minus", "Plus", "Home", "LStick", "RStick",
            "DPad Up", "DPad Down", "DPad Left", "DPad Right",
            "P1", "P2", "P3", "P4", "QuickAccess",
        };

        private static readonly string[] SwitchProButtons = new[]
        {
            None, "B", "A", "Y", "X", "L", "R", "ZL", "ZR",
            "Minus", "Plus", "Home", "Capture", "LStick", "RStick",
            "DPad Up", "DPad Down", "DPad Left", "DPad Right",
        };

        public static string[] GetForDevice(string deviceType)
        {
            switch (deviceType)
            {
                case "xbox360":
                    return Xbox360Buttons;
                case "xboxelite2":
                case "xbox-one":
                case "xbox-elite":
                    return XboxElite2Buttons;
                case "dualshock4":
                    return DualShock4Buttons;
                case "dualsenseedge":
                    return DualSenseEdgeButtons;
                case "steamdeck-generic":
                case "steam-generic":
                case "steam-controller":
                    return SteamButtons;
                case "switchpro":
                case "joycon-left":
                case "joycon-right":
                case "joycon-pair":
                    return SwitchProButtons;
                default:
                    return Xbox360Buttons;
            }
        }
    }
}
