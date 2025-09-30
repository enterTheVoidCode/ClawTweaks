using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace XboxGamingBarHelper.Performance
{
    internal static class PerformanceManager
    {
        private static Computer computer;
        private static IVisitor updateVisitor;
        private static IntPtr ryzenAdjHandle;

        #region CPU
        [HardwareSensor("CPU Total", HardwareType.Cpu, SensorType.Load)]
        private static ISensor cpuUsage;
        public static ISensor CPUUsage => cpuUsage;

        [HardwareSensor("Core #1", HardwareType.Cpu, SensorType.Clock)]
        private static ISensor cpuClock;
        public static ISensor CPUClock => cpuClock;

        [HardwareSensor("Package", HardwareType.Cpu, SensorType.Power)]
        private static ISensor cpuWattage;
        public static ISensor CPUWattage => cpuWattage;

        [HardwareSensor("Core (Tctl/Tdie)", HardwareType.Cpu, SensorType.Temperature)]
        private static ISensor cpuTemperature;
        public static ISensor CPUTemperature => cpuTemperature;
        #endregion

        #region GPU
        [HardwareSensor("GPU Core", HardwareType.GpuAmd, SensorType.Load)]
        private static ISensor gpuUsage;
        public static ISensor GPUUsage => gpuUsage;

        [HardwareSensor("GPU Core", HardwareType.GpuAmd, SensorType.Clock)]
        private static ISensor gpuClock;
        public static ISensor GPUClock => gpuClock;

        [HardwareSensor("GPU Core", HardwareType.GpuAmd, SensorType.Power)]
        private static ISensor gpuWattage;
        public static ISensor GPUWattage => gpuWattage;

        [HardwareSensor("GPU VR SoC", HardwareType.GpuAmd, SensorType.Temperature)]
        private static ISensor gpuTemperature;
        public static ISensor GPUTemperature => gpuTemperature;
        #endregion

        #region Memory
        [HardwareSensor("Memory", HardwareType.Memory, SensorType.Load)]
        private static ISensor memoryUsage;
        public static ISensor MemoryUsage => memoryUsage;

        [HardwareSensor("Memory Used", HardwareType.Memory, SensorType.Data)]
        private static ISensor memoryUsed;
        public static ISensor MemoryUsed => memoryUsed;
        #endregion

        #region Battery
        [HardwareSensor("Charge Level", HardwareType.Battery, SensorType.Level)]
        private static ISensor batteryPercent;
        public static ISensor BatteryPercent => batteryPercent;

        [HardwareSensor("Remaining Time (Estimated)", HardwareType.Battery, SensorType.TimeSpan)]
        private static ISensor batteryRemainTime;
        public static ISensor BatteryRemainTime => batteryRemainTime;
        #endregion

        internal static void Initialize()
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
            foreach (var field in typeof(PerformanceManager).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
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
                        fieldInfo.SetValue(null, sensor);
                        // Console.WriteLine("Found hardware Sensor: {0}, value: {1}, type: {2}", sensor.Name, sensor.Value, sensor.SensorType.ToString());
                        Debug.WriteLine("Found hardware Sensor: {0}, value: {1}, type: {2}", sensor.Name, sensor.Value, sensor.SensorType.ToString());
                    }
                }
            }

            ryzenAdjHandle = RyzenAdj.init_ryzenadj();
            if (ryzenAdjHandle == IntPtr.Zero)
            {
                Debug.WriteLine("Failed to initialize RyzenAdj");
            }
            else
            {
                RyzenAdj.refresh_table(ryzenAdjHandle);
                // RyzenAdj.set_fast_limit(ryzenAdjHandle, 30000);
                Debug.WriteLine($"RyzenAdj initialized successfully {RyzenAdj.get_fast_limit(ryzenAdjHandle)} {RyzenAdj.get_slow_limit(ryzenAdjHandle)} {RyzenAdj.get_stapm_limit(ryzenAdjHandle)}");
            }
        }

        internal static void Update()
        {
            if (computer == null)
                return;

            computer.Accept(updateVisitor);
        }

        public static int GetTDP()
        {
            if (ryzenAdjHandle == IntPtr.Zero)
            {
                Debug.WriteLine("RyzenAdj not initialized");
                return 10;
            }

            RyzenAdj.refresh_table(ryzenAdjHandle);
            return (int)RyzenAdj.get_fast_limit(ryzenAdjHandle);
        }

        public static void SetTDP(int tdp)
        {
            if (ryzenAdjHandle == IntPtr.Zero)
            {
                Debug.WriteLine("RyzenAdj not initialized");
                return;
            }
            //RyzenAdj.refresh_table(ryzenAdjHandle);
            RyzenAdj.set_fast_limit(ryzenAdjHandle, (uint)(tdp * 1000));
            RyzenAdj.set_slow_limit(ryzenAdjHandle, (uint)(tdp * 1000));
            RyzenAdj.set_stapm_limit(ryzenAdjHandle, (uint)(tdp * 1000));
#if DEBUG
            RyzenAdj.refresh_table(ryzenAdjHandle);
            Debug.WriteLine($"Set TDP to {tdp}, current TDP is {RyzenAdj.get_fast_limit(ryzenAdjHandle)}");
#endif
        }
    }
}
