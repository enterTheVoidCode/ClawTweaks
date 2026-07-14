using System;
using System.Threading;
using Windows.Devices.Sensors;

namespace Claw8EXProbes.GyroProbe
{
    // Read-only gyro/accelerometer presence probe for the Claw 8 AI+ EX port (Phase 1c).
    // Mirrors the acquisition pattern in
    // XboxGamingBarHelper/ControllerEmulation/GyroSourceAdapters.cs (WindowsSensorGyroSourceAdapter):
    // Gyrometer.GetDefault() / Accelerometer.GetDefault(), then sets ReportInterval to the
    // device minimum (falling back to 4ms) before subscribing to ReadingChanged.
    internal class Program
    {
        private static void Main()
        {
            var gyrometer = Gyrometer.GetDefault();
            var accelerometer = Accelerometer.GetDefault();

            Console.WriteLine($"Gyrometer.GetDefault(): {(gyrometer == null ? "NULL - no gyrometer present" : "present")}");
            Console.WriteLine($"Accelerometer.GetDefault(): {(accelerometer == null ? "NULL - no accelerometer present" : "present")}");

            if (gyrometer == null)
            {
                Console.WriteLine("No gyrometer - nothing further to probe.");
                return;
            }

            Console.WriteLine($"Gyrometer.MinimumReportInterval: {gyrometer.MinimumReportInterval}ms");
            uint interval = gyrometer.MinimumReportInterval > 0 ? gyrometer.MinimumReportInterval : 4;
            gyrometer.ReportInterval = interval;
            Console.WriteLine($"Set Gyrometer.ReportInterval to {interval}ms (was {gyrometer.ReportInterval}ms after set)");

            if (accelerometer != null)
            {
                Console.WriteLine($"Accelerometer.MinimumReportInterval: {accelerometer.MinimumReportInterval}ms");
                uint accelInterval = accelerometer.MinimumReportInterval > 0 ? accelerometer.MinimumReportInterval : 4;
                accelerometer.ReportInterval = accelInterval;
            }

            Console.WriteLine();
            Console.WriteLine("Sampling for 5 seconds AT REST - do not touch the device...");
            SampleFor(gyrometer, accelerometer, TimeSpan.FromSeconds(5));

            Console.WriteLine();
            Console.WriteLine("Done with at-rest sample.");
        }

        private static void SampleFor(Gyrometer gyrometer, Accelerometer accelerometer, TimeSpan duration)
        {
            var end = DateTime.UtcNow + duration;
            int count = 0;
            double sumX = 0, sumY = 0, sumZ = 0;
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            while (DateTime.UtcNow < end)
            {
                var reading = gyrometer.GetCurrentReading();
                if (reading != null)
                {
                    count++;
                    sumX += reading.AngularVelocityX;
                    sumY += reading.AngularVelocityY;
                    sumZ += reading.AngularVelocityZ;
                    minX = Math.Min(minX, reading.AngularVelocityX);
                    minY = Math.Min(minY, reading.AngularVelocityY);
                    minZ = Math.Min(minZ, reading.AngularVelocityZ);
                    maxX = Math.Max(maxX, reading.AngularVelocityX);
                    maxY = Math.Max(maxY, reading.AngularVelocityY);
                    maxZ = Math.Max(maxZ, reading.AngularVelocityZ);
                }
                Thread.Sleep(20);
            }

            Console.WriteLine($"Samples: {count}");
            if (count > 0)
            {
                Console.WriteLine($"Gyro AngularVelocity avg (deg/s): X={sumX / count:F3} Y={sumY / count:F3} Z={sumZ / count:F3}");
                Console.WriteLine($"Gyro AngularVelocity range: X=[{minX:F3},{maxX:F3}] Y=[{minY:F3},{maxY:F3}] Z=[{minZ:F3},{maxZ:F3}]");
            }
            else
            {
                Console.WriteLine("No readings captured via GetCurrentReading() - device may require event-based ReadingChanged subscription instead.");
            }

            if (accelerometer != null)
            {
                var accReading = accelerometer.GetCurrentReading();
                if (accReading != null)
                {
                    Console.WriteLine($"Accelerometer current reading (g): X={accReading.AccelerationX:F3} Y={accReading.AccelerationY:F3} Z={accReading.AccelerationZ:F3}");
                }
            }
        }
    }
}
