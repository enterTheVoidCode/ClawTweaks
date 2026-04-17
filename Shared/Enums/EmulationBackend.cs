namespace Shared.Enums
{
    /// <summary>
    /// Controller emulation backend selection.
    /// </summary>
    public enum EmulationBackend
    {
        /// <summary>
        /// Legacy ViGEm-based emulation (currently shipped).
        /// Supports DS4 + Xbox 360 via ViGEmBus driver.
        /// </summary>
        Legacy = 0,

        /// <summary>
        /// VIIPER (USBIP-based) emulation. Experimental.
        /// Supports DualShock 4, DualSense Edge, Xbox 360, Xbox Elite 2,
        /// Steam controllers, Switch Pro, and Joy-Con pair.
        /// Requires usbip-win2 driver to be installed.
        /// </summary>
        Viiper = 1
    }
}
