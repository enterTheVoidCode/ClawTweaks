using Shared.Constants;
using System;
using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemBattery : OSDItem
    {
        private HardwareSensor batteryPercentSensor;
        private HardwareSensor batteryDischargeRateSensor;
        private HardwareSensor batteryChargeRateSensor;
        private HardwareSensor batteryRemainTimeSensor;

        public OSDItemBattery(HardwareSensor batteryPercentSensor, HardwareSensor batteryDischargeRateSensor, HardwareSensor batteryChargeRateSensor, HardwareSensor batteryRemainTimeSensor) : base("BATTERY", Color.DarkCyan)
        {
            this.batteryPercentSensor = batteryPercentSensor;
            this.batteryDischargeRateSensor = batteryDischargeRateSensor;
            this.batteryChargeRateSensor = batteryChargeRateSensor;
            this.batteryRemainTimeSensor = batteryRemainTimeSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            if (osdLevel >= 2)
            {
                osdItems.Add(new OSDItemValue(batteryPercentSensor.Value, "%"));

                if (batteryDischargeRateSensor.Value > 0)
                {
                    osdItems.Add(new OSDItemValue(batteryDischargeRateSensor.Value, "W/H", "-"));
                }

                if (batteryChargeRateSensor.Value > 0)
                {
                    osdItems.Add(new OSDItemValue(batteryChargeRateSensor.Value, "W/H", "+"));
                }

                if (batteryRemainTimeSensor.Value > 0)
                {
                    var hours = Math.Floor(batteryRemainTimeSensor.Value / MathConstants.SECONDS_PER_HOUR);
                    var minutes = (batteryRemainTimeSensor.Value - hours * MathConstants.SECONDS_PER_HOUR) / MathConstants.SECONDS_PER_MINUTE;
                    osdItems.Add(new OSDItemValue((float)hours, "H"));
                    osdItems.Add(new OSDItemValue((float)minutes, "M"));
                }
            }

            return osdItems;
        }
    }
}
