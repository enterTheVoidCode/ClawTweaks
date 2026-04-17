using System.Collections.Generic;

namespace XboxGamingBarHelper.ControllerEmulation.Viiper
{
    /// <summary>
    /// Represents a virtual device managed by libviiper.
    /// </summary>
    internal sealed class ViiperVirtualDevice
    {
        public uint BusId { get; set; }
        public uint DeviceId { get; set; }
        public string TypeName { get; set; } = "xbox360";
        public bool IsActive { get; set; }
    }

    /// <summary>Physical buttons recognized by VIIPER's remap layer.</summary>
    internal enum PhysicalButton
    {
        A, B, X, Y, LB, RB, Back, Start, LS, RS, Guide,
        DPadUp, DPadDown, DPadLeft, DPadRight,
        // Legion Go extras
        Y1, Y2, Y3, M3, M1, M2, Mode, Share,
    }

    internal sealed class ButtonMappingEntry
    {
        public PhysicalButton Physical { get; set; }
        public string VirtualButton { get; set; } = string.Empty;
    }

    internal sealed class ButtonMappingProfile
    {
        public string DeviceType { get; set; } = string.Empty;
        public List<ButtonMappingEntry> Mappings { get; set; } = new List<ButtonMappingEntry>();
    }

    /// <summary>
    /// Global VIIPER settings (not per-game-profile by design — see plan).
    /// </summary>
    internal sealed class ViiperAppSettings
    {
        public string DeviceType { get; set; }          // e.g. "xbox360", "dualshock4", "dualsenseedge"
        public string SteamSubDevice { get; set; }      // e.g. "legion-go", "steam-deck" when DeviceType is Steam
        public string InputSource { get; set; } = "XInput";   // "XInput" or "LegionHid"
        public string GyroSource { get; set; } = "Left";      // "Left", "Right", "Handheld"
        public string GyroMapX { get; set; } = "X";
        public string GyroMapY { get; set; } = "Y";
        public string GyroMapZ { get; set; } = "Z";
        public string AccelMapX { get; set; } = "X";
        public string AccelMapY { get; set; } = "Y";
        public string AccelMapZ { get; set; } = "Z";
        public bool AutoStartEmulation { get; set; }
        public bool SwapRumbleSides { get; set; }
        public bool InvertRightMotor { get; set; }
        public int RumbleIntensityPercent { get; set; } = 100;
        public Dictionary<string, List<ButtonMappingEntry>> ButtonMappings { get; set; }
    }
}
