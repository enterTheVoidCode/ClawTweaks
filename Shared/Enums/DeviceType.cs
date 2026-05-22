namespace Shared.Enums
{
    /// <summary>
    /// Known device types with specific feature support.
    /// </summary>
    public enum DeviceType
    {
        /// <summary>
        /// Unknown or generic device - uses basic TDP presets only
        /// </summary>
        Generic = 0,

        /// <summary>
        /// Lenovo Legion Go (Z1 Extreme) - Models: 83E1
        /// Full feature support: WMI TDP modes, controller remapping, RGB lighting
        /// </summary>
        LegionGo = 1,

        /// <summary>
        /// Lenovo Legion Go 2 (Ryzen AI) - Models: 83N0 (8ASP2, 8AHP2 variants)
        /// Full feature support similar to Legion Go
        /// </summary>
        LegionGo2 = 2,

        /// <summary>
        /// Lenovo Legion Go S (budget model) - Different hardware, may have limited features
        /// </summary>
        LegionGoS = 3,

        /// <summary>
        /// GPD Win Mini - Models: G1617
        /// Supports fan control
        /// </summary>
        GPDWinMini = 50,

        /// <summary>
        /// GPD Win 4 Series - Models: G1618-04
        /// Supports fan control
        /// </summary>
        GPDWin4 = 51,

        /// <summary>
        /// GPD Win 5 - Models: G1618-05
        /// Supports fan control
        /// </summary>
        GPDWin5 = 52,

        /// <summary>
        /// MSI Claw (A1M / A2VM) — Intel Meteor Lake / Lunar Lake.
        /// Models: MS-1T41 (A1M), MS-1T42 (Claw 7 AI+ A2VM), MS-1T52 (Claw 8 AI+ A2VM).
        /// Controller emulation via XInput; no touchpad; no detachable controllers.
        /// </summary>
        MSIClaw = 40,

        // Future device types can be added here:
        // AyaNeo = 10,
        // SteamDeck = 20,
        // ROGAlly = 30,
    }
}
