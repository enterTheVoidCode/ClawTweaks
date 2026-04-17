using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NLog;
using Shared.Data;

namespace XboxGamingBarHelper.DefaultGameProfiles
{
    /// <summary>
    /// Detects Legion Go hardware variant using CPUID for profile selection.
    /// Maps to Microsoft Default Game Profile hardware models: OMNI (Z1 Extreme), HORSEM4N (Z2 Extreme).
    /// </summary>
    internal static class CpuDetector
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // CPU Model IDs from CPUID (Family_Model format for AMD APUs)
        // These are extracted from EAX register after CPUID with EAX=1
        // Format: ((Family + ExtFamily) << 8) | ((Model << 4) | ExtModel)

        /// <summary>Phoenix (Ryzen 7040 series / Z1 Extreme) - used in Legion Go 1 and Legion Go S</summary>
        private const uint PHOENIX_FAMILY_MODEL = 0x00A70F41;  // Family 19h, Model 74h (Phoenix)

        /// <summary>Hawk Point (Ryzen 8040 series / Z1 Extreme refresh) - also maps to OMNI</summary>
        private const uint HAWKPOINT_FAMILY_MODEL = 0x00A70F50; // Family 19h, Model 75h

        /// <summary>Strix Point (Z2 Extreme) - used in Legion Go 2</summary>
        private const uint STRIXPOINT_FAMILY_MODEL = 0x00B60F00; // Family 1Ah, Model 24h

        // Simplified CPU model masks for comparison
        // We check the Family+ExtFamily and Model+ExtModel portion
        private const uint Z1_EXTREME_PHOENIX = 0x00A70000;   // Phoenix base
        private const uint Z2_EXTREME_STRIX = 0x00B60000;     // Strix Point base

        [DllImport("kernel32.dll")]
        private static extern void GetNativeSystemInfo(out SYSTEM_INFO lpSystemInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_INFO
        {
            public ushort wProcessorArchitecture;
            public ushort wReserved;
            public uint dwPageSize;
            public IntPtr lpMinimumApplicationAddress;
            public IntPtr lpMaximumApplicationAddress;
            public IntPtr dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
            public uint dwProcessorType;
            public uint dwAllocationGranularity;
            public ushort wProcessorLevel;
            public ushort wProcessorRevision;
        }

        /// <summary>
        /// Detects Legion Go hardware variant by checking CPU family and model.
        /// </summary>
        public static LegionGoVariant DetectVariant()
        {
            try
            {
                // First, try to get CPU info from WMI (more reliable on Windows)
                var variant = DetectVariantFromWMI();
                if (variant != LegionGoVariant.Unknown)
                {
                    Logger.Info($"CPU variant detected via WMI: {variant}");
                    return variant;
                }

                // Fallback to processor level from system info
                variant = DetectVariantFromSystemInfo();
                if (variant != LegionGoVariant.Unknown)
                {
                    Logger.Info($"CPU variant detected via SystemInfo: {variant}");
                    return variant;
                }

                Logger.Warn("Could not determine CPU variant - using Unknown");
                return LegionGoVariant.Unknown;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to detect CPU variant: {ex.Message}");
                return LegionGoVariant.Unknown;
            }
        }

        /// <summary>
        /// Detects CPU variant using WMI Win32_Processor query.
        /// </summary>
        private static LegionGoVariant DetectVariantFromWMI()
        {
            // WMI queries can hang - use timeout
            const int timeoutMs = 5000;
            var wmiTask = Task.Run(() =>
            {
                try
                {
                    using (var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, Family FROM Win32_Processor"))
                    using (var results = searcher.Get())
                    {
                        foreach (var obj in results)
                        {
                            var cpuName = obj["Name"]?.ToString() ?? "";
                            Logger.Info($"Detected CPU: {cpuName}");

                            // Check for specific AMD APU names
                            var cpuNameUpper = cpuName.ToUpperInvariant();

                            // Z2 Extreme (Strix Point) - Legion Go 2
                            if (cpuNameUpper.Contains("Z2") && cpuNameUpper.Contains("EXTREME"))
                            {
                                return LegionGoVariant.Z2Extreme;
                            }

                            // Strix Point based CPUs
                            if (cpuNameUpper.Contains("RYZEN") &&
                                (cpuNameUpper.Contains("AI 9 HX 370") ||
                                 cpuNameUpper.Contains("AI 9 HX 375") ||
                                 cpuNameUpper.Contains("9955HX")))
                            {
                                return LegionGoVariant.Z2Extreme;
                            }

                            // Z1 Extreme (Phoenix/Hawk Point) - Legion Go 1 / Legion Go S
                            if (cpuNameUpper.Contains("Z1") && cpuNameUpper.Contains("EXTREME"))
                            {
                                return LegionGoVariant.Z1Extreme;
                            }

                            // Phoenix based CPUs (Ryzen 7040 series)
                            if (cpuNameUpper.Contains("RYZEN") &&
                                (cpuNameUpper.Contains("7840") ||
                                 cpuNameUpper.Contains("7640") ||
                                 cpuNameUpper.Contains("7540")))
                            {
                                return LegionGoVariant.Z1Extreme;
                            }

                            // Hawk Point based CPUs (Ryzen 8040 series)
                            if (cpuNameUpper.Contains("RYZEN") &&
                                (cpuNameUpper.Contains("8840") ||
                                 cpuNameUpper.Contains("8640") ||
                                 cpuNameUpper.Contains("8540")))
                            {
                                return LegionGoVariant.Z1Extreme;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"WMI CPU detection failed: {ex.Message}");
                }
                return LegionGoVariant.Unknown;
            });

            if (wmiTask.Wait(timeoutMs))
            {
                return wmiTask.Result;
            }
            else
            {
                Logger.Warn($"WMI CPU detection timed out after {timeoutMs}ms");
                return LegionGoVariant.Unknown;
            }
        }

        /// <summary>
        /// Detects CPU variant using GetNativeSystemInfo (fallback method).
        /// </summary>
        private static LegionGoVariant DetectVariantFromSystemInfo()
        {
            try
            {
                GetNativeSystemInfo(out var sysInfo);
                var processorLevel = sysInfo.wProcessorLevel;
                var processorRevision = sysInfo.wProcessorRevision;

                Logger.Info($"ProcessorLevel: {processorLevel}, ProcessorRevision: {processorRevision:X4}");

                // AMD Family 19h = Phoenix/Hawk Point (Z1 Extreme)
                // AMD Family 1Ah (26) = Strix Point (Z2 Extreme)
                if (processorLevel == 25) // Family 19h
                {
                    return LegionGoVariant.Z1Extreme;
                }
                else if (processorLevel == 26) // Family 1Ah
                {
                    return LegionGoVariant.Z2Extreme;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"SystemInfo CPU detection failed: {ex.Message}");
            }

            return LegionGoVariant.Unknown;
        }

        /// <summary>
        /// Gets the Microsoft Default Game Profile hardware key for the detected variant.
        /// </summary>
        /// <param name="variant">The detected Legion Go variant.</param>
        /// <returns>Profile key: "OMNI" for Z1, "HORSEM4N" for Z2, null for unknown.</returns>
        public static string GetProfileKey(LegionGoVariant variant)
        {
            return variant switch
            {
                LegionGoVariant.Z1Extreme => "OMNI",
                LegionGoVariant.Z2Extreme => "HORSEM4N",
                _ => null
            };
        }

        /// <summary>
        /// Gets a display name for the hardware variant.
        /// </summary>
        public static string GetVariantDisplayName(LegionGoVariant variant)
        {
            return variant switch
            {
                LegionGoVariant.Z1Extreme => "Z1 Extreme (OMNI)",
                LegionGoVariant.Z2Extreme => "Z2 Extreme (HORSEM4N)",
                _ => "Unknown"
            };
        }
    }
}
