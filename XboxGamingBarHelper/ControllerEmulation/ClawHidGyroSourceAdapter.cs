using System;
using System.Linq;
using System.Threading;
using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;
using NLog;

namespace XboxGamingBarHelper.ControllerEmulation
{
    /// <summary>
    /// Gyro source adapter that reads the controller's own vendor-HID motion stream instead of a
    /// Windows sensor. Ported from the MSI Center M decompile (API_ControlMode.dll — see
    /// reverse_engineered/RE_MSI_Gyro_and_RPM_sources.md §5), which is where the exact protocol and,
    /// importantly, the exact HidSharp usage came from. MSI's motion path is entirely model-agnostic:
    /// the same code runs on the A2VM and the Claw 8 EX (all eight 1T91 branches in that assembly are
    /// about M1/M2 key names, none touch motion), so this adapter is correct for both.
    ///
    ///   Enable : CommandType.SetMotionStatus = 47 (0x2F), payload byte 1 = start, 0 = stop.
    ///   Frame  : DeviceMessage builds { 15, 0, 0, 60, commandId } + payload
    ///            -> SetMotionStatus(true) is literally 0F 00 00 3C 2F 01.
    ///            The leading 15 is the HID REPORT ID, not a magic constant.
    ///   Data   : CommandType.MotionDataAck = 48 at byte[4]; six signed little-endian int16 axes at
    ///            byte 5,7,9 (gyro) and 11,13,15 (accel). MSI adds 32768 for its own display model —
    ///            the physical signed reading is the raw ToInt16, with no bias arithmetic.
    /// </summary>
    internal sealed class ClawHidGyroSourceAdapter : IGyroSourceAdapter
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const int VendorId = 0x0DB0;   // MSI (Center M's IncludeVidPid: VID 3504)
        private const byte CmdSetMotionStatus = 0x2F;   // 47
        private const byte CmdMotionDataAck   = 0x30;   // 48
        private const byte CommandReportId    = 15;     // vendor command interface report ID

        // Raw-LSB -> physical-unit scales. The decompile documents the frame layout but not the
        // sensor's full-scale range, so these are the defaults for the ICM-4xxxx-class IMU this
        // controller family uses (gyro +/-2000 dps, accel +/-8 g). They affect MAGNITUDE only, not
        // direction. On the A2VM the Windows-sensor path can be used side by side to calibrate them.
        private const float GyroDegPerSecondPerLsb = 1f / 16.4f;
        private const float AccelGPerLsb           = 1f / 4096f;

        private readonly object sync = new object();

        private HidStream stream;
        private HidDeviceInputReceiver receiver;
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
                    if (!TryOpenCommandInterface(out HidDevice device, out ReportDescriptor descriptor))
                    {
                        Logger.Warn("[ClawHidGyro] No vendor command interface found (no output report ID 15) - cannot start motion stream");
                        return false;
                    }

                    if (!device.TryOpen(out stream))
                    {
                        Logger.Warn("[ClawHidGyro] TryOpen failed on the vendor command interface");
                        return false;
                    }

                    // MSI runs the receiver with an infinite read timeout and drives its own 1 s wait
                    // loop; a short stream timeout is the wrong model for this interface.
                    stream.ReadTimeout = -1;
                    stream.WriteTimeout = 300;

                    receiver = descriptor.CreateHidDeviceInputReceiver();
                    receiver.Start(stream);

                    if (!SendMotionStatus(true))
                    {
                        CloseStream();
                        return false;
                    }

                    running = true;
                    readerThread = new Thread(() => ReadLoop(device.GetMaxInputReportLength()))
                    {
                        IsBackground = true,
                        Name = "ClawHidGyroReader",
                    };
                    readerThread.Start();

                    Logger.Info($"[ClawHidGyro] Motion stream started (SetMotionStatus=1) on {device.DevicePath}");
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

        /// <summary>
        /// Find the vendor command interface the way MSI does: not by usage page, but by the report
        /// descriptor carrying an OUTPUT report with ID 15. (MSI's own check is
        /// <c>descriptor.OutputReports.Any(x =&gt; x.ReportID == 15)</c>; a gamepad interface is the one
        /// with output report ID 5.) Matching on usage page 0xFFA0 happens to hit the same interface on
        /// the A2VM but is not what the vendor stack keys on.
        /// </summary>
        private static bool TryOpenCommandInterface(out HidDevice device, out ReportDescriptor descriptor)
        {
            device = null;
            descriptor = null;

            foreach (var candidate in DeviceList.Local.GetHidDevices().Where(d => d.VendorID == VendorId))
            {
                ReportDescriptor rd;
                try { rd = candidate.GetReportDescriptor(); }
                catch (Exception ex)
                {
                    Logger.Debug($"[ClawHidGyro] GetReportDescriptor failed for {candidate.DevicePath}: {ex.Message}");
                    continue;
                }
                if (rd == null) continue;

                if (rd.OutputReports.Any(r => r.ReportID == CommandReportId))
                {
                    device = candidate;
                    descriptor = rd;
                    return true;
                }
            }
            return false;
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
                try { toJoin.Join(1500); } catch { }
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
                // Exactly MSI's DeviceMessage: { 15, 0, 0, 60, commandId, payload }.
                s.Write(new byte[] { CommandReportId, 0, 0, 60, CmdSetMotionStatus, (byte)(enable ? 1 : 0) });
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ClawHidGyro] SetMotionStatus({enable}) failed: {ex.Message}");
                return false;
            }
        }

        private void ReadLoop(int maxInputReportLength)
        {
            byte[] buf = new byte[Math.Max(maxInputReportLength, 64)];

            // Diagnostics: this protocol has never been observed on our hardware, so the first runs
            // must be able to answer "does anything arrive, and what is it?". Self-limiting: the first
            // few frames verbatim, then one summary line every 5 s.
            int framesSeen = 0, framesMatched = 0, framesLogged = 0;
            var lastSummary = DateTime.UtcNow;

            while (running)
            {
                try
                {
                    if (!receiver.WaitHandle.WaitOne(1000))
                    {
                        LogSummaryIfDue(ref lastSummary, ref framesSeen, ref framesMatched);
                        continue;
                    }
                    if (!receiver.IsRunning)
                    {
                        Logger.Warn("[ClawHidGyro] Input receiver stopped (device removed or mode switched)");
                        break;
                    }

                    while (receiver.TryRead(buf, 0, out Report report))
                    {
                        int n = report.Length;
                        if (n <= 0) continue;
                        framesSeen++;

                        if (framesLogged < 8)
                        {
                            framesLogged++;
                            var sb = new System.Text.StringBuilder();
                            for (int i = 0; i < Math.Min(n, 20); i++) sb.Append(buf[i].ToString("X2")).Append(' ');
                            Logger.Info($"[ClawHidGyro] rx[{framesLogged}] id={report.ReportID} len={n}: {sb.ToString().TrimEnd()}");
                        }

                        // Need bytes 5..16 for the six axes.
                        if (n < 17 || buf[4] != CmdMotionDataAck) continue;
                        framesMatched++;

                        GyroSample sample = Parse(buf);
                        lock (sync)
                        {
                            latest = sample;
                            hasLatest = true;
                        }
                        SampleReady?.Invoke(sample);
                    }

                    LogSummaryIfDue(ref lastSummary, ref framesSeen, ref framesMatched);
                }
                catch (Exception ex)
                {
                    if (!running) break;
                    Logger.Warn($"[ClawHidGyro] Read loop error: {ex.Message}");
                    break;
                }
            }
        }

        private static void LogSummaryIfDue(ref DateTime lastSummary, ref int framesSeen, ref int framesMatched)
        {
            if ((DateTime.UtcNow - lastSummary).TotalSeconds < 5) return;
            lastSummary = DateTime.UtcNow;
            Logger.Info($"[ClawHidGyro] rx summary: frames={framesSeen}, motionFrames={framesMatched}");
            if (framesSeen == 0)
                Logger.Warn("[ClawHidGyro] no HID reports at all on the command interface - the controller is not sending motion (SetMotionStatus not accepted, or the sensor module is disabled in the device profile).");
            else if (framesMatched == 0)
                Logger.Warn("[ClawHidGyro] reports arrive but none carry the MotionDataAck command byte - see the rx dumps above for the actual layout.");
            framesSeen = framesMatched = 0;
        }

        /// <summary>
        /// Decode a MotionDataAck frame. Each axis is a SIGNED little-endian int16 — MSI's
        /// <c>BitConverter.ToInt16(data, N) + 32768</c> adds a bias purely for its own display model,
        /// so the physical reading is the plain signed value. Scaled to deg/s and g, then put through
        /// the ClawA1M axis remap so this matches the Windows-sensor path exactly.
        /// </summary>
        private static GyroSample Parse(byte[] d)
        {
            float gx = BitConverter.ToInt16(d, 5)  * GyroDegPerSecondPerLsb;
            float gy = BitConverter.ToInt16(d, 7)  * GyroDegPerSecondPerLsb;
            float gz = BitConverter.ToInt16(d, 9)  * GyroDegPerSecondPerLsb;
            float ax = BitConverter.ToInt16(d, 11) * AccelGPerLsb;
            float ay = BitConverter.ToInt16(d, 13) * AccelGPerLsb;
            float az = BitConverter.ToInt16(d, 15) * AccelGPerLsb;

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

        private void CloseStream()
        {
            try { receiver = null; } catch { }
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
