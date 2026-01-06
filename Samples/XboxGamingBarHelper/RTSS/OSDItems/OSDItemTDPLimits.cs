using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Legion;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    /// <summary>
    /// OSD item for displaying current TDP limits (SPL/SPPT/FPPT) or mode name
    /// </summary>
    internal class OSDItemTDPLimits : OSDItem
    {
        private PerformanceManager performanceManager;
        private LegionManager legionManager;

        public OSDItemTDPLimits() : base("Limits", "TDPLimits", Color.Orange)
        {
        }

        /// <summary>
        /// Sets the Performance Manager reference to read TDP limits from.
        /// Must be called after PerformanceManager is initialized.
        /// </summary>
        public void SetPerformanceManager(PerformanceManager manager)
        {
            performanceManager = manager;
        }

        /// <summary>
        /// Sets the Legion Manager reference to read performance mode from.
        /// Must be called after LegionManager is initialized.
        /// </summary>
        public void SetLegionManager(LegionManager manager)
        {
            legionManager = manager;
        }

        public override string GetOSDString(int osdLevel)
        {
            if (performanceManager == null)
            {
                return string.Empty;
            }

            // Apply opacity to label and text colors for OLED protection
            var labelColor = ApplyOpacity(colorCode);
            var tc = GetTextColorWithOpacity();

            // If Legion Go is detected and not in Custom mode, show mode name instead of limits
            if (legionManager != null && legionManager.LegionGoDetected?.Value == true)
            {
                int mode = legionManager.CurrentPerformanceMode;
                if (mode != 255) // Not Custom mode
                {
                    string modeName = LegionManager.GetPerformanceModeName(mode);
                    return $"<C={labelColor}>Mode<C={tc}> <C={tc}>{modeName}<C={tc}>";
                }
            }

            int spl = performanceManager.CurrentSPL;
            int sppt = performanceManager.CurrentSPPT;
            int fppt = performanceManager.CurrentFPPT;

            // Don't show if no TDP has been set yet
            if (spl == 0 && sppt == 0 && fppt == 0)
            {
                return string.Empty;
            }

            // Format: "Limits: SPL/SPPT/FPPT" e.g. "Limits: 25/26/28"
            return $"<C={labelColor}>Limits<C={tc}> <C={tc}>{spl}/{sppt}/{fppt}W<C={tc}>";
        }
    }
}
