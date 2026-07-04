using System;
using System.Collections.Generic;
using System.Linq;
using HidSharp;

namespace Claw8EXProbes.SensorDescriptorProbe
{
    // Phase 3 P1 - raw HID report-descriptor walk for the Claw 8 AI+ EX gyro question.
    //
    // Windows.Devices.Sensors.Gyrometer.GetDefault() returns null on this device (see
    // GyroProbe + CLAW8_EX_PORT_LOG.md), even undocked. Human confirmed there IS a physical
    // gyroscope on this hardware. So either (a) it's declared in a HID Sensor collection that
    // the WinRT Sensor API isn't surfacing for some driver reason, or (b) it lives somewhere
    // this probe doesn't look. This bypasses HidSharp's high-level ReportDescriptor/DeviceItem
    // abstraction (which only surfaces top-level Application collections) and manually walks
    // every Main/Global/Local item in the raw descriptor bytes, so nested Collections (Logical/
    // Physical) and their Usage announcements are visible - that's where a HID Sensor page
    // (0x0020) device declares its per-sensor-type sub-collections (e.g. Gyrometer 3D = 0x76).
    internal class Program
    {
        private static readonly Dictionary<int, string> SensorPageUsageNames = new Dictionary<int, string>
        {
            { 0x0001, "Sensor (top-level)" },
            { 0x0010, "Category: Biometric" },
            { 0x0011, "Category: Electrical" },
            { 0x0012, "Category: Environmental" },
            { 0x0013, "Category: Light" },
            { 0x0014, "Category: Location" },
            { 0x0015, "Category: Mechanical" },
            { 0x0016, "Category: Motion" },
            { 0x0017, "Category: Orientation" },
            { 0x0018, "Category: Scanner" },
            { 0x0019, "Category: Time" },
            { 0x001A, "Category: PersonalActivity" },
            { 0x001B, "Category: OrientationExtended" },
            { 0x001C, "Category: Gesture" },
            { 0x001D, "Category: Other" },
            { 0x001E, "Category: VendorReserved" },
            { 0x0073, "Motion: Accelerometer 3D" },
            { 0x0074, "Motion: Accelerometer 2D" },
            { 0x0075, "Motion: Accelerometer 1D" },
            { 0x0076, "Motion: GYROMETER 3D" },
            { 0x0077, "Motion: Gyrometer 2D" },
            { 0x0078, "Motion: Gyrometer 1D" },
            { 0x0079, "Motion: Motion Detector" },
            { 0x007A, "Motion: Speedometer" },
            { 0x0080, "Orientation: Inclinometer 3D" },
            { 0x0082, "Orientation: Compass 3D" },
            { 0x0084, "Orientation: Device Orientation" },
            { 0x0086, "Orientation: Inclinometer 3D (alt)" },
            { 0x008A, "Other: Custom" },
        };

        private static void Main()
        {
            var allDevices = DeviceList.Local.GetHidDevices().ToList();
            Console.WriteLine("All HID devices visible to HidSharp (VID:PID):");
            foreach (var d in allDevices.OrderBy(d => d.VendorID).ThenBy(d => d.ProductID))
            {
                Console.WriteLine($"  0x{d.VendorID:X4}:0x{d.ProductID:X4}  {d.DevicePath}");
            }
            Console.WriteLine();

            var devices = allDevices
                .Where(d => d.VendorID == 0x8087) // Intel
                .OrderBy(d => d.DevicePath)
                .ToList();

            if (devices.Count == 0)
            {
                Console.WriteLine("No VID_8087 (Intel) HID devices found. Falling back to ALL HID devices with a Sensor-page (0x0020) top-level usage.");
                devices = DeviceList.Local.GetHidDevices().ToList();
            }

            Console.WriteLine($"Examining {devices.Count} candidate device(s).");
            Console.WriteLine();

            foreach (var device in devices)
            {
                Console.WriteLine($"=== DevicePath: {device.DevicePath} ===");
                Console.WriteLine($"VID: 0x{device.VendorID:X4}  PID: 0x{device.ProductID:X4}  Release: 0x{device.ReleaseNumberBcd:X4}");

                byte[] raw;
                try
                {
                    raw = device.GetRawReportDescriptor();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Could not read raw report descriptor: {ex.Message}");
                    Console.WriteLine();
                    continue;
                }

                Console.WriteLine($"  Raw descriptor: {raw.Length} bytes");
                WalkDescriptor(raw);
                Console.WriteLine();
            }

            Console.WriteLine("Done.");
        }

        // Minimal HID report-descriptor walker: tracks Usage Page (Global) and Usage (Local)
        // announcements and prints them alongside Collection Open/Close (Main) items, so nested
        // sensor-type sub-collections are visible, not just the top-level Application collection.
        private static void WalkDescriptor(byte[] data)
        {
            int i = 0;
            int usagePage = 0;
            int depth = 0;
            var pendingUsages = new List<int>();

            while (i < data.Length)
            {
                byte prefix = data[i++];
                int size = prefix & 0x03;
                int byteCount = size == 3 ? 4 : size;
                int type = (prefix >> 2) & 0x03; // 0=Main,1=Global,2=Local
                int tag = (prefix >> 4) & 0x0F;

                if (i + byteCount > data.Length)
                {
                    Console.WriteLine("  <descriptor truncated / malformed - stopping walk>");
                    break;
                }

                long value = 0;
                for (int b = 0; b < byteCount; b++)
                {
                    value |= ((long)data[i + b]) << (8 * b);
                }
                i += byteCount;

                switch (type)
                {
                    case 1: // Global
                        if (tag == 0) // Usage Page
                        {
                            usagePage = (int)value;
                        }
                        break;

                    case 2: // Local
                        if (tag == 0) // Usage
                        {
                            int page = byteCount == 4 ? (int)((value >> 16) & 0xFFFF) : usagePage;
                            int usage = (int)(value & 0xFFFF);
                            pendingUsages.Add((page << 16) | usage);
                        }
                        break;

                    case 0: // Main
                        if (tag == 0x0A || tag == 0x08 || tag == 0x09 || tag == 0x0B) // Collection/Input/Output/Feature
                        {
                            string tagName = tag == 0x0A ? "Collection(" + CollectionTypeName((int)value) + ")"
                                : tag == 0x08 ? "Input"
                                : tag == 0x09 ? "Output"
                                : "Feature";

                            if (pendingUsages.Count > 0)
                            {
                                foreach (var pu in pendingUsages)
                                {
                                    int page = pu >> 16;
                                    int usage = pu & 0xFFFF;
                                    string indent = new string(' ', depth * 2);
                                    string note = page == 0x0020 && SensorPageUsageNames.TryGetValue(usage, out var name)
                                        ? $"  <-- Sensor page usage: {name}"
                                        : "";
                                    Console.WriteLine($"  {indent}{tagName}  UsagePage=0x{page:X4} Usage=0x{usage:X4}{note}");
                                }
                            }
                            else
                            {
                                string indent = new string(' ', depth * 2);
                                Console.WriteLine($"  {indent}{tagName}  (no local usage set)");
                            }
                            pendingUsages.Clear();

                            if (tag == 0x0A) depth++;
                        }
                        else if (tag == 0x0C) // End Collection
                        {
                            depth = Math.Max(0, depth - 1);
                        }
                        break;
                }
            }
        }

        private static string CollectionTypeName(int value)
        {
            switch (value)
            {
                case 0x00: return "Physical";
                case 0x01: return "Application";
                case 0x02: return "Logical";
                case 0x03: return "Report";
                case 0x04: return "NamedArray";
                case 0x05: return "UsageSwitch";
                case 0x06: return "UsageModifier";
                default: return $"0x{value:X2}";
            }
        }
    }
}
