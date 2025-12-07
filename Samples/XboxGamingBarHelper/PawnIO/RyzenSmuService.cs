// RyzenSMU Service via PawnIO
// Provides AMD SMU access for TDP control without WinRing0
// Compatible with anti-cheat systems (EAC, BattlEye)

using System;
using System.IO;
using System.Reflection;
using NLog;

namespace XboxGamingBarHelper.PawnIO
{
    /// <summary>
    /// AMD SMU response status codes.
    /// </summary>
    public enum SmuStatus : uint
    {
        OK = 0x01,
        Failed = 0xFF,
        UnknownCmd = 0xFE,
        CmdRejectedPrereq = 0xFD,
        CmdRejectedBusy = 0xFC
    }

    /// <summary>
    /// AMD CPU codenames supported by RyzenSMU module.
    /// </summary>
    public enum CpuCodeName : uint
    {
        Unknown = 0,
        SummitRidge = 1,
        Threadripper = 2,
        RavenRidge = 3,
        PinnacleRidge = 4,
        RavenRidge2 = 5,
        Picasso = 5,
        Dali = 5,
        Matisse = 6,
        Renoir = 7,
        VanGogh = 8,
        Vermeer = 9,
        Cezanne = 10,
        Lucienne = 10,
        Milan = 11,
        Chagall = 12,
        Rembrandt = 13,
        Mendocino = 14,
        Raphael = 15,
        DragonRange = 16,
        PhoenixPoint = 17,
        PhoenixPoint2 = 18,
        HawkPoint = 19,
        GraniteRidge = 20,
        StrixPoint = 21,
        StrixPoint2 = 22,
        Krackan = 23
    }

    /// <summary>
    /// Common SMU command IDs for TDP control.
    /// These vary by CPU family - these are for Renoir/Cezanne/Phoenix.
    /// </summary>
    public static class SmuCommands
    {
        // Renoir/Lucienne/Cezanne APU commands (Family 17h/19h Mobile)
        public const uint RENOIR_SET_STAPM_LIMIT = 0x14;
        public const uint RENOIR_SET_FAST_LIMIT = 0x15;
        public const uint RENOIR_SET_SLOW_LIMIT = 0x16;
        public const uint RENOIR_SET_SLOW_TIME = 0x17;
        public const uint RENOIR_SET_STAPM_TIME = 0x18;
        public const uint RENOIR_SET_TCTL_TEMP = 0x19;
        public const uint RENOIR_SET_APU_SLOW_LIMIT = 0x21;

        // Phoenix/Hawk Point APU commands (Family 19h Model 70+)
        public const uint PHOENIX_SET_STAPM_LIMIT = 0x14;
        public const uint PHOENIX_SET_FAST_LIMIT = 0x15;
        public const uint PHOENIX_SET_SLOW_LIMIT = 0x16;
        public const uint PHOENIX_SET_APU_SLOW_LIMIT = 0x21;

        // Strix Point commands (Family 1Ah)
        public const uint STRIX_SET_STAPM_LIMIT = 0x14;
        public const uint STRIX_SET_FAST_LIMIT = 0x15;
        public const uint STRIX_SET_SLOW_LIMIT = 0x16;
    }

    /// <summary>
    /// Service for AMD SMU communication via PawnIO RyzenSMU module.
    /// Provides TDP control functionality similar to RyzenAdj but without WinRing0.
    /// </summary>
    public class RyzenSmuService : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly PawnIOWrapper _pawnIO;
        private bool _disposed;
        private bool _initialized;
        private CpuCodeName _cpuCodeName;
        private uint _smuVersion;

        /// <summary>
        /// Gets whether the service is initialized and ready.
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// Gets the detected CPU codename.
        /// </summary>
        public CpuCodeName CpuCodeName => _cpuCodeName;

        /// <summary>
        /// Gets the SMU firmware version.
        /// </summary>
        public uint SmuVersion => _smuVersion;

        public RyzenSmuService()
        {
            _pawnIO = new PawnIOWrapper();
        }

        /// <summary>
        /// Initializes the RyzenSMU service.
        /// Connects to PawnIO driver and loads the RyzenSMU module.
        /// </summary>
        /// <param name="ryzenSmuModulePath">Path to the RyzenSMU.amx module file.</param>
        /// <returns>True if initialization successful.</returns>
        public bool Initialize(string ryzenSmuModulePath = null)
        {
            if (_initialized)
                return true;

            try
            {
                Logger.Info("Initializing RyzenSMU service via PawnIO...");

                // Connect to PawnIO driver
                if (!_pawnIO.Connect())
                {
                    Logger.Error("Failed to connect to PawnIO driver. Is PawnIO installed?");
                    return false;
                }

                // Get and log version
                var version = _pawnIO.GetVersion();
                if (version.HasValue)
                {
                    Logger.Info($"PawnIO driver version: {version.Value.Major}.{version.Value.Minor}.{version.Value.Patch}");
                }

                // Load RyzenSMU module
                bool moduleLoaded = false;

                if (!string.IsNullOrEmpty(ryzenSmuModulePath))
                {
                    // Use explicitly provided path
                    if (_pawnIO.LoadModule(ryzenSmuModulePath))
                    {
                        moduleLoaded = true;
                    }
                    else
                    {
                        Logger.Error("Failed to load RyzenSMU module from specified file");
                    }
                }

                // Try embedded resource first (like ZenStates-Core)
                if (!moduleLoaded)
                {
                    Logger.Info("Attempting to load RyzenSMU module from embedded resource...");
                    const string embeddedResourceName = "XboxGamingBarHelper.Resources.PawnIO.RyzenSMU.bin";
                    if (_pawnIO.LoadModuleFromResource(Assembly.GetExecutingAssembly(), embeddedResourceName))
                    {
                        moduleLoaded = true;
                        Logger.Info("Successfully loaded RyzenSMU module from embedded resource");
                    }
                    else
                    {
                        Logger.Warn("Failed to load embedded resource, will try file paths...");
                    }
                }

                // Fallback: try to find the module in common file locations
                if (!moduleLoaded)
                {
                    string[] searchPaths = new[]
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RyzenSMU.bin"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "RyzenSMU.bin"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "Modules", "RyzenSMU.bin"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "release_0_2_1", "RyzenSMU.bin"),
                    };

                    Logger.Info($"Searching for RyzenSMU module in {searchPaths.Length} file locations...");
                    foreach (var path in searchPaths)
                    {
                        if (File.Exists(path))
                        {
                            Logger.Info($"Found RyzenSMU module at: {path}");
                            if (_pawnIO.LoadModule(path))
                            {
                                moduleLoaded = true;
                                break;
                            }
                            else
                            {
                                Logger.Warn($"Module found but failed to load from: {path}");
                            }
                        }
                    }
                }

                if (!moduleLoaded)
                {
                    Logger.Error("RyzenSMU module could not be loaded from embedded resource or any file location");
                    return false;
                }

                // Get CPU codename
                if (!GetCodeName(out _cpuCodeName))
                {
                    Logger.Warn("Failed to get CPU codename, but continuing...");
                }
                else
                {
                    Logger.Info($"Detected CPU: {_cpuCodeName}");
                }

                // Get SMU version
                if (!GetSmuVersion(out _smuVersion))
                {
                    Logger.Warn("Failed to get SMU version, but continuing...");
                }
                else
                {
                    Logger.Info($"SMU version: 0x{_smuVersion:X8}");
                }

                _initialized = true;
                Logger.Info("RyzenSMU service initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception initializing RyzenSMU service: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the CPU codename.
        /// </summary>
        public bool GetCodeName(out CpuCodeName codeName)
        {
            codeName = CpuCodeName.Unknown;

            ulong[] output = new ulong[1];
            if (_pawnIO.ExecuteFunction("ioctl_get_code_name", null, output))
            {
                codeName = (CpuCodeName)output[0];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the SMU firmware version.
        /// </summary>
        public bool GetSmuVersion(out uint version)
        {
            version = 0;

            ulong[] output = new ulong[1];
            if (_pawnIO.ExecuteFunction("ioctl_get_smu_version", null, output))
            {
                version = (uint)output[0];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sends a raw SMU command.
        /// </summary>
        /// <param name="command">SMU command ID.</param>
        /// <param name="args">Up to 6 command arguments.</param>
        /// <param name="response">Response arguments (6 values).</param>
        /// <returns>SMU status code.</returns>
        public SmuStatus SendCommand(uint command, uint[] args, out uint[] response)
        {
            response = new uint[6];

            if (!_initialized)
            {
                Logger.Error("RyzenSMU service not initialized");
                return SmuStatus.Failed;
            }

            try
            {
                // Input: command + 6 args = 7 values
                ulong[] input = new ulong[7];
                input[0] = command;
                for (int i = 0; i < 6 && args != null && i < args.Length; i++)
                {
                    input[i + 1] = args[i];
                }

                // Output: status + 6 response args = 7 values (we get 6 back per the module)
                ulong[] output = new ulong[6];

                if (_pawnIO.ExecuteFunction("ioctl_send_smu_command", input, output))
                {
                    // First output is status, rest are response args
                    // Actually looking at the module, output is the 6 response args
                    // Status is returned differently - let's check by trying
                    for (int i = 0; i < 6; i++)
                    {
                        response[i] = (uint)output[i];
                    }

                    Logger.Debug($"SMU command 0x{command:X2} executed. Response: [{string.Join(", ", response)}]");
                    return SmuStatus.OK;
                }
                else
                {
                    Logger.Error($"Failed to execute SMU command 0x{command:X2}");
                    return SmuStatus.Failed;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception sending SMU command: {ex.Message}");
                return SmuStatus.Failed;
            }
        }

        /// <summary>
        /// Sets the STAPM (Skin Temperature Aware Power Management) limit in milliwatts.
        /// </summary>
        /// <param name="limitMw">Power limit in milliwatts (e.g., 25000 for 25W).</param>
        public bool SetStapmLimit(uint limitMw)
        {
            uint cmdId = GetSetStapmCommand();
            if (cmdId == 0)
            {
                Logger.Error("Unknown CPU, cannot set STAPM limit");
                return false;
            }

            var status = SendCommand(cmdId, new uint[] { limitMw }, out _);
            return status == SmuStatus.OK;
        }

        /// <summary>
        /// Sets the Fast (PPT Fast) limit in milliwatts.
        /// </summary>
        /// <param name="limitMw">Power limit in milliwatts.</param>
        public bool SetFastLimit(uint limitMw)
        {
            uint cmdId = GetSetFastCommand();
            if (cmdId == 0)
            {
                Logger.Error("Unknown CPU, cannot set Fast limit");
                return false;
            }

            var status = SendCommand(cmdId, new uint[] { limitMw }, out _);
            return status == SmuStatus.OK;
        }

        /// <summary>
        /// Sets the Slow (PPT Slow) limit in milliwatts.
        /// </summary>
        /// <param name="limitMw">Power limit in milliwatts.</param>
        public bool SetSlowLimit(uint limitMw)
        {
            uint cmdId = GetSetSlowCommand();
            if (cmdId == 0)
            {
                Logger.Error("Unknown CPU, cannot set Slow limit");
                return false;
            }

            var status = SendCommand(cmdId, new uint[] { limitMw }, out _);
            return status == SmuStatus.OK;
        }

        /// <summary>
        /// Sets all TDP limits at once (STAPM, Fast, Slow) in watts.
        /// </summary>
        /// <param name="stapmWatts">STAPM limit in watts.</param>
        /// <param name="fastWatts">Fast/SPPL limit in watts.</param>
        /// <param name="slowWatts">Slow/SPL limit in watts.</param>
        public bool SetAllLimits(int stapmWatts, int fastWatts, int slowWatts)
        {
            Logger.Info($"Setting TDP limits via PawnIO: STAPM={stapmWatts}W, Fast={fastWatts}W, Slow={slowWatts}W");

            bool success = true;

            // Convert to milliwatts
            success &= SetStapmLimit((uint)(stapmWatts * 1000));
            success &= SetFastLimit((uint)(fastWatts * 1000));
            success &= SetSlowLimit((uint)(slowWatts * 1000));

            if (success)
            {
                Logger.Info("TDP limits set successfully");
            }
            else
            {
                Logger.Error("Failed to set one or more TDP limits");
            }

            return success;
        }

        /// <summary>
        /// Reads SMU register value.
        /// </summary>
        public bool ReadSmuRegister(uint address, out uint value)
        {
            value = 0;

            ulong[] input = new ulong[] { address };
            ulong[] output = new ulong[1];

            if (_pawnIO.ExecuteFunction("ioctl_read_smu_register", input, output))
            {
                value = (uint)output[0];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Writes SMU register value.
        /// </summary>
        public bool WriteSmuRegister(uint address, uint value)
        {
            ulong[] input = new ulong[] { address, value };

            return _pawnIO.ExecuteFunction("ioctl_write_smu_register", input, null);
        }

        // Helper methods to get correct command IDs based on CPU
        // Note: Lucienne = Cezanne = 10, Picasso = Dali = RavenRidge2 = 5 (same enum values)
        private uint GetSetStapmCommand()
        {
            switch (_cpuCodeName)
            {
                case CpuCodeName.Renoir:
                case CpuCodeName.Cezanne: // Also covers Lucienne (same value = 10)
                    return SmuCommands.RENOIR_SET_STAPM_LIMIT;
                case CpuCodeName.PhoenixPoint:
                case CpuCodeName.PhoenixPoint2:
                case CpuCodeName.HawkPoint:
                    return SmuCommands.PHOENIX_SET_STAPM_LIMIT;
                case CpuCodeName.StrixPoint:
                case CpuCodeName.StrixPoint2:
                    return SmuCommands.STRIX_SET_STAPM_LIMIT;
                case CpuCodeName.VanGogh: // Steam Deck
                case CpuCodeName.Rembrandt:
                case CpuCodeName.Mendocino:
                    return SmuCommands.RENOIR_SET_STAPM_LIMIT;
                default:
                    return SmuCommands.RENOIR_SET_STAPM_LIMIT; // Default fallback
            }
        }

        private uint GetSetFastCommand()
        {
            switch (_cpuCodeName)
            {
                case CpuCodeName.Renoir:
                case CpuCodeName.Cezanne: // Also covers Lucienne (same value = 10)
                    return SmuCommands.RENOIR_SET_FAST_LIMIT;
                case CpuCodeName.PhoenixPoint:
                case CpuCodeName.PhoenixPoint2:
                case CpuCodeName.HawkPoint:
                    return SmuCommands.PHOENIX_SET_FAST_LIMIT;
                case CpuCodeName.StrixPoint:
                case CpuCodeName.StrixPoint2:
                    return SmuCommands.STRIX_SET_FAST_LIMIT;
                case CpuCodeName.VanGogh:
                case CpuCodeName.Rembrandt:
                case CpuCodeName.Mendocino:
                    return SmuCommands.RENOIR_SET_FAST_LIMIT;
                default:
                    return SmuCommands.RENOIR_SET_FAST_LIMIT;
            }
        }

        private uint GetSetSlowCommand()
        {
            switch (_cpuCodeName)
            {
                case CpuCodeName.Renoir:
                case CpuCodeName.Cezanne: // Also covers Lucienne (same value = 10)
                    return SmuCommands.RENOIR_SET_SLOW_LIMIT;
                case CpuCodeName.PhoenixPoint:
                case CpuCodeName.PhoenixPoint2:
                case CpuCodeName.HawkPoint:
                    return SmuCommands.PHOENIX_SET_SLOW_LIMIT;
                case CpuCodeName.StrixPoint:
                case CpuCodeName.StrixPoint2:
                    return SmuCommands.STRIX_SET_SLOW_LIMIT;
                case CpuCodeName.VanGogh:
                case CpuCodeName.Rembrandt:
                case CpuCodeName.Mendocino:
                    return SmuCommands.RENOIR_SET_SLOW_LIMIT;
                default:
                    return SmuCommands.RENOIR_SET_SLOW_LIMIT;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _pawnIO?.Dispose();
                }
                _initialized = false;
                _disposed = true;
            }
        }

        ~RyzenSmuService()
        {
            Dispose(false);
        }
    }
}
