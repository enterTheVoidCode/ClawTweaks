using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Legion;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    /// <summary>
    /// OSD item for displaying CPU fan speed (Legion Go only)
    /// </summary>
    internal class OSDItemFan : OSDItem
    {
        private LegionManager legionManager;

        public OSDItemFan() : base("FAN", Color.Cyan)
        {
        }

        /// <summary>
        /// Sets the Legion Manager reference to read fan speed from.
        /// Must be called after LegionManager is initialized.
        /// </summary>
        public void SetLegionManager(LegionManager manager)
        {
            legionManager = manager;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            // Only show fan speed at detailed level (3+) and if Legion is available
            if (osdLevel >= 3 && legionManager != null)
            {
                int fanRpm = legionManager.GetCpuFanSpeed();
                if (fanRpm > 0)
                {
                    osdItems.Add(new OSDItemValue(fanRpm, "RPM"));
                }
            }

            return osdItems;
        }
    }
}
