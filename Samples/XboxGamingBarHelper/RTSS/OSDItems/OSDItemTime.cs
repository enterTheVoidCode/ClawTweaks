using System;
using System.Drawing;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    /// <summary>
    /// OSD item for displaying the current time in 12-hour format (HH:MM AM/PM)
    /// </summary>
    internal class OSDItemTime : OSDItem
    {
        public OSDItemTime() : base("TIME", "Time", Color.White)
        {
        }

        public override string GetOSDString(int osdLevel)
        {
            var now = DateTime.Now;
            var timeString = now.ToString("h:mm tt"); // e.g., "2:30 PM"
            return $"<C={GetTextColorWithOpacity()}>{timeString}";
        }
    }
}
