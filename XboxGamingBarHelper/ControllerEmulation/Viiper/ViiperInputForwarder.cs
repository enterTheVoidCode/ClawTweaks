using System;
using System.Threading;
using NLog;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.Labs;

namespace XboxGamingBarHelper.ControllerEmulation.Viiper
{
    /// <summary>Which physical controller source drives the VIIPER forwarder.</summary>
    internal enum ViiperInputSourceKind
    {
        XInput = 0,
        LegionHid = 1,
    }

    /// <summary>Which IMU source (if any) feeds the gyro bytes of the VIIPER wire format.</summary>
    internal enum ViiperGyroSourceKind
    {
        None = 0,
        Left = 1,
        Right = 2,
        Handheld = 3,  // Windows sensor — not wired yet; treated as None for now.
    }

    /// <summary>
    /// Bitmasks for Legion Go auxiliary buttons (Y1/Y2/Y3, M3, Mode, Share, front-top/bot).
    /// Matches LegionButtonMonitor's AuxButtons output.
    /// </summary>
    internal static class LegionAux
    {
        public const ushort Y1     = 0x0001;
        public const ushort Y2     = 0x0002;
        public const ushort Y3     = 0x0004;
        public const ushort M3     = 0x0008;
        public const ushort M1     = 0x0010;
        public const ushort M2     = 0x0020;
        public const ushort Mode   = 0x0040;
        public const ushort Share  = 0x0080;
        public const ushort FrTop  = 0x0100;
        public const ushort FrBot  = 0x0200;
    }

    /// <summary>
    /// Phase 5a: minimal XInput -> VIIPER forwarding loop.
    /// Polls XInput for a single physical controller and forwards the state to a
    /// VIIPER virtual device at ~250 Hz. Currently supports Xbox 360, DualShock 4,
    /// DualSense Edge, and Xbox Elite 2 target types. Gyro, Legion HID input,
    /// button remap, and rumble feedback come in 5b/5c/5d.
    /// </summary>
    internal sealed class ViiperInputForwarder : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ViiperService service;
        private readonly LegionManager legionManager;
        private Thread pollThread;
        private volatile bool running;

        private uint physicalIndex;
        private uint busId;
        private uint deviceId;

        // Throttle LED writes — Legion's WMI/USB write path is slow; don't re-send the same color.
        private long lastLedPacked = -1;
        private long lastLedWriteTicks;
        private const long LedWriteMinIntervalTicks = TimeSpan.TicksPerSecond / 8; // 125 ms
        private string targetType = "xbox360";
        private volatile ViiperInputSourceKind inputSource = ViiperInputSourceKind.XInput;
        private volatile ViiperGyroSourceKind gyroSource = ViiperGyroSourceKind.None;

        // Latest aux buttons sampled from Legion HID this cycle (0 when the active source
        // doesn't expose them, e.g. plain XInput). Read by the DS4/DSE wire builders to map
        // Legion Y1/Y2/Y3/M3/Mode/Share onto the virtual device's extended button bits.
        private ushort currentAuxButtons;

        // IMU axis counts/second and counts/G for Legion Go BMI323:
        //   gyro: 16 counts per deg/sec
        //   accel: 4096 counts per G (BMI323 ±8g)
        // DS4 host apps expect 8192 counts/g on accel → multiply by 2.
        private const float GyroDpsToRawCounts = 16.0f;
        private const float AccelGToRawCounts = 4096.0f;

        public ViiperInputForwarder(ViiperService inService, LegionManager inLegionManager)
        {
            service = inService;
            legionManager = inLegionManager;
            if (service != null)
            {
                service.FeedbackReceived += OnFeedbackReceived;
            }
        }

        /// <summary>
        /// Called by the native thread when the virtual device receives a rumble/LED report
        /// from the consuming application. We parse the relevant motor bytes based on the
        /// current device type and forward them to the physical XInput controller.
        /// </summary>
        private void OnFeedbackReceived(uint cbBusId, uint cbDeviceId, byte[] data)
        {
            if (!running || data == null || data.Length == 0) return;
            // Ignore late events from a hot-swapped-out device.
            if (cbBusId != busId || cbDeviceId != deviceId) return;

            byte rumbleLarge = 0;
            byte rumbleSmall = 0;
            bool haveLed = false;
            byte ledR = 0, ledG = 0, ledB = 0;
            switch (targetType)
            {
                case "xbox360":
                    if (data.Length >= 2) { rumbleLarge = data[0]; rumbleSmall = data[1]; }
                    break;
                case "dualshock4":
                    // DS4 report: data[0]=rumbleSmall, data[1]=rumbleLarge, data[2..4]=LED RGB,
                    // data[5]=flashOn, data[6]=flashOff.
                    if (data.Length >= 2) { rumbleSmall = data[0]; rumbleLarge = data[1]; }
                    if (data.Length >= 5) { haveLed = true; ledR = data[2]; ledG = data[3]; ledB = data[4]; }
                    break;
                case "dualsenseedge":
                    // DSE report: data[0]=rumbleSmall, data[1]=rumbleLarge, data[2..4]=LED RGB,
                    // data[5]=playerLeds.
                    if (data.Length >= 2) { rumbleSmall = data[0]; rumbleLarge = data[1]; }
                    if (data.Length >= 5) { haveLed = true; ledR = data[2]; ledG = data[3]; ledB = data[4]; }
                    break;
                case "xboxelite2":
                case "xbox-one":
                case "xbox-elite":
                case "steamdeck-generic":
                case "steam-generic":
                case "steam-controller":
                    if (data.Length >= 2) { rumbleLarge = data[0]; rumbleSmall = data[1]; }
                    break;
                case "switchpro":
                case "joycon-left":
                case "joycon-right":
                case "joycon-pair":
                    if (data.Length >= 2) { rumbleLarge = data[0]; rumbleSmall = data[1]; }
                    break;
                default:
                    return;
            }

            // XInput only forwards rumble when the physical source is XInput.
            // Legion HID rumble forwarding lands in a later phase (needs LegionControllerService).
            if (inputSource == ViiperInputSourceKind.XInput)
            {
                try
                {
                    var vib = new ViiperXInputVibration
                    {
                        LeftMotorSpeed = (ushort)(rumbleLarge * 257),   // 0-255 -> 0-65535
                        RightMotorSpeed = (ushort)(rumbleSmall * 257),
                    };
                    ViiperXInput.SetState(physicalIndex, ref vib);
                }
                catch (Exception ex) { Logger.Debug($"XInput SetState rumble failed: {ex.Message}"); }
            }

            // LED color forwarding — the emulated device's RGB lightbar is pushed to the
            // Legion Go stick lights. Throttle: skip when unchanged and rate-limit writes.
            if (haveLed && legionManager != null)
            {
                long packed = ((long)ledR << 16) | ((long)ledG << 8) | ledB;
                long now = DateTime.UtcNow.Ticks;
                if (packed != lastLedPacked && (now - lastLedWriteTicks) >= LedWriteMinIntervalTicks)
                {
                    lastLedPacked = packed;
                    lastLedWriteTicks = now;
                    try
                    {
                        string hex = string.Format("#{0:X2}{1:X2}{2:X2}", ledR, ledG, ledB);
                        legionManager.SetLightColor(hex);
                    }
                    catch (Exception ex) { Logger.Debug($"Legion SetLightColor failed: {ex.Message}"); }
                }
            }
        }

        /// <summary>Discover which XInput index (0-3) has a connected physical controller.</summary>
        public static uint DetectPhysicalXInputIndex()
        {
            var state = new ViiperXInputState();
            for (uint i = 0; i < 4; i++)
            {
                if (ViiperXInput.GetState(i, ref state) == ViiperXInput.ErrorSuccess)
                {
                    return i;
                }
            }
            return 0;
        }

        public void Start(uint inPhysicalIndex, uint inBusId, uint inDeviceId, string inTargetType)
        {
            if (running) return;

            physicalIndex = inPhysicalIndex;
            busId = inBusId;
            deviceId = inDeviceId;
            targetType = string.IsNullOrEmpty(inTargetType) ? "xbox360" : inTargetType;

            running = true;
            pollThread = new Thread(PollLoop)
            {
                IsBackground = true,
                Name = "ViiperInputForwarder",
                Priority = ThreadPriority.AboveNormal,
            };
            pollThread.Start();
            Logger.Info($"VIIPER forwarder started (xinput={physicalIndex}, bus={busId}, dev={deviceId}, type={targetType})");
        }

        public void UpdateTarget(uint newBusId, uint newDeviceId, string newTypeName)
        {
            busId = newBusId;
            deviceId = newDeviceId;
            targetType = string.IsNullOrEmpty(newTypeName) ? "xbox360" : newTypeName;
            Logger.Info($"VIIPER forwarder target updated: bus={busId}, dev={deviceId}, type={targetType}");
        }

        public void SetInputSource(ViiperInputSourceKind kind)
        {
            if (inputSource == kind) return;
            inputSource = kind;
            Logger.Info($"VIIPER forwarder input source -> {kind}");
        }

        public void SetGyroSource(ViiperGyroSourceKind kind)
        {
            if (gyroSource == kind) return;
            gyroSource = kind;
            Logger.Info($"VIIPER forwarder gyro source -> {kind}");
        }

        /// <summary>
        /// Fetches the current gyro/accel sample from the selected source, converted to DS4
        /// wire-format int16 counts. Returns false when no source is selected, the source
        /// has no fresh data, or the source is "Handheld" (not wired yet).
        /// </summary>
        private bool TryBuildImuCounts(out short gyroXRaw, out short gyroYRaw, out short gyroZRaw,
                                        out short accelXRaw, out short accelYRaw, out short accelZRaw)
        {
            gyroXRaw = gyroYRaw = gyroZRaw = 0;
            accelXRaw = accelYRaw = accelZRaw = 0;

            var src = gyroSource;
            if (src == ViiperGyroSourceKind.None || src == ViiperGyroSourceKind.Handheld)
            {
                return false;
            }

            bool useLeft = src == ViiperGyroSourceKind.Left;
            LegionGyroSample sample;
            if (!LegionButtonMonitor.TryGetLatestGyroSample(useLeft, out sample))
            {
                return false;
            }

            gyroXRaw = SaturateToShort(sample.GyroXDegPerSecond * GyroDpsToRawCounts);
            gyroYRaw = SaturateToShort(sample.GyroYDegPerSecond * GyroDpsToRawCounts);
            gyroZRaw = SaturateToShort(sample.GyroZDegPerSecond * GyroDpsToRawCounts);
            accelXRaw = SaturateToShort(sample.AccelXG * AccelGToRawCounts);
            accelYRaw = SaturateToShort(sample.AccelYG * AccelGToRawCounts);
            accelZRaw = SaturateToShort(sample.AccelZG * AccelGToRawCounts);
            return true;
        }

        private static short SaturateToShort(float value)
        {
            if (value > short.MaxValue) return short.MaxValue;
            if (value < short.MinValue) return short.MinValue;
            return (short)value;
        }

        private static short ScaleDs4Accel(short raw)
        {
            int scaled = raw * 2;
            if (scaled > short.MaxValue) return short.MaxValue;
            if (scaled < short.MinValue) return short.MinValue;
            return (short)scaled;
        }

        public void Stop()
        {
            if (!running) return;
            running = false;
            try
            {
                if (pollThread != null && pollThread.IsAlive)
                {
                    pollThread.Join(500);
                }
            }
            catch (Exception ex) { Logger.Warn($"VIIPER forwarder join threw: {ex.Message}"); }
            pollThread = null;

            // Clear any lingering rumble on the physical controller.
            try
            {
                var zero = new ViiperXInputVibration();
                ViiperXInput.SetState(physicalIndex, ref zero);
            }
            catch { }

            Logger.Info("VIIPER forwarder stopped");
        }

        public void Dispose()
        {
            Stop();
            if (service != null)
            {
                service.FeedbackReceived -= OnFeedbackReceived;
            }
        }

        private void PollLoop()
        {
            var xiState = new ViiperXInputState();
            uint lastPacket = unchecked((uint)-1);
            long lastLegionTicks = 0;
            int errorCount = 0;

            while (running)
            {
                try
                {
                    if (inputSource == ViiperInputSourceKind.LegionHid)
                    {
                        LegionGamepadSample sample;
                        if (!LegionButtonMonitor.TryGetLatestGamepadSample(out sample))
                        {
                            Thread.Sleep(8);
                            continue;
                        }
                        if (sample.TimestampTicksUtc == lastLegionTicks)
                        {
                            Thread.Sleep(4);
                            continue;
                        }
                        lastLegionTicks = sample.TimestampTicksUtc;
                        currentAuxButtons = sample.AuxButtons;

                        var gp = ConvertLegionToXInputGamepad(sample);
                        byte[] data = BuildDeviceInput(gp);
                        if (data != null && data.Length > 0)
                        {
                            service.SetInput(busId, deviceId, data);
                        }
                    }
                    else // XInput
                    {
                        var rc = ViiperXInput.GetState(physicalIndex, ref xiState);
                        if (rc != ViiperXInput.ErrorSuccess)
                        {
                            if (errorCount++ < 5 && Logger.IsDebugEnabled)
                            {
                                Logger.Debug($"XInput.GetState({physicalIndex}) rc=0x{rc:X8}");
                            }
                            Thread.Sleep(16);
                            continue;
                        }
                        errorCount = 0;

                        if (xiState.PacketNumber == lastPacket)
                        {
                            Thread.Sleep(4);
                            continue;
                        }
                        lastPacket = xiState.PacketNumber;
                        currentAuxButtons = 0;  // XInput has no Legion aux buttons.

                        byte[] data = BuildDeviceInput(xiState.Gamepad);
                        if (data != null && data.Length > 0)
                        {
                            service.SetInput(busId, deviceId, data);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"VIIPER forwarder poll error: {ex.Message}");
                    Thread.Sleep(100);
                }
                Thread.Sleep(4);
            }
        }

        /// <summary>
        /// Adapts a Legion Go HID gamepad sample to the XInput-shaped struct the
        /// wire-format builders already consume. The Buttons bitfield from the Legion
        /// monitor is already XInput-compatible.
        /// </summary>
        private static ViiperXInputGamepad ConvertLegionToXInputGamepad(LegionGamepadSample s)
        {
            return new ViiperXInputGamepad
            {
                Buttons = s.Buttons,
                LeftTrigger = s.LeftTrigger,
                RightTrigger = s.RightTrigger,
                ThumbLX = s.LeftStickX,
                ThumbLY = s.LeftStickY,
                ThumbRX = s.RightStickX,
                ThumbRY = s.RightStickY,
            };
        }

        private byte[] BuildDeviceInput(ViiperXInputGamepad gp)
        {
            switch (targetType)
            {
                case "xbox360":
                    return BuildXbox360Input(gp);
                case "dualshock4":
                    return BuildDualShock4Input(gp);
                case "dualsenseedge":
                    return BuildDualSenseEdgeInput(gp);
                case "xboxelite2":
                case "xbox-one":
                case "xbox-elite":
                case "steamdeck-generic":
                case "steam-generic":
                case "steam-controller":
                    return BuildXboxElite2Input(gp);
                default:
                    return BuildXbox360Input(gp);
            }
        }

        // -------------------------------------------------------------------
        // Wire format builders (ported from ViiperController reference impl)
        // -------------------------------------------------------------------

        private static byte[] BuildXbox360Input(ViiperXInputGamepad gp)
        {
            var data = new byte[20];
            WriteU32(data, 0, gp.Buttons);
            data[4] = gp.LeftTrigger;
            data[5] = gp.RightTrigger;
            WriteI16(data, 6, gp.ThumbLX);
            WriteI16(data, 8, gp.ThumbLY);
            WriteI16(data, 10, gp.ThumbRX);
            WriteI16(data, 12, gp.ThumbRY);
            return data;
        }

        private byte[] BuildDualShock4Input(ViiperXInputGamepad gp)
        {
            var data = new byte[31];
            // DS4 sticks are int8. Y-axis: XInput positive=UP, DS4 positive=DOWN → negate.
            data[0] = (byte)(gp.ThumbLX >> 8);
            data[1] = (byte)(NegateClamp(gp.ThumbLY) >> 8);
            data[2] = (byte)(gp.ThumbRX >> 8);
            data[3] = (byte)(NegateClamp(gp.ThumbRY) >> 8);

            ushort ds4Buttons = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) ds4Buttons |= 0x0020;
            if ((gp.Buttons & ViiperXInput.B) != 0) ds4Buttons |= 0x0040;
            if ((gp.Buttons & ViiperXInput.X) != 0) ds4Buttons |= 0x0010;
            if ((gp.Buttons & ViiperXInput.Y) != 0) ds4Buttons |= 0x0080;
            if ((gp.Buttons & ViiperXInput.LB) != 0) ds4Buttons |= 0x0100;
            if ((gp.Buttons & ViiperXInput.RB) != 0) ds4Buttons |= 0x0200;
            if ((gp.Buttons & ViiperXInput.Back) != 0) ds4Buttons |= 0x1000;
            if ((gp.Buttons & ViiperXInput.Start) != 0) ds4Buttons |= 0x2000;
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) ds4Buttons |= 0x4000;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) ds4Buttons |= 0x8000;
            if ((gp.Buttons & ViiperXInput.Guide) != 0) ds4Buttons |= 0x0001;

            // Legion back buttons -> DS4 extensions (preserve the extra-button channel the
            // user gets from Legion HID, so they don't go to waste in the DS4 mapping).
            ushort aux = currentAuxButtons;
            if ((aux & LegionAux.Mode) != 0) ds4Buttons |= 0x0001;   // Mode -> PS
            if ((aux & LegionAux.Share) != 0) ds4Buttons |= 0x0002;  // Share -> Touchpad click
            WriteU16(data, 4, ds4Buttons);

            byte dpad = 0;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) dpad |= 0x01;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) dpad |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) dpad |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) dpad |= 0x08;
            data[6] = dpad;

            data[7] = gp.LeftTrigger;
            data[8] = gp.RightTrigger;

            // IMU bytes at offsets 19-30 (gyroX,Y,Z then accelX,Y,Z as int16).
            short gx, gy, gz, ax, ay, az;
            if (TryBuildImuCounts(out gx, out gy, out gz, out ax, out ay, out az))
            {
                WriteI16(data, 19, gx);
                WriteI16(data, 21, gy);
                WriteI16(data, 23, gz);
                WriteI16(data, 25, ScaleDs4Accel(ax));
                WriteI16(data, 27, ScaleDs4Accel(ay));
                WriteI16(data, 29, ScaleDs4Accel(az));
            }
            return data;
        }

        private byte[] BuildDualSenseEdgeInput(ViiperXInputGamepad gp)
        {
            var data = new byte[33];
            data[0] = (byte)(gp.ThumbLX >> 8);
            data[1] = (byte)(NegateClamp(gp.ThumbLY) >> 8);
            data[2] = (byte)(gp.ThumbRX >> 8);
            data[3] = (byte)(NegateClamp(gp.ThumbRY) >> 8);

            uint dseButtons = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) dseButtons |= 0x0020;
            if ((gp.Buttons & ViiperXInput.B) != 0) dseButtons |= 0x0040;
            if ((gp.Buttons & ViiperXInput.X) != 0) dseButtons |= 0x0010;
            if ((gp.Buttons & ViiperXInput.Y) != 0) dseButtons |= 0x0080;
            if ((gp.Buttons & ViiperXInput.LB) != 0) dseButtons |= 0x0100;
            if ((gp.Buttons & ViiperXInput.RB) != 0) dseButtons |= 0x0200;
            if ((gp.Buttons & ViiperXInput.Back) != 0) dseButtons |= 0x1000;
            if ((gp.Buttons & ViiperXInput.Start) != 0) dseButtons |= 0x2000;
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) dseButtons |= 0x4000;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) dseButtons |= 0x8000;
            if ((gp.Buttons & ViiperXInput.Guide) != 0) dseButtons |= 0x00010000;

            // Legion back paddles -> DSE Edge paddle buttons. DSE paddle bit masks:
            //   ExtraL2=0x00100000, ExtraL1=0x00400000,
            //   ExtraR1=0x00200000, ExtraL3=0x00800000.
            ushort aux = currentAuxButtons;
            if ((aux & LegionAux.Y1) != 0) dseButtons |= 0x00100000;    // Y1 (left upper)  -> ExtraL2
            if ((aux & LegionAux.Y2) != 0) dseButtons |= 0x00400000;    // Y2 (left lower)  -> ExtraL1
            if ((aux & LegionAux.Y3) != 0) dseButtons |= 0x00200000;    // Y3 (right upper) -> ExtraR1
            if ((aux & LegionAux.M3) != 0) dseButtons |= 0x00800000;    // M3 (right lower) -> ExtraL3
            if ((aux & LegionAux.Mode) != 0) dseButtons |= 0x00010000;  // Mode  -> PS
            if ((aux & LegionAux.Share) != 0) dseButtons |= 0x00020000; // Share -> Touchpad click
            WriteU32(data, 4, dseButtons);

            byte dpad = 0;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) dpad |= 0x01;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) dpad |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) dpad |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) dpad |= 0x08;
            data[8] = dpad;

            data[9] = gp.LeftTrigger;
            data[10] = gp.RightTrigger;

            // DSE IMU bytes at offsets 21..32.
            short gx, gy, gz, ax, ay, az;
            if (TryBuildImuCounts(out gx, out gy, out gz, out ax, out ay, out az))
            {
                WriteI16(data, 21, gx);
                WriteI16(data, 23, gy);
                WriteI16(data, 25, gz);
                WriteI16(data, 27, ScaleDs4Accel(ax));
                WriteI16(data, 29, ScaleDs4Accel(ay));
                WriteI16(data, 31, ScaleDs4Accel(az));
            }
            return data;
        }

        private static byte[] BuildXboxElite2Input(ViiperXInputGamepad gp)
        {
            var data = new byte[33];
            ushort buttons = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) buttons |= 0x0001;
            if ((gp.Buttons & ViiperXInput.B) != 0) buttons |= 0x0002;
            if ((gp.Buttons & ViiperXInput.X) != 0) buttons |= 0x0004;
            if ((gp.Buttons & ViiperXInput.Y) != 0) buttons |= 0x0008;
            if ((gp.Buttons & ViiperXInput.LB) != 0) buttons |= 0x0010;
            if ((gp.Buttons & ViiperXInput.RB) != 0) buttons |= 0x0020;
            if ((gp.Buttons & ViiperXInput.Back) != 0) buttons |= 0x0040;
            if ((gp.Buttons & ViiperXInput.Start) != 0) buttons |= 0x0080;
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) buttons |= 0x0100;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) buttons |= 0x0200;
            if ((gp.Buttons & ViiperXInput.Guide) != 0) buttons |= 0x0400;
            WriteU16(data, 0, buttons);

            data[2] = gp.LeftTrigger;
            data[3] = gp.RightTrigger;

            WriteI16(data, 4, gp.ThumbLX);
            WriteI16(data, 6, gp.ThumbLY);
            WriteI16(data, 8, gp.ThumbRX);
            WriteI16(data, 10, gp.ThumbRY);

            byte dpad = 0;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) dpad |= 0x01;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) dpad |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) dpad |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) dpad |= 0x08;
            data[12] = dpad;
            return data;
        }

        // -------------------------------------------------------------------
        // Wire helpers (replacements for BitConverter.TryWriteBytes/Span which
        // aren't available on .NET Framework 4.8)
        // -------------------------------------------------------------------

        private static void WriteU16(byte[] buf, int offset, ushort value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteI16(byte[] buf, int offset, short value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteU32(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static int NegateClamp(short value)
        {
            int neg = -(int)value;
            if (neg > short.MaxValue) neg = short.MaxValue;
            if (neg < short.MinValue) neg = short.MinValue;
            return neg;
        }
    }
}
