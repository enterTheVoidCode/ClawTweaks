using Shared.Data;
using System;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Concrete MSI Claw hardware generations ClawTweaks knows about. The MSI Claw family shares one
    /// software surface (DeviceType.MSIClaw), but individual generations differ in a few capabilities
    /// (TDP path, fan support, ...). This enum + <see cref="MSIClawModelCatalog"/> is the single place
    /// where those per-device differences are defined.
    /// </summary>
    public enum MSIClawModel
    {
        Unknown = 0,
        A2VM,   // Claw 7/8 AI+ (Lunar Lake)         — RE baseline, fully supported
        Ex,     // Claw 8 EX AI+ CG3EM (Panther Lake)
        A8,     // Claw A8 (AMD Z2 Extreme)          — future, not wired yet
        A1M,    // Claw A1M (Meteor Lake)            — first gen, different EC/controller
    }

    /// <summary>
    /// Per-model capability record. THIS is the one spot to define, per device, what ClawTweaks exposes
    /// and where the hardware differs — flip a flag here to enable/disable a feature on a single model
    /// without touching the rest of the codebase. Add a block when a new generation lands.
    /// </summary>
    public sealed class MSIClawModelSpec
    {
        public MSIClawModel Model;
        public string DisplayName;   // marketing / WMI product name (diagnostics + logging)
        public string Platform;      // SoC platform
        public bool Supported;       // false = recognized but not wired up yet (future models)

        // Capabilities — surfaced through MSIClawConfig → DeviceInfo → widget feature gates.
        public bool SupportsWmiTdp;
        public bool SupportsControllerRemap;
        public bool SupportsRgbLighting;
        public bool SupportsGyro;
        public bool SupportsFirmwareKeyboardRemap;
        public bool SupportsFanControl;
        public bool SupportsDriverManagement;   // the Drivers tab (Intel GPU driver updates etc.)
        public bool SupportsCpuAdvanced;        // scheduling policy + P/E core max frequency
        public bool HasTouchpad;
        public bool HasScrollWheel;
        public bool HasDetachableControllers;

        // TDP (PL1 = sustained power, PL2 = boost power). MaxPL1 is also the slider ceiling for the
        // base TDP control; MaxPL2 is the ceiling for the Boost/FPPT control. Pl2MinOffset is the
        // minimum PL2-over-PL1 headroom the platform enforces (0 = no minimum offset, i.e. PL2 may
        // equal PL1) — see ApplyTDPInternal in PerformanceManager.cs, which clamps
        // PL2 to [PL1 + Pl2MinOffset, MaxPL2].
        public int MaxPL1;
        public int MaxPL2;
        public int Pl2MinOffset;

        public string Notes;

        /// <summary>Shallow copy so debug overrides never mutate the shared definitions.</summary>
        public MSIClawModelSpec Clone() => (MSIClawModelSpec)MemberwiseClone();
    }

    /// <summary>
    /// Optional per-model capability overrides for debugging / early-enablement. EMPTY by default.
    /// This is the hook for "unlock feature X on device Y for a test build" without shipping it on.
    /// Example — turn the fan card back on for the EX during a fan test:
    ///   MSIClawDebugFlags.Overrides[MSIClawModel.Ex] = s => s.SupportsFanControl = true;
    /// </summary>
    public static class MSIClawDebugFlags
    {
        public static readonly Dictionary<MSIClawModel, Action<MSIClawModelSpec>> Overrides
            = new Dictionary<MSIClawModel, Action<MSIClawModelSpec>>();

        public static void Apply(MSIClawModelSpec spec)
        {
            if (spec != null && Overrides.TryGetValue(spec.Model, out var mutate))
                mutate?.Invoke(spec);
        }
    }

    /// <summary>
    /// The per-device definitions and the resolver from a detected <see cref="DeviceInfo"/> to a model.
    /// </summary>
    public static class MSIClawModelCatalog
    {
        // ── Claw 7/8 AI+ A2VM (Lunar Lake) ───────────────────────────────────────
        // The reverse-engineering baseline. Everything is verified on this device.
        private static readonly MSIClawModelSpec A2VM = new MSIClawModelSpec
        {
            Model = MSIClawModel.A2VM,
            DisplayName = "MSI Claw (A2VM)",
            Platform = "Lunar Lake",
            Supported = true,
            SupportsWmiTdp = false,                 // TDP via Intel IGCL / MCHBAR, not WMI
            SupportsControllerRemap = true,
            SupportsRgbLighting = true,
            SupportsGyro = true,
            SupportsFirmwareKeyboardRemap = true,
            SupportsFanControl = true,
            SupportsDriverManagement = true,        // Intel GPU driver updates work on Lunar Lake
            SupportsCpuAdvanced = true,             // scheduling policy + P/E max freq (Lunar Lake)
            HasTouchpad = false,
            HasScrollWheel = false,
            HasDetachableControllers = false,
            MaxPL1 = 30,
            MaxPL2 = 37,
            Pl2MinOffset = 1,                       // PL2 must be at least PL1 + 1W
            Notes = "RE baseline — controller/LED/fan/gyro/drivers all verified.",
        };

        // ── Claw 8 EX AI+ CG3EM (Panther Lake) ───────────────────────────────────
        // Shares the A2VM controller / HID (FFA0) / EEPROM / profile.rec surface 1:1 (confirmed
        // on-device). DIFFERENCES vs A2VM:
        //   • Fan  — ON. Same MSI ACPI-WMI fan path as the A2VM; the temp axis + the "MSI Default" duty
        //            curve are read LIVE from the EC (GetFirmwareTempAxis / GetFirmwareDutyAxis), so the
        //            EX's own factory curve is used with no hardcoded per-model values.
        //   • TDP  — Panther-Lake path unverified; stays on the Intel path until probed on-device.
        private static readonly MSIClawModelSpec Ex = new MSIClawModelSpec
        {
            Model = MSIClawModel.Ex,
            DisplayName = "MSI Claw 8 EX AI+ CG3EM",
            Platform = "Panther Lake",
            Supported = true,
            SupportsWmiTdp = false,                 // device-gated (see plan step 4)
            SupportsControllerRemap = true,         // VID_0DB0/PID_1901, FFA0 — identical to A2VM
            SupportsRgbLighting = true,             // LED fw 0x0414 → [02,4A]; verify on-device
            SupportsGyro = true,
            SupportsFirmwareKeyboardRemap = true,   // EEPROM confirmed 1:1 with A2VM
            SupportsFanControl = true,              // MSI ACPI-WMI fan path; axis + default duty read live from EC
            SupportsDriverManagement = true,        // manifest v2 has an EX-scoped block (BIOS E1T91IMS.105, Center M 3.0, Intel Arc; no controller FW); ModelCode gate keeps it EX-only
            SupportsCpuAdvanced = false,            // OFF on Panther Lake: scheduling policy + P/E max freq are not reliably persistent and gain little even on Lunar Lake — EX gets the Boost toggle only
            HasTouchpad = false,
            HasScrollWheel = false,
            HasDetachableControllers = false,
            MaxPL1 = 35,
            MaxPL2 = 45,
            Pl2MinOffset = 2,                       // PL2 must be at least PL1 + 2W (Panther Lake)
            Notes = "Controller/paddles/front buttons port 1:1. Fan on (live EC axis + default duty); drivers on (manifest v2, EX-scoped); TDP device-gated.",
        };

        // ── Claw A8 (AMD Z2 Extreme) — FUTURE, not wired up yet ───────────────────
        // AMD Claw. The MSI ACPI-WMI / controller-HID (VID_0DB0 PID_1901) / LED surface is expected to
        // port from the A2VM (LED needs the fw-0x0313 address); TDP is the big difference — no Intel
        // path, it goes through PawnIO/RyzenSMU + MSI Set_Power. Values below are best-known defaults;
        // Supported=false so they are documentation until detection + the AMD TDP path are wired.
        private static readonly MSIClawModelSpec A8 = new MSIClawModelSpec
        {
            Model = MSIClawModel.A8,
            DisplayName = "MSI Claw A8",
            Platform = "AMD Z2 Extreme",
            Supported = false,
            SupportsWmiTdp = false,                 // AMD: TDP via PawnIO/RyzenSMU + MSI Set_Power (custom path)
            SupportsControllerRemap = true,         // same MSI controller HID as the Intel Claws
            SupportsRgbLighting = true,             // LED via HID; needs the fw-0x0313 address entry
            SupportsGyro = true,
            SupportsFirmwareKeyboardRemap = false,  // EEPROM keyboard-remap layout not verified on AMD yet
            SupportsFanControl = false,             // MSI fan WMI likely portable — verify before enabling
            SupportsDriverManagement = false,       // AMD GPU drivers ≠ Intel DSA flow; needs its own path
            SupportsCpuAdvanced = false,            // AMD scheduling/freq path unverified
            HasTouchpad = false,
            HasScrollWheel = false,
            HasDetachableControllers = false,
            Notes = "First AMD Claw. TDP via PawnIO/RyzenSMU + MSI Set_Power. MSI Center M v2 MysticLight may fight the LED. Detection not enabled yet.",
        };

        // ── Claw A1M (Meteor Lake) — first gen, different EC + HW controller ──────
        // The original Claw. Different EC firmware and a different hardware controller from the A2VM,
        // so most reverse-engineered paths do NOT apply. Intentionally unsupported; kept here so the
        // catalog is complete and the exclusion is explicit.
        private static readonly MSIClawModelSpec A1M = new MSIClawModelSpec
        {
            Model = MSIClawModel.A1M,
            DisplayName = "MSI Claw A1M",
            Platform = "Meteor Lake",
            Supported = false,
            SupportsWmiTdp = false,
            SupportsControllerRemap = false,        // different HW controller — remap paths not verified
            SupportsRgbLighting = false,            // different LED firmware address map
            SupportsGyro = false,
            SupportsFirmwareKeyboardRemap = false,
            SupportsFanControl = false,
            SupportsDriverManagement = false,
            SupportsCpuAdvanced = false,
            HasTouchpad = false,
            HasScrollWheel = false,
            HasDetachableControllers = false,
            Notes = "First-gen Claw (different EC + HW controller). Intentionally unsupported.",
        };

        private static readonly MSIClawModelSpec UnknownSpec = new MSIClawModelSpec
        {
            Model = MSIClawModel.Unknown,
            DisplayName = "MSI Claw (unknown)",
            Platform = "?",
            Supported = false,
            Notes = "Unrecognized MSI Claw variant.",
        };

        /// <summary>Resolve the concrete model from the WMI product name (Win32_ComputerSystemProduct.Name).</summary>
        public static MSIClawModel Resolve(DeviceInfo deviceInfo)
        {
            string model = deviceInfo?.Model ?? string.Empty;

            // Lunar Lake — "A2VM" covers A2VM and A2VMX.
            if (model.IndexOf("A2VM", StringComparison.OrdinalIgnoreCase) >= 0)
                return MSIClawModel.A2VM;

            // Panther Lake — board suffix "CG3EM" or the marketing substring "Claw 8 EX".
            if (model.IndexOf("CG3EM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("Claw 8 EX", StringComparison.OrdinalIgnoreCase) >= 0)
                return MSIClawModel.Ex;

            // A8 / A1M are intentionally NOT matched yet (their specs are Supported=false).
            return MSIClawModel.Unknown;
        }

        /// <summary>Get the (debug-override-applied) capability spec for a model. Never returns null.</summary>
        public static MSIClawModelSpec Spec(MSIClawModel model)
        {
            MSIClawModelSpec basis;
            switch (model)
            {
                case MSIClawModel.A2VM: basis = A2VM; break;
                case MSIClawModel.Ex:   basis = Ex;   break;
                case MSIClawModel.A8:   basis = A8;   break;
                case MSIClawModel.A1M:  basis = A1M;  break;
                default:                basis = UnknownSpec; break;
            }
            var spec = basis.Clone();
            MSIClawDebugFlags.Apply(spec);
            return spec;
        }
    }
}
