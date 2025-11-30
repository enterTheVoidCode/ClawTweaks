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

        public OSDItemBattery(HardwareSensor batteryPercentSensor, HardwareSensor batteryDischargeRateSensor, HardwareSensor batteryChargeRateSensor, HardwareSensor batteryRemainTimeSensor) : base("BATTERY", "Battery", Color.DarkCyan)
        {
            this.batteryPercentSensor = batteryPercentSensor;
            this.batteryDischargeRateSensor = batteryDischargeRateSensor;
            this.batteryChargeRateSensor = batteryChargeRateSensor;
            this.batteryRemainTimeSensor = batteryRemainTimeSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            // Always show battery info when enabled (inverted % - green when full, red when low)
            osdItems.Add(new OSDItemValue(batteryPercentSensor.Value, "%", OSDValueType.PercentageInv));

            if (batteryDischargeRateSensor.Value > 0)
            {
                osdItems.Add(new OSDItemValue(batteryDischargeRateSensor.Value, "W/H", "-", OSDValueType.Wattage));
            }

            if (batteryChargeRateSensor.Value > 0)
            {
                osdItems.Add(new OSDItemValue(batteryChargeRateSensor.Value, "W/H", "+", OSDValueType.None));
            }

            if (batteryRemainTimeSensor.Value > 0)
            {
                var hours = Math.Floor(batteryRemainTimeSensor.Value / MathConstants.SECONDS_PER_HOUR);
                var minutes = (batteryRemainTimeSensor.Value - hours * MathConstants.SECONDS_PER_HOUR) / MathConstants.SECONDS_PER_MINUTE;
                osdItems.Add(new OSDItemValue((float)hours, "H", OSDValueType.None));
                osdItems.Add(new OSDItemValue((float)minutes, "M", OSDValueType.None));
            }

            return osdItems;
        }
    }
}
