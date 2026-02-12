// =============================================================================
// GPDWin5Controller.cs
//
// HID communication library for GPD Win 5 controller configuration.
// Based on reverse engineering the Win 5 HID protocol.
//
// Protocol Reference:
// - Vendor ID: 0x2F24 (GPD)
// - Product ID: 0x0137 (Note: 0x0135 also exists)
// - Usage Page: 0xFF00 (Vendor Defined)
// - Report ID: 0x01
// - Command: 01 43 38 00 [offset:2 LE] [checksum:2] [magic header] [data...]
//
// CORRECTED VERSION - Fixed button positions and added full protocol sequence
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HidSharp;
using NLog;

namespace XboxGamingBarHelper.Devices.Libraries.GPD
{
    /// <summary>
    /// Controller for GPD Win 5 HID communication.
    /// Provides methods to read and write button configurations.
    /// </summary>
    public class GPDWin5Controller : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        #region Constants

        /// <summary>GPD Vendor ID</summary>
        public const int VendorId = 0x2F24;

        /// <summary>GPD Win 5 Product ID (primary)</summary>
        public const int ProductId = 0x0137;

        /// <summary>GPD Win 5 Product ID (alternate - some device variants)</summary>
        public const int ProductIdAlt = 0x0135;

        /// <summary>All valid GPD Win 5 Product IDs</summary>
        public static readonly int[] ValidProductIds = { ProductId, ProductIdAlt };

        /// <summary>Vendor-defined usage page for configuration</summary>
        private const int UsagePage = 0xFF00;

        /// <summary>Report ID for configuration commands</summary>
        private const byte ReportId = 0x01;

        /// <summary>Configuration command byte</summary>
        private const byte CmdConfig = 0x43;

        /// <summary>Configuration subcommand byte</summary>
        private const byte SubCmdConfig = 0x38;

        /// <summary>Lookup/query command byte</summary>
        private const byte CmdLookup = 0x21;

        /// <summary>Unlock command byte</summary>
        private const byte CmdUnlock = 0x45;

        /// <summary>Apply command byte</summary>
        private const byte CmdApply = 0x22;

        /// <summary>Finalize command byte</summary>
        private const byte CmdFinalize = 0x25;

        /// <summary>Command packet length</summary>
        private const int CommandLength = 64;

        /// <summary>Delay between commands in milliseconds</summary>
        private const int CommandDelayMs = 50;

        // Configuration offsets (little-endian)
        /// <summary>Offset for main button configuration (offset 0x0000)</summary>
        public const ushort OffsetMainButtons = 0x0000;

        /// <summary>Offset for L4 paddle configuration (offset 0x00A8)</summary>
        public const ushort OffsetL4Paddle = 0x00A8;

        /// <summary>Offset for R4 paddle configuration (offset 0x0150)</summary>
        public const ushort OffsetR4Paddle = 0x0150;

        /// <summary>Offset where button data starts in main config packet</summary>
        public const int ButtonDataOffset = 0x14; // 20 decimal

        /// <summary>
        /// Button positions in the GPD Win 5 configuration.
        /// CORRECTED MAPPING - discovered through systematic testing.
        /// Original Python script had wrong positions (everything from Y onwards shifted by 1).
        /// </summary>
        public static class ButtonPosition
        {
            // DPAD
            public const int DPadUp = 0;
            public const int DPadDown = 1;
            public const int DPadLeft = 2;
            public const int DPadRight = 3;

            // System buttons
            public const int Start = 4;          // Menu/Start button
            public const int Back = 5;           // View/Select/Back button (same physical button)
            public const int Xbox = 6;           // Center Xbox logo button

            // Face buttons
            public const int A = 7;
            public const int B = 8;
            public const int X = 9;
            public const int Y = 10;             // CORRECTED: was 11

            // Shoulder buttons
            public const int LB = 11;            // CORRECTED: was 12
            public const int RB = 12;            // CORRECTED: was 13

            // Positions 13-14: Unknown/unmapped
            public const int Position13 = 13;    // Unknown button
            public const int Position14 = 14;    // Unknown button

            // Stick clicks
            public const int L3 = 15;            // CORRECTED: was 4
            public const int R3 = 16;            // CORRECTED: was 5

            // Left stick directions
            public const int LeftStickUp = 17;
            public const int LeftStickDown = 18;
            public const int LeftStickRight = 19;
            public const int LeftStickLeft = 20;

            // Position 21: Unknown
            public const int Position21 = 21;    // Unknown button

            // NOTE: LT and RT (analog triggers) are NOT remappable in keyboard mode
            // NOTE: L4 and R4 (back paddles) require special packets at different offsets
        }

        #endregion

        #region Private Fields

        private HidDevice _device;
        private HidStream _stream;
        private bool _disposed;
        private readonly object _lock = new object();

        #endregion

        #region Events

        /// <summary>
        /// Raised when connection status changes.
        /// </summary>
        public event EventHandler<bool> ConnectionChanged;

        /// <summary>
        /// Raised when a HID command is sent or received (for debugging).
        /// </summary>
        public event EventHandler<GPDHidCommandEventArgs> CommandExecuted;

        #endregion

        #region Properties

        /// <summary>
        /// Gets whether a GPD Win 5 controller is currently connected.
        /// </summary>
        public bool IsConnected => _device != null && _stream != null;

        /// <summary>
        /// Gets information about the connected device.
        /// </summary>
        public string DeviceInfo => _device?.ToString();

        /// <summary>
        /// Checks if a GPD Win 5 device is available in the system.
        /// </summary>
        public static bool IsDeviceAvailable()
        {
            try
            {
                var devices = DeviceList.Local.GetHidDevices()
                    .Where(d => d.VendorID == VendorId && ValidProductIds.Contains(d.ProductID))
                    .ToList();
                return devices.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Connection Methods

        /// <summary>
        /// Attempts to connect to a GPD Win 5 controller.
        /// </summary>
        /// <returns>True if connection successful, false otherwise.</returns>
        public bool Connect()
        {
            Logger.Info("[GPDWin5] ========== CONNECTION ATTEMPT START ==========");
            Logger.Info($"[GPDWin5] Looking for VID=0x{VendorId:X4}, PIDs=[{string.Join(", ", ValidProductIds.Select(p => $"0x{p:X4}"))}], UsagePage=0x{UsagePage:X4}");

            try
            {
                Disconnect();

                var devices = DeviceList.Local.GetHidDevices()
                    .Where(d => d.VendorID == VendorId && ValidProductIds.Contains(d.ProductID))
                    .ToList();

                Logger.Info($"[GPDWin5] Found {devices.Count} matching GPD Win 5 device(s)");

                foreach (var device in devices)
                {
                    try
                    {
                        Logger.Info($"[GPDWin5] Attempting to open: VID=0x{device.VendorID:X4}, PID=0x{device.ProductID:X4}");
                        Logger.Info($"[GPDWin5]   Path: {device.DevicePath}");

                        _stream = device.Open();
                        _device = device;

                        Logger.Info($"[GPDWin5] ========== CONNECTION SUCCESS ==========");

                        // Log device info (these may fail on some devices, but that's OK)
                        try
                        {
                            Logger.Info($"[GPDWin5] Connected to: {device.GetProductName()}");
                        }
                        catch { Logger.Info("[GPDWin5] Connected to: (unable to get product name)"); }

                        try
                        {
                            Logger.Info($"[GPDWin5] Manufacturer: {device.GetManufacturer()}");
                        }
                        catch { Logger.Info("[GPDWin5] Manufacturer: (unable to get manufacturer)"); }

                        try
                        {
                            Logger.Info($"[GPDWin5] Serial: {device.GetSerialNumber()}");
                        }
                        catch { Logger.Info("[GPDWin5] Serial: (unable to get serial number)"); }

                        ConnectionChanged?.Invoke(this, true);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[GPDWin5] Failed to open device: {ex.Message}");
                        // Clean up if we partially opened
                        if (_stream != null)
                        {
                            try { _stream.Dispose(); } catch { }
                            _stream = null;
                        }
                        _device = null;
                    }
                }

                Logger.Error("[GPDWin5] No GPD Win 5 devices could be opened");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDWin5] Connection failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the controller.
        /// </summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                if (_stream != null)
                {
                    try
                    {
                        _stream.Close();
                        _stream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"[GPDWin5] Error closing stream: {ex.Message}");
                    }
                    finally
                    {
                        _stream = null;
                    }
                }

                if (_device != null)
                {
                    Logger.Info("[GPDWin5] Disconnected from device");
                    _device = null;
                    ConnectionChanged?.Invoke(this, false);
                }
            }
        }

        #endregion

        #region Configuration Methods

        /// <summary>
        /// Reads the current button configuration from the device.
        /// </summary>
        /// <returns>Array of 22 keycodes, or null if failed</returns>
        public ushort[] ReadConfiguration()
        {
            if (!IsConnected)
            {
                Logger.Error("[GPDWin5] ReadConfiguration failed: not connected");
                return null;
            }

            Logger.Info("[GPDWin5] ReadConfiguration: Device readback not implemented - returning known default map");
            return GetDefaultButtonMap();
        }

        /// <summary>
        /// Reads the L4 paddle configuration.
        /// </summary>
        /// <returns>L4 paddle config bytes, or null if failed</returns>
        public byte[] ReadL4PaddleConfig()
        {
            if (!IsConnected)
            {
                Logger.Error("[GPDWin5] ReadL4PaddleConfig failed: not connected");
                return null;
            }

            Logger.Info("[GPDWin5] ReadL4PaddleConfig: Not fully implemented yet");
            // TODO: Implement actual L4 read from device at OffsetL4Paddle
            return new byte[2];  // Return empty config
        }

        /// <summary>
        /// Reads the R4 paddle configuration.
        /// </summary>
        /// <returns>R4 paddle config bytes, or null if failed</returns>
        public byte[] ReadR4PaddleConfig()
        {
            if (!IsConnected)
            {
                Logger.Error("[GPDWin5] ReadR4PaddleConfig failed: not connected");
                return null;
            }

            Logger.Info("[GPDWin5] ReadR4PaddleConfig: Not fully implemented yet");
            // TODO: Implement actual R4 read from device at OffsetR4Paddle
            return new byte[2];  // Return empty config
        }

        /// <summary>
        /// Remaps a single button to a keycode.
        /// </summary>
        /// <param name="buttonPosition">Button position from ButtonPosition class</param>
        /// <param name="keycode">HID keycode to map to</param>
        /// <returns>True if successful</returns>
        public bool RemapButton(int buttonPosition, ushort keycode)
        {
            Logger.Info($"[GPDWin5] RemapButton: position={buttonPosition}, keycode=0x{keycode:X4}");

            // Build from known-good defaults and apply one change.
            // Device readback is not implemented/reliable.
            var currentConfig = GetDefaultButtonMap();

            if (buttonPosition >= 0 && buttonPosition < currentConfig.Length)
            {
                currentConfig[buttonPosition] = keycode;
                return WriteButtonConfiguration(currentConfig, 0x002B);
            }

            Logger.Error($"[GPDWin5] RemapButton: Invalid button position {buttonPosition}");
            return false;
        }

        /// <summary>
        /// Remaps multiple buttons at once.
        /// </summary>
        /// <param name="mappings">Dictionary of button positions to keycodes</param>
        /// <param name="r4Keycode">Optional R4 paddle keycode</param>
        /// <returns>True if successful</returns>
        public bool RemapButtons(Dictionary<int, ushort> mappings, ushort r4Keycode = 0x002B)
        {
            var safeMappings = mappings ?? new Dictionary<int, ushort>();
            Logger.Info($"[GPDWin5] RemapButtons: Applying {safeMappings.Count} mappings, R4=0x{r4Keycode:X4}");

            // Start from known-good defaults and apply all overrides in one transaction.
            var currentConfig = GetDefaultButtonMap();
            foreach (var mapping in safeMappings)
            {
                if (mapping.Key >= 0 && mapping.Key < currentConfig.Length)
                {
                    currentConfig[mapping.Key] = mapping.Value;
                }
            }

            return WriteButtonConfiguration(currentConfig, r4Keycode);
        }

        /// <summary>
        /// Restores the device to default button configuration.
        /// </summary>
        /// <returns>True if successful</returns>
        public bool RestoreDefaults()
        {
            Logger.Info("[GPDWin5] RestoreDefaults: Restoring default button configuration");
            return WriteButtonConfiguration(GetDefaultButtonMap(), 0x002B);
        }

        /// <summary>
        /// Writes a complete button configuration to the device.
        /// Uses the CORRECTED full protocol sequence.
        /// </summary>
        /// <param name="buttonMap">Array of 22 keycodes (positions 0-21)</param>
        /// <returns>True if successful</returns>
        public bool WriteButtonConfiguration(ushort[] buttonMap, ushort r4Keycode = 0x002B)
        {
            if (!IsConnected)
            {
                Logger.Error("[GPDWin5] WriteButtonConfiguration failed: not connected");
                return false;
            }

            if (buttonMap == null || buttonMap.Length < 22)
            {
                Logger.Error("[GPDWin5] WriteButtonConfiguration failed: invalid button map");
                return false;
            }

            Logger.Info("[GPDWin5] ========== WRITE CONFIGURATION START ==========");

            try
            {
                // Step 1: Send unlock sequence
                if (!SendUnlockSequence())
                {
                    Logger.Error("[GPDWin5] Unlock sequence failed");
                    return false;
                }

                // Step 2: Send full configuration packet sequence
                if (!SendConfigPackets(buttonMap, r4Keycode))
                {
                    Logger.Error("[GPDWin5] Configuration packet sequence failed");
                    return false;
                }

                // Step 3: Send apply sequence
                if (!SendApplySequence())
                {
                    Logger.Error("[GPDWin5] Apply sequence failed");
                    return false;
                }

                Logger.Info("[GPDWin5] ========== WRITE CONFIGURATION SUCCESS ==========");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDWin5] WriteButtonConfiguration failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Sends the unlock sequence to prepare device for configuration.
        /// </summary>
        private bool SendUnlockSequence()
        {
            Logger.Info("[GPDWin5] --- Unlock Sequence ---");

            // Send unlock command
            byte[] unlock = new byte[CommandLength];
            unlock[0] = ReportId;
            unlock[1] = CmdUnlock;

            if (!SendCommand(unlock, "Unlock"))
                return false;

            Thread.Sleep(10);

            // Try to read response (device may send acknowledgment)
            byte[] response = TryReadInputReport();
            if (response != null && response.Length > 0)
            {
                Logger.Info("[GPDWin5] Received unlock response, echoing back");
                if (!SendCommand(response, "Unlock Echo"))
                    return false;
            }
            else
            {
                // Fallback: send hardcoded confirmation
                Logger.Info("[GPDWin5] No unlock response, sending fallback confirmation");
                byte[] confirm = ParseHexString(
                    "01 45 01 00 27 04 e2 00 e2 00 00 00 00 00 00 00 00 00 00 00 00 " +
                    "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 " +
                    "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
                );
                if (!SendCommand(confirm, "Unlock Confirm"))
                    return false;
            }

            Thread.Sleep(10);
            return true;
        }

        /// <summary>
        /// Sends a lookup packet (used multiple times in protocol).
        /// </summary>
        private bool SendLookupPacket()
        {
            byte[] lookup = BuildLookupPacket();
            InsertChecksum(lookup);
            return SendCommand(lookup, "Lookup");
        }

        /// <summary>
        /// Sends the full Win5 configuration packet sequence (main + auxiliary + R4).
        /// Matches the known-good packet flow used by the working remapper.
        /// </summary>
        private bool SendConfigPackets(ushort[] buttonMap, ushort r4Keycode)
        {
            if (!SendLookupPacket())
                return false;

            if (!SendMainConfigPacket(buttonMap))
                return false;

            string[] packetsBeforeR4 =
            {
                "01 43 38 00 38 00 2d 05 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 01 80 02 80 03 80 04 80 05 80 06 80 07 80 08 80 09 80",
                "01 43 38 00 70 00 63 0d 0a 80 0b 80 0c 80 0d 80 0e 80 0f 80 10 80 11 80 12 80 13 80 14 80 15 80 16 80 17 80 18 80 19 80 1a 80 1b 80 1c 80 1d 80 1e 80 1f 80 20 80 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 a8 00 f9 00 00 00 00 00 02 04 00 00 2b 00 00 00 32 00 00 00 00 00 32 00 00 00 00 00 32 00 00 00 00 00 32 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 e0 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 18 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
            };

            foreach (string hex in packetsBeforeR4)
            {
                byte[] packet = ParseHexString(hex);
                if (!SendCommand(packet, "Config Aux (pre-R4)"))
                    return false;
            }

            if (!SendCommand(BuildR4Packet(r4Keycode), "Config R4"))
                return false;

            string[] packetsAfterR4 =
            {
                "01 43 38 00 88 01 32 00 00 00 32 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 c0 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 f8 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 30 02 06 00 00 00 00 00 02 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 68 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 a0 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 d8 02 06 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 10 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 48 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 80 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
                "01 43 38 00 b8 03 5e 24 00 00 00 00 01 80 00 00 00 00 00 00 00 00 00 00 00 00 01 00 ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff",
                "01 43 10 00 f0 03 f0 0f ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
            };

            foreach (string hex in packetsAfterR4)
            {
                byte[] packet = ParseHexString(hex);
                if (!SendCommand(packet, "Config Aux (post-R4)"))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Sends the main configuration packet with button mappings.
        /// CORRECTED: Includes magic header bytes.
        /// </summary>
        private bool SendMainConfigPacket(ushort[] buttonMap)
        {
            byte[] packet = new byte[CommandLength];

            // Header
            packet[0] = ReportId;
            packet[1] = CmdConfig;
            packet[2] = SubCmdConfig;
            packet[3] = 0x00;  // Offset low byte
            packet[4] = 0x00;  // Offset high byte
            packet[5] = 0x00;  // Reserved

            // Checksum will be inserted at bytes 6-7

            // CRITICAL: Magic header bytes (8-19)
            // These were missing in the original implementation!
            packet[8] = 0x75;
            packet[9] = 0x56;
            packet[10] = 0x34;
            packet[11] = 0x12;
            packet[12] = 0x98;  // CRITICAL: was 0xa2 in old broken code
            packet[13] = 0x4f;
            packet[14] = 0x00;
            packet[15] = 0x00;
            packet[16] = 0x67;  // CRITICAL: was 0x5d in old broken code
            packet[17] = 0xb0;
            packet[18] = 0xff;
            packet[19] = 0xff;

            // Button mappings start at byte 20 (ButtonDataOffset)
            for (int i = 0; i < buttonMap.Length && i < 22; i++)
            {
                int offset = ButtonDataOffset + (i * 2);
                if (offset < CommandLength - 1)
                {
                    // Little-endian encoding
                    packet[offset] = (byte)(buttonMap[i] & 0xFF);
                    packet[offset + 1] = (byte)((buttonMap[i] >> 8) & 0xFF);
                }
            }

            // Insert checksum
            InsertChecksum(packet);

            return SendCommand(packet, "Main Config");
        }

        /// <summary>
        /// Builds the R4 paddle packet at offset 0x0150.
        /// </summary>
        private byte[] BuildR4Packet(ushort r4Keycode)
        {
            byte[] packet = ParseHexString(
                "01 43 38 00 50 01 d8 04 00 00 00 00 00 00 00 00 00 00 00 00 " +
                "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 " +
                "00 02 04 00 40 00 00 00 32 00 00 00 00 00 32 00 ff ff 00 00 " +
                "32 00 ff ff"
            );

            // R4 value at byte 0x2C (little-endian)
            packet[0x2C] = (byte)(r4Keycode & 0xFF);
            packet[0x2D] = (byte)((r4Keycode >> 8) & 0xFF);
            InsertChecksum(packet);
            return packet;
        }

        /// <summary>
        /// Sends the apply sequence to commit configuration changes.
        /// </summary>
        private bool SendApplySequence()
        {
            Logger.Info("[GPDWin5] --- Apply Sequence ---");

            // Apply step 1: Send lookup
            if (!SendLookupPacket())
                return false;
            Thread.Sleep(10);

            // Apply step 2: Send apply command (0x22)
            byte[] cmd22 = new byte[CommandLength];
            cmd22[0] = ReportId;
            cmd22[1] = CmdApply;
            if (!SendCommand(cmd22, "Apply 0x22"))
                return false;
            Thread.Sleep(10);

            // Apply step 3: Send lookup again
            if (!SendLookupPacket())
                return false;
            Thread.Sleep(300);  // Longer delay

            // Apply step 4: Send finalize command (0x25)
            byte[] cmd25 = ParseHexString(
                "01 25 04 00 00 00 04 00 00 04 00 00 00 00 00 00 00 00 00 00 00 " +
                "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 " +
                "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
            );
            if (!SendCommand(cmd25, "Finalize 0x25"))
                return false;
            Thread.Sleep(10);

            // Apply step 5: Send apply command again
            if (!SendCommand(cmd22, "Apply 0x22 (2)"))
                return false;
            Thread.Sleep(10);

            // Apply step 6: Final lookup
            if (!SendLookupPacket())
                return false;
            Thread.Sleep(100);

            return true;
        }

        #endregion

        #region Packet Builders

        /// <summary>
        /// Builds a lookup packet.
        /// </summary>
        private byte[] BuildLookupPacket()
        {
            byte[] packet = new byte[CommandLength];
            packet[0] = ReportId;
            packet[1] = CmdLookup;
            packet[2] = SubCmdConfig;

            // Fill with specific pattern (reverse engineered from protocol)
            // Bytes 8-63 follow specific pattern
            byte[] pattern = ParseHexString(
                "84 22 a2 a3 a0 a1 a6 a7 a4 a5 ba bb b8 b9 be bf bc bd b2 b3 b0 b1 b6 b7 b4 b5 " +
                "8a 8b 88 89 8e 8f 8c 8d 82 83 80 81 86 87 84 85 9a 9b 98 99 9e 9f 9c 9d 92 93 90 91 96 97 94 95"
            );

            Array.Copy(pattern, 0, packet, 8, Math.Min(pattern.Length, 56));
            return packet;
        }

        /// <summary>
        /// Calculates and inserts checksum at bytes 6-7.
        /// </summary>
        private void InsertChecksum(byte[] packet)
        {
            if (packet == null || packet.Length < 64)
                return;

            ushort checksum = CalculateChecksum(packet);
            packet[6] = (byte)(checksum & 0xFF);        // Low byte
            packet[7] = (byte)((checksum >> 8) & 0xFF); // High byte
        }

        /// <summary>
        /// Calculates checksum for packet (bytes 8-63).
        /// Matches the known-good Win5 remapper implementation.
        /// </summary>
        private ushort CalculateChecksum(byte[] packet)
        {
            if (packet == null || packet.Length < 64)
                return 0;

            uint sum = 0;

            // Sum bytes 8-63 (data after checksum)
            for (int i = 8; i < 64; i++)
            {
                sum += packet[i];
            }

            return (ushort)(sum & 0xFFFF);
        }

        #endregion

        #region Private Methods

        private static int _packetCounter = 0;

        /// <summary>
        /// Sends a command to the controller with detailed logging.
        /// </summary>
        private bool SendCommand(byte[] command, string description = null)
        {
            if (!IsConnected || _stream == null)
            {
                Logger.Error("[GPDWin5] SendCommand failed: not connected");
                return false;
            }

            int packetNum = ++_packetCounter;

            try
            {
                lock (_lock)
                {
                    Logger.Info($"[GPDWin5] TX Packet #{packetNum} {(description != null ? $"({description})" : "")}");
                    _stream.Write(command);
                    Thread.Sleep(3);  // Small delay after write

                    CommandExecuted?.Invoke(this, new GPDHidCommandEventArgs(command, true));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDWin5] TX Packet #{packetNum} FAILED: {ex.Message}");
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// Attempts to read an input report (non-blocking).
        /// Returns null if no data available.
        /// </summary>
        private byte[] TryReadInputReport()
        {
            try
            {
                if (_stream != null && _stream.CanRead)
                {
                    byte[] buffer = new byte[CommandLength];
                    _stream.ReadTimeout = 100;  // 100ms timeout

                    int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        Logger.Info($"[GPDWin5] RX: Read {bytesRead} bytes");
                        return buffer;
                    }
                }
            }
            catch (TimeoutException)
            {
                // Timeout is expected, not an error
            }
            catch (Exception ex)
            {
                Logger.Debug($"[GPDWin5] Read error (non-fatal): {ex.Message}");
            }

            return null;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Returns the known-good default Win5 button map.
        /// </summary>
        public static ushort[] GetDefaultButtonMap()
        {
            return new ushort[]
            {
                0x001C, // 0: DPadUp
                0x0016, // 1: DPadDown
                0x000F, // 2: DPadLeft
                0x0018, // 3: DPadRight
                0x0024, // 4: Start
                0x002C, // 5: Back/View
                0x00E0, // 6: Xbox
                0x0009, // 7: A
                0x0004, // 8: B
                0x000A, // 9: X
                0x00EA, // 10: Y
                0x00EB, // 11: LB
                0x00EC, // 12: RB
                0x00ED, // 13: Position13
                0x002B, // 14: Position14
                0x004C, // 15: L3
                0x0029, // 16: R3
                0x0000, // 17: LeftStickUp
                0x0000, // 18: LeftStickDown
                0x001D, // 19: LeftStickRight
                0x0000, // 20: LeftStickLeft
                0x001A, // 21: Position21
            };
        }

        /// <summary>
        /// Formats a byte array as hex string for display.
        /// </summary>
        public static string FormatHex(byte[] data, int maxBytes = 16)
        {
            if (data == null || data.Length == 0)
                return "(empty)";

            var bytes = data.Take(maxBytes).ToArray();
            return BitConverter.ToString(bytes).Replace("-", " ") + (data.Length > maxBytes ? "..." : "");
        }

        /// <summary>
        /// Parses a hex string into bytes.
        /// </summary>
        public static byte[] ParseHexString(string hexString)
        {
            try
            {
                hexString = hexString.Replace("0x", "").Replace("0X", "")
                                     .Replace(" ", "").Replace("-", "")
                                     .Replace(",", "").Trim();

                if (hexString.Length % 2 != 0)
                    return Array.Empty<byte>();

                byte[] bytes = new byte[hexString.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);

                return bytes;
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                Logger.Debug("[GPDWin5] Disposing GPDWin5Controller...");
                Disconnect();
                _disposed = true;
            }
        }

        #endregion
    }

    #region Event Args

    /// <summary>
    /// Event args for GPD HID command logging.
    /// </summary>
    public class GPDHidCommandEventArgs : EventArgs
    {
        /// <summary>The command data.</summary>
        public byte[] Data { get; }

        /// <summary>True if sent to device, false if received from device.</summary>
        public bool IsSent { get; }

        /// <summary>Timestamp when the command was executed.</summary>
        public DateTime Timestamp { get; }

        /// <summary>Hex string representation of the command.</summary>
        public string Hex => GPDWin5Controller.FormatHex(Data, 32);

        public GPDHidCommandEventArgs(byte[] data, bool isSent)
        {
            Data = data;
            IsSent = isSent;
            Timestamp = DateTime.Now;
        }
    }

    #endregion
}
