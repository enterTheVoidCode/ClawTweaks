using NLog;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace XboxGamingBarHelper.Devices.EC
{
    /// <summary>
    /// Low-level Embedded Controller (EC) communication.
    /// Supports IT5570 (GPD Win 5) using indirect memory access protocol.
    /// Uses inpoutx64.dll for port I/O access.
    ///
    /// Based on the Linux gpd-fan driver and Handheld Companion implementation.
    /// Reference: https://github.com/Cryolitia/gpd-fan-driver
    /// </summary>
    internal class EcController : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Super I/O port addresses (used for chip detection and EC access)
        private const ushort SIO_INDEX_PORT = 0x4E;
        private const ushort SIO_DATA_PORT = 0x4F;

        // Super I/O configuration registers (for chip ID detection)
        private const byte SIO_REG_CHIPID1 = 0x20; // Chip ID byte 1
        private const byte SIO_REG_CHIPID2 = 0x21; // Chip ID byte 2

        // IT5570 indirect memory access registers
        // These are used to access EC RAM via the 0x2E/0x2F register pair
        private const byte EC_INDIRECT_ADDR_HIGH = 0x11;  // High byte address register
        private const byte EC_INDIRECT_ADDR_LOW = 0x10;   // Low byte address register
        private const byte EC_INDIRECT_DATA = 0x12;        // Data register

        // Register select values (written to SIO_INDEX_PORT)
        private const byte EC_REG_SELECT_INDEX = 0x2E;    // Index register selector
        private const byte EC_REG_SELECT_DATA = 0x2F;     // Data register selector

        // Known chip IDs
        private const ushort CHIP_ID_IT5570 = 0x5570;  // GPD Win 5
        private const ushort CHIP_ID_IT5571 = 0x5571;  // Similar to IT5570

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

                // Read chip ID from registers 0x20 and 0x21
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
        /// Enters Super I/O configuration mode (IT87xx/IT55xx unlock sequence).
        /// </summary>
        private void EnterSuperIoConfig()
        {
            // IT87xx/IT55xx entry sequence: write 0x87 twice to index port
            DlPortWritePortUchar(SIO_INDEX_PORT, 0x87);
            DlPortWritePortUchar(SIO_INDEX_PORT, 0x87);
        }

        /// <summary>
        /// Exits Super I/O configuration mode.
        /// </summary>
        private void ExitSuperIoConfig()
        {
            // IT87xx/IT55xx exit sequence: write 0xAA to index port
            DlPortWritePortUchar(SIO_INDEX_PORT, 0xAA);
        }

        /// <summary>
        /// Reads a Super I/O register (used for chip detection).
        /// </summary>
        private byte ReadSioRegister(byte register)
        {
            DlPortWritePortUchar(SIO_INDEX_PORT, register);
            return DlPortReadPortUchar(SIO_DATA_PORT);
        }

        #region IT5570 Indirect Memory Access

        /// <summary>
        /// Selects an EC indirect register by writing to 0x2E register selector.
        /// </summary>
        /// <param name="reg">Register to select (0x10=low addr, 0x11=high addr, 0x12=data)</param>
        private void SelectIndirectRegister(byte reg)
        {
            DlPortWritePortUchar(SIO_INDEX_PORT, EC_REG_SELECT_INDEX);  // Write 0x2E to port 0x4E
            DlPortWritePortUchar(SIO_DATA_PORT, reg);                   // Write register selector to port 0x4F
        }

        /// <summary>
        /// Writes a value to the currently selected indirect register.
        /// </summary>
        /// <param name="value">Value to write</param>
        private void WriteIndirectValue(byte value)
        {
            DlPortWritePortUchar(SIO_INDEX_PORT, EC_REG_SELECT_DATA);   // Write 0x2F to port 0x4E
            DlPortWritePortUchar(SIO_DATA_PORT, value);                 // Write value to port 0x4F
        }

        /// <summary>
        /// Reads a value from the currently selected indirect register.
        /// </summary>
        /// <returns>Value read from register</returns>
        private byte ReadIndirectValue()
        {
            DlPortWritePortUchar(SIO_INDEX_PORT, EC_REG_SELECT_DATA);   // Write 0x2F to port 0x4E
            return DlPortReadPortUchar(SIO_DATA_PORT);                  // Read value from port 0x4F
        }

        /// <summary>
        /// Sets the EC RAM address for indirect access using IT5570 protocol.
        /// </summary>
        /// <param name="address">16-bit EC RAM address</param>
        private void SetIndirectAddress(ushort address)
        {
            // Set high byte of address
            // 1. Select high byte address register (0x11)
            SelectIndirectRegister(EC_INDIRECT_ADDR_HIGH);
            // 2. Write high byte of address
            WriteIndirectValue((byte)((address >> 8) & 0xFF));

            // Set low byte of address
            // 3. Select low byte address register (0x10)
            SelectIndirectRegister(EC_INDIRECT_ADDR_LOW);
            // 4. Write low byte of address
            WriteIndirectValue((byte)(address & 0xFF));
        }

        #endregion

        /// <summary>
        /// Reads a byte from EC RAM at the specified address.
        /// Uses IT5570 indirect memory access protocol.
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
                    // IT5570 indirect memory access sequence:
                    // 1. Set the address (high byte then low byte)
                    SetIndirectAddress(address);

                    // 2. Select data register (0x12)
                    SelectIndirectRegister(EC_INDIRECT_DATA);

                    // 3. Read data value
                    byte data = ReadIndirectValue();

                    return data;
                }
                catch (Exception ex)
                {
                    Logger.Error($"[EC] Error reading byte at 0x{address:X4}: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Writes a byte to EC RAM at the specified address.
        /// Uses IT5570 indirect memory access protocol.
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
                    // IT5570 indirect memory access sequence:
                    // 1. Set the address (high byte then low byte)
                    SetIndirectAddress(address);

                    // 2. Select data register (0x12)
                    SelectIndirectRegister(EC_INDIRECT_DATA);

                    // 3. Write data value
                    WriteIndirectValue(value);

                    Logger.Debug($"[EC] Wrote 0x{value:X2} to address 0x{address:X4}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"[EC] Error writing byte at 0x{address:X4}: {ex.Message}");
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
