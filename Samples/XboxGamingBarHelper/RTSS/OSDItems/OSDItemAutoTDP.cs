using System.Drawing;
using XboxGamingBarHelper.AutoTDP;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemAutoTDP : OSDItem
    {
        private AutoTDPManager autoTDPManager;

        public OSDItemAutoTDP() : base("AutoTDP", "AutoTDP", Color.FromArgb(0x00, 0xBF, 0xFF)) // DeepSkyBlue
        {
        }

        public void SetAutoTDPManager(AutoTDPManager manager)
        {
            autoTDPManager = manager;
        }

        public override string GetOSDString(int osdLevel)
        {
            // Only show when AutoTDP is enabled
            if (autoTDPManager == null || !autoTDPManager.Enabled.Value)
            {
                return string.Empty;
            }

            var statusText = autoTDPManager.StatusText;
            var targetFPS = autoTDPManager.TargetFPS.Value;
            var currentFPS = autoTDPManager.CurrentFPS.Value;
            var currentTDP = autoTDPManager.CurrentTDPValue;
            var newTDP = autoTDPManager.NewTDPValue;
            var isProbing = autoTDPManager.IsProbing;
            var sweetSpotTDP = autoTDPManager.SweetSpotTDP;
            var sweetSpotConfidence = autoTDPManager.SweetSpotConfidence;

            if (string.IsNullOrEmpty(statusText))
            {
                return string.Empty;
            }

            // Determine display status (probing takes precedence)
            string displayStatus = isProbing ? "Probing" : statusText;

            // Check if we're locked on sweet spot
            bool isLocked = displayStatus.StartsWith("Locked");

            // Get text color with opacity for OLED protection
            var tc = GetTextColorWithOpacity();

            // Color code based on status - apply opacity for OLED protection
            string statusColor;
            if (isLocked)
                statusColor = ApplyOpacity("00FF00"); // Green for locked
            else if (displayStatus == "On target")
                statusColor = ApplyOpacity("00FF00"); // Green
            else if (displayStatus == "Below target" || displayStatus.StartsWith("Increasing"))
                statusColor = ApplyOpacity("FFFF00"); // Yellow
            else if (displayStatus == "Above target" || displayStatus.StartsWith("Decreasing"))
                statusColor = ApplyOpacity("00BFFF"); // Cyan
            else if (displayStatus == "Probing")
                statusColor = ApplyOpacity("FF00FF"); // Magenta for probing
            else
                statusColor = tc; // Use text color with opacity

            // Build OSD string
            // Format: AutoTDP 58/60 FPS 14W Locked 13W
            var osdString = $"<C={ApplyOpacity(colorCode)}>{name}<C={tc}> {currentFPS}/{targetFPS} FPS";

            // Show TDP change when increasing, decreasing, or probing
            if (displayStatus.StartsWith("Increasing") || displayStatus.StartsWith("Decreasing") || displayStatus == "Probing")
            {
                osdString += $" {currentTDP}W->{newTDP}W";
            }
            else if (currentTDP > 0)
            {
                // Show current TDP for other states
                osdString += $" {currentTDP}W";
            }

            osdString += $" <C={statusColor}>{displayStatus}<C={tc}>";

            // Show sweet spot if detected but not yet locked
            if (sweetSpotConfidence >= 60 && !isLocked)
            {
                osdString += $" <C={ApplyOpacity("888888")}>(sweet:{sweetSpotTDP}W)<C={tc}>";
            }

            return osdString;
        }
    }
}
