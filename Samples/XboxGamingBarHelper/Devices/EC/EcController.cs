using NLog;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace XboxGamingBarHelper.Devices.EC
{
    /// <summary>
    /// Low-level Embedded Controller (EC) communication via Super I/O protocol.
    /// Uses inpoutx64.dll for port I/O access on IT87xx chips (used by GPD devices).
    /// </summary>
    internal class EcController : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Super I/O port addresses
        private const ushort SIO_INDEX_PORT = 0x4E;
        private const ushort SIO_DATA_PORT = 0x4F;

        // Super I/O configuration registers
        private const byte SIO_REG_LDNSEL = 0x07;   // Logical Device Number Select
        private const byte SIO_REG_CHIPID1 = 0x20; // Chip ID byte 1
        private const byte SIO_REG_CHIPID2 = 0x21; // Chip ID byte 2

        // IT87xx EC RAM access registers
        private const byte SIO_LDN_EC = 0x04;      // EC logical device number
        private const byte SIO_REG_ECADDR_MSB = 0x60; // EC address MSB
        private const byte SIO_REG_ECADDR_LSB = 0x61; // EC address LSB
        private const byte SIO_REG_ECDATA = 0x62;     // EC data register

        // Lock for thread safety (EC is a single shared resource)
        private static readonly object _ecLock = new object();

        private bool _disposed = false;
        private bool _isInitialized = false;
        private ushort _chipId = 0;

        // P/Invoke declarations for inpoutx64.dll
        [DllImport("inpoutx64.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern byte DlPortReadPortUchar(ushort port);

        [DllImport("inpoutx64.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern void DlPortWritePortUchar(ushort port, byte value);

        [DllImport("inpoutx64.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int IsInpOutDriverOpen();

        public EcController()
        {
            Logger.Debug("[EC] EcController constructor called");
        }

        /// <summary>
        /// Initializes the EC controller and verifies the driver is available.
        /// </summary>
        /// <returns>True if initialization successful.</returns>
        public bool Initialize()
        {
            if (_isInitialized)
                return true;

            lock (_ecLock)
            {
                try
                {
                    // Check if inpoutx64 driver is loaded
                    int driverOpen = IsInpOutDriverOpen();
                    if (driverOpen == 0)
                    {
                        Logger.Error("[EC] inpoutx64 driver is not open/loaded");
                        return false;
                    }

                    Logger.Info("[EC] inpoutx64 driver is available");

                    // Try to detect the Super I/O chip
                    _chipId = DetectChipId();
                    if (_chipId == 0)
                    {
                        Logger.Warn("[EC] Could not detect Super I/O chip ID - EC access may not work");
                        // Continue anyway - some systems may still work
                    }
                    else
                    {
                        Logger.Info($"[EC] Detected Super I/O chip ID: 0x{_chipId:X4}");
                    }

                    _isInitialized = true;
                    return true;
                }
                catch (DllNotFoundException ex)
                {
                    Logger.Error($"[EC] inpoutx64.dll not found: {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    Logger.Error($"[EC] Failed to initialize EC controller: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets whether the EC controller is initialized and ready.
        /// </summary>
        public bool IsReady => _isInitialized;

        /// <summary>
        /// Gets the detected chip ID.
        /// </summary>
        public ushort ChipId => _chipId;

        /// <summary>
        /// Detects the Super I/O chip ID.
        /// </summary>
        private ushort DetectChipId()
        {
            try
            {
                EnterSuperIoConfig();

                // Read chip ID
                byte id1 = ReadSioRegister(SIO_REG_CHIPID1);
                byte id2 = ReadSioRegister(SIO_REG_CHIPID2);
                ushort chipId = (ushort)((id1 << 8) | id2);

                ExitSuperIoConfig();

                return chipId;
            }
            catch (Exception ex)
            {
                Logger.Debug($"[EC] Error detecting chip ID: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Enters Super I/O configuration mode (IT87xx unlock sequence).
        /// </summary>
        private void EnterSuperIoConfig()
        {
            // IT87xx entry sequence: write 0x87 twice to index port
            DlPortWritePortUchar(SIO_INDEX_PORT, 0x87);
            DlPortWritePortUchar(SIO_INDEX_PORT, 0x87);
        }

        /// <summary>
        /// Exits Super I/O configuration mode.
        /// </summary>
        private void ExitSuperIoConfig()
        {
            // IT87xx exit sequence: write 0xAA to index port
            DlPortWritePortUchar(SIO_INDEX_PORT, 0xAA);
        }

        /// <summary>
        /// Reads a Super I/O register.
        /// </summary>
        private byte ReadSioRegister(byte register)
        {
            DlPortWritePortUchar(SIO_INDEX_PORT, register);
            return DlPortReadPortUchar(SIO_DATA_PORT);
        }

        /// <summary>
        /// Writes to a Super I/O register.
        /// </summary>
        private void WriteSioRegister(byte register, byte value)
        {
            DlPortWritePortUchar(SIO_INDEX_PORT, register);
            DlPortWritePortUchar(SIO_DATA_PORT, value);
        }

        /// <summary>
        /// Reads a byte from EC RAM at the specified address.
        /// Uses IT87xx EC RAM access protocol.
        /// </summary>
        /// <param name="address">16-bit EC RAM address.</param>
        /// <returns>Byte value read from EC RAM.</returns>
        public byte ReadByte(ushort address)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("EC controller not initialized");

            lock (_ecLock)
            {
                try
                {
                    EnterSuperIoConfig();

                    // Select EC logical device
                    WriteSioRegister(SIO_REG_LDNSEL, SIO_LDN_EC);

                    // Set address MSB and LSB
                    WriteSioRegister(SIO_REG_ECADDR_MSB, (byte)(address >> 8));
                    WriteSioRegister(SIO_REG_ECADDR_LSB, (byte)(address & 0xFF));

                    // Read data
                    byte data = ReadSioRegister(SIO_REG_ECDATA);

                    ExitSuperIoConfig();

                    return data;
                }
                catch (Exception ex)
                {
                    Logger.Error($"[EC] Error reading byte at 0x{address:X4}: {ex.Message}");
                    ExitSuperIoConfig();
                    throw;
                }
            }
        }

        /// <summary>
        /// Writes a byte to EC RAM at the specified address.
        /// Uses IT87xx EC RAM access protocol.
        /// </summary>
        /// <param name="address">16-bit EC RAM address.</param>
        /// <param name="value">Byte value to write.</param>
        public void WriteByte(ushort address, byte value)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("EC controller not initialized");

            lock (_ecLock)
            {
                try
                {
                    EnterSuperIoConfig();

                    // Select EC logical device
                    WriteSioRegister(SIO_REG_LDNSEL, SIO_LDN_EC);

                    // Set address MSB and LSB
                    WriteSioRegister(SIO_REG_ECADDR_MSB, (byte)(address >> 8));
                    WriteSioRegister(SIO_REG_ECADDR_LSB, (byte)(address & 0xFF));

                    // Write data
                    WriteSioRegister(SIO_REG_ECDATA, value);

                    ExitSuperIoConfig();

                    Logger.Debug($"[EC] Wrote 0x{value:X2} to address 0x{address:X4}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"[EC] Error writing byte at 0x{address:X4}: {ex.Message}");
                    ExitSuperIoConfig();
                    throw;
                }
            }
        }

        /// <summary>
        /// Reads a 16-bit word from EC RAM (low byte first).
        /// </summary>
        /// <param name="addressLow">Address of low byte.</param>
        /// <param name="addressHigh">Address of high byte.</param>
        /// <returns>16-bit value.</returns>
        public ushort ReadWord(ushort addressLow, ushort addressHigh)
        {
            byte low = ReadByte(addressLow);
            byte high = ReadByte(addressHigh);
            return (ushort)((high << 8) | low);
        }

        /// <summary>
        /// Reads a 16-bit word from EC RAM (high byte at address, low byte at address+1).
        /// </summary>
        /// <param name="addressHigh">Address of high byte.</param>
        /// <returns>16-bit value.</returns>
        public ushort ReadWordHighFirst(ushort addressHigh)
        {
            byte high = ReadByte(addressHigh);
            byte low = ReadByte((ushort)(addressHigh + 1));
            return (ushort)((high << 8) | low);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _isInitialized = false;
                Logger.Debug("[EC] EcController disposed");
            }
        }
    }
}
