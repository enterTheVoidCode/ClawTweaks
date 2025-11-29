using System.Drawing;
using XboxGamingBarHelper.AutoTDP;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemAutoTDP : OSDItem
    {
        private AutoTDPManager autoTDPManager;

        public OSDItemAutoTDP() : base("AutoTDP", Color.FromArgb(0x00, 0xBF, 0xFF)) // DeepSkyBlue
        {
        }

        public void SetAutoTDPManager(AutoTDPManager manager)
        {
            autoTDPManager = manager;
        }

        public override string GetOSDString(int osdLevel)
        {
            // Only show in full overlay (level 3+) and when AutoTDP is enabled
            if (osdLevel < 3 || autoTDPManager == null || !autoTDPManager.Enabled.Value)
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

            // Color code based on status
            string statusColor;
            if (isLocked)
                statusColor = "00FF00"; // Green for locked
            else if (displayStatus == "On target")
                statusColor = "00FF00"; // Green
            else if (displayStatus == "Below target" || displayStatus.StartsWith("Increasing"))
                statusColor = "FFFF00"; // Yellow
            else if (displayStatus == "Above target" || displayStatus.StartsWith("Decreasing"))
                statusColor = "00BFFF"; // Cyan
            else if (displayStatus == "Probing")
                statusColor = "FF00FF"; // Magenta for probing
            else
                statusColor = "FFFFFF"; // White

            // Build OSD string
            // Format: AutoTDP 58/60 FPS 14W Locked 13W
            var osdString = $"<C={colorCode}>{name}<C> <C=FFFFFF>{currentFPS}/{targetFPS}<S=50> FPS<S><C>";

            // Show TDP change when increasing, decreasing, or probing
            if (displayStatus.StartsWith("Increasing") || displayStatus.StartsWith("Decreasing") || displayStatus == "Probing")
            {
                osdString += $" <C=FFFFFF>{currentTDP}W->{newTDP}W<C>";
            }
            else if (currentTDP > 0)
            {
                // Show current TDP for other states
                osdString += $" <C=FFFFFF>{currentTDP}W<C>";
            }

            osdString += $" <C={statusColor}>{displayStatus}<C>";

            // Show sweet spot if detected but not yet locked
            if (sweetSpotConfidence >= 60 && !isLocked)
            {
                osdString += $" <C=888888>(sweet:{sweetSpotTDP}W)<C>";
            }

            return osdString;
        }
    }
}
