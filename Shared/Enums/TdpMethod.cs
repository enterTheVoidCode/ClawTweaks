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
        /// Use kx.exe (Intel kernel extension) for MCHBAR PL1/PL2 writes.
        /// Targets Intel Core Ultra 200V (Lunar Lake) — MSI Claw 8 AI A2VM.
        /// kx.exe must be present in the helper directory.
        /// Falls back automatically when PawnIO/RyzenSMU is unavailable (Intel hardware).
        /// </summary>
        IntelKxExe = 2

        // WinRing0 = 3 - Removed: WinRing0 driver deprecated and no longer bundled
    }
}
