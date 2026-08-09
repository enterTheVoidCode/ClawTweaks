using NLog;
using Shared.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Xml.Serialization;

namespace Shared.Data
{
    /// <summary>
    /// A performance profile — the global one (global.xml) or one per game (profiles\&lt;game&gt;.xml).
    /// This is the helper's own store and the superset of every performance setting; see
    /// Doku/PLAN_Performance_SingleStore.md.
    ///
    /// ── Why this is a CLASS and must stay one (was a struct until §5.1) ──────────────────────────
    /// Every setter here ends in <see cref="Save()"/>, i.e. "mutate me and I persist myself". With
    /// value semantics that promise was false half the time: any code that received a GameProfile —
    /// a local, a method parameter, an <c>Action&lt;GameProfile&gt;</c>, a dictionary lookup — got a
    /// COPY, mutated the copy, and the copy's Save() wrote a file the caller was not looking at (or,
    /// with an empty Path, wrote nothing at all). That is the whole content of the
    /// global-profile-struct-save-bug note: RouteProfileSave's global branch took
    /// <c>Action&lt;GameProfile&gt;</c>, so every single "save to global" was silently discarded. It was
    /// patched with a <c>ref</c> delegate and, separately, by mirroring four values into a second
    /// store (settings.json's Global* keys) — a scar, not a design.
    ///
    /// As a reference type the promise holds by construction: one profile is one object, whoever
    /// holds it mutates the real thing, and Save() persists that. The ref delegate is gone; the
    /// Global* keys go away in §5.2.
    ///
    /// The XML shape is unchanged by this — XmlSerializer works off the PUBLIC PROPERTIES (the
    /// [XmlElement] attributes sit on private backing fields, which it never sees, and every
    /// property name happens to match its element name). So no profile file needs migrating.
    /// </summary>
    [XmlRoot("GameProfile")]
    public sealed class GameProfile
    {
        public const string GLOBAL_PROFILE_NAME = "global";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Lock object for thread-safe cache and file operations.
        /// Prevents race conditions during profile switching.
        /// </summary>
        private static readonly object ProfileLock = new object();

        [XmlElement("GameId")]
        public GameId GameId;

        [XmlElement("Use")]
        private bool use;
        public bool Use
        {
            get
            {
                if (IsGlobalProfile)
                {
                    // Logger.Warn("Per-game profile is preferred over global profile.");
                    return false;
                }

                return use;
            }
            set
            {
                if (IsGlobalProfile)
                {
                    Logger.Warn("Can't change \"Use\" property of global profile.");
                    return;
                }

                if (use != value)
                {
                    use = value;
                    Save();
                }
            }
        }

        [XmlElement("TDP")]
        private int tdp;
        public int TDP
        {
            get { return tdp; }
            set
            {
                if (tdp != value)
                {
                    tdp = value;
                    Save();
                }
            }
        }

        [XmlElement("CPUBoost")]
        private bool cpuBoost;
        public bool CPUBoost
        {
            get { return cpuBoost; }
            set
            {
                if (cpuBoost != value)
                {
                    cpuBoost = value;
                    Save();
                }
            }
        }

        [XmlElement("CPUEPP")]
        private int cpuEPP;
        public int CPUEPP
        {
            get { return cpuEPP; }
            set
            {
                if (cpuEPP != value)
                {
                    cpuEPP = value;
                    Save();
                }
            }
        }

        [XmlElement("MaxCPUState")]
        private int maxCPUState;
        public int MaxCPUState
        {
            get { return maxCPUState; }
            set
            {
                if (maxCPUState != value)
                {
                    maxCPUState = value;
                    Save();
                }
            }
        }

        [XmlElement("MinCPUState")]
        private int minCPUState;
        public int MinCPUState
        {
            get { return minCPUState; }
            set
            {
                if (minCPUState != value)
                {
                    minCPUState = value;
                    Save();
                }
            }
        }

        // ========== CPU Advanced (ToothNClaw port) ==========
        // -1 = "unset" (don't apply / fall back). For freq, 0 = unlimited.
        // (Boost mode was removed — boost is plain on/off now, see CPUBoost above.)

        /// <summary>Processor scheduling policy: -1=unset, 0=Auto, 1=PreferPCore, 2=PreferECore, 3=OnlyPCore, 4=OnlyECore.</summary>
        [XmlElement("ProcessorSchedulingPolicy")]
        private int processorSchedulingPolicy;
        public int ProcessorSchedulingPolicy
        {
            get { return processorSchedulingPolicy; }
            set
            {
                if (processorSchedulingPolicy != value)
                {
                    processorSchedulingPolicy = value;
                    Save();
                }
            }
        }

        /// <summary>P-core (Efficiency Class 1) max frequency in MHz. 0 = unlimited.</summary>
        [XmlElement("MaxPCoreFreqMHz")]
        private int maxPCoreFreqMHz;
        public int MaxPCoreFreqMHz
        {
            get { return maxPCoreFreqMHz; }
            set
            {
                if (maxPCoreFreqMHz != value)
                {
                    maxPCoreFreqMHz = value;
                    Save();
                }
            }
        }

        /// <summary>E-core / all-core max frequency in MHz. 0 = unlimited.</summary>
        [XmlElement("MaxECoreFreqMHz")]
        private int maxECoreFreqMHz;
        public int MaxECoreFreqMHz
        {
            get { return maxECoreFreqMHz; }
            set
            {
                if (maxECoreFreqMHz != value)
                {
                    maxECoreFreqMHz = value;
                    Save();
                }
            }
        }

        // ========== Intel Display (IGCL) — part of the performance profile ==========
        // Nullable: null = not configured (don't snap old profiles to grayscale on load).
        // Units (TnC/IGCL): sharpness 0..100 (0=off); saturation/contrast/brightness 0..100
        // (50=neutral); hue -180..180 (0); gamma ×100 30..280 (100=1.0).

        [XmlElement("IntelAdaptiveSharpness")]
        private int? intelAdaptiveSharpness;
        public int? IntelAdaptiveSharpness
        {
            get { return intelAdaptiveSharpness; }
            set { if (intelAdaptiveSharpness != value) { intelAdaptiveSharpness = value; Save(); } }
        }

        [XmlElement("IntelColorSaturation")]
        private int? intelColorSaturation;
        public int? IntelColorSaturation
        {
            get { return intelColorSaturation; }
            set { if (intelColorSaturation != value) { intelColorSaturation = value; Save(); } }
        }

        [XmlElement("IntelColorHue")]
        private int? intelColorHue;
        public int? IntelColorHue
        {
            get { return intelColorHue; }
            set { if (intelColorHue != value) { intelColorHue = value; Save(); } }
        }

        [XmlElement("IntelDisplayContrast")]
        private int? intelDisplayContrast;
        public int? IntelDisplayContrast
        {
            get { return intelDisplayContrast; }
            set { if (intelDisplayContrast != value) { intelDisplayContrast = value; Save(); } }
        }

        [XmlElement("IntelDisplayBrightness")]
        private int? intelDisplayBrightness;
        public int? IntelDisplayBrightness
        {
            get { return intelDisplayBrightness; }
            set { if (intelDisplayBrightness != value) { intelDisplayBrightness = value; Save(); } }
        }

        /// <summary>Gamma stored ×100 (100 = 1.0).</summary>
        [XmlElement("IntelDisplayGamma")]
        private int? intelDisplayGamma;
        public int? IntelDisplayGamma
        {
            get { return intelDisplayGamma; }
            set { if (intelDisplayGamma != value) { intelDisplayGamma = value; Save(); } }
        }

        // Intel gaming 3D features (IGCL). null = not configured (don't override on load).
        /// <summary>Intel low latency / anti-lag: 0=off, 1=on, 2=on+boost.</summary>
        [XmlElement("IntelLowLatency")]
        private int? intelLowLatency;
        public int? IntelLowLatency
        {
            get { return intelLowLatency; }
            set { if (intelLowLatency != value) { intelLowLatency = value; Save(); } }
        }

        /// <summary>Intel frame sync / flip mode: 0=App default,1=VSync off,2=VSync on,3=Smooth,4=Speed.</summary>
        [XmlElement("IntelFrameSync")]
        private int? intelFrameSync;
        public int? IntelFrameSync
        {
            get { return intelFrameSync; }
            set { if (intelFrameSync != value) { intelFrameSync = value; Save(); } }
        }

        /// <summary>XeSS frame generation override: 0=App choice, 1=2X, 2=3X, 3=4X.</summary>
        [XmlElement("IntelFrameGeneration")]
        private int? intelFrameGeneration;
        public int? IntelFrameGeneration
        {
            get { return intelFrameGeneration; }
            set { if (intelFrameGeneration != value) { intelFrameGeneration = value; Save(); } }
        }

        /// <summary>Variable refresh rate (Intel Arc Sync): 0=off, 1=on.</summary>
        [XmlElement("IntelVrr")]
        private int? intelVrr;
        public int? IntelVrr
        {
            get { return intelVrr; }
            set { if (intelVrr != value) { intelVrr = value; Save(); } }
        }

        /// <summary>Which presentations VRR applies to: 0=Auto, 1=Windowed and fullscreen, 2=Fullscreen
        /// only. Separate from IntelVrr because Intel keeps them as two controls — the on/off is the
        /// display's Arc Sync profile, this is the driver's windowed-VRR mode.</summary>
        [XmlElement("IntelVrrMode")]
        private int? intelVrrMode;
        public int? IntelVrrMode
        {
            get { return intelVrrMode; }
            set { if (intelVrrMode != value) { intelVrrMode = value; Save(); } }
        }

        /// <summary>Scaling group: 0=Display, 1=GPU, 2=Retro. Only meaningful together with
        /// IntelScalingMethod — the two are one setting split the way Intel splits it.</summary>
        [XmlElement("IntelScalingMode")]
        private int? intelScalingMode;
        public int? IntelScalingMode
        {
            get { return intelScalingMode; }
            set { if (intelScalingMode != value) { intelScalingMode = value; Save(); } }
        }

        /// <summary>Entry within the scaling group (see IntelScalingMode).</summary>
        [XmlElement("IntelScalingMethod")]
        private int? intelScalingMethod;
        public int? IntelScalingMethod
        {
            get { return intelScalingMethod; }
            set { if (intelScalingMethod != value) { intelScalingMethod = value; Save(); } }
        }

        [XmlElement("TDPBoostEnabled")]
        private bool tdpBoostEnabled;
        public bool TDPBoostEnabled
        {
            get { return tdpBoostEnabled; }
            set
            {
                if (tdpBoostEnabled != value)
                {
                    tdpBoostEnabled = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Whether this profile keeps SEPARATE values for mains and battery ("Plugged in" / "On
        /// battery" in the UI — the trigger is the power source, not a dock).
        ///
        /// Off: one value applies in both states — the *_Plugged slots stay untouched and Effective*
        /// resolves to the base value.
        /// On: a change made while PLUGGED IN is written to the *_Plugged slot instead of the base one,
        /// so the two states can differ.
        ///
        /// THE BASE VALUE IS THE UNPLUGGED ONE. This direction is the whole point and was reversed on
        /// 2026-08-02 (user requirement): a handheld runs on battery ~90 % of the time, most people
        /// never enable this split at all, so the value someone configures without thinking about power
        /// states has to be the battery value. An empty override then means "plugged inherits from
        /// unplugged", and nothing done while plugged in — nor a field added later that nobody has
        /// configured yet — can move the battery side. The reverse direction had the opposite property:
        /// every edit made on mains silently redefined what the device does on battery.
        ///
        /// It lives HERE, in the profile, rather than in a settings store beside it — the rule from
        /// plan §4.1: a new profile setting is a field on GameProfile and nothing else. That makes it
        /// per-profile automatically (each game AND the global profile can decide for itself), and it
        /// travels in the snapshot, in backup and in export without another line of code.
        ///
        /// The global profile carrying this is deliberate and reverses an earlier decision. The global
        /// split was removed after #21 because a stale global AC/DC value in a SECOND store clobbered
        /// the real global TDP on plug/unplug. With a single store and a real override slot, the value
        /// that gets applied is the value that was stored — the failure mode needed two stores.
        /// </summary>
        [XmlElement("PowerSourceSplit")]
        private bool powerSourceSplit;
        public bool PowerSourceSplit
        {
            get { return powerSourceSplit; }
            set
            {
                if (powerSourceSplit != value)
                {
                    powerSourceSplit = value;
                    if (value) SeedPowerStateOverrides(); else ClearPowerStateOverrides();
                    Save();
                }
            }
        }

        /// <summary>
        /// Copies each base (unplugged) value into its plugged override slot, so the two power states
        /// start out identical but INDEPENDENT — "enabling the split copies the unplugged settings over
        /// to plugged, and you adjust from there".
        ///
        /// Unlike the previous direction, this is a CONVENIENCE, not a safety net: with the base
        /// meaning unplugged, an empty slot already resolves to the unplugged value, so a profile whose
        /// slots never got seeded still behaves correctly. Seeding only makes the copy explicit in the
        /// file and freezes it, so a later unplugged edit does not drag the plugged side along with it.
        ///
        /// Only empty slots are filled: a profile that already carries plugged values keeps them when
        /// the split is toggled off and on again.
        /// </summary>
        private void SeedPowerStateOverrides()
        {
            if (tdpPlugged == null) tdpPlugged = tdp;
            if (cpuBoostPlugged == null) cpuBoostPlugged = cpuBoost;
            if (cpuEppPlugged == null) cpuEppPlugged = cpuEPP;
            if (maxCpuStatePlugged == null) maxCpuStatePlugged = maxCPUState;
            if (minCpuStatePlugged == null) minCpuStatePlugged = minCPUState;
            if (fpsLimitPlugged == null) fpsLimitPlugged = fpsLimit;
            if (osPowerModePlugged == null) osPowerModePlugged = osPowerMode;
            if (tdpBoostEnabledPlugged == null) tdpBoostEnabledPlugged = tdpBoostEnabled;
            if (tdpBoostFPPTWattsPlugged == null) tdpBoostFPPTWattsPlugged = tdpBoostFPPTWatts;
            if (fpsCapModePlugged == null) fpsCapModePlugged = fpsCapMode;
            if (intelFpsTierPlugged == null) intelFpsTierPlugged = intelFpsTier;
            // The fan curve seeds on empty, not on null: see EffectiveMsiFanCurve on why both spellings
            // of "unset" occur for this one. Seeding an empty string would freeze "no override" into the
            // plugged slot and make the two states diverge in the one direction nobody asked for.
            if (string.IsNullOrEmpty(msiFanCurvePlugged)) msiFanCurvePlugged = msiFanCurve;
        }

        /// <summary>
        /// Drops the plugged overrides, so one value applies in both states again. Without this a
        /// profile switched back to "one value" would keep resolving to its old plugged values while
        /// plugged in — the setting would look off while still behaving as if it were on.
        ///
        /// Note which side survives: the base, i.e. the UNPLUGGED values. Turning the split off can
        /// therefore never change what the handheld does on battery.
        /// </summary>
        private void ClearPowerStateOverrides()
        {
            tdpPlugged = null;
            cpuBoostPlugged = null;
            cpuEppPlugged = null;
            maxCpuStatePlugged = null;
            minCpuStatePlugged = null;
            fpsLimitPlugged = null;
            osPowerModePlugged = null;
            tdpBoostEnabledPlugged = null;
            tdpBoostFPPTWattsPlugged = null;
            fpsCapModePlugged = null;
            intelFpsTierPlugged = null;
            msiFanCurvePlugged = null;
        }

        /// <summary>
        /// PL2 Overboost target in watts — an ABSOLUTE PL2 value, not an offset on PL1.
        ///
        /// The "additional watts (FPPT = TDP + this value)" this comment used to claim was wrong and
        /// contradicted both implementations: PerformanceManager.ApplyTDPInternal treats it as the
        /// absolute FPPT target and clamps it into [PL1 + Pl2MinOffset, MaxPL2] per model (A2VM: +1 /
        /// 37W, Claw 8 EX: +2 / 45W), and the widget's PL2 slider maxes out at the device's MaxPL2.
        /// The distinction matters now that the helper owns PL2 — reading it as an offset would be a
        /// factor error straight into a hardware power limit.
        ///
        /// Any value &lt;= 0 means "not set" and resolves to the device default (the minimum burst,
        /// PL1 + Pl2MinOffset) via that same clamp; the ctor default is 0.
        /// </summary>
        [XmlElement("TDPBoostFPPTWatts")]
        private int tdpBoostFPPTWatts;
        public int TDPBoostFPPTWatts
        {
            get { return tdpBoostFPPTWatts; }
            set
            {
                if (tdpBoostFPPTWatts != value)
                {
                    tdpBoostFPPTWatts = value;
                    Save();
                }
            }
        }

        // ========== DC (Battery) Overrides ==========
        // When null, the AC value (above) is used. When set, overrides for DC power.

        [XmlElement("TDP_Plugged")]
        private int? tdpPlugged;
        public int? TDP_Plugged
        {
            get { return tdpPlugged; }
            set
            {
                if (tdpPlugged != value)
                {
                    tdpPlugged = value;
                    Save();
                }
            }
        }

        [XmlElement("CPUBoost_Plugged")]
        private bool? cpuBoostPlugged;
        public bool? CPUBoost_Plugged
        {
            get { return cpuBoostPlugged; }
            set
            {
                if (cpuBoostPlugged != value)
                {
                    cpuBoostPlugged = value;
                    Save();
                }
            }
        }

        [XmlElement("CPUEPP_Plugged")]
        private int? cpuEppPlugged;
        public int? CPUEPP_Plugged
        {
            get { return cpuEppPlugged; }
            set
            {
                if (cpuEppPlugged != value)
                {
                    cpuEppPlugged = value;
                    Save();
                }
            }
        }

        [XmlElement("MaxCPUState_Plugged")]
        private int? maxCpuStatePlugged;
        public int? MaxCPUState_Plugged
        {
            get { return maxCpuStatePlugged; }
            set
            {
                if (maxCpuStatePlugged != value)
                {
                    maxCpuStatePlugged = value;
                    Save();
                }
            }
        }

        [XmlElement("MinCPUState_Plugged")]
        private int? minCpuStatePlugged;
        public int? MinCPUState_Plugged
        {
            get { return minCpuStatePlugged; }
            set
            {
                if (minCpuStatePlugged != value)
                {
                    minCpuStatePlugged = value;
                    Save();
                }
            }
        }

        // The four below were added 2026-08-02. They had no override slot, so with the power-state
        // split on they still applied one value in both states — reported as "Overboost, PL2 and the
        // FPS cap follow whatever I set while plugged in". TDPBoost in particular was single-valued by
        // the #21 rule, which only ever existed because the global profile had no split at all.

        [XmlElement("TDPBoostEnabled_Plugged")]
        private bool? tdpBoostEnabledPlugged;
        public bool? TDPBoostEnabled_Plugged
        {
            get { return tdpBoostEnabledPlugged; }
            set
            {
                if (tdpBoostEnabledPlugged != value)
                {
                    tdpBoostEnabledPlugged = value;
                    Save();
                }
            }
        }

        [XmlElement("TDPBoostFPPTWatts_Plugged")]
        private int? tdpBoostFPPTWattsPlugged;
        public int? TDPBoostFPPTWatts_Plugged
        {
            get { return tdpBoostFPPTWattsPlugged; }
            set
            {
                if (tdpBoostFPPTWattsPlugged != value)
                {
                    tdpBoostFPPTWattsPlugged = value;
                    Save();
                }
            }
        }

        [XmlElement("FpsCapMode_Plugged")]
        private int? fpsCapModePlugged;
        public int? FpsCapMode_Plugged
        {
            get { return fpsCapModePlugged; }
            set
            {
                if (fpsCapModePlugged != value)
                {
                    fpsCapModePlugged = value;
                    Save();
                }
            }
        }

        [XmlElement("IntelFpsTier_Plugged")]
        private int? intelFpsTierPlugged;
        public int? IntelFpsTier_Plugged
        {
            get { return intelFpsTierPlugged; }
            set
            {
                if (intelFpsTierPlugged != value)
                {
                    intelFpsTierPlugged = value;
                    Save();
                }
            }
        }

        // DgpEnabledOnAC / DgpEnabledOnDC removed 2026-08-02 with the Default Game Profiles feature.
        // Existing profile files still carry the two elements; XmlSerializer ignores unknown elements,
        // so they are simply dropped the next time a profile is saved. Nothing to migrate — they only
        // ever held a per-game on/off preference for a feature that no longer exists.

        // ========== Additional Profile Settings ==========

        // ========== Intel Endurance Gaming FPS Tier ==========

        [XmlElement("IntelFpsTier")]
        private int intelFpsTier;
        /// <summary>Intel endurance gaming tier: 0=off, 1=Performance(60fps), 2=Balanced(40fps), 3=Efficiency(30fps).</summary>
        public int IntelFpsTier
        {
            get { return intelFpsTier; }
            set
            {
                if (intelFpsTier != value)
                {
                    intelFpsTier = value;
                    Save();
                }
            }
        }

        [XmlElement("FpsCapMode")]
        private int fpsCapMode;
        /// <summary>Active FPS-cap source: 0=RTSS (or none), 1=Intel IGCL.</summary>
        public int FpsCapMode
        {
            get { return fpsCapMode; }
            set
            {
                if (fpsCapMode != value)
                {
                    fpsCapMode = value;
                    Save();
                }
            }
        }

        // ========== Additional Profile Settings ==========

        [XmlElement("FPSLimit")]
        private int fpsLimit;
        public int FPSLimit
        {
            get { return fpsLimit; }
            set
            {
                if (fpsLimit != value)
                {
                    fpsLimit = value;
                    Save();
                }
            }
        }

        [XmlElement("FPSLimit_Plugged")]
        private int? fpsLimitPlugged;
        public int? FPSLimit_Plugged
        {
            get { return fpsLimitPlugged; }
            set
            {
                if (fpsLimitPlugged != value)
                {
                    fpsLimitPlugged = value;
                    Save();
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Effective-value resolution for the eleven fields that have a power-state override.
        //
        // THE BASE VALUE IS THE UNPLUGGED ONE; the override is the plugged-in one. The rule is always
        // "plugged override if set, otherwise the base (unplugged) value", and it lives HERE rather
        // than at each reader. It used to be open-coded wherever someone needed it (e.g.
        // Sidebar/ProfilesTab.cs), which means every new reader re-decides the semantics and the
        // widget's cards could disagree with what the helper actually applies. Both sides call these.
        //
        // The direction was reversed on 2026-08-02 and this is the single most important property of
        // the whole feature: on a handheld the battery state is the product, so an override that was
        // never filled — because the user never enabled the split, because a seeding pass missed it, or
        // because the field was added in a later build — resolves to the value the user configured, and
        // the battery side simply cannot be moved by anything that happens while plugged in. Under the
        // previous direction (base = plugged) an empty slot meant battery READ the mains value, so every
        // edit made on mains silently redefined battery behaviour. Do not turn this around again.
        //
        // Only these twelve carry an override (the eleven below plus OSPowerMode and MsiFanCurve further
        // down, which sit next to their own fields); every other group-A/B field is power-source
        // independent, so its stored value IS its effective value and no resolver exists on purpose.
        //
        // WHEN A THIRTEENTH IS ADDED, MOVE EVERY READER WITH IT. A partial switch to Effective* is more
        // dangerous than none at all: the AC/DC handler resolves correctly, so the result looks right for
        // a moment, and only a later re-apply from some other trigger reveals the raw read. That is
        // exactly how the 0.1.8.114 case behaved (see CLAUDE.md).
        // ---------------------------------------------------------------------------------------

        /// <summary>Effective TDP in watts for the given power source.</summary>
        public int EffectiveTDP(bool onBattery) => onBattery ? TDP : (TDP_Plugged ?? TDP);

        /// <summary>Effective CPU-boost state for the given power source.</summary>
        public bool EffectiveCPUBoost(bool onBattery) => onBattery ? CPUBoost : (CPUBoost_Plugged ?? CPUBoost);

        /// <summary>Effective CPU EPP for the given power source.</summary>
        public int EffectiveCPUEPP(bool onBattery) => onBattery ? CPUEPP : (CPUEPP_Plugged ?? CPUEPP);

        /// <summary>Effective maximum CPU state (%) for the given power source.</summary>
        public int EffectiveMaxCPUState(bool onBattery) => onBattery ? MaxCPUState : (MaxCPUState_Plugged ?? MaxCPUState);

        /// <summary>Effective minimum CPU state (%) for the given power source.</summary>
        public int EffectiveMinCPUState(bool onBattery) => onBattery ? MinCPUState : (MinCPUState_Plugged ?? MinCPUState);

        /// <summary>Effective RTSS FPS limit for the given power source.</summary>
        public int EffectiveFPSLimit(bool onBattery) => onBattery ? FPSLimit : (FPSLimit_Plugged ?? FPSLimit);

        /// <summary>Effective PL2 Overboost state for the given power source.</summary>
        public bool EffectiveTDPBoostEnabled(bool onBattery) => onBattery ? TDPBoostEnabled : (TDPBoostEnabled_Plugged ?? TDPBoostEnabled);

        /// <summary>Effective absolute PL2 target in watts for the given power source.</summary>
        public int EffectiveTDPBoostFPPTWatts(bool onBattery) => onBattery ? TDPBoostFPPTWatts : (TDPBoostFPPTWatts_Plugged ?? TDPBoostFPPTWatts);

        /// <summary>Effective FPS cap mode (0=RTSS, 1=Intel) for the given power source.</summary>
        public int EffectiveFpsCapMode(bool onBattery) => onBattery ? FpsCapMode : (FpsCapMode_Plugged ?? FpsCapMode);

        /// <summary>Effective Intel cap in fps for the given power source (0 = off).</summary>
        public int EffectiveIntelFpsTier(bool onBattery) => onBattery ? IntelFpsTier : (IntelFpsTier_Plugged ?? IntelFpsTier);

        // AutoTDP (AutoTDPEnabled/_DC, AutoTDPTargetFPS/_DC, AutoTDPMinTDP, AutoTDPMaxTDP,
        // AutoTDPUseMLMode, AutoTDPPauseWhenUnfocused, AutoTDPControllerType) was REMOVED here.
        // Inherited from the GoTweaks origin project, disconnected in the product and explicitly not
        // wanted back (user decision 2026-07-31; see Doku/PLAN_Performance_SingleStore.md §3 group D
        // and the no-software-autotdp note). XmlSerializer ignores unknown elements, so existing
        // profile files keep loading — the stale elements simply disappear on the next write.
        // NOT to be confused with TDPBoostEnabled / TDPBoostFPPTWatts above: PL2 Overboost is a live
        // feature owned by the helper and stays.

        // Windows power-mode overlay: 0 = Best Power Efficiency, 1 = Balanced, 2 = Best Performance.
        // null = not configured, so a profile that never set one does not drag the OS mode around.
        //
        // This was a `string` that NOTHING in the helper ever wrote or applied — it did not appear in a
        // single real profile file, so there is no legacy data to migrate and the type change is safe.
        // The runtime property (PowerManager.OSPowerMode) has always been an int and always worked;
        // only the per-profile half was missing, which is why the mode never followed a game.
        [XmlElement("OSPowerMode")]
        private int? osPowerMode;
        public int? OSPowerMode
        {
            get { return osPowerMode; }
            set
            {
                if (osPowerMode != value)
                {
                    osPowerMode = value;
                    Save();
                }
            }
        }

        [XmlElement("OSPowerMode_Plugged")]
        private int? osPowerModePlugged;
        public int? OSPowerMode_Plugged
        {
            get { return osPowerModePlugged; }
            set
            {
                if (osPowerModePlugged != value)
                {
                    osPowerModePlugged = value;
                    Save();
                }
            }
        }

        /// <summary>Plugged override if set, else the base (unplugged) value. Mirrors the other Effective* resolvers.</summary>
        public int? EffectiveOSPowerMode(bool onBattery) => onBattery ? OSPowerMode : (OSPowerMode_Plugged ?? OSPowerMode);

        // MSI Claw custom fan curve, "sync|cpuD0..D5|gpuD0..D5" (raw EC duties, the same wire format the
        // fan editor and MsiClawFanController.ApplyMsiCurve6 already speak).
        //
        // EMPTY MEANS "NO OVERRIDE", and that distinction is the whole reason only the curve is stored and
        // not a preset index. The previous attempt stored a preset, where 0 = "MSI Default" was
        // indistinguishable from "nothing captured" — so every game without a fan setting wrote the factory
        // curve over the user's global one. A profile that never got a curve must leave the fan alone.
        //
        // The GLOBAL curve deliberately does NOT live here; it stays in the helper's LocalSettings
        // (MsiFan_Curve3), which is what the boot and resume restore paths read. This field is the per-game
        // override on top of it (user decision 2026-08-02, Doku/PLAN_Fan_PerGame_Curves.md §6).
        [XmlElement("MsiFanCurve")]
        private string msiFanCurve;
        public string MsiFanCurve
        {
            get { return msiFanCurve; }
            set
            {
                if (msiFanCurve != value)
                {
                    msiFanCurve = value;
                    Save();
                }
            }
        }

        [XmlElement("MsiFanCurve_Plugged")]
        private string msiFanCurvePlugged;
        public string MsiFanCurve_Plugged
        {
            get { return msiFanCurvePlugged; }
            set
            {
                if (msiFanCurvePlugged != value)
                {
                    msiFanCurvePlugged = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Plugged override if set, else the base (unplugged) curve. The twelfth split-capable field —
        /// same direction as all the others: the base slot is the UNPLUGGED one.
        ///
        /// Tests for empty rather than null because an unset string slot can arrive either way (a profile
        /// written before this field existed has no element at all; one saved with the split on and nothing
        /// entered yet has an empty one). Both mean "inherit", and an empty string reaching the fan
        /// controller would parse to no curve at all.
        /// </summary>
        public string EffectiveMsiFanCurve(bool onBattery)
            => onBattery
                ? MsiFanCurve
                : (string.IsNullOrEmpty(MsiFanCurve_Plugged) ? MsiFanCurve : MsiFanCurve_Plugged);

        [XmlElement("HDREnabled")]
        private bool hdrEnabled;
        public bool HDREnabled
        {
            get { return hdrEnabled; }
            set
            {
                if (hdrEnabled != value)
                {
                    hdrEnabled = value;
                    Save();
                }
            }
        }

        [XmlElement("Resolution")]
        private string resolution;
        public string Resolution
        {
            get { return resolution; }
            set
            {
                if (resolution != value)
                {
                    resolution = value;
                    Save();
                }
            }
        }

        [XmlElement("RefreshRate")]
        private int? refreshRate;
        public int? RefreshRate
        {
            get { return refreshRate; }
            set
            {
                if (refreshRate != value)
                {
                    refreshRate = value;
                    Save();
                }
            }
        }

        // StickyTDP was REMOVED here — same GoTweaks legacy as AutoTDP above, same user decision.

        /// <summary>
        /// Performance overlay level (0=Off, 1=Basic, 2=Detailed, 3=Full for RTSS; 1-4 for AMD)
        /// </summary>
        [XmlElement("OverlayLevel")]
        private int? overlayLevel;
        public int? OverlayLevel
        {
            get { return overlayLevel; }
            set
            {
                if (overlayLevel != value)
                {
                    overlayLevel = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Which renderer draws the overlay for this game: 0 = RTSS, 1 = the built-in one.
        ///
        /// Deliberately NOT split by power source, unlike TDP and the FPS cap. Those are about how much
        /// power a game may have, which is exactly what changes when the cable comes out; which
        /// renderer draws a text box is not. Two more slots here would be two more places to forget in
        /// the Effective* sweep for no behaviour anyone asked for.
        /// </summary>
        [XmlElement("OsdType")]
        private int? osdType;
        public int? OsdType
        {
            get { return osdType; }
            set
            {
                if (osdType != value)
                {
                    osdType = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Where the built-in overlay sits for this game: MSI's six anchor points, 1..6.
        /// Ignored while RTSS is the renderer - RTSS keeps its own position setting.
        /// </summary>
        [XmlElement("OsdPosition")]
        private int? osdPosition;
        public int? OsdPosition
        {
            get { return osdPosition; }
            set
            {
                if (osdPosition != value)
                {
                    osdPosition = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// CPU Affinity configuration as "activePCores,activeECores" string
        /// </summary>
        [XmlElement("CPUAffinity")]
        private string cpuAffinity;
        public string CPUAffinity
        {
            get { return cpuAffinity; }
            set
            {
                if (cpuAffinity != value)
                {
                    cpuAffinity = value;
                    Save();
                }
            }
        }

        // ========== Legion Controller Remapping ==========

        [XmlElement("LegionButtonY1")]
        private string legionButtonY1;
        public string LegionButtonY1
        {
            get { return legionButtonY1; }
            set
            {
                if (legionButtonY1 != value)
                {
                    legionButtonY1 = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionButtonY2")]
        private string legionButtonY2;
        public string LegionButtonY2
        {
            get { return legionButtonY2; }
            set
            {
                if (legionButtonY2 != value)
                {
                    legionButtonY2 = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionButtonY3")]
        private string legionButtonY3;
        public string LegionButtonY3
        {
            get { return legionButtonY3; }
            set
            {
                if (legionButtonY3 != value)
                {
                    legionButtonY3 = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionButtonM2")]
        private string legionButtonM2;
        public string LegionButtonM2
        {
            get { return legionButtonM2; }
            set
            {
                if (legionButtonM2 != value)
                {
                    legionButtonM2 = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionButtonM3")]
        private string legionButtonM3;
        public string LegionButtonM3
        {
            get { return legionButtonM3; }
            set
            {
                if (legionButtonM3 != value)
                {
                    legionButtonM3 = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionButtonDesktop")]
        private string legionButtonDesktop;
        public string LegionButtonDesktop
        {
            get { return legionButtonDesktop; }
            set
            {
                if (legionButtonDesktop != value)
                {
                    legionButtonDesktop = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionButtonPage")]
        private string legionButtonPage;
        public string LegionButtonPage
        {
            get { return legionButtonPage; }
            set
            {
                if (legionButtonPage != value)
                {
                    legionButtonPage = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionGyroButton")]
        private int? legionGyroButton;
        public int? LegionGyroButton
        {
            get { return legionGyroButton; }
            set
            {
                if (legionGyroButton != value)
                {
                    legionGyroButton = value;
                    Save();
                }
            }
        }

        // ========== Additional Legion Controller Settings ==========

        [XmlElement("LegionControllerProfileEnabled")]
        private bool? legionControllerProfileEnabled;
        public bool? LegionControllerProfileEnabled
        {
            get { return legionControllerProfileEnabled; }
            set
            {
                if (legionControllerProfileEnabled != value)
                {
                    legionControllerProfileEnabled = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionButtonM1")]
        private string legionButtonM1;
        public string LegionButtonM1
        {
            get { return legionButtonM1; }
            set
            {
                if (legionButtonM1 != value)
                {
                    legionButtonM1 = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionGyroTarget")]
        private int? legionGyroTarget;
        public int? LegionGyroTarget
        {
            get { return legionGyroTarget; }
            set
            {
                if (legionGyroTarget != value)
                {
                    legionGyroTarget = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionGyroSensitivityX")]
        private int? legionGyroSensitivityX;
        public int? LegionGyroSensitivityX
        {
            get { return legionGyroSensitivityX; }
            set
            {
                if (legionGyroSensitivityX != value)
                {
                    legionGyroSensitivityX = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionGyroSensitivityY")]
        private int? legionGyroSensitivityY;
        public int? LegionGyroSensitivityY
        {
            get { return legionGyroSensitivityY; }
            set
            {
                if (legionGyroSensitivityY != value)
                {
                    legionGyroSensitivityY = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionGyroInvertX")]
        private bool? legionGyroInvertX;
        public bool? LegionGyroInvertX
        {
            get { return legionGyroInvertX; }
            set
            {
                if (legionGyroInvertX != value)
                {
                    legionGyroInvertX = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionGyroInvertY")]
        private bool? legionGyroInvertY;
        public bool? LegionGyroInvertY
        {
            get { return legionGyroInvertY; }
            set
            {
                if (legionGyroInvertY != value)
                {
                    legionGyroInvertY = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionGyroMappingType")]
        private int? legionGyroMappingType;
        public int? LegionGyroMappingType
        {
            get { return legionGyroMappingType; }
            set
            {
                if (legionGyroMappingType != value)
                {
                    legionGyroMappingType = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionGyroActivationMode")]
        private int? legionGyroActivationMode;
        public int? LegionGyroActivationMode
        {
            get { return legionGyroActivationMode; }
            set
            {
                if (legionGyroActivationMode != value)
                {
                    legionGyroActivationMode = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionGyroDeadzone")]
        private int? legionGyroDeadzone;
        public int? LegionGyroDeadzone
        {
            get { return legionGyroDeadzone; }
            set
            {
                if (legionGyroDeadzone != value)
                {
                    legionGyroDeadzone = value;
                    Save();
                }
            }
        }

        // NOTE: the per-game gyro tuning added 2026-08-03 (anti-deadzone, hold-boost button/factor)
        // deliberately has NO field here. It lives only in the widget's controller-profile container,
        // like smoothing and the stick deadzones — see Program.LegionControllerHandlers. A second copy
        // in this XML would be a second truth for the same value, which is how the profile-save bug of
        // 0.1.8.65 happened.
        [XmlElement("LegionLeftStickDeadzone")]
        private int? legionLeftStickDeadzone;
        public int? LegionLeftStickDeadzone
        {
            get { return legionLeftStickDeadzone; }
            set
            {
                if (legionLeftStickDeadzone != value)
                {
                    legionLeftStickDeadzone = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionRightStickDeadzone")]
        private int? legionRightStickDeadzone;
        public int? LegionRightStickDeadzone
        {
            get { return legionRightStickDeadzone; }
            set
            {
                if (legionRightStickDeadzone != value)
                {
                    legionRightStickDeadzone = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionLeftTriggerStart")]
        private int? legionLeftTriggerStart;
        public int? LegionLeftTriggerStart
        {
            get { return legionLeftTriggerStart; }
            set
            {
                if (legionLeftTriggerStart != value)
                {
                    legionLeftTriggerStart = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionLeftTriggerEnd")]
        private int? legionLeftTriggerEnd;
        public int? LegionLeftTriggerEnd
        {
            get { return legionLeftTriggerEnd; }
            set
            {
                if (legionLeftTriggerEnd != value)
                {
                    legionLeftTriggerEnd = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionRightTriggerStart")]
        private int? legionRightTriggerStart;
        public int? LegionRightTriggerStart
        {
            get { return legionRightTriggerStart; }
            set
            {
                if (legionRightTriggerStart != value)
                {
                    legionRightTriggerStart = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionRightTriggerEnd")]
        private int? legionRightTriggerEnd;
        public int? LegionRightTriggerEnd
        {
            get { return legionRightTriggerEnd; }
            set
            {
                if (legionRightTriggerEnd != value)
                {
                    legionRightTriggerEnd = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionHairTriggers")]
        private bool? legionHairTriggers;
        public bool? LegionHairTriggers
        {
            get { return legionHairTriggers; }
            set
            {
                if (legionHairTriggers != value)
                {
                    legionHairTriggers = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionJoystickAsMouseMode")]
        private int? legionJoystickAsMouseMode;
        public int? LegionJoystickAsMouseMode
        {
            get { return legionJoystickAsMouseMode; }
            set
            {
                if (legionJoystickAsMouseMode != value)
                {
                    legionJoystickAsMouseMode = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionJoystickMouseSens")]
        private int? legionJoystickMouseSens;
        public int? LegionJoystickMouseSens
        {
            get { return legionJoystickMouseSens; }
            set
            {
                if (legionJoystickMouseSens != value)
                {
                    legionJoystickMouseSens = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionGamepadMapping")]
        private string legionGamepadMapping;
        public string LegionGamepadMapping
        {
            get { return legionGamepadMapping; }
            set
            {
                if (legionGamepadMapping != value)
                {
                    legionGamepadMapping = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionNintendoLayout")]
        private bool? legionNintendoLayout;
        public bool? LegionNintendoLayout
        {
            get { return legionNintendoLayout; }
            set
            {
                if (legionNintendoLayout != value)
                {
                    legionNintendoLayout = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionVibration")]
        private int? legionVibration;
        public int? LegionVibration
        {
            get { return legionVibration; }
            set
            {
                if (legionVibration != value)
                {
                    legionVibration = value;
                    Save();
                }
            }
        }

        [XmlElement("LegionVibrationMode")]
        private int? legionVibrationMode;
        public int? LegionVibrationMode
        {
            get { return legionVibrationMode; }
            set
            {
                if (legionVibrationMode != value)
                {
                    legionVibrationMode = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Stepless controller vibration intensity (0-100 %, default 100). MSI Claw: scales the
        /// rumble report sent to the physical controller. null = use global / built-in default.
        /// </summary>
        [XmlElement("LegionVibrationIntensity")]
        private int? legionVibrationIntensity;
        public int? LegionVibrationIntensity
        {
            get { return legionVibrationIntensity; }
            set
            {
                if (legionVibrationIntensity != value)
                {
                    legionVibrationIntensity = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Legion Performance Mode (1=Quiet, 2=Balanced, 3=Performance, 255=Custom)
        /// null = use current system mode (don't change on profile switch)
        /// </summary>
        [XmlElement("LegionPerformanceMode")]
        private int? legionPerformanceMode;
        public int? LegionPerformanceMode
        {
            get { return legionPerformanceMode; }
            set
            {
                if (legionPerformanceMode != value)
                {
                    legionPerformanceMode = value;
                    Save();
                }
            }
        }

        // ========== Legion Controller Lighting ==========

        /// <summary>
        /// Legion Light Mode (0=Off, 1=Solid, 2=Pulse, 3=Dynamic, 4=Spiral)
        /// </summary>
        [XmlElement("LegionLightMode")]
        private int? legionLightMode;
        public int? LegionLightMode
        {
            get { return legionLightMode; }
            set
            {
                if (legionLightMode != value)
                {
                    legionLightMode = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Legion Light Color as hex string (RRGGBB format)
        /// </summary>
        [XmlElement("LegionLightColor")]
        private string legionLightColor;
        public string LegionLightColor
        {
            get { return legionLightColor; }
            set
            {
                if (legionLightColor != value)
                {
                    legionLightColor = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Legion Light Brightness (0-100%)
        /// </summary>
        [XmlElement("LegionLightBrightness")]
        private int? legionLightBrightness;
        public int? LegionLightBrightness
        {
            get { return legionLightBrightness; }
            set
            {
                if (legionLightBrightness != value)
                {
                    legionLightBrightness = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Legion Light Speed (0-100%)
        /// </summary>
        [XmlElement("LegionLightSpeed")]
        private int? legionLightSpeed;
        public int? LegionLightSpeed
        {
            get { return legionLightSpeed; }
            set
            {
                if (legionLightSpeed != value)
                {
                    legionLightSpeed = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Legion Power Light (controller's power indicator LED)
        /// </summary>
        [XmlElement("LegionPowerLight")]
        private bool? legionPowerLight;
        public bool? LegionPowerLight
        {
            get { return legionPowerLight; }
            set
            {
                if (legionPowerLight != value)
                {
                    legionPowerLight = value;
                    Save();
                }
            }
        }

        [XmlIgnore]
        public string Path;

        public bool IsGlobalProfile { get { return string.Compare(GameId.Name, GLOBAL_PROFILE_NAME) == 0; } }

        [XmlIgnore]
        private IDictionary<GameId, GameProfile> cache;
        [XmlIgnore]
        public IDictionary<GameId, GameProfile> Cache
        {
            get { return cache; }
            set { cache = value; }
        }

        /// <summary>
        /// For XmlSerializer only — it constructs the instance and then assigns whatever elements the
        /// file contains. DELIBERATELY EMPTY: it must leave every field at its default(T), exactly the
        /// blank slate a struct gave the serializer before §5.1. Seeding the "unset" sentinels here
        /// (ProcessorSchedulingPolicy = -1, …) would silently change how OLD profile files load: an
        /// element absent from the file would come back as the sentinel instead of 0, i.e. "unset"
        /// instead of "Auto". The sentinels belong in the real ctor below, which is what new profiles
        /// go through.
        /// </summary>
        public GameProfile()
        {
        }

        public GameProfile(string gameName, string gamePath, bool inUse, int inTDP, bool inCPUBoost, int inCPUEPP, int inMaxCPUState, int inMinCPUState, bool inTDPBoostEnabled, string inPath, IDictionary<GameId, GameProfile> inCache)
        {
            GameId = new GameId(gameName, gamePath);
            use = inUse;
            // AC values (main settings)
            tdp = inTDP;
            cpuBoost = inCPUBoost;
            cpuEPP = inCPUEPP;
            maxCPUState = inMaxCPUState;
            minCPUState = inMinCPUState;
            tdpBoostEnabled = inTDPBoostEnabled;
            tdpBoostFPPTWatts = 0; // 0 = not set (use device default)
            // CPU advanced (ToothNClaw port): -1 = unset, 0 = unlimited (freq)
            processorSchedulingPolicy = -1;
            maxPCoreFreqMHz = 0;
            maxECoreFreqMHz = 0;
            // Intel display: null = not configured.
            intelAdaptiveSharpness = null;
            intelColorSaturation = null;
            intelColorHue = null;
            intelDisplayContrast = null;
            intelDisplayBrightness = null;
            intelDisplayGamma = null;
            // Intel gaming 3D features: null = not configured.
            intelLowLatency = null;
            intelFrameSync = null;
            // Plugged-in overrides (null = inherit the base, i.e. the unplugged value)
            tdpPlugged = null;
            cpuBoostPlugged = null;
            cpuEppPlugged = null;
            maxCpuStatePlugged = null;
            minCpuStatePlugged = null;
            // Intel FPS Tier and cap mode (global/per-game)
            intelFpsTier = 0;
            fpsCapMode   = 0;
            // Additional profile settings (AC)
            fpsLimit = 0;
            osPowerMode = null;
            // Additional profile settings (DC overrides)
            fpsLimitPlugged = null;
            osPowerModePlugged = null;
            // Display settings (shared AC/DC)
            hdrEnabled = false;
            resolution = null;
            refreshRate = null;
            // Overlay and CPU affinity
            overlayLevel = null;
            osdType = null;
            osdPosition = null;
            cpuAffinity = null;
            // Legion controller remapping (shared AC/DC)
            legionButtonY1 = null;
            legionButtonY2 = null;
            legionButtonY3 = null;
            legionButtonM2 = null;
            legionButtonM3 = null;
            legionButtonDesktop = null;
            legionButtonPage = null;
            legionGyroButton = null;
            // Additional Legion controller settings
            legionControllerProfileEnabled = null;
            legionButtonM1 = null;
            legionGyroTarget = null;
            legionGyroSensitivityX = null;
            legionGyroSensitivityY = null;
            legionGyroInvertX = null;
            legionGyroInvertY = null;
            legionGyroMappingType = null;
            legionGyroActivationMode = null;
            legionGyroDeadzone = null;
            legionLeftStickDeadzone = null;
            legionRightStickDeadzone = null;
            legionLeftTriggerStart = null;
            legionLeftTriggerEnd = null;
            legionRightTriggerStart = null;
            legionRightTriggerEnd = null;
            legionHairTriggers = null;
            legionJoystickAsMouseMode = null;
            legionJoystickMouseSens = null;
            legionGamepadMapping = null;
            legionNintendoLayout = null;
            legionVibration = null;
            legionVibrationMode = null;
            legionVibrationIntensity = null;
            legionPerformanceMode = null;
            // Lighting settings
            legionLightMode = null;
            legionLightColor = null;
            legionLightBrightness = null;
            legionLightSpeed = null;
            legionPowerLight = null;
            Path = inPath;
            cache = inCache;
        }

        public bool IsValid()
        {
            return GameId.IsValid();
        }

        /// <summary>
        /// Identity is the GameId alone — two instances describing the same game are "equal" no matter
        /// what values they carry. That was true as a struct and stays true; only the null handling is
        /// new, and it must be here: without it every <c>profile == null</c> in the codebase would
        /// recurse into a NullReferenceException on GameId.
        /// </summary>
        public static bool operator ==(GameProfile g1, GameProfile g2)
        {
            if (ReferenceEquals(g1, g2)) return true;
            if (ReferenceEquals(g1, null) || ReferenceEquals(g2, null)) return false;
            return g1.GameId == g2.GameId;
        }

        public static bool operator !=(GameProfile p1, GameProfile p2)
        {
            return !(p1 == p2);
        }

        public override bool Equals(object obj)
        {
            if (obj is GameProfile other)
            {
                return this == other;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return GameId.GetHashCode();
        }

        // Export to xml string.
        public override string ToString()
        {
            return XmlHelper.ToXMLString(this, true);
        }

        /// <summary>
        /// Save debounce state keyed by file path. A GameProfile setter typically changes one
        /// field at a time, and UI sliders can fire dozens of setters per second. Writing the
        /// full XML to disk each time is wasteful. We instead update the cache synchronously
        /// (so reads are always consistent) and schedule the disk write after a short delay,
        /// collapsing bursts of changes into a single write.
        ///
        /// Since §5.1 the queued entry is a REFERENCE to the live profile, not a snapshot copy, so the
        /// deferred write always serializes the newest state — a field changed during the debounce
        /// window can no longer be written stale by an earlier queued copy.
        /// </summary>
        private const int SaveDebounceMs = 250;
        private static readonly ConcurrentDictionary<string, GameProfile> PendingWrites
            = new ConcurrentDictionary<string, GameProfile>(System.StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Timer> PendingTimers
            = new ConcurrentDictionary<string, Timer>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Raised after ANY profile save has updated the in-memory state. The single invalidation point
        /// for the helper's ProfileSnapshot (plan §5.3): every setter of this type ends in Save(), so
        /// subscribing here catches every value change without the callers having to remember anything.
        /// Notifying from the individual call sites is what this design deliberately avoids — the first
        /// forgotten call site is the next "two truths" bug.
        ///
        /// Fires on the caller's thread, before the debounced disk write, and describes the IN-MEMORY
        /// state (which Save() has already updated). Subscribers must therefore be cheap and must not
        /// throw; treat it as "mark dirty", not "do work". Deliberately static: GameProfile lives in
        /// Shared and must not know about the helper's property system.
        /// </summary>
        public static event Action<GameProfile> Saved;

        public void Save()
        {
            // Update cache synchronously so other code sees the latest state immediately.
            lock (ProfileLock)
            {
                if (cache != null)
                {
                    cache[GameId] = this;
                }
            }

            // Announce BEFORE the early return below: a profile without a Path still changed in memory,
            // and the snapshot mirrors memory, not the disk.
            try { Saved?.Invoke(this); } catch { /* a broken subscriber must never break a save */ }

            if (string.IsNullOrEmpty(Path))
            {
                return;
            }

            // Queue the disk write; coalesce bursts of changes into a single debounced write.
            PendingWrites[Path] = this;

            var newTimer = new Timer(FlushPendingWrite, Path, SaveDebounceMs, Timeout.Infinite);
            if (PendingTimers.TryGetValue(Path, out var existing))
            {
                // Cancel and dispose the previous timer to reset the debounce window.
                existing.Dispose();
            }
            PendingTimers[Path] = newTimer;
        }

        /// <summary>
        /// Timer callback: flushes the pending profile snapshot for <paramref name="state"/> (a file path) to disk.
        /// </summary>
        private static void FlushPendingWrite(object state)
        {
            var path = (string)state;
            if (!PendingWrites.TryRemove(path, out var profile))
            {
                return;
            }
            if (PendingTimers.TryRemove(path, out var timer))
            {
                timer.Dispose();
            }

            lock (ProfileLock)
            {
                XmlHelper.ToXMLFile(profile, path);
            }
        }

        /// <summary>
        /// Copies the entries of a profile cache under the SAME lock <see cref="Save"/> uses to write
        /// into it. Reading such a dictionary without this lock races the cache update in Save() and
        /// throws "collection was modified" at unpredictable moments — which is why the lock stays
        /// private and callers get this instead of direct enumeration.
        /// </summary>
        public static List<GameProfile> SnapshotCache(IDictionary<GameId, GameProfile> cache)
        {
            if (cache == null) return new List<GameProfile>();

            lock (ProfileLock)
            {
                return new List<GameProfile>(cache.Values);
            }
        }

        /// <summary>
        /// Forces any pending debounced saves to flush immediately. Useful on shutdown.
        /// </summary>
        public static void FlushAllPendingWrites()
        {
            foreach (var path in PendingWrites.Keys)
            {
                FlushPendingWrite(path);
            }
        }
    }
}
