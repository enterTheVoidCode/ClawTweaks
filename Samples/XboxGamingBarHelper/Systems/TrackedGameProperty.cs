using NLog;
using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class TrackedGameProperty : HelperProperty<TrackedGame, SystemManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

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

        /// <summary>
        /// Override SetValue to reject empty/invalid TrackedGame updates from the widget.
        /// When the widget loses focus, Game Bar sends empty TrackedGame which should not
        /// clear our currently detected game.
        /// </summary>
        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            // Check if the incoming TrackedGame is valid before accepting it
            if (newValue is TrackedGame trackedGame)
            {
                bool newValueIsValid = !string.IsNullOrEmpty(trackedGame.DisplayName) || !string.IsNullOrEmpty(trackedGame.TitleId);

                // If we have a valid tracked game and the new one is invalid, reject the update
                if (IsValid() && !newValueIsValid)
                {
                    Logger.Info($"Rejecting empty TrackedGame update - preserving current: {Value.DisplayName}");
                    return false;
                }
            }

            return base.SetValue(newValue, updatedTime);
        }

        public TrackedGameProperty(SystemManager inManager) : base(new TrackedGame(), null, Function.TrackedGame, inManager)
        {
        }
    }
}
