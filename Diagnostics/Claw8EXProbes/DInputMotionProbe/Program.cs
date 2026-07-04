using System;
using System.Linq;
using System.Threading;
using HidSharp;
using SharpDX.DirectInput;

namespace Claw8EXProbes.DInputMotionProbe
{
    // Phase 3 P2 - last gyro avenue: check whether DirectInput exposes extra vendor
    // axes/sliders (beyond the standard stick/trigger axes) once the Claw controller is in
    // DInput mode (PID 0x1902), that raw HID report reading didn't reveal. Mirrors
    // ClawButtonMonitor.cs's FindAndAcquireJoystick() pattern exactly: DirectInput() →
    // GetDevices(Gamepad) → match VID_0DB0/PID_1902 in InterfacePath → new Joystick(...) →
    // Acquire(). Switches the controller to DInput via the same HID command
    // MSIClawHidController uses, polls JoystickState for 12s while the device is moved, then
    // switches back to XInput.
    internal class Program
    {
        private const int VendorId = 0x0DB0;
        private const int CmdUsagePageXInput = 0xFFA0;
        private const int CmdUsageXInput = 0x0001;

        private static readonly byte[] SwitchModeDInputCmd = { 15, 0, 0, 60, 36, 2, 0 };
        private static readonly byte[] SwitchModeXInputCmd = { 15, 0, 0, 60, 36, 1, 0 };

        private static void Main()
        {
            Console.WriteLine("Switching controller to DInput mode...");
            if (!SendModeCommand(SwitchModeDInputCmd))
            {
                Console.WriteLine("Could not send DInput switch command (command interface not found). Aborting.");
                return;
            }

            Console.WriteLine("Waiting for re-enumeration (retrying acquire for up to 8s)...");
            Joystick joystick = null;
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && joystick == null)
            {
                Thread.Sleep(500);
                joystick = FindAndAcquireJoystick();
            }

            if (joystick == null)
            {
                Console.WriteLine("Could not find/acquire DInput joystick (VID_0DB0/PID_1902) after switch. Restoring XInput mode and aborting.");
                SendModeCommand(SwitchModeXInputCmd);
                return;
            }

            using (joystick)
            {
                Console.WriteLine($"Acquired: {joystick.Information.ProductName}  InterfacePath: {joystick.Properties.InterfacePath}");
                Console.WriteLine();
                Console.WriteLine("Device objects (axes/sliders/buttons/POV):");
                foreach (var obj in joystick.GetObjects())
                {
                    Console.WriteLine($"  Offset={obj.Offset} Type={obj.ObjectType} Aspect={obj.Aspect}");
                }

                Console.WriteLine();
                Console.WriteLine("Polling JoystickState for 12 seconds. TILT/ROTATE THE DEVICE NOW.");
                Console.WriteLine("Printing whenever axis/slider values change.");
                Console.WriteLine();

                JoystickState last = null;
                var end = DateTime.UtcNow.AddSeconds(12);
                int reads = 0, changed = 0;

                while (DateTime.UtcNow < end)
                {
                    JoystickState state;
                    try
                    {
                        joystick.Poll();
                        state = joystick.GetCurrentState();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Poll/GetCurrentState failed: {ex.Message}");
                        break;
                    }

                    reads++;
                    if (last == null || StateChanged(last, state))
                    {
                        changed++;
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] X={state.X} Y={state.Y} Z={state.Z} " +
                            $"RX={state.RotationX} RY={state.RotationY} RZ={state.RotationZ} " +
                            $"Sliders=[{string.Join(",", state.Sliders)}] " +
                            $"AccelX={state.AccelerationX} AccelY={state.AccelerationY} AccelZ={state.AccelerationZ} " +
                            $"AngAccelX={state.AngularAccelerationX} AngAccelY={state.AngularAccelerationY} AngAccelZ={state.AngularAccelerationZ} " +
                            $"VelX={state.VelocityX} VelY={state.VelocityY} VelZ={state.VelocityZ}");
                        last = state;
                    }

                    Thread.Sleep(20);
                }

                Console.WriteLine();
                Console.WriteLine($"Total polls: {reads}, changed states: {changed}");
            }

            Console.WriteLine();
            Console.WriteLine("Restoring XInput mode...");
            SendModeCommand(SwitchModeXInputCmd);
            Console.WriteLine("Done.");
        }

        private static bool StateChanged(JoystickState a, JoystickState b)
        {
            return a.X != b.X || a.Y != b.Y || a.Z != b.Z ||
                   a.RotationX != b.RotationX || a.RotationY != b.RotationY || a.RotationZ != b.RotationZ ||
                   !a.Sliders.SequenceEqual(b.Sliders) ||
                   a.AccelerationX != b.AccelerationX || a.AccelerationY != b.AccelerationY || a.AccelerationZ != b.AccelerationZ ||
                   a.AngularAccelerationX != b.AngularAccelerationX || a.AngularAccelerationY != b.AngularAccelerationY || a.AngularAccelerationZ != b.AngularAccelerationZ ||
                   a.VelocityX != b.VelocityX || a.VelocityY != b.VelocityY || a.VelocityZ != b.VelocityZ;
        }

        private static Joystick FindAndAcquireJoystick()
        {
            try
            {
                using (var di = new DirectInput())
                {
                    var all = di.GetDevices(SharpDX.DirectInput.DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices)
                        .Concat(di.GetDevices(SharpDX.DirectInput.DeviceType.Joystick, DeviceEnumerationFlags.AllDevices))
                        .Concat(di.GetDevices(SharpDX.DirectInput.DeviceType.Driving, DeviceEnumerationFlags.AllDevices))
                        .GroupBy(d => d.InstanceGuid).Select(g => g.First())
                        .ToList();

                    Console.WriteLine($"  [diag] DirectInput sees {all.Count} candidate device(s):");
                    foreach (var devInfo in all)
                    {
                        try
                        {
                            var js = new Joystick(di, devInfo.InstanceGuid);
                            string path = js.Properties.InterfacePath ?? "";
                            Console.WriteLine($"    {devInfo.ProductName}  Type={devInfo.Type}  Path={path}");

                            if (path.IndexOf("vid_0db0", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                path.IndexOf("pid_1902", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                js.Acquire();
                                return js;
                            }
                            js.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"    {devInfo.ProductName}: <error: {ex.Message}>");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FindAndAcquireJoystick failed: {ex.Message}");
            }
            return null;
        }

        private static bool SendModeCommand(byte[] cmd)
        {
            var device = FindClawCommandDevice();
            if (device == null) return false;

            try
            {
                using (HidStream stream = device.Open())
                {
                    byte[] msg = new byte[64];
                    Array.Copy(cmd, msg, cmd.Length);
                    stream.Write(msg);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SendModeCommand failed: {ex.Message}");
                return false;
            }
        }

        private static HidDevice FindClawCommandDevice()
        {
            var candidates = DeviceList.Local.GetHidDevices().Where(d => d.VendorID == VendorId).ToList();
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
                            if (page == CmdUsagePageXInput && usage == CmdUsageXInput)
                                return device;
                            // Also accept the DInput-mode command usage page (0xFFF0/0x0040)
                            if (page == 0xFFF0 && usage == 0x0040)
                                return device;
                        }
                    }
                }
                catch
                {
                    // restricted interface - skip
                }
            }
            return null;
        }
    }
}
