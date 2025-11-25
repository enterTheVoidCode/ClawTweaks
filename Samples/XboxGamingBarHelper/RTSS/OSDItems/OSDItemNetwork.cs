using System;
using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemNetwork : OSDItem
    {
        private HardwareSensor networkDownloadSensor;
        private HardwareSensor networkUploadSensor;

        public OSDItemNetwork(HardwareSensor networkDownloadSensor, HardwareSensor networkUploadSensor) : base("NET", Color.Orange)
        {
            this.networkDownloadSensor = networkDownloadSensor;
            this.networkUploadSensor = networkUploadSensor;
        }

        public override string GetOSDString(int osdLevel)
        {
            if (osdLevel < 3)
                return string.Empty;

            // Format network speeds in KB/s or MB/s
            var downloadSpeed = networkDownloadSensor.Value;
            var uploadSpeed = networkUploadSensor.Value;

            string downloadStr = FormatSpeed(downloadSpeed);
            string uploadStr = FormatSpeed(uploadSpeed);

            // Show download with down arrow and upload with up arrow
            return $"<C=FFA500>NET<C> <C=FFFFFF>↓{downloadStr} ↑{uploadStr}<C>";
        }

        private string FormatSpeed(float bytesPerSecond)
        {
            if (bytesPerSecond < 0)
                return "N/A";

            // Convert to KB/s
            float kbps = bytesPerSecond / 1024f;

            if (kbps >= 1024)
            {
                // Show as MB/s
                return $"{Math.Floor(kbps / 1024)}<S=50> MB/s<S>";
            }
            else
            {
                // Show as KB/s
                return $"{Math.Floor(kbps)}<S=50> KB/s<S>";
            }
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            // Not used - we override GetOSDString directly for custom formatting
            return base.GetValues(osdLevel);
        }
    }
}
