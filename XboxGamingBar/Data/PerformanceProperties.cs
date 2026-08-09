using Shared.Enums;

namespace XboxGamingBar.Data
{
    // These lived in Data/AutoTDP/AutoTDPProperties.cs purely by accident of history — none of them
    // has anything to do with AutoTDP, which was removed in full on 2026-07-31 (GoTweaks legacy,
    // see Doku/PLAN_Performance_SingleStore.md §5.0). Deleting that file took the live TDP-Boost and
    // CPU-core properties with it, which is exactly the "TDPBoost is NOT AutoTDP" trap the plan warns
    // about. They are collected here under a name that says what they are.

    /// <summary>Configured PL1 slider range as "min,max" watts. Not an AutoTDP range.</summary>
    internal class TDPLimitsProperty : WidgetProperty<string>
    {
        public TDPLimitsProperty(string inValue) : base(inValue, null, Function.TDPLimits)
        {
        }
    }

    internal class CPUCoreConfigProperty : WidgetProperty<string>
    {
        public CPUCoreConfigProperty(string inValue) : base(inValue, null, Function.CPUCoreConfig)
        {
        }
    }

    internal class CPUCoreActiveConfigProperty : WidgetProperty<string>
    {
        public CPUCoreActiveConfigProperty(string inValue) : base(inValue, null, Function.CPUCoreActiveConfig)
        {
        }
    }

    internal class CoreParkingPercentProperty : WidgetProperty<int>
    {
        public CoreParkingPercentProperty(int inValue) : base(inValue, null, Function.CoreParkingPercent)
        {
        }
    }

    internal class ForceParkModeProperty : WidgetProperty<bool>
    {
        public ForceParkModeProperty(bool inValue) : base(inValue, null, Function.ForceParkMode)
        {
        }
    }

    /// <summary>PL2 Overboost on/off. Owned by the helper since 0.1.8 — the widget only displays it.</summary>
    internal class TDPBoostEnabledProperty : WidgetProperty<bool>
    {
        public TDPBoostEnabledProperty(bool inValue) : base(inValue, null, Function.TDPBoostEnabled)
        {
        }
    }

    internal class TDPBoostSPPTProperty : WidgetProperty<int>
    {
        public TDPBoostSPPTProperty(int inValue) : base(inValue, null, Function.TDPBoostSPPT)
        {
        }
    }

    /// <summary>Absolute PL2 target in watts (NOT an offset on PL1 — see GameProfile.TDPBoostFPPTWatts).</summary>
    internal class TDPBoostFPPTProperty : WidgetProperty<int>
    {
        public TDPBoostFPPTProperty(int inValue) : base(inValue, null, Function.TDPBoostFPPT)
        {
        }
    }
}
