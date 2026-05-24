using System;
using System.Drawing;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    /// <summary>
    /// OSD item for displaying the current time in 24-hour format (HH:MM)
    /// </summary>
    internal class OSDItemTime : OSDItem
    {
        public OSDItemTime() : base("TIME", "Time", Color.White)
        {
        }

        public override string GetOSDString(int osdLevel)
        {
            var now = DateTime.Now;
            var timeString = now.ToString("HH:mm"); // e.g., "14:30"
            return $"<C={GetTextColorWithOpacity()}>{timeString}";
        }
    }
}
