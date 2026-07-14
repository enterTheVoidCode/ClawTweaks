using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HidSharp;

namespace Claw8EXProbes.ControllerMotionProbe
{
    // Phase 3 P2 - tests whether the Claw's gyroscope lives in the CONTROLLER hardware
    // (streamed over its own HID command interface) rather than the laptop chassis's Intel
    // Sensor Hub. Per HandheldCompanion's ClawA1M.cs, the Claw controller firmware has a
    // "SetMotionStatus" vendor command (report id 0x0F, commandType 0x2F) that turns on IMU
    // report streaming - HC sends this but its own readout path (SensorFamily.Controller) has
    // no actual implementation, so this has never been verified end-to-end anywhere. This probe:
    //   1. Finds the vendor command interface exactly like MSIClawHidController.FindClawHidDevice
    //      (VID 0x0DB0, UsagePage 0xFFA0/Usage 0x0001 in XInput mode, or 0xFFF0/0x0040 in DInput).
    //   2. Sends SetMotionStatus(enable=1): { 0x0F, 0, 0, 0x3C, 0x2F, 1 } padded to 64 bytes,
    //      matching MSIClawHidController.TrySendModeCmd's wire format exactly.
    //   3. Reads raw Input reports from the SAME device for ~12 seconds, printing hex whenever
    //      a report's bytes change - if gyro data is streaming, tilting the device should
    //      produce visibly changing bytes; sitting still should look static.
    //   4. Sends SetMotionStatus(enable=0) to restore original state before exiting.
    internal class Program
    {
        private const int VendorId = 0x0DB0;
        private const int CmdUsagePageXInput = 0xFFA0;
        private const int CmdUsageXInput = 0x0001;
        private const int CmdUsagePageDInput = 0xFFF0;
        private const int CmdUsageDInput = 0x0040;

        private static readonly byte[] SetMotionStatusEnable = { 15, 0, 0, 60, 47, 1 };
        private static readonly byte[] SetMotionStatusDisable = { 15, 0, 0, 60, 47, 0 };

        private static void Main()
        {
            var cmdDevice = FindClawCommandDevice();
            if (cmdDevice == null)
            {
                Console.WriteLine("Could not find the Claw vendor command interface (VID 0x0DB0, UsagePage 0xFFA0 or 0xFFF0). Aborting.");
                return;
            }

            var allClawDevices = DeviceList.Local.GetHidDevices().Where(d => d.VendorID == VendorId).ToList();
            Console.WriteLine($"Found {allClawDevices.Count} VID_0DB0 device(s). Command interface: {cmdDevice.DevicePath}");
            Console.WriteLine();

            using (HidStream cmdStream = cmdDevice.Open())
            {
                Console.WriteLine("Sending SetMotionStatus(enable=1) [0x0F,0,0,0x3C,0x2F,1] on the command interface...");
                SendCommand(cmdStream, SetMotionStatusEnable);
                Console.WriteLine();

                // Open every VID_0DB0 interface we can (skip ones exclusively locked by other
                // drivers, e.g. the XInput-claimed collection) and read them all in parallel.
                var openStreams = new List<(HidDevice device, HidStream stream)>();
                foreach (var d in allClawDevices)
                {
                    try
                    {
                        var s = d.Open();
                        s.ReadTimeout = 150;
                        openStreams.Add((d, s));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not open {d.DevicePath}: {ex.Message}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"Listening on {openStreams.Count} interface(s) for 12 seconds. TILT/ROTATE THE DEVICE NOW.");
                Console.WriteLine("Printing whenever a report's bytes change from that interface's previous report.");
                Console.WriteLine();

                var lastReports = new Dictionary<string, byte[]>();
                var totalByDevice = new Dictionary<string, int>();
                var changedByDevice = new Dictionary<string, int>();
                var threads = new List<Thread>();
                var stopFlag = new System.Threading.ManualResetEventSlim(false);

                foreach (var (device, stream) in openStreams)
                {
                    string key = device.DevicePath;
                    lastReports[key] = null;
                    totalByDevice[key] = 0;
                    changedByDevice[key] = 0;

                    var t = new Thread(() =>
                    {
                        byte[] buffer = new byte[64];
                        while (!stopFlag.IsSet)
                        {
                            int read;
                            try
                            {
                                read = stream.Read(buffer, 0, buffer.Length);
                            }
                            catch (TimeoutException)
                            {
                                continue;
                            }
                            catch
                            {
                                break;
                            }

                            if (read <= 0) continue;
                            lock (lastReports)
                            {
                                totalByDevice[key]++;
                                var current = buffer.Take(read).ToArray();
                                var last = lastReports[key];
                                if (last == null || !current.SequenceEqual(last))
                                {
                                    changedByDevice[key]++;
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {ShortName(key)} ({read}B) {BitConverter.ToString(current)}");
                                    lastReports[key] = current;
                                }
                            }
                        }
                    });
                    t.IsBackground = true;
                    threads.Add(t);
                    t.Start();
                }

                Thread.Sleep(12000);
                stopFlag.Set();
                foreach (var t in threads) t.Join(500);

                Console.WriteLine();
                Console.WriteLine("Per-interface summary:");
                foreach (var key in totalByDevice.Keys)
                {
                    Console.WriteLine($"  {ShortName(key)}: total={totalByDevice[key]} changed={changedByDevice[key]}");
                }

                foreach (var (_, stream) in openStreams)
                {
                    try { stream.Dispose(); } catch { }
                }

                Console.WriteLine();
                Console.WriteLine("Sending SetMotionStatus(enable=0) to restore original state...");
                SendCommand(cmdStream, SetMotionStatusDisable);
            }

            Console.WriteLine("Done.");
        }

        private static string ShortName(string devicePath)
        {
            // Trim the long HID GUID suffix for readability.
            int idx = devicePath.IndexOf("#{", StringComparison.Ordinal);
            return idx > 0 ? devicePath.Substring(0, idx) : devicePath;
        }

        private static void SendCommand(HidStream stream, byte[] cmd)
        {
            byte[] msg = new byte[64];
            Array.Copy(cmd, msg, cmd.Length);
            try
            {
                stream.Write(msg);
                Console.WriteLine("  Sent OK.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Send failed: {ex.Message}");
            }
        }

        private static int SafeMaxInputLen(HidDevice d)
        {
            try { return d.GetMaxInputReportLength(); } catch { return -1; }
        }

        private static HidDevice FindClawCommandDevice()
        {
            var candidates = DeviceList.Local.GetHidDevices()
                .Where(d => d.VendorID == VendorId)
                .ToList();

            foreach (var device in candidates)
            {
                try
                {
                    var descriptor = device.GetReportDescriptor();
                    if (descriptor == null) continue;

                    foreach (var item in descriptor.DeviceItems)
                    {
                        foreach (uint encodedUsage in item.Usages.GetAllValues())
                        {
                            int page = (int)((encodedUsage >> 16) & 0xFFFF);
                            int usage = (int)(encodedUsage & 0xFFFF);
                            if ((page == CmdUsagePageXInput && usage == CmdUsageXInput) ||
                                (page == CmdUsagePageDInput && usage == CmdUsageDInput))
                                return device;
                        }
                    }
                }
                catch
                {
                    // Restricted interface - skip
                }
            }

            return null;
        }
    }
}
