using System;
using System.Linq;
using HidSharp;

namespace Claw8EXProbes.HidInventory
{
    // Read-only HID enumeration probe for the Claw 8 AI+ EX port (Phase 1b).
    // Lists every HID device matching MSI's vendor ID (0x0DB0), and if none are
    // found, falls back to listing every HID device on the system. For each
    // match, prints VID/PID, device path, usage page/usage per top-level
    // collection, and max report lengths - modeled on the enumeration code in
    // XboxGamingBarHelper/Devices/MSIClaw/MSIClawHidController.cs.
    internal class Program
    {
        private const int MsiVendorId = 0x0DB0;

        private static void Main()
        {
            var devices = DeviceList.Local.GetHidDevices().ToList();
            var msiDevices = devices.Where(d => d.VendorID == MsiVendorId).ToList();

            Console.WriteLine($"Total HID devices on system: {devices.Count}");
            Console.WriteLine($"MSI (VID 0x{MsiVendorId:X4}) HID devices: {msiDevices.Count}");
            Console.WriteLine();

            var toPrint = msiDevices.Count > 0 ? msiDevices : devices;
            if (msiDevices.Count == 0)
            {
                Console.WriteLine("No VID_0DB0 devices found via HidSharp - listing ALL HID devices instead.");
                Console.WriteLine();
            }

            foreach (var device in toPrint.OrderBy(d => d.VendorID).ThenBy(d => d.ProductID).ThenBy(d => d.DevicePath))
            {
                Console.WriteLine($"DevicePath: {device.DevicePath}");
                Console.WriteLine($"  VID: 0x{device.VendorID:X4}  PID: 0x{device.ProductID:X4}  Release: 0x{device.ReleaseNumberBcd:X4}");
                try { Console.WriteLine($"  ProductName: {device.GetProductName()}"); } catch (Exception ex) { Console.WriteLine($"  ProductName: <error: {ex.Message}>"); }
                try { Console.WriteLine($"  Manufacturer: {device.GetManufacturer()}"); } catch (Exception ex) { Console.WriteLine($"  Manufacturer: <error: {ex.Message}>"); }
                try { Console.WriteLine($"  SerialNumber: {device.GetSerialNumber()}"); } catch (Exception ex) { Console.WriteLine($"  SerialNumber: <error: {ex.Message}>"); }
                try { Console.WriteLine($"  MaxInputReportLength: {device.MaxInputReportLength}  MaxOutputReportLength: {device.MaxOutputReportLength}  MaxFeatureReportLength: {device.MaxFeatureReportLength}"); }
                catch (Exception ex) { Console.WriteLine($"  Report lengths: <error: {ex.Message}>"); }

                try
                {
                    var rawReportDescriptor = device.GetRawReportDescriptor();
                    var parser = device.GetReportDescriptor();
                    Console.WriteLine($"  RawReportDescriptor length: {rawReportDescriptor.Length} bytes");
                    foreach (var deviceItem in parser.DeviceItems)
                    {
                        Console.WriteLine($"  Collection: UsagePage=0x{(deviceItem.Usages.GetAllValues().FirstOrDefault() >> 16):X4} Usages=[{string.Join(",", deviceItem.Usages.GetAllValues().Select(u => $"0x{u:X8}"))}]");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ReportDescriptor: <error: {ex.Message}>");
                }

                Console.WriteLine();
            }

            Console.WriteLine("Done.");
        }
    }
}
