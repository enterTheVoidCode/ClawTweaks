using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class RunningGameProperty : HelperProperty<RunningGame, SystemManager>
    {
        public RunningGameProperty(SystemManager inManager) : base(new RunningGame(), null, Function.RunningGame, inManager)
        {
        }

        public GameId GameId
        {
            get { return Value.GameId; }
        }

        public bool IsValid()
        {
            return Value.IsValid();
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // Manager.RunningGame = Value;
        }
    }
}
