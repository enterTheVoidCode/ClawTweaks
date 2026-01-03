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
        private Func<float> getTimeToFull;

        public OSDItemBattery(HardwareSensor batteryPercentSensor, HardwareSensor batteryDischargeRateSensor, HardwareSensor batteryChargeRateSensor, HardwareSensor batteryRemainTimeSensor, Func<float> getTimeToFull = null) : base("BATTERY", "Battery", Color.DarkCyan)
        {
            this.batteryPercentSensor = batteryPercentSensor;
            this.batteryDischargeRateSensor = batteryDischargeRateSensor;
            this.batteryChargeRateSensor = batteryChargeRateSensor;
            this.batteryRemainTimeSensor = batteryRemainTimeSensor;
            this.getTimeToFull = getTimeToFull;
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

            // Show time remaining (discharge) or time to full (charging)
            bool isCharging = batteryChargeRateSensor.Value > 0;

            if (isCharging && getTimeToFull != null)
            {
                // When charging, show time to full
                float timeToFull = getTimeToFull();
                if (timeToFull > 0)
                {
                    var hours = Math.Floor(timeToFull / MathConstants.SECONDS_PER_HOUR);
                    var minutes = (timeToFull - hours * MathConstants.SECONDS_PER_HOUR) / MathConstants.SECONDS_PER_MINUTE;
                    osdItems.Add(new OSDItemValue((float)hours, "H", OSDValueType.None));
                    osdItems.Add(new OSDItemValue((float)minutes, "M", "~", OSDValueType.None)); // ~ indicates estimate to full
                }
                else if (timeToFull == 0)
                {
                    // Fully charged
                    osdItems.Add(new OSDItemValue(0, "", "FULL", OSDValueType.None));
                }
            }
            else if (batteryRemainTimeSensor.Value > 0)
            {
                // When discharging, show time remaining
                var hours = Math.Floor(batteryRemainTimeSensor.Value / MathConstants.SECONDS_PER_HOUR);
                var minutes = (batteryRemainTimeSensor.Value - hours * MathConstants.SECONDS_PER_HOUR) / MathConstants.SECONDS_PER_MINUTE;
                osdItems.Add(new OSDItemValue((float)hours, "H", OSDValueType.None));
                osdItems.Add(new OSDItemValue((float)minutes, "M", OSDValueType.None));
            }

            return osdItems;
        }
    }
}
