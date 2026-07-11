using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Devices.Libraries.Legion;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    /// <summary>
    /// OSD item for displaying CPU fan speed. Legion Go reads it from the Legion WMI; the MSI Claw
    /// falls back to the EC tach (Get_Fan[0], RPM = 478000 / word — decoded 2026-07-11).
    /// </summary>
    internal class OSDItemFan : OSDItem
    {
        private LegionManager legionManager;

        public OSDItemFan() : base("FAN", "Fan", Color.Cyan)
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

            // Legion Go reports fan RPM via its WMI; on the MSI Claw that returns 0, so fall back to the
            // EC tach (Get_Fan[0] → 478000 / word). GetCpuFanRpmCached is throttled + self-gating, so it
            // is safe to poll here every OSD tick and is a no-op (0) on non-Claw hardware.
            int fanRpm = legionManager != null ? legionManager.GetCpuFanSpeed() : 0;
            if (fanRpm <= 0)
                fanRpm = XboxGamingBarHelper.MSI.MsiClawFanController.GetCpuFanRpmCached();

            if (fanRpm > 0)
                osdItems.Add(new OSDItemValue(fanRpm, "RPM", OSDValueType.Speed));

            return osdItems;
        }
    }
}
