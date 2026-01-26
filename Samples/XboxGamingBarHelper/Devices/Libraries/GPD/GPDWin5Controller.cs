// =============================================================================
// GPDWin5Controller.cs
//
// HID communication library for GPD Win 5 controller configuration.
// Based on reverse engineering the Win 5 HID protocol.
//
// Protocol Reference:
// - Vendor ID: 0x2F24 (GPD)
// - Product ID: 0x0135
// - Usage Page: 0xFF00 (Vendor Defined)
// - Report ID: 0x01
// - Command: 01 43 38 00 [offset:2 LE] [checksum:2] [data...]
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
    /// Provides methods to read and (future) write button configurations.
    /// </summary>
    public class GPDWin5Controller : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        #region Constants

        /// <summary>GPD Vendor ID</summary>
        public const int VendorId = 0x2F24;

        /// <summary>GPD Win 5 Product ID (primary - from reference implementation)</summary>
        public const int ProductId = 0x0135;

        /// <summary>GPD Win 5 Product ID (alternate - some device variants)</summary>
        public const int ProductIdAlt = 0x0137;

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
        /// These map to indices in the button map array.
        /// Based on reference implementation (GPDWin5Device.cpp).
        /// </summary>
        public static class ButtonPosition
        {
            public const int DPadUp = 0;
            public const int DPadDown = 1;
            public const int DPadLeft = 2;
            public const int DPadRight = 3;
            public const int L3 = 4;
            public const int R3 = 5;
            // position 6 unused
            public const int A = 7;
            public const int B = 8;
            public const int X = 9;
            public const int Select = 10;
            public const int Y = 11;
            public const int LB = 12;
            public const int RB = 13;
            public const int LT = 14;
            public const int L4 = 15;
            public const int RT = 16;
            public const int Menu = 17;     // Start
            public const int View = 18;     // Back
            public const int Xbox = 19;
            public const int LeftStickLeft = 20;
            public const int LeftStickRight = 21;
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

                var devices = DeviceList.Local.GetHidDevices().ToList();
                Logger.Info($"[GPDWin5] Found {devices.Count} total HID devices on system");

                // Log ALL HID devices for debugging - group by VID for readability
                var devicesByVid = devices.GroupBy(d => d.VendorID).OrderBy(g => g.Key);
                Logger.Info($"[GPDWin5] === ALL HID DEVICES BY VENDOR ===");
                foreach (var group in devicesByVid)
                {
                    var pids = group.Select(d => $"0x{d.ProductID:X4}").Distinct().ToList();
                    Logger.Info($"[GPDWin5]   VID=0x{group.Key:X4}: {group.Count()} device(s), PIDs: {string.Join(", ", pids)}");
                }
                Logger.Info($"[GPDWin5] === END ALL HID DEVICES ===");

                // Log ALL GPD devices (any PID) for reverse engineering
                var gpdDevices = devices.Where(d => d.VendorID == VendorId).ToList();
                Logger.Info($"[GPDWin5] Found {gpdDevices.Count} GPD devices (VID=0x{VendorId:X4}):");

                foreach (var dev in gpdDevices)
                {
                    Logger.Info($"[GPDWin5] --- GPD Device ---");
                    Logger.Info($"[GPDWin5]   VID=0x{dev.VendorID:X4}, PID=0x{dev.ProductID:X4}");
                    Logger.Info($"[GPDWin5]   Path: {dev.DevicePath}");
                    try
                    {
                        Logger.Info($"[GPDWin5]   ProductName: {dev.GetProductName()}");
                        Logger.Info($"[GPDWin5]   Manufacturer: {dev.GetManufacturer()}");
                        Logger.Info($"[GPDWin5]   SerialNumber: {dev.GetSerialNumber()}");
                        Logger.Info($"[GPDWin5]   MaxInputReportLength: {dev.GetMaxInputReportLength()}");
                        Logger.Info($"[GPDWin5]   MaxOutputReportLength: {dev.GetMaxOutputReportLength()}");
                        Logger.Info($"[GPDWin5]   MaxFeatureReportLength: {dev.GetMaxFeatureReportLength()}");

                        // Log usage pages for this device
                        var reportDesc = dev.GetReportDescriptor();
                        var usagePages = reportDesc.DeviceItems
                            .SelectMany(item => item.Usages.GetAllValues())
                            .Select(usage => $"0x{usage:X8} (Page=0x{(usage >> 16):X4}, Usage=0x{(usage & 0xFFFF):X4})")
                            .Distinct()
                            .ToList();
                        Logger.Info($"[GPDWin5]   UsagePages ({usagePages.Count}): {string.Join(", ", usagePages)}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[GPDWin5]   Error getting device details: {ex.Message}");
                    }
                }

                if (!gpdDevices.Any())
                {
                    Logger.Warn($"[GPDWin5] No GPD devices found at all (VID=0x{VendorId:X4})");
                    Logger.Info("[GPDWin5] ========== CONNECTION ATTEMPT END (NO GPD DEVICES) ==========");
                    return false;
                }

                // Try to connect to matching device
                // NOTE: Reference implementation does NOT filter by usage page - just VID/PID.
                // We try each device that matches PID and attempt to open it.
                foreach (var device in gpdDevices)
                {
                    Logger.Info($"[GPDWin5] Checking device PID=0x{device.ProductID:X4}...");

                    if (!ValidProductIds.Contains(device.ProductID))
                    {
                        Logger.Info($"[GPDWin5]   PID mismatch: expected [{string.Join(", ", ValidProductIds.Select(p => $"0x{p:X4}"))}], got 0x{device.ProductID:X4} - skipping");
                        continue;
                    }

                    try
                    {
                        // Log usage pages for debugging (but don't filter by them)
                        try
                        {
                            var reportDescriptor = device.GetReportDescriptor();
                            var allUsages = reportDescriptor.DeviceItems
                                .SelectMany(item => item.Usages.GetAllValues())
                                .ToList();

                            bool hasUsagePage = allUsages.Any(usage => (usage >> 16) == UsagePage);
                            Logger.Info($"[GPDWin5]   Has vendor usage page 0x{UsagePage:X4}: {hasUsagePage}");
                            // Note: We no longer skip devices without vendor usage page
                            // The reference implementation connects to any device matching VID/PID
                        }
                        catch (Exception usageEx)
                        {
                            Logger.Warn($"[GPDWin5]   Could not read usage pages: {usageEx.Message}");
                            // Continue anyway - we'll try to open the device
                        }

                        Logger.Info($"[GPDWin5] >>> VID/PID MATCH! Attempting to open...");
                        Logger.Info($"[GPDWin5]   Device path: {device.DevicePath}");

                        _device = device;
                        _stream = device.Open();
                        _stream.ReadTimeout = 1000;
                        _stream.WriteTimeout = 1000;

                        Logger.Info("[GPDWin5] Successfully opened HID stream");
                        Logger.Info($"[GPDWin5]   ReadTimeout: {_stream.ReadTimeout}ms");
                        Logger.Info($"[GPDWin5]   WriteTimeout: {_stream.WriteTimeout}ms");
                        Logger.Info("[GPDWin5] ========== CONNECTION SUCCESS ==========");

                        ConnectionChanged?.Invoke(this, true);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[GPDWin5] Failed to open device: {ex.GetType().Name}: {ex.Message}");
                        Logger.Error($"[GPDWin5]   Stack: {ex.StackTrace}");
                        continue;
                    }
                }

                Logger.Warn("[GPDWin5] No matching GPD Win 5 controller found (correct PID + usage page)");
                Logger.Info("[GPDWin5] ========== CONNECTION ATTEMPT END (NO MATCH) ==========");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDWin5] Fatal error during connection: {ex.GetType().Name}: {ex.Message}");
                Logger.Error($"[GPDWin5]   Stack: {ex.StackTrace}");
                Logger.Info("[GPDWin5] ========== CONNECTION ATTEMPT END (ERROR) ==========");
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the current controller.
        /// </summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                if (_stream != null)
                {
                    try
                    {
                        Logger.Debug("[GPDWin5] Closing HID stream...");
                        _stream.Close();
                        _stream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"[GPDWin5] Error closing stream: {ex.Message}");
                    }
                    _stream = null;
                }

                if (_device != null)
                {
                    Logger.Info("[GPDWin5] Disconnected from GPD Win 5 controller");
                    _device = null;
                    ConnectionChanged?.Invoke(this, false);
                }
            }
        }

        /// <summary>
        /// Checks if a GPD Win 5 controller is available without connecting.
        /// </summary>
        /// <returns>True if a compatible device is found.</returns>
        public static bool IsDeviceAvailable()
        {
            try
            {
                var devices = DeviceList.Local.GetHidDevices().ToList();
                Logger.Info($"[GPDWin5] IsDeviceAvailable: Scanning {devices.Count} HID devices for VID=0x{VendorId:X4}, PIDs=[{string.Join(", ", ValidProductIds.Select(p => $"0x{p:X4}"))}]");

                // Log ALL HID devices grouped by VID for debugging
                var devicesByVid = devices.GroupBy(d => d.VendorID).OrderBy(g => g.Key);
                Logger.Info($"[GPDWin5] === ALL HID DEVICES ON SYSTEM ===");
                foreach (var group in devicesByVid)
                {
                    var pids = group.Select(d => $"0x{d.ProductID:X4}").Distinct().ToList();
                    Logger.Info($"[GPDWin5]   VID=0x{group.Key:X4}: {group.Count()} device(s), PIDs: [{string.Join(", ", pids)}]");
                }
                Logger.Info($"[GPDWin5] === END HID DEVICE LIST ===");

                // Check for our target device (any valid PID)
                bool found = devices.Any(d => d.VendorID == VendorId && ValidProductIds.Contains(d.ProductID));
                Logger.Info($"[GPDWin5] Target device (VID=0x{VendorId:X4}, PIDs=[{string.Join(", ", ValidProductIds.Select(p => $"0x{p:X4}"))}]) found: {found}");
                return found;
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDWin5] IsDeviceAvailable error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Read Configuration

        /// <summary>
        /// Reads the current button configuration from the device.
        /// </summary>
        /// <returns>The raw configuration data, or null if failed.</returns>
        public byte[] ReadConfiguration()
        {
            Logger.Info("[GPDWin5] Reading button configuration...");

            if (!IsConnected)
            {
                Logger.Warn("[GPDWin5] Cannot read configuration - not connected");
                return null;
            }

            try
            {
                // Read main button configuration at offset 0x0000
                var config = ReadConfigChunk(OffsetMainButtons);
                if (config != null)
                {
                    Logger.Info($"[GPDWin5] Successfully read configuration ({config.Length} bytes)");
                    LogButtonMappings(config);
                }
                return config;
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDWin5] Error reading configuration: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads a configuration chunk at the specified offset.
        /// </summary>
        /// <param name="offset">The offset to read from.</param>
        /// <returns>The configuration data, or null if failed.</returns>
        public byte[] ReadConfigChunk(ushort offset)
        {
            Logger.Info($"[GPDWin5] ========== READ CONFIG CHUNK offset=0x{offset:X4} ==========");

            if (!IsConnected)
            {
                Logger.Warn("[GPDWin5] Cannot read - not connected");
                return null;
            }

            try
            {
                // Build read command: 01 43 38 00 [offset LE] 00 00 ...
                var command = new byte[CommandLength];
                command[0] = ReportId;
                command[1] = CmdConfig;
                command[2] = SubCmdConfig;
                command[3] = 0x00;
                // Offset as little-endian 16-bit
                command[4] = (byte)(offset & 0xFF);
                command[5] = (byte)((offset >> 8) & 0xFF);
                // Checksum placeholder (not needed for reads)
                command[6] = 0x00;
                command[7] = 0x00;

                SendCommand(command, $"READ CONFIG offset=0x{offset:X4}");

                Thread.Sleep(CommandDelayMs);

                // Read response
                var response = ReadResponse(1000, $"CONFIG DATA offset=0x{offset:X4}");
                if (response != null && response.Length > 0)
                {
                    Logger.Info($"[GPDWin5] Read successful: {response.Length} bytes from offset 0x{offset:X4}");
                    return response;
                }
                else
                {
                    Logger.Warn($"[GPDWin5] No response received for offset 0x{offset:X4}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDWin5] Error reading config chunk: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads the L4 paddle configuration.
        /// </summary>
        /// <returns>L4 paddle config data, or null if failed.</returns>
        public byte[] ReadL4PaddleConfig()
        {
            Logger.Info("[GPDWin5] Reading L4 paddle configuration...");
            var config = ReadConfigChunk(OffsetL4Paddle);
            if (config != null)
            {
                LogPaddleConfig("L4", config);
            }
            return config;
        }

        /// <summary>
        /// Reads the R4 paddle configuration.
        /// </summary>
        /// <returns>R4 paddle config data, or null if failed.</returns>
        public byte[] ReadR4PaddleConfig()
        {
            Logger.Info("[GPDWin5] Reading R4 paddle configuration...");
            var config = ReadConfigChunk(OffsetR4Paddle);
            if (config != null)
            {
                LogPaddleConfig("R4", config);
            }
            return config;
        }

        /// <summary>
        /// Reads a response from the controller.
        /// </summary>
        /// <param name="timeoutMs">Read timeout in milliseconds.</param>
        /// <returns>Response bytes, or null if no response.</returns>
        private static int _rxCounter = 0;

        /// <summary>
        /// Reads a response from the controller with detailed logging.
        /// </summary>
        private byte[] ReadResponse(int timeoutMs = 500, string context = null)
        {
            if (!IsConnected || _stream == null)
            {
                Logger.Debug("[GPDWin5] ReadResponse: not connected");
                return null;
            }

            int rxNum = ++_rxCounter;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                lock (_lock)
                {
                    _stream.ReadTimeout = timeoutMs;
                    byte[] buffer = new byte[64];

                    Logger.Debug($"[GPDWin5] RX #{rxNum} waiting (timeout={timeoutMs}ms) {(context != null ? $"[{context}]" : "")}...");
                    int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                    sw.Stop();

                    if (bytesRead > 0)
                    {
                        var response = new byte[bytesRead];
                        Array.Copy(buffer, response, bytesRead);

                        Logger.Info($"[GPDWin5] === RX Packet #{rxNum} {(context != null ? $"({context})" : "")} ===");
                        Logger.Info($"[GPDWin5] RX Length: {bytesRead} bytes in {sw.ElapsedMilliseconds}ms");
                        Logger.Info($"[GPDWin5] RX Header: ReportID=0x{response[0]:X2}, Cmd=0x{(response.Length > 1 ? response[1] : 0):X2}");
                        Logger.Info($"[GPDWin5] RX Full hex dump:");
                        LogHexDump(response, "RX");

                        CommandExecuted?.Invoke(this, new GPDHidCommandEventArgs(response, false));
                        return response;
                    }
                    else
                    {
                        Logger.Warn($"[GPDWin5] RX #{rxNum}: Read returned 0 bytes after {sw.ElapsedMilliseconds}ms");
                    }
                }
            }
            catch (TimeoutException)
            {
                sw.Stop();
                Logger.Info($"[GPDWin5] RX #{rxNum}: Timeout after {sw.ElapsedMilliseconds}ms (expected for some commands)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.Warn($"[GPDWin5] RX #{rxNum}: Error after {sw.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }

        #endregion

        #region Checksum Algorithm

        /// <summary>
        /// Calculates the checksum for a GPD Win 5 configuration packet.
        /// Algorithm: sum bytes 8-63 and return lower 16 bits.
        /// </summary>
        /// <param name="packet">The 64-byte packet.</param>
        /// <returns>16-bit checksum value.</returns>
        public static ushort CalculateChecksum(byte[] packet)
        {
            if (packet == null || packet.Length < 64)
                return 0;

            uint sum = 0;
            for (int i = 8; i < 64; i++)
            {
                sum += packet[i];
            }

            return (ushort)(sum & 0xFFFF);
        }

        /// <summary>
        /// Inserts the calculated checksum into a packet at bytes 6-7 (little-endian).
        /// </summary>
        /// <param name="packet">The 64-byte packet to modify.</param>
        private static void InsertChecksum(byte[] packet)
        {
            ushort checksum = CalculateChecksum(packet);
            packet[6] = (byte)(checksum & 0xFF);         // Low byte
            packet[7] = (byte)((checksum >> 8) & 0xFF);  // High byte
        }

        #endregion

        #region Packet Builders

        /// <summary>
        /// Builds the unlock packet (command 0x45).
        /// Must be sent before writing configuration.
        /// </summary>
        private static byte[] BuildUnlockPacket()
        {
            var packet = new byte[64];
            packet[0] = ReportId;
            packet[1] = 0x45;  // Unlock command
            return packet;
        }

        /// <summary>
        /// Builds the lookup table packet (command 0x21).
        /// Contains the button mapping lookup table.
        /// Based on reference implementation (GPDWin5Device.cpp).
        /// </summary>
        private static byte[] BuildLookupPacket()
        {
            var packet = new byte[64];
            packet[0] = ReportId;     // 0x01
            packet[1] = 0x21;         // Lookup command
            packet[2] = 0x75;         // Magic bytes from reference
            packet[3] = 0x56;
            packet[4] = 0x34;
            packet[5] = 0x12;
            // bytes 6-7 = 0x00 (no checksum needed)
            packet[8] = 0xa2;
            packet[9] = 0x4f;
            return packet;
        }

        /// <summary>
        /// Builds the main configuration packet with button mappings.
        /// </summary>
        /// <param name="buttonMap">Array of 22 keycodes for button positions 0-21.</param>
        private static byte[] BuildMainPacket(ushort[] buttonMap)
        {
            var packet = new byte[64];
            packet[0] = ReportId;
            packet[1] = CmdConfig;
            packet[2] = SubCmdConfig;
            packet[3] = 0x00;  // Offset low (0x0000) - FIXED: was at [4]
            packet[4] = 0x00;  // Offset high - FIXED: was at [5]
            // [5] unused, [6-7] = checksum

            // Magic header - exact bytes from reference implementation
            packet[8] = 0x75;
            packet[9] = 0x56;
            packet[10] = 0x34;
            packet[11] = 0x12;
            packet[12] = 0xa2;
            packet[13] = 0x4f;
            packet[14] = 0x00;
            packet[15] = 0x00;
            packet[16] = 0x5d;
            packet[17] = 0xb0;
            packet[18] = 0xff;
            packet[19] = 0xff;

            // Button mappings start at byte 20 (0x14)
            // Each button uses 2 bytes (little-endian)
            // Reference only puts 14 buttons (0-13) in main packet
            for (int i = 0; i < buttonMap.Length && i < 14; i++)
            {
                int offset = 20 + (i * 2);
                if (offset + 1 < 64)
                {
                    packet[offset] = (byte)(buttonMap[i] & 0xFF);
                    packet[offset + 1] = (byte)((buttonMap[i] >> 8) & 0xFF);
                }
            }

            InsertChecksum(packet);
            return packet;
        }

        /// <summary>
        /// Builds the R4 paddle configuration packet.
        /// Based on reference implementation (GPDWin5Device.cpp).
        /// </summary>
        /// <param name="r4Keycode">The keycode to assign to R4.</param>
        private static byte[] BuildR4Packet(ushort r4Keycode)
        {
            var packet = new byte[64];
            packet[0] = ReportId;     // 0x01
            packet[1] = CmdConfig;    // 0x43
            packet[2] = SubCmdConfig; // 0x38
            packet[3] = 0x50;         // Offset low (0x0150)
            packet[4] = 0x01;         // Offset high
            // [5] unused, [6-7] = checksum (inserted below)

            // R4 special structure from reference
            packet[39] = 0x02;
            packet[40] = 0x04;
            packet[47] = 0x32;
            packet[49] = (byte)(r4Keycode & 0xFF);        // R4 keycode low
            packet[50] = (byte)((r4Keycode >> 8) & 0xFF); // R4 keycode high
            packet[53] = 0x32;

            InsertChecksum(packet);
            return packet;
        }

        /// <summary>
        /// Builds an offset packet. Based on reference implementation.
        /// These packets just have header + offset + checksum, minimal data.
        /// </summary>
        /// <param name="offset">The offset value (e.g., 0x0038, 0x0070, etc.)</param>
        private static byte[] BuildOffsetPacket(ushort offset)
        {
            var packet = new byte[64];
            packet[0] = ReportId;     // 0x01
            packet[1] = CmdConfig;    // 0x43
            packet[2] = SubCmdConfig; // 0x38
            packet[3] = (byte)(offset & 0xFF);        // Offset low
            packet[4] = (byte)((offset >> 8) & 0xFF); // Offset high
            // Rest is zeros, checksum calculated over bytes 8-63
            InsertChecksum(packet);
            return packet;
        }

        /// <summary>
        /// Offsets for pre-R4 packets (5 packets).
        /// </summary>
        private static readonly ushort[] OffsetsBeforeR4 = { 0x0038, 0x0070, 0x00a8, 0x00e0, 0x0118 };

        /// <summary>
        /// Offsets for post-R4 packets (12 packets).
        /// </summary>
        private static readonly ushort[] OffsetsAfterR4 = { 0x0188, 0x01c0, 0x01f8, 0x0230, 0x0268, 0x02a0, 0x02d8, 0x0310, 0x0348, 0x0380, 0x03b8, 0x03f0 };

        #endregion

        #region Write Configuration

        /// <summary>
        /// Gets the default button map for positions 0-21.
        /// Used as a base when remapping specific buttons.
        /// Based on reference implementation (GPDWin5Device.cpp).
        /// </summary>
        public static ushort[] GetDefaultButtonMap()
        {
            return new ushort[]
            {
                0x8000,  // 0: DPAD_UP (Gamepad)
                0x8001,  // 1: DPAD_DOWN (Gamepad)
                0x8002,  // 2: DPAD_LEFT (Gamepad)
                0x8003,  // 3: DPAD_RIGHT (Gamepad)
                0x800f,  // 4: L3 (Gamepad)
                0x8010,  // 5: R3 (Gamepad)
                0x0000,  // 6: (unused)
                0x8007,  // 7: A (Gamepad)
                0x8008,  // 8: B (Gamepad)
                0x8009,  // 9: X (Gamepad)
                0x8005,  // 10: SELECT (Gamepad)
                0x800a,  // 11: Y (Gamepad)
                0x800b,  // 12: LB (Gamepad)
                0x800c,  // 13: RB (Gamepad)
                0x800d,  // 14: LT (Gamepad)
                0x002b,  // 15: L4 (TAB by default)
                0x800e,  // 16: RT (Gamepad)
                0x8004,  // 17: MENU/START (Gamepad)
                0x8005,  // 18: VIEW/BACK (Gamepad)
                0x8006,  // 19: XBOX (Gamepad)
                0x0000,  // 20: LEFT_STICK_LEFT
                0x0000   // 21: LEFT_STICK_RIGHT
            };
        }

        /// <summary>
        /// Sends the unlock sequence required before writing configuration.
        /// </summary>
        /// <returns>True if successful.</returns>
        private bool SendUnlockSequence()
        {
            Logger.Info("[GPDWin5] ========== UNLOCK SEQUENCE START ==========");

            Logger.Info("[GPDWin5] Step 1/2: Sending UNLOCK packet (cmd 0x45)");
            if (!SendCommand(BuildUnlockPacket(), "UNLOCK cmd=0x45"))
            {
                Logger.Error("[GPDWin5] UNLOCK SEQUENCE FAILED at step 1 (unlock packet)");
                return false;
            }
            Thread.Sleep(CommandDelayMs);

            Logger.Info("[GPDWin5] Step 2/2: Sending LOOKUP packet (cmd 0x21)");
            if (!SendCommand(BuildLookupPacket(), "LOOKUP cmd=0x21"))
            {
                Logger.Error("[GPDWin5] UNLOCK SEQUENCE FAILED at step 2 (lookup packet)");
                return false;
            }
            Thread.Sleep(CommandDelayMs);

            Logger.Info("[GPDWin5] ========== UNLOCK SEQUENCE COMPLETE ==========");
            return true;
        }

        /// <summary>
        /// Sends the configuration packets including button mappings.
        /// Based on reference implementation (GPDWin5Device.cpp).
        /// </summary>
        /// <param name="buttonMap">Button mappings for positions 0-21.</param>
        /// <param name="r4Keycode">Keycode for R4 paddle.</param>
        /// <returns>True if successful.</returns>
        private bool SendConfigPackets(ushort[] buttonMap, ushort r4Keycode = 0x002B)
        {
            Logger.Info("[GPDWin5] ========== CONFIG PACKETS START ==========");
            Logger.Info($"[GPDWin5] Button map ({buttonMap.Length} entries):");
            for (int i = 0; i < buttonMap.Length; i++)
            {
                string keyName = GPDWin5Keycodes.GetKeyName(buttonMap[i]);
                Logger.Info($"[GPDWin5]   Position {i,2}: 0x{buttonMap[i]:X4} ({keyName})");
            }
            Logger.Info($"[GPDWin5] R4 keycode: 0x{r4Keycode:X4} ({GPDWin5Keycodes.GetKeyName(r4Keycode)})");

            // Main packet with button mappings (first 14 buttons)
            Logger.Info("[GPDWin5] Step 1/19: Sending MAIN CONFIG packet (cmd 0x43, offset 0x0000)");
            var mainPacket = BuildMainPacket(buttonMap);
            if (!SendCommand(mainPacket, "MAIN CONFIG cmd=0x43 offset=0x0000"))
            {
                Logger.Error("[GPDWin5] CONFIG FAILED at step 1 (main packet)");
                return false;
            }
            Thread.Sleep(CommandDelayMs);

            // 5 offset packets before R4 - based on reference implementation
            for (int i = 0; i < OffsetsBeforeR4.Length; i++)
            {
                ushort offset = OffsetsBeforeR4[i];
                var packet = BuildOffsetPacket(offset);
                Logger.Info($"[GPDWin5] Step {i + 2}/19: Sending PRE-R4 offset packet (offset 0x{offset:X4})");
                if (!SendCommand(packet, $"PRE-R4 #{i} offset=0x{offset:X4}"))
                {
                    Logger.Error($"[GPDWin5] CONFIG FAILED at step {i + 2} (pre-R4 packet {i})");
                    return false;
                }
                Thread.Sleep(CommandDelayMs);
            }

            // R4 packet (special handling for R4 paddle button)
            Logger.Info("[GPDWin5] Step 7/19: Sending R4 PADDLE packet (offset 0x0150)");
            var r4Packet = BuildR4Packet(r4Keycode);
            if (!SendCommand(r4Packet, "R4 PADDLE offset=0x0150"))
            {
                Logger.Error("[GPDWin5] CONFIG FAILED at step 7 (R4 packet)");
                return false;
            }
            Thread.Sleep(CommandDelayMs);

            // 12 offset packets after R4 - based on reference implementation
            for (int i = 0; i < OffsetsAfterR4.Length; i++)
            {
                ushort offset = OffsetsAfterR4[i];
                var packet = BuildOffsetPacket(offset);
                Logger.Info($"[GPDWin5] Step {i + 8}/19: Sending POST-R4 offset packet (offset 0x{offset:X4})");
                if (!SendCommand(packet, $"POST-R4 #{i} offset=0x{offset:X4}"))
                {
                    Logger.Error($"[GPDWin5] CONFIG FAILED at step {i + 8} (post-R4 packet {i})");
                    return false;
                }
                Thread.Sleep(CommandDelayMs);
            }

            Logger.Info("[GPDWin5] ========== CONFIG PACKETS COMPLETE (19/19) ==========");
            return true;
        }

        /// <summary>
        /// Builds a simple apply packet (just report ID + command).
        /// Used in the apply sequence. Based on reference implementation.
        /// </summary>
        private static byte[] BuildApplyPacket(byte command)
        {
            var packet = new byte[64];
            packet[0] = ReportId;  // 0x01
            packet[1] = command;
            return packet;
        }

        /// <summary>
        /// Sends the apply sequence to commit configuration changes.
        /// Based on reference implementation (GPDWin5Device.cpp).
        /// Sequence: 0x21 -> 0x22 -> 0x21 -> 0x25 -> 0x22
        /// </summary>
        /// <returns>True if successful.</returns>
        private bool SendApplySequence()
        {
            Logger.Info("[GPDWin5] ========== APPLY SEQUENCE START ==========");

            // Step 1: Send 0x21 (lookup/read)
            Logger.Info("[GPDWin5] Apply Step 1/5: Sending cmd 0x21");
            if (!SendCommand(BuildApplyPacket(0x21), "APPLY cmd=0x21 #1"))
            {
                Logger.Error("[GPDWin5] APPLY FAILED at step 1");
                return false;
            }
            Thread.Sleep(CommandDelayMs);

            // Step 2: Send 0x22 (apply/commit)
            Logger.Info("[GPDWin5] Apply Step 2/5: Sending cmd 0x22");
            if (!SendCommand(BuildApplyPacket(0x22), "APPLY cmd=0x22 #1"))
            {
                Logger.Error("[GPDWin5] APPLY FAILED at step 2");
                return false;
            }
            Thread.Sleep(CommandDelayMs);

            // Step 3: Send 0x21 again
            Logger.Info("[GPDWin5] Apply Step 3/5: Sending cmd 0x21");
            if (!SendCommand(BuildApplyPacket(0x21), "APPLY cmd=0x21 #2"))
            {
                Logger.Error("[GPDWin5] APPLY FAILED at step 3");
                return false;
            }
            Thread.Sleep(CommandDelayMs);

            // Step 4: Send 0x25 (save to flash)
            Logger.Info("[GPDWin5] Apply Step 4/5: Sending cmd 0x25 (save)");
            if (!SendCommand(BuildApplyPacket(0x25), "APPLY cmd=0x25 (save)"))
            {
                Logger.Error("[GPDWin5] APPLY FAILED at step 4");
                return false;
            }
            Thread.Sleep(CommandDelayMs);

            // Step 5: Send 0x22 again (final apply)
            Logger.Info("[GPDWin5] Apply Step 5/5: Sending cmd 0x22 (final)");
            if (!SendCommand(BuildApplyPacket(0x22), "APPLY cmd=0x22 #2 (final)"))
            {
                Logger.Error("[GPDWin5] APPLY FAILED at step 5");
                return false;
            }
            Thread.Sleep(CommandDelayMs);

            Logger.Info("[GPDWin5] ========== APPLY SEQUENCE COMPLETE ==========");
            return true;
        }

        /// <summary>
        /// Remaps a single button to a new keycode.
        /// </summary>
        /// <param name="buttonPosition">Button position (0-21).</param>
        /// <param name="keycode">New keycode for the button.</param>
        /// <returns>True if successful.</returns>
        public bool RemapButton(int buttonPosition, ushort keycode)
        {
            Logger.Info("[GPDWin5] ############################################################");
            Logger.Info("[GPDWin5] ###           BUTTON REMAP OPERATION START              ###");
            Logger.Info("[GPDWin5] ############################################################");
            Logger.Info($"[GPDWin5] Request: Position={buttonPosition}, Keycode=0x{keycode:X4} ({GPDWin5Keycodes.GetKeyName(keycode)})");
            Logger.Info($"[GPDWin5] Connected: {IsConnected}");

            if (!IsConnected)
            {
                Logger.Error("[GPDWin5] REMAP ABORTED: Not connected to controller");
                Logger.Info("[GPDWin5] ############################################################");
                return false;
            }

            if (buttonPosition < 0 || buttonPosition > 21)
            {
                Logger.Error($"[GPDWin5] REMAP ABORTED: Invalid button position {buttonPosition} (must be 0-21)");
                Logger.Info("[GPDWin5] ############################################################");
                return false;
            }

            string buttonName = GetButtonName(buttonPosition);
            Logger.Info($"[GPDWin5] Button name: {buttonName}");
            Logger.Info($"[GPDWin5] Target keycode: 0x{keycode:X4} = {GPDWin5Keycodes.GetKeyName(keycode)}");

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                Logger.Info("[GPDWin5] Phase 1: Building button map with single change");
                var buttonMap = GetDefaultButtonMap();
                ushort oldValue = buttonMap[buttonPosition];
                buttonMap[buttonPosition] = keycode;
                Logger.Info($"[GPDWin5]   Changed position {buttonPosition}: 0x{oldValue:X4} -> 0x{keycode:X4}");

                Logger.Info("[GPDWin5] Phase 2: Unlock sequence");
                if (!SendUnlockSequence())
                {
                    Logger.Error("[GPDWin5] REMAP FAILED: Unlock sequence failed");
                    Logger.Info("[GPDWin5] ############################################################");
                    return false;
                }

                Logger.Info("[GPDWin5] Phase 3: Send config packets");
                if (!SendConfigPackets(buttonMap))
                {
                    Logger.Error("[GPDWin5] REMAP FAILED: Config packets failed");
                    Logger.Info("[GPDWin5] ############################################################");
                    return false;
                }

                Logger.Info("[GPDWin5] Phase 4: Apply sequence");
                if (!SendApplySequence())
                {
                    Logger.Error("[GPDWin5] REMAP FAILED: Apply sequence failed");
                    Logger.Info("[GPDWin5] ############################################################");
                    return false;
                }

                sw.Stop();
                Logger.Info($"[GPDWin5] REMAP SUCCESS! Total time: {sw.ElapsedMilliseconds}ms");
                Logger.Info("[GPDWin5] ############################################################");
                return true;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.Error($"[GPDWin5] REMAP EXCEPTION after {sw.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}");
                Logger.Error($"[GPDWin5]   Stack: {ex.StackTrace}");
                Logger.Info("[GPDWin5] ############################################################");
                return false;
            }
        }

        /// <summary>
        /// Gets a human-readable name for a button position.
        /// </summary>
        private static string GetButtonName(int position)
        {
            return position switch
            {
                0 => "DPad Up",
                1 => "DPad Down",
                2 => "DPad Left",
                3 => "DPad Right",
                4 => "L3 (Left Stick Click)",
                5 => "R3 (Right Stick Click)",
                6 => "(unused)",
                7 => "A",
                8 => "B",
                9 => "X",
                10 => "Select",
                11 => "Y",
                12 => "LB",
                13 => "RB",
                14 => "LT",
                15 => "L4",
                16 => "RT",
                17 => "Menu/Start",
                18 => "View/Back",
                19 => "Xbox",
                20 => "Left Stick Left",
                21 => "Left Stick Right",
                _ => $"Unknown({position})"
            };
        }

        /// <summary>
        /// Remaps multiple buttons at once.
        /// </summary>
        /// <param name="mappings">Dictionary of button position to keycode.</param>
        /// <param name="r4Keycode">Optional R4 paddle keycode.</param>
        /// <returns>True if successful.</returns>
        public bool RemapButtons(Dictionary<int, ushort> mappings, ushort r4Keycode = 0x002B)
        {
            if (!IsConnected)
            {
                Logger.Error("[GPDWin5] Cannot remap buttons - not connected");
                return false;
            }

            Logger.Info($"[GPDWin5] Remapping {mappings.Count} buttons");

            try
            {
                var buttonMap = GetDefaultButtonMap();

                foreach (var kvp in mappings)
                {
                    if (kvp.Key >= 0 && kvp.Key < buttonMap.Length)
                    {
                        Logger.Debug($"[GPDWin5]   Position {kvp.Key}: 0x{kvp.Value:X4}");
                        buttonMap[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        Logger.Warn($"[GPDWin5] Skipping invalid position: {kvp.Key}");
                    }
                }

                if (!SendUnlockSequence())
                    return false;

                if (!SendConfigPackets(buttonMap, r4Keycode))
                    return false;

                if (!SendApplySequence())
                    return false;

                Logger.Info("[GPDWin5] Buttons remapped successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDWin5] Error remapping buttons: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restores all buttons to their default mappings.
        /// </summary>
        /// <returns>True if successful.</returns>
        public bool RestoreDefaults()
        {
            if (!IsConnected)
            {
                Logger.Error("[GPDWin5] Cannot restore defaults - not connected");
                return false;
            }

            Logger.Info("[GPDWin5] Restoring default button mappings");

            try
            {
                var buttonMap = GetDefaultButtonMap();

                if (!SendUnlockSequence())
                    return false;

                if (!SendConfigPackets(buttonMap))
                    return false;

                if (!SendApplySequence())
                    return false;

                Logger.Info("[GPDWin5] Defaults restored successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[GPDWin5] Error restoring defaults: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Logging Helpers

        /// <summary>
        /// Logs the decoded button mappings from configuration data.
        /// </summary>
        private void LogButtonMappings(byte[] config)
        {
            if (config == null || config.Length < ButtonDataOffset + 32)
            {
                Logger.Warn("[GPDWin5] Configuration data too short for button mappings");
                return;
            }

            Logger.Info("[GPDWin5] === Current Button Mappings ===");

            // Button positions and their offsets (relative to ButtonDataOffset)
            var buttons = new (string Name, int Offset)[]
            {
                ("D-pad UP", 0x00),
                ("D-pad DOWN", 0x02),
                ("D-pad LEFT", 0x04),
                ("D-pad RIGHT", 0x06),
                ("A button", 0x0E),      // Position 7 (0x22-0x23)
                ("B button", 0x10),      // Position 8 (0x24-0x25)
                ("X button", 0x12),      // Position 9 (0x26-0x27)
                ("Y button", 0x14),      // Position 10 (0x28-0x29)
                ("L3 (L-stick click)", 0x18),  // Position 12 (0x2c-0x2d)
                ("R3 (R-stick click)", 0x1A),  // Position 13 (0x2e-0x2f)
                ("L-stick UP", 0x1C),    // Position 14 (0x30-0x31)
                ("L-stick DOWN", 0x1E),  // Position 15 (0x32-0x33)
                ("L-stick LEFT", 0x20),  // Position 16 (0x34-0x35)
                ("L-stick RIGHT", 0x2A), // Position 21 (0x3e-0x3f)
            };

            foreach (var (name, offset) in buttons)
            {
                int absOffset = ButtonDataOffset + offset;
                if (absOffset + 1 < config.Length)
                {
                    ushort keycode = (ushort)(config[absOffset] | (config[absOffset + 1] << 8));
                    string keyName = GPDWin5Keycodes.GetKeyName(keycode);
                    Logger.Info($"[GPDWin5]   {name,-20}: 0x{keycode:X4} ({keyName})");
                }
            }

            Logger.Info("[GPDWin5] ==============================");
        }

        /// <summary>
        /// Logs paddle configuration data.
        /// </summary>
        private void LogPaddleConfig(string paddle, byte[] config)
        {
            if (config == null)
            {
                Logger.Warn($"[GPDWin5] No {paddle} paddle configuration data");
                return;
            }

            Logger.Info($"[GPDWin5] === {paddle} Paddle Configuration ===");
            Logger.Debug($"[GPDWin5] Raw data: {FormatHex(config, 40)}");

            // L4 paddle byte positions (from PADDLE_MAPPING_COMPLETE.md)
            if (paddle == "L4")
            {
                if (config.Length >= 30)
                {
                    // Modifier at bytes 12-13
                    ushort modifier = (ushort)(config[12] | (config[13] << 8));
                    // Slot 2 at bytes 22-23
                    ushort slot2 = config.Length > 23 ? (ushort)(config[22] | (config[23] << 8)) : (ushort)0;
                    // Slot 3 at bytes 24-25
                    ushort slot3 = config.Length > 25 ? (ushort)(config[24] | (config[25] << 8)) : (ushort)0;
                    // Slot 4 at bytes 28-29
                    ushort slot4 = config.Length > 29 ? (ushort)(config[28] | (config[29] << 8)) : (ushort)0;

                    Logger.Info($"[GPDWin5]   Modifier: 0x{modifier:X4}");
                    Logger.Info($"[GPDWin5]   Slot 2 (Primary): 0x{slot2:X4} ({GPDWin5Keycodes.GetKeyName(slot2)})");
                    Logger.Info($"[GPDWin5]   Slot 3 (Secondary): 0x{slot3:X4} ({GPDWin5Keycodes.GetKeyName(slot3)})");
                    Logger.Info($"[GPDWin5]   Slot 4 (Tertiary): 0x{slot4:X4} ({GPDWin5Keycodes.GetKeyName(slot4)})");
                }
            }
            else if (paddle == "R4")
            {
                if (config.Length >= 56)
                {
                    // Modifier at bytes 38-39
                    ushort modifier = (ushort)(config[38] | (config[39] << 8));
                    // Slot 2 at bytes 48-49
                    ushort slot2 = config.Length > 49 ? (ushort)(config[48] | (config[49] << 8)) : (ushort)0;
                    // Slot 3 at bytes 50-51
                    ushort slot3 = config.Length > 51 ? (ushort)(config[50] | (config[51] << 8)) : (ushort)0;
                    // Slot 4 at bytes 54-55
                    ushort slot4 = config.Length > 55 ? (ushort)(config[54] | (config[55] << 8)) : (ushort)0;

                    Logger.Info($"[GPDWin5]   Modifier: 0x{modifier:X4}");
                    Logger.Info($"[GPDWin5]   Slot 2 (Primary): 0x{slot2:X4} ({GPDWin5Keycodes.GetKeyName(slot2)})");
                    Logger.Info($"[GPDWin5]   Slot 3 (Secondary): 0x{slot3:X4} ({GPDWin5Keycodes.GetKeyName(slot3)})");
                    Logger.Info($"[GPDWin5]   Slot 4 (Tertiary): 0x{slot4:X4} ({GPDWin5Keycodes.GetKeyName(slot4)})");
                }
            }

            Logger.Info($"[GPDWin5] ================================");
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
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                lock (_lock)
                {
                    // Log full packet for reverse engineering
                    Logger.Info($"[GPDWin5] === TX Packet #{packetNum} {(description != null ? $"({description})" : "")} ===");
                    Logger.Info($"[GPDWin5] TX Length: {command.Length} bytes");
                    Logger.Info($"[GPDWin5] TX Header: ReportID=0x{command[0]:X2}, Cmd=0x{command[1]:X2}");
                    Logger.Info($"[GPDWin5] TX Full hex dump:");
                    LogHexDump(command, "TX");

                    _stream.Write(command);
                    sw.Stop();

                    Logger.Info($"[GPDWin5] TX Packet #{packetNum} sent in {sw.ElapsedMilliseconds}ms");
                    CommandExecuted?.Invoke(this, new GPDHidCommandEventArgs(command, true));
                    return true;
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.Error($"[GPDWin5] TX Packet #{packetNum} FAILED after {sw.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}");
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// Logs a hex dump of data in a readable format (16 bytes per line with ASCII).
        /// </summary>
        private static void LogHexDump(byte[] data, string prefix)
        {
            if (data == null || data.Length == 0)
            {
                Logger.Info($"[GPDWin5] {prefix}: (empty)");
                return;
            }

            for (int i = 0; i < data.Length; i += 16)
            {
                var hexPart = new System.Text.StringBuilder();
                var asciiPart = new System.Text.StringBuilder();

                for (int j = 0; j < 16; j++)
                {
                    if (i + j < data.Length)
                    {
                        byte b = data[i + j];
                        hexPart.Append($"{b:X2} ");
                        asciiPart.Append(b >= 32 && b < 127 ? (char)b : '.');
                    }
                    else
                    {
                        hexPart.Append("   ");
                    }
                }

                Logger.Info($"[GPDWin5] {prefix} {i:X4}: {hexPart} |{asciiPart}|");
            }
        }

        #endregion

        #region Utility Methods

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
