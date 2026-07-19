using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Power
{
    // CPU advanced settings (ToothNClaw port). Each property applies to the live power
    // scheme (AC + DC) on change. -1 = "unset" (don't touch); for frequency, 0 = unlimited.
    // A "hasUserModified" guard prevents overwriting system defaults on first sync.
    //
    // Master switch for the advanced CPU controls (detailed boost mode, scheduling policy, P/E-core
    // max frequency). When false, all four applies below are no-ops at the source (nothing is written
    // to Windows power settings regardless of widget pushes, profile switches or startup). Re-enabled
    // as an EXPERIMENTAL feature (P/E max frequency + Only-P/Only-E core scheduling), surfaced in the
    // Performance tab with an "experimental" badge.
    internal static class CpuAdvancedApply
    {
        /// <summary>Compile-time master switch. Kept so the whole subsystem can be disabled at once.</summary>
        public const bool Compiled = true;

        /// <summary>Per-device gate, set once from the detected device's SupportsCpuAdvanced capability.
        /// False on the Claw 8 EX (Panther Lake): the scheduling policy and P/E max-frequency settings are
        /// not reliably persistent there. Gating here — at the single point every apply already checks —
        /// means a stored profile value cannot be applied behind the disabled UI either.</summary>
        public static bool DeviceSupported = true;

        public static bool Enabled => Compiled && DeviceSupported;
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
