using Shared.Enums;

namespace XboxGamingBar.Data
{
    internal class AutoTDPEnabledProperty : WidgetProperty<bool>
    {
        public AutoTDPEnabledProperty(bool inValue) : base(inValue, null, Function.AutoTDPEnabled)
        {
        }
    }

    internal class AutoTDPTargetFPSProperty : WidgetProperty<int>
    {
        public AutoTDPTargetFPSProperty(int inValue) : base(inValue, null, Function.AutoTDPTargetFPS)
        {
        }
    }

    internal class AutoTDPCurrentFPSProperty : WidgetProperty<int>
    {
        public AutoTDPCurrentFPSProperty(int inValue) : base(inValue, null, Function.AutoTDPCurrentFPS)
        {
        }
    }

    internal class AutoTDPMinTDPProperty : WidgetProperty<int>
    {
        public AutoTDPMinTDPProperty(int inValue) : base(inValue, null, Function.AutoTDPMinTDP)
        {
        }
    }

    internal class AutoTDPMaxTDPProperty : WidgetProperty<int>
    {
        public AutoTDPMaxTDPProperty(int inValue) : base(inValue, null, Function.AutoTDPMaxTDP)
        {
        }
    }

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

    internal class ForceDefaultGameProfileProperty : WidgetProperty<bool>
    {
        public ForceDefaultGameProfileProperty(bool inValue) : base(inValue, null, Function.ForceDefaultGameProfile)
        {
        }
    }

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

    internal class TDPBoostFPPTProperty : WidgetProperty<int>
    {
        public TDPBoostFPPTProperty(int inValue) : base(inValue, null, Function.TDPBoostFPPT)
        {
        }
    }
}
