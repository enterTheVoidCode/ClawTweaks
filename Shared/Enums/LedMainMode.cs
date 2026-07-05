namespace Shared.Enums
{
    /// <summary>
    /// MSI Claw controller LED main mode, chosen from the LED card's top dropdown.
    /// Static / Breathing / ColorCycle / Wave map to firmware effect modes
    /// (MsiClawLedController.LedEffectMode); Battery is our software SoC-tint (LedSoc), applied as
    /// solid writes per 10% charge band.
    /// </summary>
    public enum LedMainMode
    {
        Static     = 0,
        Breathing  = 1,
        ColorCycle = 2,
        Wave       = 3,
        Battery    = 4,
    }
}
