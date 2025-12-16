using NLog;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Performance
{
    /// <summary>
    /// Property for TDP Boost enabled state (profile-synced).
    /// When enabled, SPPT and FPPT values are boosted above the base TDP.
    /// </summary>
    internal class TDPBoostEnabledProperty : HelperProperty<bool, PerformanceManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public TDPBoostEnabledProperty(bool inValue, PerformanceManager inManager)
            : base(inValue, null, Function.TDPBoostEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"TDP Boost enabled: {Value}");

            // Re-apply current TDP with new boost setting
            if (Manager?.TDP != null)
            {
                Manager.SetTDP(Manager.TDP.Value);
            }
        }
    }

    /// <summary>
    /// Property for SPPT boost value (device setting, 0-10W).
    /// When TDP Boost is enabled, SPPT = TDP + this value.
    /// </summary>
    internal class TDPBoostSPPTProperty : HelperProperty<int, PerformanceManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public TDPBoostSPPTProperty(int inValue, PerformanceManager inManager)
            : base(inValue, null, Function.TDPBoostSPPT, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"TDP Boost SPPT: {Value}W");

            // Re-apply current TDP with new boost value if boost is enabled
            if (Manager?.TDPBoostEnabled?.Value == true && Manager?.TDP != null)
            {
                Manager.SetTDP(Manager.TDP.Value);
            }
        }
    }

    /// <summary>
    /// Property for FPPT boost value (device setting, 0-15W).
    /// When TDP Boost is enabled, FPPT = TDP + this value.
    /// </summary>
    internal class TDPBoostFPPTProperty : HelperProperty<int, PerformanceManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public TDPBoostFPPTProperty(int inValue, PerformanceManager inManager)
            : base(inValue, null, Function.TDPBoostFPPT, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"TDP Boost FPPT: {Value}W");

            // Re-apply current TDP with new boost value if boost is enabled
            if (Manager?.TDPBoostEnabled?.Value == true && Manager?.TDP != null)
            {
                Manager.SetTDP(Manager.TDP.Value);
            }
        }
    }
}
