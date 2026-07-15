using System;
using System.Management;
using Shared.Data;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Lightweight, Setup-scoped device detection for the Center menu banner ("which handheld did we
    /// find, do we support it"). Ported from the Helper's device detection rather than reinvented —
    /// see XboxGamingBarHelper/Devices/DeviceDetector.cs::QueryDeviceInfoCombined (WMI query) and
    /// XboxGamingBarHelper/Devices/MSIClaw/MSIClawModels.cs::MSIClawModelCatalog.Resolve (model
    /// matching + display names). No disk cache / debug.json here — this runs once per Setup launch,
    /// and only the two MSI Claw generations the team is actively developing on are recognized; other
    /// device families (Legion, ASUS, ...) are out of scope for this installer.
    /// </summary>
    public static class DeviceDetect
    {
        /// <summary>Which device photo to show — see Ui/DeviceIcons.cs.</summary>
        public enum Model { Unknown, A2VM, Ex }

        public readonly struct Result
        {
            public readonly Model Model;
            public readonly string DisplayName;
            public readonly bool Supported;
            public Result(Model model, string displayName, bool supported)
            {
                Model = model; DisplayName = displayName; Supported = supported;
            }
        }

        /// <summary>
        /// Debug-only override so the Center's device-specific UI (icon, gating) can be exercised
        /// without the actual hardware — set from a --device=8ai/8ex CLI arg in App.xaml.cs.
        /// </summary>
        public static Model? DebugOverrideModel;

        public static Result Detect()
        {
            if (DebugOverrideModel.HasValue)
            {
                var m = DebugOverrideModel.Value;
                return m switch
                {
                    Model.A2VM => new Result(Model.A2VM, "MSI Claw (A2VM) — DEBUG", true),
                    Model.Ex => new Result(Model.Ex, "MSI Claw 8 EX AI+ CG3EM — DEBUG", true),
                    _ => new Result(Model.Unknown, "Unknown device — DEBUG", false),
                };
            }

            var info = QueryComputerSystemProduct();

            // Lunar Lake — "A2VM" covers A2VM and A2VMX. Matches MSIClawModelCatalog.Resolve().
            if (info.Model.IndexOf("A2VM", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Result(Model.A2VM, "MSI Claw (A2VM)", true);

            // Panther Lake — board suffix "CG3EM" or the marketing substring "Claw 8 EX".
            if (info.Model.IndexOf("CG3EM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                info.Model.IndexOf("Claw 8 EX", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Result(Model.Ex, "MSI Claw 8 EX AI+ CG3EM", true);

            return new Result(Model.Unknown, "Unknown device", false);
        }

        /// <summary>
        /// Oldest ClawTweaks version this device is actually supported on, or null if there's no
        /// floor. The Claw 8 EX (Panther Lake) only landed proper support in 0.1.7.63 — anything
        /// older predates the port and shouldn't be offered for install on that device.
        /// </summary>
        public static Version MinimumSupportedVersion(Model model) => model switch
        {
            Model.Ex => new Version(0, 1, 7, 63),
            _ => null,
        };

        private static DeviceInfo QueryComputerSystemProduct()
        {
            var info = new DeviceInfo();
            try
            {
                var scope = new ManagementScope(@"\\.\root\cimv2");
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT Vendor, Name, Version FROM Win32_ComputerSystemProduct"));
                searcher.Options.Timeout = TimeSpan.FromSeconds(3);
                foreach (var obj in searcher.Get())
                {
                    info.Manufacturer = obj["Vendor"]?.ToString()?.Trim() ?? "Unknown";
                    info.Model = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                    info.Version = obj["Version"]?.ToString()?.Trim() ?? "Unknown";
                    break;
                }
            }
            catch { /* leave defaults — Detect() falls through to "Unknown device" */ }
            return info;
        }
    }
}
