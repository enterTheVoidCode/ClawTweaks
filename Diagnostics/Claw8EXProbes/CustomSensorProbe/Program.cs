using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Sensors;
using Windows.Devices.Sensors.Custom;

namespace Claw8EXProbes.CustomSensorProbe
{
    // Follow-up to GyroProbe's null-Gyrometer finding on the Claw 8 AI+ EX.
    //
    // Registry/CM-API discovery (2026-07-05, see docs/hardware/CLAW8_EX_PORT_LOG.md):
    // the Intel ISH "HID Sensor Collection V2" node publishes FOUR live device
    // interfaces whose interface-class GUIDs follow the HID-usage-derived custom
    // sensor pattern {000000XX-766d-4333-8262-27e82dd158b1}:
    //   0x73 = Accelerometer 3D ("Physical Accelerometer" reference string)
    //   0x76 = Gyrometer 3D     ("Physical Gyrometer")
    //   0x233, 0x302 = unknown vendor usages
    // ...while GUID_DEVINTERFACE_SENSOR (what Gyrometer.GetDefault()/the Win32
    // Sensor COM API enumerate) has ZERO interfaces system-wide. So the standard
    // sensor stack sees nothing, but the data may still be readable through
    // Windows.Devices.Sensors.Custom.CustomSensor, which selects by exactly those
    // usage-derived interface-class GUIDs.
    //
    // This probe, read-only:
    //  1. prints Gyrometer/Accelerometer.GetDeviceSelector() AQS + match counts
    //     (expected 0 - documents WHY GetDefault() is null),
    //  2. for each of the four usage GUIDs: enumerates DeviceInformation matches,
    //     opens the first via CustomSensor.FromIdAsync, prints report interval and
    //     ~5 s of readings (full property bag) so we can see live gyro data and
    //     learn the property key names an adapter would need.
    internal class Program
    {
        private static readonly (string Label, Guid InterfaceClass)[] UsageGuids =
        {
            ("Accelerometer3D (usage 0x73)", new Guid("00000073-766d-4333-8262-27e82dd158b1")),
            ("Gyrometer3D (usage 0x76)",     new Guid("00000076-766d-4333-8262-27e82dd158b1")),
            ("Unknown usage 0x233",          new Guid("00000233-766d-4333-8262-27e82dd158b1")),
            ("Unknown usage 0x302",          new Guid("00000302-766d-4333-8262-27e82dd158b1")),
        };

        private static async Task Main()
        {
            Console.WriteLine("=== Standard WinRT sensor selectors (for comparison) ===");
            await DumpSelectorAsync("Gyrometer", Gyrometer.GetDeviceSelector());
            await DumpSelectorAsync("Accelerometer", Accelerometer.GetDeviceSelector(AccelerometerReadingType.Standard));

            foreach (var (label, guid) in UsageGuids)
            {
                Console.WriteLine();
                Console.WriteLine($"=== CustomSensor probe: {label} ===");
                await ProbeCustomSensorAsync(label, guid);
            }
        }

        private static async Task DumpSelectorAsync(string name, string selector)
        {
            Console.WriteLine($"{name}.GetDeviceSelector(): {selector}");
            var matches = await DeviceInformation.FindAllAsync(selector);
            Console.WriteLine($"{name} selector matches: {matches.Count}");
            foreach (var d in matches)
            {
                Console.WriteLine($"  - {d.Id} ('{d.Name}', enabled={d.IsEnabled})");
            }
        }

        private static async Task ProbeCustomSensorAsync(string label, Guid interfaceClass)
        {
            string selector = CustomSensor.GetDeviceSelector(interfaceClass);
            var matches = await DeviceInformation.FindAllAsync(selector);
            Console.WriteLine($"DeviceInformation matches: {matches.Count}");
            foreach (var d in matches)
            {
                Console.WriteLine($"  - {d.Id} ('{d.Name}', enabled={d.IsEnabled})");
            }
            if (matches.Count == 0)
            {
                return;
            }

            CustomSensor sensor;
            try
            {
                sensor = await CustomSensor.FromIdAsync(matches[0].Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CustomSensor.FromIdAsync threw: {ex.GetType().Name}: {ex.Message}");
                return;
            }
            if (sensor == null)
            {
                Console.WriteLine("CustomSensor.FromIdAsync returned NULL (access denied or claimed elsewhere).");
                return;
            }

            Console.WriteLine($"Opened. DeviceId={sensor.DeviceId}");
            Console.WriteLine($"MinimumReportInterval={sensor.MinimumReportInterval}ms");
            uint interval = sensor.MinimumReportInterval > 0 ? sensor.MinimumReportInterval : 16;
            sensor.ReportInterval = interval;
            Console.WriteLine($"ReportInterval set to {interval}ms");

            int eventCount = 0;
            var printedKeys = false;
            var lastPrint = DateTime.MinValue;
            void OnReading(CustomSensor s, CustomSensorReadingChangedEventArgs e)
            {
                int n = Interlocked.Increment(ref eventCount);
                // Print the full property bag on the first event (key discovery),
                // then throttle to ~2 lines/second so the console stays readable.
                var now = DateTime.UtcNow;
                if (!printedKeys || (now - lastPrint) > TimeSpan.FromMilliseconds(500))
                {
                    printedKeys = true;
                    lastPrint = now;
                    var parts = new List<string>();
                    foreach (var kv in e.Reading.Properties)
                    {
                        parts.Add($"{kv.Key}={FormatValue(kv.Value)}");
                    }
                    Console.WriteLine($"  [{n}] ts={e.Reading.Timestamp:HH:mm:ss.fff} {string.Join(", ", parts)}");
                }
            }

            sensor.ReadingChanged += OnReading;
            Console.WriteLine("Listening for 5 seconds - tilt/rotate the device now...");
            await Task.Delay(TimeSpan.FromSeconds(5));
            sensor.ReadingChanged -= OnReading;
            Console.WriteLine($"Total ReadingChanged events in 5s: {eventCount}");
        }

        private static string FormatValue(object value)
        {
            switch (value)
            {
                case null: return "null";
                case double d: return d.ToString("F4");
                case float f: return f.ToString("F4");
                case byte[] bytes: return $"byte[{bytes.Length}]={BitConverter.ToString(bytes, 0, Math.Min(bytes.Length, 32))}";
                default: return value.ToString();
            }
        }
    }
}
