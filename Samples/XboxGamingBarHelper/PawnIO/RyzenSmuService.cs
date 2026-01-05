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
    /// AMD CPU codenames - values must match RyzenSMU module's CodeName enum order.
    /// </summary>
    public enum CpuCodeName : uint
    {
        Undefined = 0xFFFFFFFF, // -1 in module
        Colfax = 0,
        Renoir = 1,
        Picasso = 2,
        Matisse = 3,
        Threadripper = 4,
        CastlePeak = 5,
        RavenRidge = 6,
        RavenRidge2 = 7,
        SummitRidge = 8,
        PinnacleRidge = 9,
        Rembrandt = 10,
        Vermeer = 11,
        Vangogh = 12,
        Cezanne = 13,
        Milan = 14,
        Dali = 15,
        Raphael = 16,
        GraniteRidge = 17,
        Naples = 18,
        FireFlight = 19,
        Rome = 20,
        Chagall = 21,
        Lucienne = 22,
        Phoenix = 23,
        Phoenix2 = 24,
        Mendocino = 25,
        Genoa = 26,
        StormPeak = 27,
        DragonRange = 28,
        Mero = 29,
        HawkPoint = 30,
        StrixPoint = 31,
        StrixHalo = 32,        // Ryzen AI Max 385/395
        KrackanPoint = 33,
        KrackanPoint2 = 34,
        Z2Extreme = 35,        // Legion Go S (confirmed working)
        Turin = 36,
        TurinD = 37,
        Bergamo = 38,
        ShimadaPeak = 39,
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
            codeName = CpuCodeName.Undefined;

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
        /// CPUs that require MP1 mailbox for TDP commands (Strix Point family and newer).
        /// These use MP1 addresses: CMD=0x3B10928, RSP=0x3B10978, ARGS=0x3B10998
        /// </summary>
        private bool RequiresMp1Mailbox()
        {
            switch (_cpuCodeName)
            {
                case CpuCodeName.HawkPoint:
                case CpuCodeName.StrixPoint:
                case CpuCodeName.StrixHalo:      // Ryzen AI Max 385/395
                case CpuCodeName.KrackanPoint:
                case CpuCodeName.KrackanPoint2:
                case CpuCodeName.Z2Extreme:      // Legion Go S
                    return true;
                default:
                    return false;
            }
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

                Logger.Info($"Sending SMU command 0x{command:X2} with arg: {(args != null && args.Length > 0 ? args[0].ToString() : "none")}");

                if (_pawnIO.ExecuteFunction("ioctl_send_smu_command", input, output))
                {
                    // Copy response data
                    for (int i = 0; i < 6; i++)
                    {
                        response[i] = (uint)output[i];
                    }

                    Logger.Info($"SMU command 0x{command:X2} response: [{response[0]}, {response[1]}, {response[2]}, {response[3]}, {response[4]}, {response[5]}]");

                    // ExecuteFunction returning true means the SMU accepted the command
                    // Response values are data, not status codes (e.g., current limits in mW)
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

        // MP1 mailbox addresses for Strix Point / Z2 Extreme (from RyzenAdj)
        private const uint MP1_ADDR_CMD = 0x3B10928;
        private const uint MP1_ADDR_RSP = 0x3B10978;
        private const uint MP1_ADDR_ARG_BASE = 0x3B10998;
        private const int SMU_RETRIES_MAX = 8096;

        /// <summary>
        /// Sends a raw SMU command via MP1 mailbox (for TDP commands on Strix/Z2E).
        /// Implements the SMU protocol directly using register read/write.
        /// </summary>
        /// <param name="command">SMU command ID.</param>
        /// <param name="args">Up to 6 command arguments.</param>
        /// <param name="response">Response arguments (6 values).</param>
        /// <returns>SMU status code.</returns>
        public SmuStatus SendCommandMp1(uint command, uint[] args, out uint[] response)
        {
            response = new uint[6];

            if (!_initialized)
            {
                Logger.Error("RyzenSMU service not initialized");
                return SmuStatus.Failed;
            }

            try
            {
                Logger.Info($"Sending SMU command 0x{command:X2} via MP1 with arg: {(args != null && args.Length > 0 ? args[0].ToString() : "none")}");

                // Step 1: Wait until the RSP register is non-zero
                uint rspValue = 0;
                for (int i = 0; i < SMU_RETRIES_MAX; i++)
                {
                    if (!ReadSmuRegister(MP1_ADDR_RSP, out rspValue))
                    {
                        Logger.Error("Failed to read MP1 RSP register");
                        return SmuStatus.Failed;
                    }
                    if (rspValue != 0)
                        break;
                }
                if (rspValue == 0)
                {
                    Logger.Error("MP1 SMU busy (RSP stayed 0)");
                    return SmuStatus.CmdRejectedBusy;
                }

                // Step 2: Write zero to the RSP register
                if (!WriteSmuRegister(MP1_ADDR_RSP, 0))
                {
                    Logger.Error("Failed to clear MP1 RSP register");
                    return SmuStatus.Failed;
                }

                // Step 3: Write the arguments into the argument registers
                for (int i = 0; i < 6; i++)
                {
                    uint argValue = (args != null && i < args.Length) ? args[i] : 0;
                    if (!WriteSmuRegister(MP1_ADDR_ARG_BASE + (uint)(i * 4), argValue))
                    {
                        Logger.Error($"Failed to write MP1 arg[{i}]");
                        return SmuStatus.Failed;
                    }
                }

                // Step 4: Write the command to the CMD register
                if (!WriteSmuRegister(MP1_ADDR_CMD, command))
                {
                    Logger.Error("Failed to write MP1 CMD register");
                    return SmuStatus.Failed;
                }

                // Step 5: Wait until the RSP register is non-zero
                rspValue = 0;
                for (int i = 0; i < SMU_RETRIES_MAX; i++)
                {
                    if (!ReadSmuRegister(MP1_ADDR_RSP, out rspValue))
                    {
                        Logger.Error("Failed to read MP1 RSP register (waiting for response)");
                        return SmuStatus.Failed;
                    }
                    if (rspValue != 0)
                        break;
                }
                if (rspValue == 0)
                {
                    Logger.Error("MP1 SMU timeout (RSP stayed 0 after command)");
                    return SmuStatus.Failed;
                }

                // Step 6: Check response status
                if (rspValue != 0x01) // SMU_OK
                {
                    Logger.Warn($"MP1 SMU returned status 0x{rspValue:X2}");
                    return (SmuStatus)rspValue;
                }

                // Step 7: Read back the argument registers
                for (int i = 0; i < 6; i++)
                {
                    if (!ReadSmuRegister(MP1_ADDR_ARG_BASE + (uint)(i * 4), out response[i]))
                    {
                        Logger.Error($"Failed to read MP1 response arg[{i}]");
                        return SmuStatus.Failed;
                    }
                }

                Logger.Info($"SMU MP1 command 0x{command:X2} response: [{response[0]}, {response[1]}, {response[2]}, {response[3]}, {response[4]}, {response[5]}]");

                return SmuStatus.OK;
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception sending SMU MP1 command: {ex.Message}");
                return SmuStatus.Failed;
            }
        }

        /// <summary>
        /// Sends a TDP-related SMU command, automatically using MP1 mailbox for CPUs that require it.
        /// </summary>
        private SmuStatus SendTdpCommand(uint command, uint[] args, out uint[] response)
        {
            if (RequiresMp1Mailbox())
            {
                return SendCommandMp1(command, args, out response);
            }
            else
            {
                return SendCommand(command, args, out response);
            }
        }

        /// <summary>
        /// Sets the STAPM (Skin Temperature Aware Power Management) limit in milliwatts.
        /// </summary>
        /// <param name="limitMw">Power limit in milliwatts (e.g., 25000 for 25W).</param>
        /// <param name="responseValue">Optional: receives the SMU response value (may contain actual applied limit).</param>
        public bool SetStapmLimit(uint limitMw, out uint responseValue)
        {
            responseValue = 0;
            uint cmdId = GetSetStapmCommand();
            if (cmdId == 0)
            {
                Logger.Error("Unknown CPU, cannot set STAPM limit");
                return false;
            }

            var status = SendTdpCommand(cmdId, new uint[] { limitMw }, out uint[] response);
            if (response != null && response.Length > 0)
            {
                responseValue = response[0];
                Logger.Info($"STAPM set command response: [{response[0]}, {response[1]}, {response[2]}, {response[3]}, {response[4]}, {response[5]}]");
            }
            return status == SmuStatus.OK;
        }

        /// <summary>
        /// Sets the STAPM limit (overload without response).
        /// </summary>
        public bool SetStapmLimit(uint limitMw) => SetStapmLimit(limitMw, out _);

        /// <summary>
        /// Sets the Fast (PPT Fast) limit in milliwatts.
        /// </summary>
        /// <param name="limitMw">Power limit in milliwatts.</param>
        /// <param name="responseValue">Optional: receives the SMU response value (may contain actual applied limit).</param>
        public bool SetFastLimit(uint limitMw, out uint responseValue)
        {
            responseValue = 0;
            uint cmdId = GetSetFastCommand();
            if (cmdId == 0)
            {
                Logger.Error("Unknown CPU, cannot set Fast limit");
                return false;
            }

            var status = SendTdpCommand(cmdId, new uint[] { limitMw }, out uint[] response);
            if (response != null && response.Length > 0)
            {
                responseValue = response[0];
                Logger.Info($"Fast set command response: [{response[0]}, {response[1]}, {response[2]}, {response[3]}, {response[4]}, {response[5]}]");
            }
            return status == SmuStatus.OK;
        }

        /// <summary>
        /// Sets the Fast limit (overload without response).
        /// </summary>
        public bool SetFastLimit(uint limitMw) => SetFastLimit(limitMw, out _);

        /// <summary>
        /// Sets the Slow (PPT Slow) limit in milliwatts.
        /// </summary>
        /// <param name="limitMw">Power limit in milliwatts.</param>
        /// <param name="responseValue">Optional: receives the SMU response value (may contain actual applied limit).</param>
        public bool SetSlowLimit(uint limitMw, out uint responseValue)
        {
            responseValue = 0;
            uint cmdId = GetSetSlowCommand();
            if (cmdId == 0)
            {
                Logger.Error("Unknown CPU, cannot set Slow limit");
                return false;
            }

            var status = SendTdpCommand(cmdId, new uint[] { limitMw }, out uint[] response);
            if (response != null && response.Length > 0)
            {
                responseValue = response[0];
                Logger.Info($"Slow set command response: [{response[0]}, {response[1]}, {response[2]}, {response[3]}, {response[4]}, {response[5]}]");
            }
            return status == SmuStatus.OK;
        }

        /// <summary>
        /// Sets the Slow limit (overload without response).
        /// </summary>
        public bool SetSlowLimit(uint limitMw) => SetSlowLimit(limitMw, out _);

        /// <summary>
        /// Sets all TDP limits at once (STAPM, Fast, Slow) in watts.
        /// Returns the SMU response values which may contain actual applied limits.
        /// </summary>
        /// <param name="stapmWatts">STAPM limit in watts.</param>
        /// <param name="fastWatts">Fast/SPPL limit in watts.</param>
        /// <param name="slowWatts">Slow/SPL limit in watts.</param>
        /// <param name="stapmResponse">SMU response for STAPM command (may be in mW).</param>
        /// <param name="fastResponse">SMU response for Fast command (may be in mW).</param>
        /// <param name="slowResponse">SMU response for Slow command (may be in mW).</param>
        public bool SetAllLimits(int stapmWatts, int fastWatts, int slowWatts,
            out uint stapmResponse, out uint fastResponse, out uint slowResponse)
        {
            Logger.Info($"Setting TDP limits via PawnIO: STAPM={stapmWatts}W, Fast={fastWatts}W, Slow={slowWatts}W");

            bool success = true;
            stapmResponse = 0;
            fastResponse = 0;
            slowResponse = 0;

            // Convert to milliwatts and capture responses
            success &= SetStapmLimit((uint)(stapmWatts * 1000), out stapmResponse);
            success &= SetFastLimit((uint)(fastWatts * 1000), out fastResponse);
            success &= SetSlowLimit((uint)(slowWatts * 1000), out slowResponse);

            if (success)
            {
                Logger.Info($"TDP limits set successfully. Responses: STAPM={stapmResponse}, Fast={fastResponse}, Slow={slowResponse}");
            }
            else
            {
                // Z2E: SMU returns current values instead of status codes
                Logger.Warn("PawnIO: SMU commands may not be working for this CPU - check RyzenSMU module Z2E support");
            }

            return success;
        }

        /// <summary>
        /// Sets all TDP limits (overload without responses).
        /// </summary>
        public bool SetAllLimits(int stapmWatts, int fastWatts, int slowWatts)
        {
            return SetAllLimits(stapmWatts, fastWatts, slowWatts, out _, out _, out _);
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
                case CpuCodeName.Lucienne:
                case CpuCodeName.Cezanne:
                    return SmuCommands.RENOIR_SET_STAPM_LIMIT;
                case CpuCodeName.Phoenix:
                case CpuCodeName.Phoenix2:
                case CpuCodeName.HawkPoint:
                    return SmuCommands.PHOENIX_SET_STAPM_LIMIT;
                case CpuCodeName.StrixPoint:
                case CpuCodeName.StrixHalo:
                case CpuCodeName.KrackanPoint:
                case CpuCodeName.KrackanPoint2:
                case CpuCodeName.Z2Extreme:
                    return SmuCommands.STRIX_SET_STAPM_LIMIT;
                case CpuCodeName.Vangogh: // Steam Deck
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
                case CpuCodeName.Lucienne:
                case CpuCodeName.Cezanne:
                    return SmuCommands.RENOIR_SET_FAST_LIMIT;
                case CpuCodeName.Phoenix:
                case CpuCodeName.Phoenix2:
                case CpuCodeName.HawkPoint:
                    return SmuCommands.PHOENIX_SET_FAST_LIMIT;
                case CpuCodeName.StrixPoint:
                case CpuCodeName.StrixHalo:
                case CpuCodeName.KrackanPoint:
                case CpuCodeName.KrackanPoint2:
                case CpuCodeName.Z2Extreme:
                    return SmuCommands.STRIX_SET_FAST_LIMIT;
                case CpuCodeName.Vangogh:
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
                case CpuCodeName.Lucienne:
                case CpuCodeName.Cezanne:
                    return SmuCommands.RENOIR_SET_SLOW_LIMIT;
                case CpuCodeName.Phoenix:
                case CpuCodeName.Phoenix2:
                case CpuCodeName.HawkPoint:
                    return SmuCommands.PHOENIX_SET_SLOW_LIMIT;
                case CpuCodeName.StrixPoint:
                case CpuCodeName.StrixHalo:
                case CpuCodeName.KrackanPoint:
                case CpuCodeName.KrackanPoint2:
                case CpuCodeName.Z2Extreme:
                    return SmuCommands.STRIX_SET_SLOW_LIMIT;
                case CpuCodeName.Vangogh:
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
