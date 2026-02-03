using Shared.Constants;
using System;
using System.Collections.Generic;
using System.Drawing;
using Windows.System.Power;
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

        public OSDItemBattery(HardwareSensor batteryPercentSensor, HardwareSensor batteryDischargeRateSensor, HardwareSensor batteryChargeRateSensor, HardwareSensor batteryRemainTimeSensor, Func<float> getTimeToFull = null) : base("BATT", "Battery", Color.DarkCyan)
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

            bool isDischarging = batteryDischargeRateSensor.Value > 0;
            bool isCharging = batteryChargeRateSensor.Value > 0;

            if (isDischarging)
            {
                osdItems.Add(new OSDItemValue(batteryDischargeRateSensor.Value, "W/H", "-", OSDValueType.Wattage));
            }
            else if (isCharging)
            {
                osdItems.Add(new OSDItemValue(batteryChargeRateSensor.Value, "W/H", "+", OSDValueType.None));
            }
            else
            {
                // Not charging or discharging - check if on AC power
                try
                {
                    var powerSupply = PowerManager.PowerSupplyStatus;
                    if (powerSupply == PowerSupplyStatus.Adequate || powerSupply == PowerSupplyStatus.Inadequate)
                    {
                        // On AC power but not actively charging (battery full or trickle charging)
                        osdItems.Add(new OSDItemValue(0, "", "AC", OSDValueType.None));
                    }
                }
                catch
                {
                    // Fallback if PowerManager unavailable
                }
            }

            // Show time remaining (discharge) or time to full (charging)
            if (isCharging && getTimeToFull != null)
            {
                // When charging, show time to full
                float timeToFull = getTimeToFull();
                if (timeToFull > 0)
                {
                    var hours = Math.Floor(timeToFull / MathConstants.SECONDS_PER_HOUR);
                    var minutes = (timeToFull - hours * MathConstants.SECONDS_PER_HOUR) / MathConstants.SECONDS_PER_MINUTE;
                    if (hours > 0)
                    {
                        osdItems.Add(new OSDItemValue((float)hours, "H", OSDValueType.None));
                    }
                    osdItems.Add(new OSDItemValue((float)minutes, "M", "~", OSDValueType.None)); // ~ indicates estimate to full
                }
            }
            else if (isDischarging && batteryRemainTimeSensor.Value > 0)
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
