using NLog;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.ControllerEmulation
{
    internal class ControllerEmulationAvailableProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        public ControllerEmulationAvailableProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationAvailable, manager)
        {
        }
    }

    internal class ControllerEmulationGyroSourceProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationGyroSourceProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationGyroSource, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationGyroSource changed to {Value}");
            Manager?.SetGyroSource(Value);
        }
    }

    internal class ControllerEmulationModeProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationModeProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationMode, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationMode changed to {Value}");
            Manager?.SetMode(Value);
        }
    }
}
