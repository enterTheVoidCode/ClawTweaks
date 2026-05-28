using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemCPU : OSDItem
    {
        private HardwareSensor cpuUsageSensor;
        private HardwareSensor cpuClockSensor;
        private HardwareSensor cpuWattageSensor;
        private HardwareSensor cpuTemperatureSensor;

        private bool showClock = false;
        private PerformanceManager performanceManager;

        public OSDItemCPU(HardwareSensor cpuUsageSensor, HardwareSensor cpuClockSensor, HardwareSensor cpuWattageSensor, HardwareSensor cpuTemperatureSensor) : base("CPU", "CPU", Color.Turquoise)
        {
            this.cpuWattageSensor = cpuWattageSensor;
            this.cpuUsageSensor = cpuUsageSensor;
            this.cpuClockSensor = cpuClockSensor;
            this.cpuTemperatureSensor = cpuTemperatureSensor;
        }

        public void SetShowClock(bool show)
        {
            showClock = show;
        }

        public void SetPerformanceManager(PerformanceManager manager)
        {
            performanceManager = manager;
        }

        public override string GetOSDString(int osdLevel)
        {
            var baseString = base.GetOSDString(osdLevel);

            // Append subtle PL1/PL2 hint when limits are set (all levels)
            if (performanceManager != null)
            {
                int pl1 = performanceManager.CurrentSPL;
                int pl2 = performanceManager.CurrentFPPT;
                if (pl1 > 0)
                    baseString += BuildPLHint(pl1, pl2);
            }

            return baseString;
        }

        /// <summary>
        /// Builds a PL1/PL2 limit indicator appended after the CPU values.
        /// Rendered at standard font size and full opacity to match the rest of the overlay.
        /// </summary>
        private string BuildPLHint(int pl1, int pl2)
        {
            var tc = GetTextColorWithOpacity();
            string plText = pl2 > 0 ? $"PL1:{pl1}W PL2:{pl2}W" : $"PL1:{pl1}W";
            return $" <C={tc}>({plText})<C>";
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            // IntelGameBar-style order: usage% | clock (MHz, optional) | temp °C | wattage W
            // Skip sensors with value < 0 (N/A) so unavailable sensors are omitted
            osdItems.Add(new OSDItemValue(cpuUsageSensor.Value, "%", OSDValueType.Percentage));

            // Clock shown inline (before temp/wattage) when CPUClock is enabled for the level
            if (showClock && cpuClockSensor.Value >= 0)
            {
                osdItems.Add(new OSDItemValue(cpuClockSensor.Value, "MHz", OSDValueType.Speed));
            }

            if (cpuTemperatureSensor.Value >= 0)
                osdItems.Add(new OSDItemValue(cpuTemperatureSensor.Value, "C", OSDValueType.Temperature));

            if (cpuWattageSensor.Value >= 0)
                osdItems.Add(new OSDItemValue(cpuWattageSensor.Value, "W", OSDValueType.Wattage));

            return osdItems;
        }
    }
}
