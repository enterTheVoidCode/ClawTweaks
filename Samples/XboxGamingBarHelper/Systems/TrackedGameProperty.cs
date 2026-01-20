using NLog;
using Shared.Data;
using Shared.Enums;
using Shared.Utilities;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class TrackedGameProperty : HelperProperty<TrackedGame, SystemManager>
    {
        private static new readonly Logger Logger = LogManager.GetCurrentClassLogger();

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
        /// Check if a TrackedGame instance is valid (has DisplayName or TitleId).
        /// </summary>
        private bool IsTrackedGameValid(TrackedGame trackedGame)
        {
            return !string.IsNullOrEmpty(trackedGame.DisplayName) || !string.IsNullOrEmpty(trackedGame.TitleId);
        }

        /// <summary>
        /// Override SetValue to reject empty/invalid TrackedGame updates from the widget.
        /// When the widget loses focus, Game Bar sends empty TrackedGame which should not
        /// clear our currently detected game.
        /// </summary>
        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            TrackedGame? trackedGame = null;

            // Handle both cases: direct TrackedGame object or XML string that needs deserialization
            if (newValue is TrackedGame directTrackedGame)
            {
                trackedGame = directTrackedGame;
            }
            else if (newValue is string xmlString && !string.IsNullOrEmpty(xmlString))
            {
                // Deserialize XML string to TrackedGame for validation
                try
                {
                    trackedGame = XmlHelper.FromXMLString<TrackedGame>(xmlString);
                }
                catch
                {
                    // If deserialization fails, let base class handle it (will also fail there)
                    Logger.Warn($"Failed to deserialize TrackedGame XML for validation, passing to base");
                }
            }

            // Check if the incoming TrackedGame is valid before accepting it
            if (trackedGame.HasValue)
            {
                bool newValueIsValid = IsTrackedGameValid(trackedGame.Value);

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
