namespace Shared.Enums
{
    /// <summary>
    /// TDP control method selection
    /// </summary>
    public enum TdpMethod
    {
        /// <summary>
        /// Use manufacturer's WMI interface (Legion Go, etc.)
        /// Only available when supported device is detected
        /// </summary>
        ManufacturerWMI = 0,

        /// <summary>
        /// Use PawnIO driver with RyzenSMU module
        /// Anti-cheat safe
        /// </summary>
        PawnIO = 1,

        /// <summary>
        /// Use WinRing0 driver (deprecated)
        /// May trigger anti-cheat systems like EAC
        /// </summary>
        WinRing0 = 2
    }
}
