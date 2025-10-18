using Shared.Data;
using Shared.Enums;
using System.Runtime.CompilerServices;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class RunningGameProperty : HelperProperty<RunningGame, SystemManager>
    {
        public RunningGameProperty(RunningGame inValue, SystemManager inManager) : base(inValue, null, Function.CurrentGame, inManager)
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

        protected override void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // Manager.RunningGame = Value;
        }
    }
}
