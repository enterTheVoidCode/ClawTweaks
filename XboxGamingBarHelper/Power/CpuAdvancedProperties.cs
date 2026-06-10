using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Power
{
    // CPU advanced settings (ToothNClaw port). Each property applies to the live power
    // scheme (AC + DC) on change. -1 = "unset" (don't touch); for frequency, 0 = unlimited.
    // A "hasUserModified" guard prevents overwriting system defaults on first sync.
    //
    // LOCKED: the advanced CPU controls (detailed boost mode, scheduling policy, P/E-core max
    // frequency) are hidden in the UI and must NOT touch Windows power settings — only the simple
    // CPU boost on/off (CPUBoostProperty → PowerManager.SetCpuBoostMode) stays active, like upstream
    // GoTweaks. The single switch below disables all four applies at the source so nothing is written
    // regardless of widget pushes, profile switches or startup. Flip to true to re-enable the port.
    internal static class CpuAdvancedApply
    {
        public const bool Enabled = false;
    }

    /// <summary>
    /// CPU Boost mode 0-6 (Disabled..Efficient Aggressive At Guaranteed). This is the
    /// richer companion to the on/off <see cref="CPUBoostProperty"/> — when the user picks a
    /// mode it writes the explicit value; the on/off toggle still maps to 0/2 separately.
    /// </summary>
    internal class CpuBoostModeProperty : HelperProperty<int, PowerManager>
    {
        private bool _hasUserModified = false;
        private int _initialValue;

        public CpuBoostModeProperty(int inValue, PowerManager inManager)
            : base(inValue, null, Function.CpuBoostMode, inManager)
        {
            _initialValue = inValue;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (!CpuAdvancedApply.Enabled) return; // LOCKED: advanced CPU apply disabled
            if (Value < 0) return; // unset

            if (!_hasUserModified)
            {
                if (Value != _initialValue) _hasUserModified = true;
                else
                {
                    Logger.Debug($"CPU Boost Mode: skipping system write - unchanged from initial ({Value})");
                    return;
                }
            }

            PowerManager.SetCpuBoostModeValue(false, Value);
            PowerManager.SetCpuBoostModeValue(true, Value);
        }
    }

    /// <summary>Processor scheduling policy 0-4 (Auto, Prefer P, Prefer E, Only P, Only E).</summary>
    internal class ProcessorSchedulingPolicyProperty : HelperProperty<int, PowerManager>
    {
        private bool _hasUserModified = false;
        private int _initialValue;

        public ProcessorSchedulingPolicyProperty(int inValue, PowerManager inManager)
            : base(inValue, null, Function.ProcessorSchedulingPolicy, inManager)
        {
            _initialValue = inValue;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (!CpuAdvancedApply.Enabled) return; // LOCKED: advanced CPU apply disabled
            if (Value < 0) return; // unset

            if (!_hasUserModified)
            {
                if (Value != _initialValue) _hasUserModified = true;
                else
                {
                    Logger.Debug($"Scheduling Policy: skipping system write - unchanged from initial ({Value})");
                    return;
                }
            }

            PowerManager.SetSchedulingPolicy(Value);
        }
    }

    /// <summary>P-core (Efficiency Class 1) max frequency in MHz. 0 = unlimited.</summary>
    internal class MaxPCoreFreqProperty : HelperProperty<int, PowerManager>
    {
        private bool _hasUserModified = false;
        private int _initialValue;

        public MaxPCoreFreqProperty(int inValue, PowerManager inManager)
            : base(inValue, null, Function.MaxPCoreFreqMHz, inManager)
        {
            _initialValue = inValue;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (!CpuAdvancedApply.Enabled) return; // LOCKED: advanced CPU apply disabled

            if (!_hasUserModified)
            {
                if (Value != _initialValue) _hasUserModified = true;
                else
                {
                    Logger.Debug($"Max P-Core Freq: skipping system write - unchanged from initial ({Value})");
                    return;
                }
            }

            PowerManager.SetCpuFreqLimit(false, (uint)(Value < 0 ? 0 : Value), isSecondary: true);
            PowerManager.SetCpuFreqLimit(true, (uint)(Value < 0 ? 0 : Value), isSecondary: true);
        }
    }

    /// <summary>E-core / all-core max frequency in MHz. 0 = unlimited.</summary>
    internal class MaxECoreFreqProperty : HelperProperty<int, PowerManager>
    {
        private bool _hasUserModified = false;
        private int _initialValue;

        public MaxECoreFreqProperty(int inValue, PowerManager inManager)
            : base(inValue, null, Function.MaxECoreFreqMHz, inManager)
        {
            _initialValue = inValue;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (!CpuAdvancedApply.Enabled) return; // LOCKED: advanced CPU apply disabled

            if (!_hasUserModified)
            {
                if (Value != _initialValue) _hasUserModified = true;
                else
                {
                    Logger.Debug($"Max E-Core Freq: skipping system write - unchanged from initial ({Value})");
                    return;
                }
            }

            PowerManager.SetCpuFreqLimit(false, (uint)(Value < 0 ? 0 : Value), isSecondary: false);
            PowerManager.SetCpuFreqLimit(true, (uint)(Value < 0 ? 0 : Value), isSecondary: false);
        }
    }
}
