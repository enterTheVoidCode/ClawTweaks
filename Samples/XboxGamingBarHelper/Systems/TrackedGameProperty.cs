using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class TrackedGameProperty : HelperProperty<TrackedGame, SystemManager>
    {
        public string AumId
        {
            get { return Value.AumId; }
        }

        public string DisplayName
        {
            get { return Value.DisplayName; }
        }

        public string TitleId
        {
            get { return Value.TitleId; }
        }

        public bool IsFullscreen
        {
            get { return Value.IsFullscreen; }
        }

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(Value.DisplayName) || !string.IsNullOrEmpty(Value.TitleId);
        }

        public TrackedGameProperty(SystemManager inManager) : base(new TrackedGame(), null, Function.TrackedGame, inManager)
        {
        }
    }
}
