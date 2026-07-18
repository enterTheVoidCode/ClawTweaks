using System;
using System.Threading;
using HidSharp;
using NLog;
using XboxGamingBarHelper.Devices.MSIClaw;

namespace XboxGamingBarHelper.ControllerEmulation
{
    /// <summary>
    /// Gyro source adapter that reads the controller's own vendor-HID motion stream instead of a
    /// Windows sensor. This mirrors what MSI Center M does (see
    /// reverse_engineered/RE_MSI_Gyro_and_RPM_sources.md): MSI never touches a Windows sensor — it
    /// enables motion uploading over the controller's vendor HID and consumes the resulting frames.
    ///
    ///   Enable : CommandType.SetMotionStatus = 47 (0x2F), payload byte 1 = start, 0 = stop.
    ///   Frame  : CommandType.MotionDataAck   = 48 (0x30).
    ///            Little-endian uint16 per axis, each carrying a +32768 bias:
    ///            Gx @5, Gy @7, Gz @9, Ax @11, Ay @13, Az @15.
    ///
    /// Used on the Claw 8 EX (Panther Lake), which does not expose the IMU as a Windows sensor the
    /// way the A2VM does — the Windows-sensor path yields no samples there. The A2VM keeps using
    /// <see cref="ClawGyroSourceAdapter"/> (Windows sensor), so the proven dev-device path is
    /// untouched.
    ///
    /// The output is put through the SAME ClawA1M axis remap the Windows-sensor path applies, so
    /// everything downstream (gyro-to-stick, gyro-mouse, calibration) sees identical conventions on
    /// both devices.
    /// </summary>
    internal sealed class ClawHidGyroSourceAdapter : IGyroSourceAdapter
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Vendor command frame layout, identical to MSIClawHidController's SwitchMode frames:
        // { 0x0F, 0x00, 0x00, 0x3C, <CommandType>, <payload...> }
        private const byte CmdSetMotionStatus = 0x2F;   // 47
        private const byte CmdMotionDataAck   = 0x30;   // 48
        private const int  AxisBias           = 32768;

        // Raw-LSB -> physical-unit scales. The decompile documents the frame LAYOUT but not the
        // sensor's full-scale range, so these are the defaults for the ICM-4xxxx-class IMU this
        // controller family uses (gyro +/-2000 dps, accel +/-8 g). They only affect MAGNITUDE, not
        // direction — if the EX gyro moves the right way but feels too fast/slow, this is the knob.
        private const float GyroDegPerSecondPerLsb = 1f / 16.4f;
        private const float AccelGPerLsb           = 1f / 4096f;

        private readonly object sync = new object();

        private HidStream stream;
        private Thread readerThread;
        private volatile bool running;
        private GyroSample latest;
        private bool hasLatest;

        public string Name => "MSI Claw Controller Motion (vendor HID)";

        public event Action<GyroSample> SampleReady;

        public bool Start()
        {
            lock (sync)
            {
                if (running) return true;

                try
                {
                    HidDevice device = MSIClawHidController.FindClawHidDeviceInternal();
                    if (device == null)
                    {
                        Logger.Warn("[ClawHidGyro] Controller vendor HID not found - cannot start motion stream");
                        return false;
                    }

                    stream = device.Open();
                    // No write timeout concerns; reads must time out so the thread can observe `running`.
                    stream.WriteTimeout = 300;
                    stream.ReadTimeout  = 500;

                    if (!SendMotionStatus(true))
                    {
                        CloseStream();
                        return false;
                    }

                    running = true;
                    readerThread = new Thread(ReadLoop)
                    {
                        IsBackground = true,
                        Name = "ClawHidGyroReader",
                    };
                    readerThread.Start();

                    Logger.Info("[ClawHidGyro] Motion stream started (SetMotionStatus=1)");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[ClawHidGyro] Start failed: {ex.Message}");
                    CloseStream();
                    return false;
                }
            }
        }

        public void Stop()
        {
            Thread toJoin;
            lock (sync)
            {
                if (!running) return;
                running = false;
                toJoin = readerThread;
                readerThread = null;

                // Tell the firmware to stop uploading before the stream goes away, so the controller
                // is not left streaming motion frames at nobody.
                try { SendMotionStatus(false); } catch { }
                CloseStream();
                hasLatest = false;
            }

            // Joined outside the lock: the reader takes `sync` when publishing samples.
            if (toJoin != null && toJoin.IsAlive)
            {
                try { toJoin.Join(1000); } catch { }
            }

            Logger.Info("[ClawHidGyro] Motion stream stopped (SetMotionStatus=0)");
        }

        public bool TryGetLatestSample(out GyroSample sample)
        {
            lock (sync)
            {
                sample = latest;
                return hasLatest;
            }
        }

        private bool SendMotionStatus(bool enable)
        {
            HidStream s = stream;
            if (s == null) return false;
            try
            {
                byte[] msg = new byte[64];
                msg[0] = 0x0F;
                msg[3] = 0x3C;
                msg[4] = CmdSetMotionStatus;
                msg[5] = (byte)(enable ? 1 : 0);
                s.Write(msg);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ClawHidGyro] SetMotionStatus({enable}) failed: {ex.Message}");
                return false;
            }
        }

        private void ReadLoop()
        {
            byte[] buf = new byte[64];
            int consecutiveErrors = 0;

            while (running)
            {
                int n;
                try
                {
                    n = stream.Read(buf);
                    consecutiveErrors = 0;
                }
                catch (TimeoutException)
                {
                    continue;   // no frame in this window; the controller only sends while moving
                }
                catch (Exception ex)
                {
                    // The device can drop out on a mode switch (XInput <-> DInput) or on sleep/resume.
                    if (!running) break;
                    if (++consecutiveErrors >= 5)
                    {
                        Logger.Warn($"[ClawHidGyro] Read loop giving up after repeated errors: {ex.Message}");
                        break;
                    }
                    Thread.Sleep(50);
                    continue;
                }

                // Need bytes 5..16 for the six axes.
                if (n < 17 || buf[4] != CmdMotionDataAck) continue;

                GyroSample sample = Parse(buf);

                lock (sync)
                {
                    latest = sample;
                    hasLatest = true;
                }

                SampleReady?.Invoke(sample);
            }
        }

        /// <summary>
        /// Decode a MotionDataAck frame. Each axis is a little-endian uint16 biased by +32768, so the
        /// signed reading is (raw - 32768); scaled to deg/s and g, then put through the ClawA1M axis
        /// remap so this matches the Windows-sensor path exactly.
        /// </summary>
        private static GyroSample Parse(byte[] d)
        {
            float gx = Axis(d, 5)  * GyroDegPerSecondPerLsb;
            float gy = Axis(d, 7)  * GyroDegPerSecondPerLsb;
            float gz = Axis(d, 9)  * GyroDegPerSecondPerLsb;
            float ax = Axis(d, 11) * AccelGPerLsb;
            float ay = Axis(d, 13) * AccelGPerLsb;
            float az = Axis(d, 15) * AccelGPerLsb;

            // Same remap as ClawGyroSourceAdapter (HC ClawA1M.cs): Y<->Z swapped, gyro Z negated,
            // accel X and Y negated.
            return new GyroSample(
                gyroXDegPerSecond:  gx,
                gyroYDegPerSecond:  gz,
                gyroZDegPerSecond: -gy,
                accelXG:           -ax,
                accelYG:           -az,
                accelZG:            ay,
                timestampTicksUtc:  DateTime.UtcNow.Ticks);
        }

        private static int Axis(byte[] d, int offset)
            => (d[offset] | (d[offset + 1] << 8)) - AxisBias;

        private void CloseStream()
        {
            HidStream s = stream;
            stream = null;
            if (s == null) return;
            try { s.Close(); } catch { }
            try { s.Dispose(); } catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
