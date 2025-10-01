using LibreHardwareMonitor.Hardware;
using Shared.Constants;
using System;
using System.Collections.Generic;
using System.Drawing;

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
                    var hours = Math.Floor(batteryRemainTimeSensor.Value.Value / MathConstants.SECONDS_PER_HOUR);
                    var minutes = (batteryRemainTimeSensor.Value.Value - hours * MathConstants.SECONDS_PER_HOUR) / MathConstants.SECONDS_PER_MINUTE;
                    osdItems.Add(new OSDItemValue((float)hours, "H"));
                    osdItems.Add(new OSDItemValue((float)minutes, "M"));
                }
            }

            return osdItems;
        }
    }
}
