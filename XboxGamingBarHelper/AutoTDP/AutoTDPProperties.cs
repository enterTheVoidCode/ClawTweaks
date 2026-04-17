using NLog;
using RTSSSharedMemoryNET;
using Shared.Data;
using Shared.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.RTSS;
using XboxGamingBarHelper.Systems;

namespace XboxGamingBarHelper.AutoTDP
{
    // Property classes
    internal class AutoTDPEnabledProperty : HelperProperty<bool, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPEnabledProperty(bool inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPEnabled, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP enabled: {Value}");

            if (!Value)
            {
                Manager?.FlushLearnedTDPStore();
            }
        }
    }

    internal class AutoTDPTargetFPSProperty : HelperProperty<int, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPTargetFPSProperty(int inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPTargetFPS, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP target FPS: {Value}");
            Manager?.UpdateLearnedGameDataProperty();
        }
    }

    internal class AutoTDPCurrentFPSProperty : HelperProperty<int, AutoTDPManager>
    {
        public AutoTDPCurrentFPSProperty(int inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPCurrentFPS, inManager)
        {
        }
    }

    internal class AutoTDPMinTDPProperty : HelperProperty<int, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPMinTDPProperty(int inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPMinTDP, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP min TDP: {Value}W");
            Manager.UpdateTDPLimits(Value, Manager.MaxTDP.Value);
        }
    }

    internal class AutoTDPMaxTDPProperty : HelperProperty<int, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPMaxTDPProperty(int inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPMaxTDP, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP max TDP: {Value}W");
            Manager.UpdateTDPLimits(Manager.MinTDP.Value, Value);
        }
    }

    internal class AutoTDPUseMLModeProperty : HelperProperty<bool, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPUseMLModeProperty(bool inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPUseMLMode, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP ML Mode: {(Value ? "Enabled" : "Disabled")}");
        }
    }

    internal class AutoTDPMLStatusProperty : HelperProperty<string, AutoTDPManager>
    {
        public AutoTDPMLStatusProperty(string inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPMLStatus, inManager)
        {
        }
    }

    internal class AutoTDPLearnedGameDataProperty : HelperProperty<string, AutoTDPManager>
    {
        public AutoTDPLearnedGameDataProperty(string inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPLearnedGameData, inManager)
        {
        }
    }

    internal class AutoTDPResetMLProperty : HelperProperty<bool, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPResetMLProperty(bool inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPResetML, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            // When set to true, trigger reset
            if (Value)
            {
                Logger.Info("AutoTDP ML Reset triggered");
                Manager.ResetMLLearning();
                // Reset the property back to false
                SetValue(false);
            }
        }
    }

    internal class AutoTDPPauseWhenUnfocusedProperty : HelperProperty<bool, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AutoTDPPauseWhenUnfocusedProperty(bool inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPPauseWhenUnfocused, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"AutoTDP pause when unfocused: {Value}");
        }
    }

    internal class AutoTDPControllerTypeProperty : HelperProperty<int, AutoTDPManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string[] ControllerNames = { "PID", "Q-Learning", "SARSA" };

        public AutoTDPControllerTypeProperty(int inValue, AutoTDPManager inManager) : base(inValue, null, Function.AutoTDPControllerType, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            string name = Value >= 0 && Value < ControllerNames.Length ? ControllerNames[Value] : "Unknown";
            Logger.Info($"AutoTDP Controller Type: {name} ({Value})");
        }
    }
}
