using NLog;
using Shared.Constants;
using System;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Windows;

namespace XboxGamingBarHelper.Power
{
    internal class PowerManager : Manager
    {
        private IntPtr ryzenAdjHandle;
        public IntPtr RyzenAdjHandle
        {
            get { return ryzenAdjHandle; }
        }

        private readonly CPUBoostProperty cpuBoost;
        public CPUBoostProperty CPUBoost
        {
            get { return cpuBoost; }
        }

        private readonly CPUEPPProperty cpuEPP;
        public CPUEPPProperty CPUEPP
        {
            get { return cpuEPP; }
        }

        private readonly LimitCPUClockProperty limitCPUClock;
        public LimitCPUClockProperty LimitCPUClock
        {
            get { return limitCPUClock; }
        }

        private readonly CPUClockMaxProperty cpuClockMax;
        public CPUClockMaxProperty CPUClockMax
        {
            get { return cpuClockMax; }
        }

        private readonly OSPowerModeProperty osPowerMode;
        public OSPowerModeProperty OSPowerMode
        {
            get { return osPowerMode; }
        }

        // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
        //private readonly LimitGPUClockProperty limitGPUClock;
        //public LimitGPUClockProperty LimitGPUClock
        //{
        //    get { return limitGPUClock; }
        //}

        //private readonly GPUClockMinProperty gpuClockMin;
        //public GPUClockMinProperty GPUClockMin
        //{
        //    get { return gpuClockMin; }
        //}

        //private readonly GPUClockMaxProperty gpuClockMax;
        //public GPUClockMaxProperty GPUClockMax
        //{
        //    get { return gpuClockMax; }
        //}

        public PowerManager(AppServiceConnection connection, IntPtr ryzenAdjHandle) : base(connection)
        {
            this.ryzenAdjHandle = ryzenAdjHandle;

            Logger.Info($"Check CPU Boost Mode and EPP.");
            cpuBoost = new CPUBoostProperty(GetCpuBoostMode(false), this);
            cpuEPP = new CPUEPPProperty((int)GetEppValue(false), this);
            var initialCPUClockMax = GetCpuFreqLimit(false);
            Logger.Info($"Initial CPU clock limit {initialCPUClockMax}Mhz.");
            limitCPUClock = new LimitCPUClockProperty(initialCPUClockMax != 0, this);
            cpuClockMax = new CPUClockMaxProperty(initialCPUClockMax != 0 ? (int)initialCPUClockMax : CPUConstants.DEFAULT_CPU_CLOCK, this);

            // OS Power Mode
            var initialPowerMode = GetOSPowerMode();
            Logger.Info($"Initial OS Power Mode: {initialPowerMode} (0=Efficiency, 1=Balanced, 2=Performance)");
            osPowerMode = new OSPowerModeProperty(initialPowerMode >= 0 ? initialPowerMode : 1, this);

            // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
            //// Initialize GPU Clock properties
            //limitGPUClock = new LimitGPUClockProperty(false, this);
            //gpuClockMin = new GPUClockMinProperty(200, this);
            //gpuClockMax = new GPUClockMaxProperty(3000, this);
            //Logger.Info($"GPU Clock limiter initialized (disabled by default).");
        }

        public static Guid GetActiveScheme()
        {
            var res = PowrProf.PowerGetActiveScheme(IntPtr.Zero, out IntPtr pGuid);
            if (res != 0)
            {
                Logger.Error("Can't get active power scheme?");
                return Guid.Empty;
            }

            var active = (Guid)Marshal.PtrToStructure(pGuid, typeof(Guid));
            Marshal.FreeHGlobal(pGuid);
            return active;
        }

        public static bool GetCpuBoostMode(bool isAC)
        {
            var scheme = GetActiveScheme();
            var subgroup = PowerGuids.GUID_PROCESSOR_SETTINGS_SUBGROUP;
            var setting = PowerGuids.GUID_PROCESSOR_PERFBOOST_MODE;

            var status = isAC
                ? PowrProf.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out uint result)
                : PowrProf.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out result);

            if (status != 0)
            {
                Logger.Error("Can't get CPU Boost Mode?");
                return false;
            }

            return result != 0;
        }

        public static void SetCpuBoostMode(bool isAC, bool enabled)
        {
            var scheme = GetActiveScheme();
            var subgroup = PowerGuids.GUID_PROCESSOR_SETTINGS_SUBGROUP;
            var setting = PowerGuids.GUID_PROCESSOR_PERFBOOST_MODE;
            uint value = (uint)(enabled ? 2 : 0);
            Logger.Info($"Set CPU Boost to {(enabled ? "Aggressive" : "Disabled")}.");

            var status = isAC ? PowrProf.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value)
                : PowrProf.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value);

            if (status != 0)
            {
                Logger.Error("Can't set CPU Boost Mode??");
                return;
            }

            Logger.Info($"Set CPU Boost {(isAC ? "AC" : "DC")} to {value}.");
            // Apply the updated plan
            PowrProf.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        }

        public static uint GetEppValue(bool isAC)
        {
            Guid scheme = GetActiveScheme();
            Guid subgroup = PowerGuids.GUID_PROCESSOR_SETTINGS_SUBGROUP;
            Guid setting = PowerGuids.GUID_PROCESSOR_EPP;

            uint result;
            uint status = isAC ? 
                PowrProf.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out result)
                : PowrProf.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out result);

            if (status != 0)
            {
                Logger.Error("Can't get EPP value.");
                return 90;
            }

            return result;
        }

        public static void SetEppValue(bool isAC, uint value)
        {
            if (value > 100) value = 100; // clamp to valid range

            Guid scheme = GetActiveScheme();
            Guid subgroup = PowerGuids.GUID_PROCESSOR_SETTINGS_SUBGROUP;
            Guid setting = PowerGuids.GUID_PROCESSOR_EPP;

            uint status = isAC
                ? PowrProf.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value)
                : PowrProf.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value);

            if (status != 0)
            {
                Logger.Error("Can't set EPP value.");
                return;
            }

            Logger.Info($"Set CPU EPP {(isAC ? "AC" : "DC")} to {value}.");
            // Apply changes to the currently active power plan
            PowrProf.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        }

        /// <summary>
        /// Reads the CPU frequency limit (in MHz) for AC or DC mode.
        /// </summary>
        public static uint GetCpuFreqLimit(bool isAC, bool isSecondary = false)
        {
            Guid scheme = GetActiveScheme();
            Guid subgroup = PowerGuids.GUID_PROCESSOR_SETTINGS_SUBGROUP;
            Guid setting = isSecondary
                ? PowerGuids.GUID_PROCESSOR_FREQUENCY_LIMIT1
                : PowerGuids.GUID_PROCESSOR_FREQUENCY_LIMIT;

            uint result;
            uint status = isAC
                ? PowrProf.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out result)
                : PowrProf.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out result);

            if (status != 0)
            {
                Logger.Error("Can't read CPU Clock limit.");
                return 1000;
            }

            return result;
        }

        /// <summary>
        /// Sets the CPU frequency limit (in MHz) for AC or DC mode.
        /// </summary>
        public static void SetCpuFreqLimit(bool isAC, uint mhzValue, bool isSecondary = false)
        {
            Guid scheme = GetActiveScheme();
            Guid subgroup = PowerGuids.GUID_PROCESSOR_SETTINGS_SUBGROUP;
            Guid setting = isSecondary
                ? PowerGuids.GUID_PROCESSOR_FREQUENCY_LIMIT1
                : PowerGuids.GUID_PROCESSOR_FREQUENCY_LIMIT;

            uint status = isAC
                ? PowrProf.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, mhzValue)
                : PowrProf.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, mhzValue);

            if (status != 0)
            {
                Logger.Error("Can't set CPU Clock limit.");
                return;
            }

            Logger.Info($"Set CPU Clock limit {(isAC ? "AC" : "DC")} {(isSecondary ? "secondary" : "primary")} to {mhzValue}MHz");
            PowrProf.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        }

        #region OS Power Mode (Windows 11 Power Slider)

        // Power mode overlay GUIDs
        private static readonly Guid GUID_POWER_SAVER = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        private static readonly Guid GUID_BALANCED = Guid.Empty; // 00000000-0000-0000-0000-000000000000
        private static readonly Guid GUID_HIGH_PERFORMANCE = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        /// <summary>
        /// Gets the current OS power mode.
        /// </summary>
        /// <returns>0 = Best Power Efficiency, 1 = Balanced, 2 = Best Performance, -1 = Unknown/Error</returns>
        public static int GetOSPowerMode()
        {
            try
            {
                uint status = PowrProf.PowerGetEffectiveOverlayScheme(out Guid overlayGuid);
                if (status != 0)
                {
                    Logger.Warn($"PowerGetEffectiveOverlayScheme failed with status {status}");
                    return -1;
                }

                if (overlayGuid == GUID_POWER_SAVER)
                    return 0; // Best Power Efficiency
                else if (overlayGuid == GUID_BALANCED || overlayGuid == Guid.Empty)
                    return 1; // Balanced
                else if (overlayGuid == GUID_HIGH_PERFORMANCE)
                    return 2; // Best Performance
                else
                {
                    Logger.Info($"Unknown power overlay GUID: {overlayGuid}");
                    return 1; // Default to Balanced for unknown
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error getting OS power mode: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Sets the OS power mode.
        /// </summary>
        /// <param name="mode">0 = Best Power Efficiency, 1 = Balanced, 2 = Best Performance</param>
        /// <returns>True if successful</returns>
        public static bool SetOSPowerMode(int mode)
        {
            try
            {
                Guid targetGuid;
                string modeName;

                switch (mode)
                {
                    case 0:
                        targetGuid = GUID_POWER_SAVER;
                        modeName = "Best Power Efficiency";
                        break;
                    case 1:
                        targetGuid = GUID_BALANCED;
                        modeName = "Balanced";
                        break;
                    case 2:
                        targetGuid = GUID_HIGH_PERFORMANCE;
                        modeName = "Best Performance";
                        break;
                    default:
                        Logger.Warn($"Invalid power mode: {mode}");
                        return false;
                }

                uint status = PowrProf.PowerSetActiveOverlayScheme(targetGuid);
                if (status != 0)
                {
                    Logger.Error($"PowerSetActiveOverlayScheme failed with status {status}");
                    return false;
                }

                Logger.Info($"Set OS Power Mode to {modeName} ({targetGuid})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting OS power mode: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
