using LibreHardwareMonitor.Hardware;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Performance
{
    internal class PerformanceManager : Manager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private Computer computer;
        private IVisitor updateVisitor;
        private IntPtr ryzenAdjHandle;

        #region CPU
        [HardwareSensor("CPU Total", HardwareType.Cpu, SensorType.Load)]
        private ISensor cpuUsage;
        public ISensor CPUUsage => cpuUsage;

        [HardwareSensor("Core #1", HardwareType.Cpu, SensorType.Clock)]
        private ISensor cpuClock;
        public ISensor CPUClock => cpuClock;

        [HardwareSensor("Package", HardwareType.Cpu, SensorType.Power)]
        private ISensor cpuWattage;
        public ISensor CPUWattage => cpuWattage;

        [HardwareSensor("Core (Tctl/Tdie)", HardwareType.Cpu, SensorType.Temperature)]
        private ISensor cpuTemperature;
        public ISensor CPUTemperature => cpuTemperature;
        #endregion

        #region GPU
        [HardwareSensor("GPU Core", HardwareType.GpuAmd, SensorType.Load)]
        private ISensor gpuUsage;
        public ISensor GPUUsage => gpuUsage;

        [HardwareSensor("GPU Core", HardwareType.GpuAmd, SensorType.Clock)]
        private ISensor gpuClock;
        public ISensor GPUClock => gpuClock;

        [HardwareSensor("GPU Core", HardwareType.GpuAmd, SensorType.Power)]
        private ISensor gpuWattage;
        public ISensor GPUWattage => gpuWattage;

        [HardwareSensor("GPU VR SoC", HardwareType.GpuAmd, SensorType.Temperature)]
        private ISensor gpuTemperature;
        public ISensor GPUTemperature => gpuTemperature;
        #endregion

        #region Memory
        [HardwareSensor("Memory", HardwareType.Memory, SensorType.Load)]
        private ISensor memoryUsage;
        public ISensor MemoryUsage => memoryUsage;

        [HardwareSensor("Memory Used", HardwareType.Memory, SensorType.Data)]
        private ISensor memoryUsed;
        public ISensor MemoryUsed => memoryUsed;
        #endregion

        #region Battery
        [HardwareSensor("Charge Level", HardwareType.Battery, SensorType.Level)]
        private ISensor batteryPercent;
        public ISensor BatteryPercent => batteryPercent;

        [HardwareSensor("Remaining Time (Estimated)", HardwareType.Battery, SensorType.TimeSpan)]
        private ISensor batteryRemainTime;
        public ISensor BatteryRemainTime => batteryRemainTime;
        // Discharge Rate
        #endregion

        private TDPProperty tdp;
        public TDPProperty TDP
        {
            get { return tdp; }
        }

        internal PerformanceManager(AppServiceConnection connection) : base(connection)
        {
            // Initialize the computer sensors
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,
                IsBatteryEnabled = true,
            };
            updateVisitor = new UpdateVisitor();

            computer.Open();
            computer.Accept(updateVisitor);

            // Initialize sensor fields
            Dictionary<(string Name, HardwareType HW, SensorType ST), FieldInfo> hardwareSensorFields = new Dictionary<(string Name, HardwareType HW, SensorType ST), FieldInfo>();
            foreach (var field in typeof(PerformanceManager).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                var hardwareSensorAttribute = field.GetCustomAttributes(typeof(HardwareSensorAttribute), inherit: true)
                                .Cast<HardwareSensorAttribute>()
                                .FirstOrDefault();
                if (hardwareSensorAttribute == null) continue;

                hardwareSensorFields.Add((hardwareSensorAttribute.Name, hardwareSensorAttribute.HardwareType, hardwareSensorAttribute.SensorType), field);
            }

            foreach (IHardware hardware in computer.Hardware)
            {
                foreach (ISensor sensor in hardware.Sensors)
                {
                    if (hardwareSensorFields.TryGetValue((sensor.Name, hardware.HardwareType, sensor.SensorType), out FieldInfo fieldInfo))
                    {
                        fieldInfo.SetValue(this, sensor);
                        // Logger.Info("Found hardware Sensor: {0}, value: {1}, type: {2}", sensor.Name, sensor.Value, sensor.SensorType.ToString());
                    }
                }
            }

            ryzenAdjHandle = RyzenAdj.init_ryzenadj();
            var initialTDP = 25;
            if (ryzenAdjHandle == IntPtr.Zero)
            {
                Logger.Error("Failed to initialize RyzenAdj");
            }
            else
            {
                RyzenAdj.refresh_table(ryzenAdjHandle);
                // RyzenAdj.set_fast_limit(ryzenAdjHandle, 30000);
                initialTDP = (int)RyzenAdj.get_fast_limit(ryzenAdjHandle);
                Logger.Info($"RyzenAdj initialized successfully at {initialTDP}W");
            }

            tdp = new TDPProperty(initialTDP, null, this);
        }

        public override void Update()
        {
            base.Update();

            if (computer == null)
                return;

            computer.Accept(updateVisitor);
        }

        public int GetTDP()
        {
            if (ryzenAdjHandle == IntPtr.Zero)
            {
                Logger.Info("RyzenAdj not initialized");
                return 10;
            }

            RyzenAdj.refresh_table(ryzenAdjHandle);
            return (int)RyzenAdj.get_fast_limit(ryzenAdjHandle);
        }

        public void SetTDP(int tdp)
        {
            if (ryzenAdjHandle == IntPtr.Zero)
            {
                Logger.Info("RyzenAdj not initialized");
                return;
            }
            //RyzenAdj.refresh_table(ryzenAdjHandle);
            RyzenAdj.set_fast_limit(ryzenAdjHandle, (uint)(tdp * 1000));
            RyzenAdj.set_slow_limit(ryzenAdjHandle, (uint)(tdp * 1000));
            RyzenAdj.set_stapm_limit(ryzenAdjHandle, (uint)(tdp * 1000));
#if DEBUG
            RyzenAdj.refresh_table(ryzenAdjHandle);
            Logger.Info($"Set TDP to {tdp}, current TDP is {RyzenAdj.get_fast_limit(ryzenAdjHandle)}");
#endif
        }
    }
}
