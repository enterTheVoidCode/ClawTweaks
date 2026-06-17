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
        private HardwareSensor cpuVoltageSensor;

        private bool showClock = false;
        private PerformanceManager performanceManager;

        public OSDItemCPU(HardwareSensor cpuUsageSensor, HardwareSensor cpuClockSensor, HardwareSensor cpuWattageSensor, HardwareSensor cpuTemperatureSensor, HardwareSensor cpuVoltageSensor = null) : base("CPU", "CPU", Color.Turquoise)
        {
            this.cpuWattageSensor = cpuWattageSensor;
            this.cpuUsageSensor = cpuUsageSensor;
            this.cpuClockSensor = cpuClockSensor;
            this.cpuTemperatureSensor = cpuTemperatureSensor;
            this.cpuVoltageSensor = cpuVoltageSensor;
        }

        public void SetShowClock(bool show)
        {
            showClock = show;
        }

        public void SetPerformanceManager(PerformanceManager manager)
        {
            performanceManager = manager;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);

            // usage% | clock (MHz, optional) | temp °C.
            // Wattage (package power) and the PL1/PL2 limits live in the dedicated TDP block now —
            // decoupled from CPU so they aren't shown twice.
            osdItems.Add(new OSDItemValue(cpuUsageSensor.Value, "%", OSDValueType.Percentage));

            if (showClock && cpuClockSensor.Value >= 0)
            {
                osdItems.Add(new OSDItemValue(cpuClockSensor.Value, "MHz", OSDValueType.Speed));
            }

            if (cpuTemperatureSensor.Value >= 0)
                osdItems.Add(new OSDItemValue(cpuTemperatureSensor.Value, "C", OSDValueType.Temperature));

            // Full preset (level 4) only: aggregate Vcore in parentheses right after the temperature,
            // e.g. "(0.72V)". LHM's "CPU Core" voltage sensor.
            // Secondary info → rendered smaller (75%, same ratio as the FPS "ms"), reset with <S>.
            if (osdLevel == 4 && cpuVoltageSensor != null && cpuVoltageSensor.Value >= 0)
                osdItems.Add(new OSDItemValue(cpuVoltageSensor.Value, "V)<S>", "<S=75>(", OSDValueType.None, 2));

            return osdItems;
        }
    }
}
