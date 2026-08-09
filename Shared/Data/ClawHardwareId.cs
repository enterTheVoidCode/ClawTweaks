using Microsoft.Win32;
using System;
using System.Text.RegularExpressions;

namespace Shared.Data
{
    /// <summary>Claw generations this identification ladder can name.</summary>
    public enum ClawHardwareModel
    {
        Unknown = 0,
        A2VM,   // Claw 7 AI+ / Claw 8 AI+ (Lunar Lake)
        Ex,     // Claw 8 EX AI+ CG3EM (Panther Lake)
    }

    /// <summary>The raw identity strings, exactly as WMI reports them. Each field names its source.</summary>
    public sealed class ClawIdentitySources
    {
        /// <summary>Win32_ComputerSystemProduct.Vendor — "Micro-Star International Co., Ltd."</summary>
        public string Manufacturer { get; set; }

        /// <summary>Win32_ComputerSystemProduct.Name — "Claw 8 AI+ A2VM". SMBIOS type 1, product name.</summary>
        public string ProductName { get; set; }

        /// <summary>Win32_BaseBoard.Product — "MS-1T52". SMBIOS type 2.</summary>
        public string BaseBoardProduct { get; set; }

        /// <summary>Win32_ComputerSystem.SystemSKUNumber — "1T52.1". SMBIOS type 1, SKU number.</summary>
        public string SystemSku { get; set; }

        /// <summary>Win32_Processor.Name — "Intel(R) Core(TM) Ultra 7 258V".</summary>
        public string ProcessorName { get; set; }

        /// <summary>Win32_Processor.Caption — "Intel64 Family 6 Model 189 Stepping 1".</summary>
        public string ProcessorCaption { get; set; }
    }

    /// <summary>Which model was identified, and which rung of the ladder said so (for the log).</summary>
    public readonly struct ClawIdentityResult
    {
        public ClawHardwareModel Model { get; }

        /// <summary>Human-readable evidence, e.g. "product name 'Claw 8 AI+ A2VM'" — goes into the log.</summary>
        public string MatchedOn { get; }

        /// <summary>True when the product name did NOT identify the device and a fallback rung did.</summary>
        public bool UsedFallback { get; }

        public ClawIdentityResult(ClawHardwareModel model, string matchedOn, bool usedFallback)
        {
            Model = model;
            MatchedOn = matchedOn;
            UsedFallback = usedFallback;
        }

        public override string ToString() =>
            Model == ClawHardwareModel.Unknown ? $"Unknown ({MatchedOn})" : $"{Model} via {MatchedOn}";
    }

    /// <summary>
    /// Identifies the Claw generation from SMBIOS/WMI, with fallbacks for devices whose SMBIOS product
    /// name is a factory placeholder.
    ///
    /// WHY THE FALLBACKS EXIST: units that come back from RMA are shipped with a reflashed board whose
    /// SMBIOS type-1 product name was never programmed — msinfo32 shows the AMI default
    /// "Please change product name" instead of "Claw 8 AI+ A2VM" (user report 2026-08-02, Claw 8 AI+).
    /// The only identity check ClawTweaks had was that product name, so such a device fell through to
    /// DeviceType.Generic: no TDP path, no controller remap, no LED, no fan control, and Center offered
    /// no device-specific install. Everything else on that machine is intact, so there is plenty left
    /// to identify it by.
    ///
    /// THE LADDER (first hit wins; every rung is checked in this order):
    ///   1. Product name        — "A2VM" / "CG3EM" / "Claw 8 EX". The normal path, unchanged.
    ///   2. Board or SKU code   — MS-1T52 / MS-1T42 / MS-1T91. Lives in SMBIOS type 2 (board) and the
    ///                            type-1 SKU field, which the RMA reflash left correct. These codes are
    ///                            unique to the Claw boards, so this rung needs no extra corroboration.
    ///   3. CPU platform        — Lunar Lake => A2VM, Panther Lake => EX, but ONLY when the MSI Claw
    ///                            controller is in the device tree. The CPU alone is NOT sufficient:
    ///                            MSI sells Lunar Lake laptops (Prestige 13 AI+ Evo carries the same
    ///                            Core Ultra 7 258V), and a laptop with a blanked product name would
    ///                            otherwise be handed the Claw's WMI/EC paths. The controller VID/PID is
    ///                            what makes it a Claw.
    ///
    /// Rung 3 cannot tell a Claw 7 AI+ from a Claw 8 AI+ — both are Lunar Lake and share the A2VM spec,
    /// so nothing downstream depends on the difference. Rung 2 does distinguish them (MS-1T42 vs
    /// MS-1T52) and is what a real RMA device hits, since the board code survives the reflash.
    ///
    /// NOT matched, deliberately: MS-1T41 (Claw A1M, Meteor Lake) — different EC and hardware
    /// controller, intentionally unsupported. Its board code must never be added here.
    /// </summary>
    public static class ClawHardwareId
    {
        /// <summary>Win32_ComputerSystemProduct.Vendor substring for MSI. The one field an RMA never blanks.</summary>
        private const string MsiVendor = "Micro-Star";

        // ── Board / SKU codes ────────────────────────────────────────────────────
        // Matched as a substring so both spellings hit: Win32_BaseBoard.Product is "MS-1T52",
        // Win32_ComputerSystem.SystemSKUNumber is "1T52.1".
        private const string Board8AiPlus = "1T52";  // Claw 8 AI+ A2VM      — confirmed on-device
        private const string Board7AiPlus = "1T42";  // Claw 7 AI+ A2VM(X)   — per MSI's board list
        private const string Board8Ex = "1T91";      // Claw 8 EX AI+ CG3EM  — confirmed (EX report)

        // ── CPUID models (Win32_Processor.Caption: "Intel64 Family 6 Model 189 Stepping 1") ──
        // Both verified on-device: the A2VM reports model 189 (0xBD), the Claw 8 EX model 204 (0xCC).
        private const int CpuModelLunarLake = 189;
        private const int CpuModelPantherLake = 204;

        /// <summary>
        /// Known placeholder identity strings. AMI/MSI ship these when a board's SMBIOS strings were
        /// never programmed. Only used for diagnostics and for the "should we even bother" check —
        /// the ladder does not depend on recognizing every possible placeholder, because a rung only
        /// fires on a positive match anyway.
        /// </summary>
        private static readonly string[] PlaceholderIdentities =
        {
            "Please change product name",
            "To be filled by O.E.M.",
            "To Be Filled By OEM",
            "System Product Name",
            "Default string",
            "Not Applicable",
            "Not Specified",
            "Unknown",
            "None",
            "N/A",
        };

        // ── MSI support pages ────────────────────────────────────────────────────
        // One definition per model, here rather than at the call sites: helper and widget both need
        // it, and when they each carried their own copy an EX owner still landed on the Claw 8 AI+
        // page (user report 2026-08-07) — one copy had been corrected, the other not. The '#bios'
        // fragment is deliberate: this button exists to get someone to a firmware download.
        private const string SupportPage8AiPlus =
            "https://www.msi.com/Handheld/Claw-8-AI-Plus-A2VMX/support?sub_product=Claw-8-AI-Plus-A2VM#bios";
        private const string SupportPage8Ex =
            "https://www.msi.com/Handheld/Claw-8-EX-AI-Plus-CG3EMX/support?sub_product=Claw-8-EX-AIplussign-CG3EM#bios";

        /// <summary>
        /// MSI's support/download page for a model. Unknown falls back to the Claw 8 AI+ page, which is
        /// the larger install base — a wrong-but-reachable page beats a dead button.
        /// </summary>
        public static string SupportPageUrl(ClawHardwareModel model) =>
            model == ClawHardwareModel.Ex ? SupportPage8Ex : SupportPage8AiPlus;

        /// <summary>
        /// Same, resolved from any identity string that happens to be at hand (board code, SKU, product
        /// name, marketing name). For callers that never ran the ladder — notably the sandboxed widget,
        /// which cannot read WMI itself and only has whatever text the helper sent it.
        /// </summary>
        public static string SupportPageUrlFor(string modelText)
        {
            if (string.IsNullOrEmpty(modelText)) return SupportPage8AiPlus;
            bool isEx = modelText.IndexOf(Board8Ex, StringComparison.OrdinalIgnoreCase) >= 0
                     || modelText.IndexOf("CG3EM", StringComparison.OrdinalIgnoreCase) >= 0
                     || modelText.IndexOf("Claw 8 EX", StringComparison.OrdinalIgnoreCase) >= 0;
            return isEx ? SupportPage8Ex : SupportPage8AiPlus;
        }

        /// <summary>True when the vendor string identifies MSI.</summary>
        public static bool IsMsiVendor(string manufacturer) =>
            !string.IsNullOrWhiteSpace(manufacturer)
            && manufacturer.IndexOf(MsiVendor, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>True when a SMBIOS string carries no information (empty or a factory placeholder).</summary>
        public static bool IsPlaceholder(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            var trimmed = value.Trim();
            foreach (var placeholder in PlaceholderIdentities)
                if (trimmed.Equals(placeholder, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Runs the ladder. <paramref name="clawControllerPresent"/> gates rung 3 only — pass the result
        /// of <see cref="IsClawControllerInDeviceTree"/>, or false to disable that rung entirely.
        /// </summary>
        public static ClawIdentityResult Resolve(ClawIdentitySources sources, bool clawControllerPresent)
        {
            if (sources == null)
                return new ClawIdentityResult(ClawHardwareModel.Unknown, "no identity data", false);

            bool isMsi = IsMsiVendor(sources.Manufacturer);

            // ── Rung 1: the SMBIOS product name ─────────────────────────────────
            // Vendor-gated, as it always was: these substrings are short enough that a non-MSI machine
            // could in principle carry them.
            if (isMsi)
            {
                var byName = MatchProductName(sources.ProductName);
                if (byName != ClawHardwareModel.Unknown)
                    return new ClawIdentityResult(byName, $"product name '{sources.ProductName}'", false);
            }

            // ── Rung 2: board product / system SKU ──────────────────────────────
            // The MS-xxxx codes are MSI-issued and unique to these boards, so the vendor gate is kept
            // for consistency but the code itself is what identifies the device.
            if (isMsi)
            {
                var byBoard = MatchBoardCode(sources.BaseBoardProduct);
                if (byBoard != ClawHardwareModel.Unknown)
                    return new ClawIdentityResult(byBoard, $"board '{sources.BaseBoardProduct}'", true);

                var bySku = MatchBoardCode(sources.SystemSku);
                if (bySku != ClawHardwareModel.Unknown)
                    return new ClawIdentityResult(bySku, $"system SKU '{sources.SystemSku}'", true);
            }

            // ── Rung 3: CPU platform, corroborated by the controller ────────────
            // Deliberately NOT vendor-gated: the controller VID/PID is MSI-exclusive and stronger
            // evidence than the vendor string, so this rung still works if the vendor field is blank
            // too. Without the controller the CPU says nothing — an MSI Lunar Lake laptop has the
            // same one.
            if (clawControllerPresent)
            {
                var platform = ResolveCpuPlatform(sources.ProcessorName, sources.ProcessorCaption);
                if (platform != ClawHardwareModel.Unknown)
                {
                    var cpu = string.IsNullOrWhiteSpace(sources.ProcessorName)
                        ? sources.ProcessorCaption : sources.ProcessorName;
                    return new ClawIdentityResult(platform, $"CPU '{cpu}' + Claw controller present", true);
                }

                return new ClawIdentityResult(ClawHardwareModel.Unknown,
                    $"Claw controller present but CPU '{sources.ProcessorName}' / '{sources.ProcessorCaption}' "
                    + "matches no known platform", false);
            }

            return new ClawIdentityResult(ClawHardwareModel.Unknown,
                $"vendor '{sources.Manufacturer}', product '{sources.ProductName}', "
                + $"board '{sources.BaseBoardProduct}', SKU '{sources.SystemSku}' matched nothing", false);
        }

        /// <summary>Rung 1 — the marketing name in Win32_ComputerSystemProduct.Name.</summary>
        private static ClawHardwareModel MatchProductName(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName)) return ClawHardwareModel.Unknown;

            // Lunar Lake — "A2VM" covers A2VM and A2VMX.
            if (productName.IndexOf("A2VM", StringComparison.OrdinalIgnoreCase) >= 0)
                return ClawHardwareModel.A2VM;

            // Panther Lake — board suffix "CG3EM" or the marketing substring "Claw 8 EX".
            if (productName.IndexOf("CG3EM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                productName.IndexOf("Claw 8 EX", StringComparison.OrdinalIgnoreCase) >= 0)
                return ClawHardwareModel.Ex;

            return ClawHardwareModel.Unknown;
        }

        /// <summary>Rung 2 — the MS-xxxx board code, from either the board product or the system SKU.</summary>
        private static ClawHardwareModel MatchBoardCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return ClawHardwareModel.Unknown;

            if (value.IndexOf(Board8AiPlus, StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf(Board7AiPlus, StringComparison.OrdinalIgnoreCase) >= 0)
                return ClawHardwareModel.A2VM;

            if (value.IndexOf(Board8Ex, StringComparison.OrdinalIgnoreCase) >= 0)
                return ClawHardwareModel.Ex;

            return ClawHardwareModel.Unknown;
        }

        /// <summary>
        /// Rung 3 — the CPU platform. CPUID (from Win32_Processor.Caption) is the primary signal
        /// because it is a hardware fact; the marketing name is only a secondary check for Lunar Lake.
        /// The Claw 8 EX reports the nonsense marketing name "Intel Arc G3 Extreme", so Panther Lake is
        /// recognized by CPUID alone — never by name.
        /// </summary>
        public static ClawHardwareModel ResolveCpuPlatform(string processorName, string processorCaption)
        {
            var (family, model) = ParseCpuId(processorCaption);
            if (family == 6)
            {
                if (model == CpuModelLunarLake) return ClawHardwareModel.A2VM;
                if (model == CpuModelPantherLake) return ClawHardwareModel.Ex;
            }

            // Name check for Lunar Lake only: the Core Ultra 200V series ("Ultra 7 258V",
            // "Ultra 5 226V", ...) is exactly the family both Lunar Lake Claws ship with.
            if (!string.IsNullOrWhiteSpace(processorName)
                && processorName.IndexOf("Ultra", StringComparison.OrdinalIgnoreCase) >= 0
                && Regex.IsMatch(processorName, @"\b2\d{2}V\b", RegexOptions.IgnoreCase))
                return ClawHardwareModel.A2VM;

            return ClawHardwareModel.Unknown;
        }

        /// <summary>Pulls family/model out of "Intel64 Family 6 Model 189 Stepping 1". (-1, -1) if absent.</summary>
        private static (int family, int model) ParseCpuId(string caption)
        {
            if (string.IsNullOrWhiteSpace(caption)) return (-1, -1);

            var match = Regex.Match(caption, @"Family\s+(\d+)\s+Model\s+(\d+)", RegexOptions.IgnoreCase);
            if (!match.Success) return (-1, -1);

            return int.TryParse(match.Groups[1].Value, out var family)
                && int.TryParse(match.Groups[2].Value, out var model)
                ? (family, model)
                : (-1, -1);
        }

        // ── Controller corroboration for rung 3 ──────────────────────────────────

        /// <summary>USB VID/PID of the MSI Claw's integrated controller. Always enumerated, in every
        /// mode: PID 1901 carries the command interface, the XInput gamepad and the keyboard HID, so it
        /// is present even when DInput mode has added 1902 and HidHide is hiding that one.</summary>
        private const string ClawControllerHwId = "VID_0DB0&PID_1901";

        private static bool? _controllerPresent;

        /// <summary>
        /// True when the Claw's controller is registered in the device tree. Reads
        /// HKLM\SYSTEM\CurrentControlSet\Enum, which lists every device that was ever INSTALLED — not
        /// only what is attached right now. That matters: the helper starts from a scheduled task at
        /// boot, potentially before the HID stack has finished enumerating, and a live-presence check
        /// would then wrongly report "no controller" and drop the device to Generic for that session.
        ///
        /// Cached: the answer cannot change without a reboot-scale event, and the ladder is consulted
        /// from several call sites.
        /// </summary>
        public static bool IsClawControllerInDeviceTree()
        {
            if (_controllerPresent.HasValue) return _controllerPresent.Value;

            bool found = false;
            try
            {
                // USB first: the parent device is a single key with exactly this name.
                using (var usb = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\USB\" + ClawControllerHwId))
                {
                    if (usb != null) found = true;
                }

                // HID children are suffixed (&MI_01, &MI_02&Col01, &IG_00 ...), so they need a scan.
                // Checked as a second source in case the USB parent key is unreadable or absent.
                if (!found)
                {
                    using (var hid = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\HID"))
                    {
                        if (hid != null)
                        {
                            foreach (var name in hid.GetSubKeyNames())
                            {
                                if (name.StartsWith(ClawControllerHwId, StringComparison.OrdinalIgnoreCase))
                                {
                                    found = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // No registry access (app container, locked-down policy) — the caller loses rung 3 only,
                // which is where it was before this existed.
                found = false;
            }

            _controllerPresent = found;
            return found;
        }
    }
}
