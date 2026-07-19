using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Devices.MSIClaw
{
    /// <summary>
    /// Configuration for MSI Claw 7/8 AI+ A2VM (Lunar Lake) and Claw 8 EX AI+ CG3EM
    /// (Panther Lake) handhelds.
    ///
    /// SUPPORTED MODELS:
    ///   "Claw 8 AI+ A2VM"     — MS-1T52, Lunar Lake   — confirmed WMI Name
    ///   "Claw 7 AI+ A2VM"     — MS-1T42, Lunar Lake   — assumed WMI Name
    ///   "Claw 7 AI+ A2VMX"    — MS-1T42 variant       — assumed WMI Name
    ///   "Claw 8 EX AI+ CG3EM" — MS-1T91, Panther Lake — confirmed WMI Name (report v1.2)
    ///
    /// The EX shares the A2VM software/HID surface 1:1 (MSI ACPI-WMI, controller FFA0
    /// command channel, EEPROM addresses and profile.rec schema all confirmed identical
    /// on-device). The only genuinely platform-dependent path is TDP (see SupportsWmiTdp).
    ///
    /// NOT SUPPORTED:
    ///   "Claw A1M"  — Meteor Lake (Intel Core Ultra 100H), different EC, different HW controller.
    ///   Any other Claw variant not covered above.
    ///
    /// Detection key: Win32_ComputerSystemProduct.Name must contain "A2VM" (Lunar Lake) OR
    /// "CG3EM"/"Claw 8 EX" (Panther Lake). The A1M contains none of these → excluded automatically.
    ///
    /// NOTE: GoTweaks queries Win32_ComputerSystemProduct.Name, NOT Win32_ComputerSystem.Model.
    ///
    /// Key differences from Legion Go:
    ///   - No detachable controllers (single integrated controller)
    ///   - No touchpad / scroll wheel
    ///   - TDP via Intel IGCL (not WMI TDP)
    ///   - XInput-based controller emulation (no Legion HID path)
    /// </summary>
    public class MSIClawConfig : DeviceConfig
    {
        // The concrete Claw generation resolved during Matches(); drives the per-model capability
        // flags below via MSIClawModelCatalog. See MSIClawModels.cs for the per-device definitions.
        private MSIClawModel _model = MSIClawModel.Unknown;
        private MSIClawModelSpec Spec => MSIClawModelCatalog.Spec(_model);

        public override DeviceType DeviceType => DeviceType.MSIClaw;
        public override string DisplayName => "MSI Claw";

        /// <summary>
        /// WMI Win32_ComputerSystemProduct.Vendor for MSI devices.
        /// Full string is "Micro-Star International Co., Ltd." — use substring match.
        /// </summary>
        public override string Manufacturer => "Micro-Star";

        /// <summary>
        /// Supported Win32_ComputerSystemProduct.Name values (product display names).
        /// Matches() is overridden below; ModelIds is kept for documentation / fallback.
        /// </summary>
        public override IReadOnlyList<string> ModelIds => new[]
        {
            "Claw 8 AI+ A2VM",     // Claw 8 AI+ A2VM  (Lunar Lake, MS-1T52)   — confirmed
            "Claw 7 AI+ A2VM",     // Claw 7 AI+ A2VM  (Lunar Lake, MS-1T42)   — assumed
            "Claw 7 AI+ A2VMX",    // Claw 7 AI+ A2VMX (Lunar Lake, MS-1T42 variant) — assumed
            "Claw 8 EX AI+ CG3EM", // Claw 8 EX AI+ CG3EM (Panther Lake, MS-1T91) — confirmed
        };

        /// <summary>
        /// Matches the supported Claw generations (Lunar Lake A2VM/A2VMX, Panther Lake EX/CG3EM) and
        /// caches the resolved model so the capability flags below reflect that exact device. The Claw
        /// A1M (Meteor Lake) and A8 (AMD) don't resolve to a Supported spec → excluded.
        /// </summary>
        public override bool Matches(DeviceInfo deviceInfo)
        {
            // Manufacturer must contain "Micro-Star" (case-insensitive)
            if (deviceInfo.Manufacturer.IndexOf(Manufacturer, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            var model = MSIClawModelCatalog.Resolve(deviceInfo);
            if (!MSIClawModelCatalog.Spec(model).Supported)
                return false;

            _model = model; // remember which Claw generation matched (drives the capability flags)
            return true;
        }

        // Feature flags — delegated to the resolved per-model spec (MSIClawModels.cs). This is where
        // per-device differences live; to change a capability for one model, edit its spec, not here.
        public override bool SupportsWmiTdp             => Spec.SupportsWmiTdp;
        public override bool SupportsControllerRemap    => Spec.SupportsControllerRemap;
        public override bool SupportsRgbLighting        => Spec.SupportsRgbLighting;
        public override bool SupportsGyro               => Spec.SupportsGyro;
        public override bool SupportsFirmwareKeyboardRemap => Spec.SupportsFirmwareKeyboardRemap;
        public override bool SupportsFanControl         => Spec.SupportsFanControl;
        public override bool SupportsDriverManagement   => Spec.SupportsDriverManagement;
        public override bool SupportsCpuAdvanced        => Spec.SupportsCpuAdvanced;
        public override bool HasTouchpad                => Spec.HasTouchpad;
        public override bool HasScrollWheel             => Spec.HasScrollWheel;
        public override bool HasDetachableControllers   => Spec.HasDetachableControllers;
        public override int MaxPL1                      => Spec.MaxPL1;
        public override int MaxPL2                      => Spec.MaxPL2;
        public override int Pl2MinOffset                => Spec.Pl2MinOffset;
    }
}
