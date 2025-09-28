using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XboxGamingBarHelper.Performance.Sensors;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemBattery : OSDItem
    {
        private ISensor batteryPercentSensor;
        private ISensor batteryRemainTimeSensor;

        public OSDItemBattery(ISensor batteryPercentSensor, ISensor batteryRemainTimeSensor) : base("BATTERY", Color.DarkCyan)
        {
            this.batteryPercentSensor = batteryPercentSensor;
            this.batteryRemainTimeSensor = batteryRemainTimeSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            if (osdLevel >= 2)
            {
                osdItems.Add(new OSDItemValue(batteryPercentSensor != null ? batteryPercentSensor.Value.Value : -1.0f, "%"));
                if (batteryRemainTimeSensor != null && batteryRemainTimeSensor.Value.HasValue)
                {
                    osdItems.Add(new OSDItemValue(batteryRemainTimeSensor.Value.Value, "min"));
                }
            }

            return osdItems;
        }
    }
}
