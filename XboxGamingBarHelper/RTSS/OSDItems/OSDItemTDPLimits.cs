using System;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    /// <summary>
    /// Dedicated TDP block: the APU's current package power (the main CPU+GPU draw — the same value
    /// that used to sit in the CPU block) followed by the configured PL1/PL2 limits (PL1=SPL, PL2=FPPT).
    /// Always slider-based — ClawTweaks has no TDP "modes"/presets, so no mode/preset name is ever shown.
    /// </summary>
    internal class OSDItemTDPLimits : OSDItem
    {
        private PerformanceManager performanceManager;

        public OSDItemTDPLimits() : base("TDP", "TDPLimits", System.Drawing.Color.Orange)
        {
        }

        public void SetPerformanceManager(PerformanceManager manager)
        {
            performanceManager = manager;
        }

        public override string GetOSDString(int osdLevel)
        {
            if (performanceManager == null)
            {
                return string.Empty;
            }

            int pl1 = performanceManager.CurrentSPL;   // PL1 (sustained)
            int pl2 = performanceManager.CurrentFPPT;  // PL2 (fast/turbo)

            // Nothing meaningful to show until a TDP has been applied.
            if (pl1 == 0 && pl2 == 0)
            {
                return string.Empty;
            }

            var labelColor = ApplyOpacity(colorCode);
            var tc = GetTextColorWithOpacity();

            // Current package power = the actual TDP we display (APU = CPU + GPU on one die).
            float watt = performanceManager.CPUWattage?.Value ?? -1f;
            string wattText = watt >= 0 ? $"{(int)Math.Round(watt)}W " : "";

            // PL1/PL2 grouped in parentheses next to the live package power, e.g. "TDP 9W (PL1:25W PL2:26W)".
            // The live watt value stays full size; the PL1/PL2 hint is secondary → rendered smaller (75%).
            string plText = pl2 > 0 ? $"<S=75>(PL1:{pl1}W PL2:{pl2}W)<S>" : $"<S=75>(PL1:{pl1}W)<S>";
            return $"<C={labelColor}>TDP<C={tc}> {wattText}{plText}<C={tc}>";
        }
    }
}
